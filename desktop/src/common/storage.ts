import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';

export function getScanDropRootDir(): string {
  const custom = process.env.SCANDROP_DATA_DIR;
  if (custom) return custom;
  return path.join(os.homedir(), 'ScanDrop');
}

export interface StorageDirs {
  root: string;
  incoming: string;
  processing: string;
  completed: string;
  derived: string;
  temporary: string;
  quarantine: string;
}

export function initStorageDirectories(): StorageDirs {
  const root = getScanDropRootDir();
  const dirs: StorageDirs = {
    root,
    incoming: path.join(root, 'Incoming'),
    processing: path.join(root, 'Processing'),
    completed: path.join(root, 'Completed'),
    derived: path.join(root, 'Derived'),
    temporary: path.join(root, 'Temporary'),
    quarantine: path.join(root, 'Quarantine')
  };

  for (const dir of Object.values(dirs)) {
    if (!fs.existsSync(dir)) {
      fs.mkdirSync(dir, { recursive: true });
    }
  }

  return dirs;
}

export function getSubmissionFolder(submissionId: string, status: 'incoming' | 'processing' | 'completed' = 'incoming'): string {
  const dirs = initStorageDirectories();
  let base = dirs.incoming;
  if (status === 'processing') base = dirs.processing;
  if (status === 'completed') base = dirs.completed;

  const folder = path.join(base, submissionId);
  if (!fs.existsSync(folder)) {
    fs.mkdirSync(folder, { recursive: true });
  }
  return folder;
}

