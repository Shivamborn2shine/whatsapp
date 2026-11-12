import { db } from '../db/database';
import { deleteSubmissionStorage } from './storage';

export function startRetentionWorker(intervalMinutes: number = 5) {
  console.log(`[Retention Worker] Started with interval ${intervalMinutes} min. Auto-purging files > 24 hours.`);

  const runCleanup = () => {
    try {
      const now = new Date().toISOString();
      const expiredSubmissions = db.prepare(`
        SELECT id, shop_id, status FROM submissions
        WHERE expires_at <= ? AND status != 'expired'
      `).all(now) as Array<{ id: string; shop_id: string; status: string }>;

      if (expiredSubmissions.length > 0) {
        console.log(`[Retention Worker] Found ${expiredSubmissions.length} expired submissions to purge.`);
      }

      for (const sub of expiredSubmissions) {
        deleteSubmissionStorage(sub.id);

        db.prepare(`
          UPDATE submissions
          SET status = 'expired', updated_at = ?
          WHERE id = ?
        `).run(now, sub.id);

        db.prepare(`
          UPDATE submission_files
          SET status = 'deleted'
          WHERE submission_id = ?
        `).run(sub.id);

        db.prepare(`
          INSERT INTO cleanup_jobs (id, job_type, target_id, status, executed_at)
          VALUES (?, 'cloud_retention_purge', ?, 'success', ?)
        `).run(`clean_${Date.now()}_${sub.id}`, sub.id, now);

        db.prepare(`
          INSERT INTO audit_events (id, shop_id, actor_type, event_type, description, created_at)
          VALUES (?, ?, 'system', 'RETENTION_PURGED', ?, ?)
        `).run(
          `evt_${Date.now()}_${sub.id}`,
          sub.shop_id,
          `Submission ${sub.id} 24-hour retention period reached. Cloud files securely deleted.`,
          now
        );
      }
    } catch (err) {
      console.error('[Retention Worker] Error running retention cleanup:', err);
    }
  };

  runCleanup();
  setInterval(runCleanup, intervalMinutes * 60 * 1000);
}

