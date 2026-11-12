import { Router, Request, Response } from 'express';
import multer from 'multer';
import path from 'node:path';
import fs from 'node:fs';
import { db } from '../../db/database';
import { getSubmissionStorageDir, computeSha256 } from '../../services/storage';

export const submissionsRouter = Router();

const ALLOWED_EXTS = ['.pdf', '.jpg', '.jpeg', '.png', '.docx'];

// Temp upload storage before moving to submission folder
const tempStorage = multer.diskStorage({
  destination: (req, file, cb) => {
    const tmp = path.resolve(process.cwd(), 'data', 'storage', 'temp');
    if (!fs.existsSync(tmp)) fs.mkdirSync(tmp, { recursive: true });
    cb(null, tmp);
  },
  filename: (req, file, cb) => {
    const unique = `${Date.now()}-${Math.round(Math.random() * 1e9)}`;
    const ext = path.extname(file.originalname).toLowerCase();
    cb(null, `${unique}${ext}`);
  }
});

const upload = multer({
  storage: tempStorage,
  limits: {
    fileSize: 100 * 1024 * 1024 // 100 MB max per file
  },
  fileFilter: (req, file, cb) => {
    const ext = path.extname(file.originalname).toLowerCase();
    if (!ALLOWED_EXTS.includes(ext)) {
      return cb(new Error(`Unsupported file type: ${ext}. Allowed: PDF, JPG, PNG, DOCX`));
    }
    cb(null, true);
  }
});

function generateReceiptId(): string {
  // Generate random 4-digit ID or sequence, e.g. SD-1042
  const randNum = Math.floor(1000 + Math.random() * 9000);
  return `SD-${randNum}`;
}

// Customer Upload Endpoint
submissionsRouter.post('/upload', upload.array('files', 10), async (req: Request, res: Response) => {
  try {
    const { shopSlug, customerName, serviceCategory, instructions } = req.body;
    const files = req.files as Express.Multer.File[];

    if (!files || files.length === 0) {
      return res.status(400).json({ error: 'At least one file must be selected.' });
    }

    if (!shopSlug) {
      return res.status(400).json({ error: 'Shop identifier is required.' });
    }

    const shop = db.prepare('SELECT id, name FROM shops WHERE slug = ?').get(shopSlug) as any;
    if (!shop) {
      return res.status(404).json({ error: 'Shop not found.' });
    }

    // Verify shop subscription allows receiving
    const subscription = db.prepare(`
      SELECT s.status, p.entitlements_json
      FROM shop_subscriptions s
      JOIN subscription_plans p ON s.plan_id = p.id
      WHERE s.shop_id = ?
    `).get(shop.id) as any;

    if (!subscription || subscription.status === 'revoked' || subscription.status === 'suspended') {
      return res.status(403).json({ error: 'Shop receiving is currently unavailable.' });
    }

    const entitlements = JSON.parse(subscription.entitlements_json);
    if (!entitlements.receiving_enabled) {
      return res.status(403).json({ error: 'Shop has reached upload receiving limit.' });
    }

    let receiptId = generateReceiptId();
    // Ensure uniqueness
    while (db.prepare('SELECT id FROM submissions WHERE id = ?').get(receiptId)) {
      receiptId = generateReceiptId();
    }

    const now = new Date();
    const expiresAt = new Date(now.getTime() + 24 * 60 * 60 * 1000); // 24-hour retention

    const targetDir = getSubmissionStorageDir(receiptId);
    let totalSizeBytes = 0;
    const fileRecords: any[] = [];

    for (let i = 0; i < files.length; i++) {
      const f = files[i];
      const ext = path.extname(f.originalname).toLowerCase();
      const sanitizedName = path.basename(f.originalname).replace(/[^a-zA-Z0-9._-]/g, '_');
      const targetFilePath = path.join(targetDir, sanitizedName);

      // Move from temp to permanent submission storage
      fs.renameSync(f.path, targetFilePath);

      const hash = await computeSha256(targetFilePath);
      totalSizeBytes += f.size;

      const fileId = `file_${receiptId}_${i + 1}`;
      fileRecords.push({
        id: fileId,
        submissionId: receiptId,
        shopId: shop.id,
        originalName: f.originalname,
        mimeType: f.mimetype,
        fileExtension: ext,
        sizeBytes: f.size,
        sha256Hash: hash,
        storagePath: targetFilePath
      });
    }

    // Insert Submission
    db.prepare(`
      INSERT INTO submissions (id, shop_id, customer_name, service_category, instructions, file_count, total_size_bytes, status, expires_at, created_at, updated_at)
      VALUES (?, ?, ?, ?, ?, ?, ?, 'received', ?, ?, ?)
    `).run(
      receiptId,
      shop.id,
      customerName || 'Anonymous Customer',
      serviceCategory || 'Standard Print',
      instructions || '',
      files.length,
      totalSizeBytes,
      expiresAt.toISOString(),
      now.toISOString(),
      now.toISOString()
    );

    // Insert Files
    const insertFile = db.prepare(`
      INSERT INTO submission_files (id, submission_id, shop_id, original_name, mime_type, file_extension, size_bytes, sha256_hash, storage_path, status, created_at, expires_at)
      VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'pending', ?, ?)
    `);

    for (const f of fileRecords) {
      insertFile.run(
        f.id,
        f.submissionId,
        f.shopId,
        f.originalName,
        f.mimeType,
        f.fileExtension,
        f.sizeBytes,
        f.sha256Hash,
        f.storagePath,
        now.toISOString(),
        expiresAt.toISOString()
      );
    }

    // Audit log
    db.prepare(`
      INSERT INTO audit_events (id, shop_id, actor_type, event_type, description, created_at)
      VALUES (?, ?, 'customer', 'SUBMISSION_RECEIVED', ?, ?)
    `).run(
      `evt_${Date.now()}`,
      shop.id,
      `Received submission ${receiptId} (${files.length} files, ${(totalSizeBytes / 1024 / 1024).toFixed(2)} MB)`,
      now.toISOString()
    );

    return res.json({
      success: true,
      receiptId,
      shopName: shop.name,
      fileCount: files.length,
      totalSizeBytes,
      expiresAt: expiresAt.toISOString(),
      status: 'received'
    });
  } catch (err: any) {
    console.error('Upload submission error:', err);
    return res.status(500).json({ error: 'File upload failed', details: err.message });
  }
});

// Device Sync Endpoint: Polled by Windows Service
submissionsRouter.get('/sync', (req: Request, res: Response) => {
  try {
    const deviceToken = (req.headers['x-device-token'] as string) || (req.query.deviceToken as string);
    if (!deviceToken) {
      return res.status(401).json({ error: 'Device token required' });
    }

    const device = db.prepare('SELECT shop_id, status FROM receiver_devices WHERE device_token = ?').get(deviceToken) as any;
    if (!device || device.status === 'revoked') {
      return res.status(403).json({ error: 'Device unauthorized or revoked' });
    }

    const now = new Date().toISOString();

    // Query active submissions for this shop within 24h retention
    const submissions = db.prepare(`
      SELECT * FROM submissions
      WHERE shop_id = ? AND expires_at > ? AND status != 'expired'
      ORDER BY created_at DESC
      LIMIT 100
    `).all(device.shop_id, now) as any[];

    // Fetch files for each submission
    const result = submissions.map(sub => {
      const files = db.prepare(`
        SELECT id, original_name, mime_type, file_extension, size_bytes, sha256_hash, status, created_at, expires_at
        FROM submission_files
        WHERE submission_id = ?
      `).all(sub.id);

      return {
        ...sub,
        files
      };
    });

    return res.json({
      success: true,
      shopId: device.shop_id,
      timestamp: now,
      submissions: result
    });
  } catch (err: any) {
    console.error('Device sync error:', err);
    return res.status(500).json({ error: 'Sync failed', details: err.message });
  }
});

// File Download Endpoint for Windows Service
submissionsRouter.get('/files/:fileId/download', (req: Request, res: Response) => {
  try {
    const deviceToken = (req.headers['x-device-token'] as string) || (req.query.deviceToken as string);
    if (!deviceToken) {
      return res.status(401).json({ error: 'Device token required' });
    }

    const device = db.prepare('SELECT shop_id, status FROM receiver_devices WHERE device_token = ?').get(deviceToken) as any;
    if (!device || device.status === 'revoked') {
      return res.status(403).json({ error: 'Unauthorized device' });
    }

    const file = db.prepare(`
      SELECT f.*, s.status as sub_status, s.expires_at
      FROM submission_files f
      JOIN submissions s ON f.submission_id = s.id
      WHERE f.id = ? AND f.shop_id = ?
    `).get(req.params.fileId, device.shop_id) as any;

    if (!file) {
      return res.status(404).json({ error: 'File not found' });
    }

    if (new Date(file.expires_at) < new Date() || file.sub_status === 'expired') {
      return res.status(410).json({ error: 'File retention period expired' });
    }

    if (!fs.existsSync(file.storage_path)) {
      return res.status(404).json({ error: 'File not found on cloud storage' });
    }

    res.setHeader('Content-Type', file.mime_type || 'application/octet-stream');
    res.setHeader('Content-Disposition', `attachment; filename="${file.original_name}"`);
    res.setHeader('X-File-Sha256', file.sha256_hash);
    res.setHeader('X-File-Size', file.size_bytes.toString());

    const stream = fs.createReadStream(file.storage_path);
    stream.pipe(res);
  } catch (err: any) {
    console.error('File download error:', err);
    return res.status(500).json({ error: 'Failed to download file', details: err.message });
  }
});

// Update Status (Processing, Completed)
submissionsRouter.patch('/:submissionId/status', (req: Request, res: Response) => {
  try {
    const { submissionId } = req.params;
    const { status } = req.body;
    const deviceToken = (req.headers['x-device-token'] as string) || req.body.deviceToken;

    if (!['received', 'processing', 'completed'].includes(status)) {
      return res.status(400).json({ error: 'Invalid status' });
    }

    const device = db.prepare('SELECT shop_id FROM receiver_devices WHERE device_token = ?').get(deviceToken) as any;
    if (!device) {
      return res.status(401).json({ error: 'Device unauthorized' });
    }

    const now = new Date().toISOString();
    db.prepare(`
      UPDATE submissions
      SET status = ?, updated_at = ?
      WHERE id = ? AND shop_id = ?
    `).run(status, now, submissionId, device.shop_id);

    return res.json({ success: true, submissionId, status });
  } catch (err: any) {
    console.error('Update status error:', err);
    return res.status(500).json({ error: 'Failed to update status' });
  }
});

// Customer Status Check
submissionsRouter.get('/status/:receiptId', (req: Request, res: Response) => {
  try {
    const { receiptId } = req.params;
    const submission = db.prepare(`
      SELECT s.id, s.customer_name, s.service_category, s.file_count, s.total_size_bytes, s.status, s.expires_at, s.created_at, sh.name as shop_name
      FROM submissions s
      JOIN shops sh ON s.shop_id = sh.id
      WHERE s.id = ?
    `).get(receiptId) as any;

    if (!submission) {
      return res.status(404).json({ error: 'Receipt not found' });
    }

    const now = new Date();
    const expiry = new Date(submission.expires_at);
    const remainingMs = Math.max(0, expiry.getTime() - now.getTime());
    const remainingHours = (remainingMs / (1000 * 60 * 60)).toFixed(1);

    return res.json({
      success: true,
      receipt: {
        ...submission,
        remainingHours,
        isExpired: remainingMs <= 0
      }
    });
  } catch (err: any) {
    return res.status(500).json({ error: 'Failed to retrieve receipt' });
  }
});

