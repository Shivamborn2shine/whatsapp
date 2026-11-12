import { DatabaseSync } from 'node:sqlite';
import path from 'node:path';
import fs from 'node:fs';

const DB_DIR = path.resolve(process.cwd(), 'data');
if (!fs.existsSync(DB_DIR)) {
  fs.mkdirSync(DB_DIR, { recursive: true });
}

const dbPath = path.join(DB_DIR, 'scandrop_cloud.db');
export const db = new DatabaseSync(dbPath);

export function initDatabase() {
  db.exec(`
    PRAGMA foreign_keys = ON;

    CREATE TABLE IF NOT EXISTS shops (
      id TEXT PRIMARY KEY,
      slug TEXT UNIQUE NOT NULL,
      name TEXT NOT NULL,
      tagline TEXT,
      owner_name TEXT NOT NULL,
      owner_email TEXT NOT NULL,
      owner_phone TEXT,
      pairing_code TEXT,
      pairing_code_expires_at TEXT,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS staff_members (
      id TEXT PRIMARY KEY,
      shop_id TEXT NOT NULL,
      name TEXT NOT NULL,
      email TEXT UNIQUE NOT NULL,
      role TEXT NOT NULL DEFAULT 'staff',
      created_at TEXT NOT NULL,
      FOREIGN KEY (shop_id) REFERENCES shops(id) ON DELETE CASCADE
    );

    CREATE TABLE IF NOT EXISTS subscription_plans (
      id TEXT PRIMARY KEY,
      name TEXT NOT NULL,
      tier TEXT NOT NULL,
      billing_interval TEXT NOT NULL,
      price_cents INTEGER NOT NULL,
      currency TEXT NOT NULL DEFAULT 'USD',
      entitlements_json TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS shop_subscriptions (
      id TEXT PRIMARY KEY,
      shop_id TEXT NOT NULL,
      plan_id TEXT NOT NULL,
      status TEXT NOT NULL,
      start_date TEXT NOT NULL,
      renewal_date TEXT NOT NULL,
      expiry_date TEXT NOT NULL,
      is_auto_renew INTEGER NOT NULL DEFAULT 1,
      notes TEXT,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL,
      FOREIGN KEY (shop_id) REFERENCES shops(id) ON DELETE CASCADE,
      FOREIGN KEY (plan_id) REFERENCES subscription_plans(id)
    );

    CREATE TABLE IF NOT EXISTS subscription_payments (
      id TEXT PRIMARY KEY,
      shop_id TEXT NOT NULL,
      subscription_id TEXT NOT NULL,
      amount_cents INTEGER NOT NULL,
      currency TEXT NOT NULL DEFAULT 'USD',
      payment_reference TEXT NOT NULL,
      status TEXT NOT NULL,
      created_at TEXT NOT NULL,
      FOREIGN KEY (shop_id) REFERENCES shops(id) ON DELETE CASCADE,
      FOREIGN KEY (subscription_id) REFERENCES shop_subscriptions(id) ON DELETE CASCADE
    );

    CREATE TABLE IF NOT EXISTS receiver_devices (
      id TEXT PRIMARY KEY,
      shop_id TEXT NOT NULL,
      device_name TEXT NOT NULL,
      windows_version TEXT,
      app_version TEXT NOT NULL,
      status TEXT NOT NULL DEFAULT 'active',
      device_token TEXT UNIQUE,
      last_heartbeat_at TEXT NOT NULL,
      last_ip TEXT,
      created_at TEXT NOT NULL,
      FOREIGN KEY (shop_id) REFERENCES shops(id) ON DELETE CASCADE
    );

    CREATE TABLE IF NOT EXISTS device_entitlements (
      id TEXT PRIMARY KEY,
      device_id TEXT NOT NULL,
      shop_id TEXT NOT NULL,
      entitlements_json TEXT NOT NULL,
      version INTEGER NOT NULL DEFAULT 1,
      expires_at TEXT NOT NULL,
      created_at TEXT NOT NULL,
      FOREIGN KEY (device_id) REFERENCES receiver_devices(id) ON DELETE CASCADE,
      FOREIGN KEY (shop_id) REFERENCES shops(id) ON DELETE CASCADE
    );

    CREATE TABLE IF NOT EXISTS submissions (
      id TEXT PRIMARY KEY,
      shop_id TEXT NOT NULL,
      customer_name TEXT,
      service_category TEXT,
      instructions TEXT,
      file_count INTEGER NOT NULL DEFAULT 0,
      total_size_bytes INTEGER NOT NULL DEFAULT 0,
      status TEXT NOT NULL DEFAULT 'received',
      expires_at TEXT NOT NULL,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL,
      FOREIGN KEY (shop_id) REFERENCES shops(id) ON DELETE CASCADE
    );

    CREATE TABLE IF NOT EXISTS submission_files (
      id TEXT PRIMARY KEY,
      submission_id TEXT NOT NULL,
      shop_id TEXT NOT NULL,
      original_name TEXT NOT NULL,
      mime_type TEXT NOT NULL,
      file_extension TEXT NOT NULL,
      size_bytes INTEGER NOT NULL,
      sha256_hash TEXT NOT NULL,
      storage_path TEXT NOT NULL,
      status TEXT NOT NULL DEFAULT 'pending',
      created_at TEXT NOT NULL,
      expires_at TEXT NOT NULL,
      FOREIGN KEY (submission_id) REFERENCES submissions(id) ON DELETE CASCADE,
      FOREIGN KEY (shop_id) REFERENCES shops(id) ON DELETE CASCADE
    );

    CREATE TABLE IF NOT EXISTS file_deliveries (
      id TEXT PRIMARY KEY,
      file_id TEXT NOT NULL,
      device_id TEXT NOT NULL,
      delivered_at TEXT NOT NULL,
      verified_hash INTEGER NOT NULL DEFAULT 1,
      FOREIGN KEY (file_id) REFERENCES submission_files(id) ON DELETE CASCADE,
      FOREIGN KEY (device_id) REFERENCES receiver_devices(id) ON DELETE CASCADE
    );

    CREATE TABLE IF NOT EXISTS receipt_access (
      id TEXT PRIMARY KEY,
      submission_id TEXT NOT NULL,
      ip_address TEXT,
      user_agent TEXT,
      accessed_at TEXT NOT NULL,
      FOREIGN KEY (submission_id) REFERENCES submissions(id) ON DELETE CASCADE
    );

    CREATE TABLE IF NOT EXISTS notifications (
      id TEXT PRIMARY KEY,
      shop_id TEXT NOT NULL,
      device_id TEXT,
      title TEXT NOT NULL,
      message TEXT NOT NULL,
      is_read INTEGER NOT NULL DEFAULT 0,
      created_at TEXT NOT NULL,
      FOREIGN KEY (shop_id) REFERENCES shops(id) ON DELETE CASCADE
    );

    CREATE TABLE IF NOT EXISTS cleanup_jobs (
      id TEXT PRIMARY KEY,
      job_type TEXT NOT NULL,
      target_id TEXT NOT NULL,
      status TEXT NOT NULL,
      error_message TEXT,
      executed_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS audit_events (
      id TEXT PRIMARY KEY,
      shop_id TEXT,
      actor_type TEXT NOT NULL,
      actor_id TEXT,
      event_type TEXT NOT NULL,
      description TEXT NOT NULL,
      metadata_json TEXT,
      created_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS conversion_jobs (
      id TEXT PRIMARY KEY,
      shop_id TEXT NOT NULL,
      submission_id TEXT,
      operation TEXT NOT NULL,
      status TEXT NOT NULL,
      duration_ms INTEGER,
      error_details TEXT,
      created_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS derived_files (
      id TEXT PRIMARY KEY,
      shop_id TEXT NOT NULL,
      original_file_id TEXT,
      submission_id TEXT,
      display_name TEXT NOT NULL,
      output_format TEXT NOT NULL,
      local_path TEXT NOT NULL,
      size_bytes INTEGER NOT NULL,
      created_at TEXT NOT NULL,
      expires_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS printer_profiles (
      id TEXT PRIMARY KEY,
      shop_id TEXT NOT NULL,
      device_id TEXT NOT NULL,
      profile_name TEXT NOT NULL,
      printer_name TEXT NOT NULL,
      is_color INTEGER NOT NULL DEFAULT 0,
      paper_size TEXT NOT NULL,
      orientation TEXT NOT NULL,
      duplex TEXT NOT NULL,
      pages_per_sheet INTEGER NOT NULL DEFAULT 1,
      scale_mode TEXT NOT NULL DEFAULT 'fit',
      created_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS app_releases (
      id TEXT PRIMARY KEY,
      version TEXT NOT NULL,
      channel TEXT NOT NULL DEFAULT 'stable',
      installer_path TEXT NOT NULL,
      checksum_sha256 TEXT NOT NULL,
      is_mandatory INTEGER NOT NULL DEFAULT 0,
      release_notes TEXT,
      created_at TEXT NOT NULL
    );
  `);

  seedDefaultData();
}

function seedDefaultData() {
  const planCheck = db.prepare('SELECT COUNT(*) as count FROM subscription_plans').get() as { count: number };
  if (planCheck.count === 0) {
    const plans = [
      {
        id: 'plan_free_trial',
        name: 'Free / Trial',
        tier: 'free',
        interval: 'month',
        price: 0,
        entitlements: {
          max_devices: 1,
          max_monthly_submissions: 50,
          max_monthly_upload_bytes: 104857600, // 100MB
          max_file_size: 15728640, // 15MB
          batch_conversion_enabled: false,
          pdf_advanced_enabled: false,
          printer_profiles_enabled: false,
          multi_staff_enabled: false,
          receiving_enabled: true
        }
      },
      {
        id: 'plan_pro_monthly',
        name: 'Pro Monthly',
        tier: 'pro',
        interval: 'month',
        price: 2900,
        entitlements: {
          max_devices: 1,
          max_monthly_submissions: 1000,
          max_monthly_upload_bytes: 5368709120, // 5GB
          max_file_size: 52428800, // 50MB
          batch_conversion_enabled: true,
          pdf_advanced_enabled: true,
          printer_profiles_enabled: true,
          multi_staff_enabled: false,
          receiving_enabled: true
        }
      },
      {
        id: 'plan_business_monthly',
        name: 'Business Multi-PC',
        tier: 'business',
        interval: 'month',
        price: 6900,
        entitlements: {
          max_devices: 5,
          max_monthly_submissions: 10000,
          max_monthly_upload_bytes: 26843545600, // 25GB
          max_file_size: 104857600, // 100MB
          batch_conversion_enabled: true,
          pdf_advanced_enabled: true,
          printer_profiles_enabled: true,
          multi_staff_enabled: true,
          receiving_enabled: true
        }
      }
    ];

    const insertPlan = db.prepare(`
      INSERT INTO subscription_plans (id, name, tier, billing_interval, price_cents, currency, entitlements_json)
      VALUES (?, ?, ?, ?, ?, 'USD', ?)
    `);

    for (const p of plans) {
      insertPlan.run(p.id, p.name, p.tier, p.interval, p.price, JSON.stringify(p.entitlements));
    }
  }

  // Seed default shop for quickstart & testing
  const shopCheck = db.prepare('SELECT COUNT(*) as count FROM shops').get() as { count: number };
  if (shopCheck.count === 0) {
    const now = new Date().toISOString();
    const expiry = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString();
    const shopId = 'shop_quickprint_01';

    db.prepare(`
      INSERT INTO shops (id, slug, name, tagline, owner_name, owner_email, owner_phone, pairing_code, pairing_code_expires_at, created_at, updated_at)
      VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `).run(
      shopId,
      'quickprint',
      'QuickPrint Xerox & Stationery',
      'Fast prints & document solutions',
      'Ramesh Kumar',
      'ramesh@quickprint.local',
      '+91 98765 43210',
      'SD-PAIR-7890',
      expiry,
      now,
      now
    );

    // Assign Pro Plan subscription
    db.prepare(`
      INSERT INTO shop_subscriptions (id, shop_id, plan_id, status, start_date, renewal_date, expiry_date, is_auto_renew, notes, created_at, updated_at)
      VALUES (?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?)
    `).run(
      'sub_quickprint_pro',
      shopId,
      'plan_pro_monthly',
      'active',
      now,
      expiry,
      expiry,
      'Founder initial pilot license',
      now,
      now
    );

    // Audit event
    db.prepare(`
      INSERT INTO audit_events (id, shop_id, actor_type, actor_id, event_type, description, created_at)
      VALUES (?, ?, 'founder', 'founder_system', 'SHOP_CREATED', 'Demo shop QuickPrint seeded with Pro license', ?)
    `).run('evt_seed_01', shopId, now);
  }
}

