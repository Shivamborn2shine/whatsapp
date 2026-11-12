import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { getConfigValue, setConfigValue, getLocalDb, logLocalAction } from '../common/db';
import { getSubmissionFolder, initStorageDirectories } from '../common/storage';

export interface SyncServiceOptions {
  cloudUrl?: string;
  heartbeatIntervalSec?: number;
  syncIntervalSec?: number;
  retentionCheckIntervalMin?: number;
  onNotification?: (title: string, message: string) => void;
}

export class BackgroundSyncService {
  private cloudUrl: string;
  private isRunning: boolean = false;
  private heartbeatTimer: NodeJS.Timeout | null = null;
  private syncTimer: NodeJS.Timeout | null = null;
  private retentionTimer: NodeJS.Timeout | null = null;
  private onNotification?: (title: string, message: string) => void;

  constructor(options: SyncServiceOptions = {}) {
    this.cloudUrl = options.cloudUrl || getConfigValue('cloudServerUrl') || 'http://localhost:3001';
    this.onNotification = options.onNotification;
  }

  public async start(): Promise<void> {
    if (this.isRunning) return;
    this.isRunning = true;
    initStorageDirectories();

    console.log(`[ScanDrop.Service] Background Sync Service started. Target Cloud: ${this.cloudUrl}`);

    // Trigger initial runs
    await this.performHeartbeat();
    await this.performSync();
    await this.performRetentionCleanup();

    // Setup periodic timers
    this.heartbeatTimer = setInterval(() => this.performHeartbeat(), 30 * 1000); // 30s
    this.syncTimer = setInterval(() => this.performSync(), 8 * 1000); // 8s
    this.retentionTimer = setInterval(() => this.performRetentionCleanup(), 5 * 60 * 1000); // 5 min
  }

  public stop(): void {
    this.isRunning = false;
    if (this.heartbeatTimer) clearInterval(this.heartbeatTimer);
    if (this.syncTimer) clearInterval(this.syncTimer);
    if (this.retentionTimer) clearInterval(this.retentionTimer);
    console.log('[ScanDrop.Service] Background Sync Service stopped.');
  }

  public async performHeartbeat(): Promise<void> {
    const deviceToken = getConfigValue('deviceToken');
    if (!deviceToken) {
      return; // Device not yet paired
    }

    try {
      const res = await fetch(`${this.cloudUrl}/api/v1/auth/heartbeat`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'x-device-token': deviceToken
        },
        body: JSON.stringify({
          appVersion: '3.0.0',
          windowsVersion: 'Windows 11'
        })
      });

      if (res.status === 403) {
        setConfigValue('deviceStatus', 'revoked');
        console.warn('[ScanDrop.Service] Device has been revoked by shop or administrator.');
        return;
      }

      if (res.ok) {
        const data = await res.json() as any;
        setConfigValue('shopName', data.shopName);
        setConfigValue('subscriptionStatus', data.subscription.status);
        setConfigValue('subscriptionTier', data.subscription.tier);
        setConfigValue('subscriptionExpiry', data.subscription.expiryDate);
        setConfigValue('entitlementsJson', JSON.stringify(data.subscription.entitlements));
        setConfigValue('lastHeartbeatSuccess', new Date().toISOString());
      }
    } catch (err: any) {
      console.warn('[ScanDrop.Service] Heartbeat connection error (offline):', err.message);
    }
  }

  public async performSync(): Promise<void> {
    const deviceToken = getConfigValue('deviceToken');
    const deviceStatus = getConfigValue('deviceStatus');

    if (!deviceToken || deviceStatus === 'revoked') {
      return;
    }

    try {
      const res = await fetch(`${this.cloudUrl}/api/v1/submissions/sync`, {
        headers: { 'x-device-token': deviceToken }
      });

      if (!res.ok) return;

      const data = await res.json() as any;
      if (!data.submissions) return;

      const db = getLocalDb();

      for (const sub of data.submissions) {
        const existingSub = db.prepare('SELECT id, status FROM local_submissions WHERE id = ?').get(sub.id) as any;
        const subFolder = getSubmissionFolder(sub.id, existingSub?.status || 'incoming');

        if (!existingSub) {
          db.prepare(`
            INSERT INTO local_submissions (id, shop_id, customer_name, service_category, instructions, status, file_count, total_size_bytes, local_folder, expires_at, created_at)
            VALUES (?, ?, ?, ?, ?, 'incoming', ?, ?, ?, ?, ?)
          `).run(
            sub.id,
            sub.shop_id,
            sub.customer_name,
            sub.service_category,
            sub.instructions,
            sub.file_count,
            sub.total_size_bytes,
            subFolder,
            sub.expires_at,
            sub.created_at
          );

          logLocalAction('SUBMISSION_RECEIVED', `New submission ${sub.id} received from ${sub.customer_name || 'Customer'}`);
          if (this.onNotification) {
            this.onNotification('New Customer Files Arrived!', `Receipt ${sub.id} (${sub.file_count} files)`);
          }
        }

        // Process files
        for (const file of sub.files) {
          const existingFile = db.prepare('SELECT id, local_path, status FROM local_files WHERE id = ?').get(file.id) as any;
          if (existingFile && fs.existsSync(existingFile.local_path)) {
            continue; // File already synced and verified
          }

          await this.downloadAndVerifyFile(file, sub.id, subFolder);
        }
      }
    } catch (err: any) {
      console.warn('[ScanDrop.Service] Sync error:', err.message);
    }
  }

  private async downloadAndVerifyFile(file: any, submissionId: string, destFolder: string): Promise<void> {
    const deviceToken = getConfigValue('deviceToken')!;
    const downloadUrl = `${this.cloudUrl}/api/v1/submissions/files/${file.id}/download`;

    try {
      const res = await fetch(downloadUrl, {
        headers: { 'x-device-token': deviceToken }
      });

      if (!res.ok) {
        console.error(`[ScanDrop.Service] Failed to download file ${file.id}: HTTP ${res.status}`);
        return;
      }

      const buffer = Buffer.from(await res.arrayBuffer());
      const hash = crypto.createHash('sha256').update(buffer).digest('hex');

      const targetPath = path.join(destFolder, file.original_name);
      const db = getLocalDb();

      // Integrity Check (Section 2.1 & 15.7)
      if (file.sha256_hash && hash.toLowerCase() !== file.sha256_hash.toLowerCase()) {
        console.error(`[ScanDrop.Service] Hash mismatch for file ${file.id}! Quarantining.`);
        const dirs = initStorageDirectories();
        const quarantinePath = path.join(dirs.quarantine, `${file.id}_${file.original_name}`);
        fs.writeFileSync(quarantinePath, buffer);

        db.prepare(`
          INSERT INTO local_files (id, submission_id, original_name, file_extension, size_bytes, sha256_hash, local_path, status, expires_at, created_at)
          VALUES (?, ?, ?, ?, ?, ?, ?, 'quarantined', ?, ?)
          ON CONFLICT(id) DO UPDATE SET status = 'quarantined'
        `).run(file.id, submissionId, file.original_name, file.file_extension, file.size_bytes, hash, quarantinePath, file.expires_at, file.created_at);

        logLocalAction('INTEGRITY_FAILED', `File ${file.original_name} failed SHA-256 integrity check and was quarantined.`);
        return;
      }

      // Hash verified -> save to local incoming folder
      fs.writeFileSync(targetPath, buffer);

      db.prepare(`
        INSERT INTO local_files (id, submission_id, original_name, file_extension, size_bytes, sha256_hash, local_path, status, expires_at, created_at)
        VALUES (?, ?, ?, ?, ?, ?, ?, 'synced', ?, ?)
        ON CONFLICT(id) DO UPDATE SET local_path = excluded.local_path, status = 'synced'
      `).run(file.id, submissionId, file.original_name, file.file_extension, file.size_bytes, hash, targetPath, file.expires_at, file.created_at);

      logLocalAction('FILE_SYNCED', `File ${file.original_name} (${(file.size_bytes / 1024).toFixed(1)} KB) downloaded and verified.`);
    } catch (err: any) {
      console.error(`[ScanDrop.Service] Download error for file ${file.id}:`, err.message);
    }
  }

  public async performRetentionCleanup(): Promise<void> {
    try {
      const db = getLocalDb();
      const now = new Date().toISOString();

      // Find local submissions where expires_at has passed
      const expiredSubmissions = db.prepare(`
        SELECT id, local_folder FROM local_submissions
        WHERE expires_at <= ? AND status != 'expired'
      `).all(now) as Array<{ id: string; local_folder: string }>;

      for (const sub of expiredSubmissions) {
        if (fs.existsSync(sub.local_folder)) {
          fs.rmSync(sub.local_folder, { recursive: true, force: true });
        }

        db.prepare("UPDATE local_submissions SET status = 'expired' WHERE id = ?").run(sub.id);
        db.prepare("DELETE FROM local_files WHERE submission_id = ?").run(sub.id);

        logLocalAction('LOCAL_RETENTION_PURGED', `Submission ${sub.id} 24h retention deadline elapsed. Files safely removed from shop disk.`);
        console.log(`[ScanDrop.Service] 24h retention purged submission ${sub.id}`);
      }

      // Also purge expired derived files
      const expiredDerived = db.prepare('SELECT id, local_path FROM local_derived_files WHERE expires_at <= ?').all(now) as any[];
      for (const der of expiredDerived) {
        if (fs.existsSync(der.local_path)) {
          fs.rmSync(der.local_path, { force: true });
        }
        db.prepare('DELETE FROM local_derived_files WHERE id = ?').run(der.id);
      }
    } catch (err: any) {
      console.error('[ScanDrop.Service] Retention cleanup error:', err);
    }
  }
}

