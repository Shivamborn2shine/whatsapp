import { DatabaseSync } from 'node:sqlite';
import path from 'node:path';
import fs from 'node:fs';
import { getScanDropRootDir } from './storage';

let localDb: DatabaseSync | null = null;

export function getLocalDb(): DatabaseSync {
  if (localDb) return localDb;

  const root = getScanDropRootDir();
  if (!fs.existsSync(root)) {
    fs.mkdirSync(root, { recursive: true });
  }

  const dbPath = path.join(root, 'scandrop_local.db');
  localDb = new DatabaseSync(dbPath);

  localDb.exec(`
    CREATE TABLE IF NOT EXISTS local_config (
      key TEXT PRIMARY KEY,
      value TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS local_submissions (
      id TEXT PRIMARY KEY,
      shop_id TEXT NOT NULL,
      customer_name TEXT,
      service_category TEXT,
      instructions TEXT,
      status TEXT NOT NULL DEFAULT 'incoming', -- incoming, processing, completed, expired
      file_count INTEGER NOT NULL DEFAULT 0,
      total_size_bytes INTEGER NOT NULL DEFAULT 0,
      local_folder TEXT NOT NULL,
      expires_at TEXT NOT NULL,
      created_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS local_files (
      id TEXT PRIMARY KEY,
      submission_id TEXT NOT NULL,
      original_name TEXT NOT NULL,
      file_extension TEXT NOT NULL,
      size_bytes INTEGER NOT NULL,
      sha256_hash TEXT NOT NULL,
      local_path TEXT NOT NULL,
      status TEXT NOT NULL DEFAULT 'synced',
      expires_at TEXT NOT NULL,
      created_at TEXT NOT NULL,
      FOREIGN KEY (submission_id) REFERENCES local_submissions(id) ON DELETE CASCADE
    );

    CREATE TABLE IF NOT EXISTS local_derived_files (
      id TEXT PRIMARY KEY,
      submission_id TEXT,
      original_file_id TEXT,
      display_name TEXT NOT NULL,
      output_format TEXT NOT NULL,
      local_path TEXT NOT NULL,
      size_bytes INTEGER NOT NULL DEFAULT 0,
      created_at TEXT NOT NULL,
      expires_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS local_printer_profiles (
      id TEXT PRIMARY KEY,
      profile_name TEXT NOT NULL,
      printer_name TEXT NOT NULL,
      settings_json TEXT NOT NULL,
      created_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS local_history (
      id TEXT PRIMARY KEY,
      action_type TEXT NOT NULL,
      description TEXT NOT NULL,
      created_at TEXT NOT NULL
    );
  `);

  return localDb;
}

export function getConfigValue(key: string): string | null {
  const db = getLocalDb();
  const row = db.prepare('SELECT value FROM local_config WHERE key = ?').get(key) as { value: string } | undefined;
  return row ? row.value : null;
}

export function setConfigValue(key: string, value: string): void {
  const db = getLocalDb();
  db.prepare(`
    INSERT INTO local_config (key, value)
    VALUES (?, ?)
    ON CONFLICT(key) DO UPDATE SET value = excluded.value
  `).run(key, value);
}

export function logLocalAction(actionType: string, description: string): void {
  const db = getLocalDb();
  const id = `act_${Date.now()}_${Math.floor(Math.random() * 1000)}`;
  db.prepare(`
    INSERT INTO local_history (id, action_type, description, created_at)
    VALUES (?, ?, ?, ?)
  `).run(id, actionType, description, new Date().toISOString());
}

