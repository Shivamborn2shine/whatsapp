import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const UPLOAD_ROOT = path.resolve(process.cwd(), 'data', 'storage', 'cloud');

if (!fs.existsSync(UPLOAD_ROOT)) {
  fs.mkdirSync(UPLOAD_ROOT, { recursive: true });
}

export function getSubmissionStorageDir(submissionId: string): string {
  const dir = path.join(UPLOAD_ROOT, submissionId);
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  return dir;
}

export function computeSha256(filePath: string): Promise<string> {
  return new Promise((resolve, reject) => {
    const hash = crypto.createHash('sha256');
    const stream = fs.createReadStream(filePath);
    stream.on('data', data => hash.update(data));
    stream.on('end', () => resolve(hash.digest('hex')));
    stream.on('error', err => reject(err));
  });
}

export function deleteSubmissionStorage(submissionId: string): boolean {
  try {
    const dir = path.join(UPLOAD_ROOT, submissionId);
    if (fs.existsSync(dir)) {
      fs.rmSync(dir, { recursive: true, force: true });
      return true;
    }
  } catch (err) {
    console.error(`Failed to delete storage for submission ${submissionId}:`, err);
  }
  return false;
}

