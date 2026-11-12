import { Router, Request, Response } from 'express';
import crypto from 'node:crypto';
import { db } from '../../db/database';

export const superadminRouter = Router();

// Overview statistics
superadminRouter.get('/overview', (req: Request, res: Response) => {
  try {
    const totalShops = db.prepare('SELECT COUNT(*) as count FROM shops').get() as { count: number };
    const activeSubs = db.prepare("SELECT COUNT(*) as count FROM shop_subscriptions WHERE status = 'active'").get() as { count: number };
    const totalSubmissions = db.prepare('SELECT COUNT(*) as count FROM submissions').get() as { count: number };
    const activeDevices = db.prepare("SELECT COUNT(*) as count FROM receiver_devices WHERE status = 'active'").get() as { count: number };
    const totalStorage = db.prepare('SELECT COALESCE(SUM(total_size_bytes), 0) as total FROM submissions').get() as { total: number };

    res.json({
      success: true,
      stats: {
        totalShops: totalShops.count,
        activeSubscriptions: activeSubs.count,
        totalSubmissions: totalSubmissions.count,
        activeDevices: activeDevices.count,
        totalStorageBytes: totalStorage.total
      }
    });
  } catch (err: any) {
    res.status(500).json({ error: 'Failed to fetch overview stats', details: err.message });
  }
});

// List all shops with subscription & device count
superadminRouter.get('/shops', (req: Request, res: Response) => {
  try {
    const shops = db.prepare(`
      SELECT 
        s.*,
        sub.id as subscription_id,
        sub.status as subscription_status,
        sub.start_date,
        sub.expiry_date,
        p.id as plan_id,
        p.name as plan_name,
        p.tier as plan_tier,
        p.entitlements_json,
        (SELECT COUNT(*) FROM receiver_devices d WHERE d.shop_id = s.id AND d.status = 'active') as active_devices_count,
        (SELECT MAX(last_heartbeat_at) FROM receiver_devices d WHERE d.shop_id = s.id) as last_heartbeat
      FROM shops s
      LEFT JOIN shop_subscriptions sub ON s.id = sub.shop_id
      LEFT JOIN subscription_plans p ON sub.plan_id = p.id
      ORDER BY s.created_at DESC
    `).all() as any[];

    const formatted = shops.map(shop => ({
      ...shop,
      entitlements: shop.entitlements_json ? JSON.parse(shop.entitlements_json) : null
    }));

    res.json({ success: true, shops: formatted });
  } catch (err: any) {
    res.status(500).json({ error: 'Failed to list shops', details: err.message });
  }
});

// Create new shop
superadminRouter.post('/shops', (req: Request, res: Response) => {
  try {
    const { name, slug, tagline, ownerName, ownerEmail, ownerPhone, planId } = req.body;

    if (!name || !slug || !ownerName || !ownerEmail) {
      return res.status(400).json({ error: 'Name, slug, owner name, and email are required.' });
    }

    const cleanSlug = slug.toLowerCase().replace(/[^a-z0-9-]/g, '-');
    const existing = db.prepare('SELECT id FROM shops WHERE slug = ?').get(cleanSlug);
    if (existing) {
      return res.status(400).json({ error: 'Shop slug already exists. Please choose another.' });
    }

    const shopId = `shop_${crypto.randomBytes(6).toString('hex')}`;
    const pairingCode = `SD-PAIR-${Math.floor(1000 + Math.random() * 9000)}`;
    const now = new Date().toISOString();
    const pairingExpiresAt = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString(); // 30 days
    const chosenPlanId = planId || 'plan_free_trial';

    db.prepare(`
      INSERT INTO shops (id, slug, name, tagline, owner_name, owner_email, owner_phone, pairing_code, pairing_code_expires_at, created_at, updated_at)
      VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `).run(
      shopId,
      cleanSlug,
      name,
      tagline || '',
      ownerName,
      ownerEmail,
      ownerPhone || '',
      pairingCode,
      pairingExpiresAt,
      now,
      now
    );

    // Initial subscription
    const subExpiry = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString();
    db.prepare(`
      INSERT INTO shop_subscriptions (id, shop_id, plan_id, status, start_date, renewal_date, expiry_date, is_auto_renew, created_at, updated_at)
      VALUES (?, ?, ?, 'active', ?, ?, ?, 1, ?, ?)
    `).run(`sub_${shopId}`, shopId, chosenPlanId, now, subExpiry, subExpiry, now, now);

    // Audit log
    db.prepare(`
      INSERT INTO audit_events (id, shop_id, actor_type, event_type, description, created_at)
      VALUES (?, ?, 'founder', 'SHOP_CREATED', ?, ?)
    `).run(`evt_${Date.now()}`, shopId, `Shop '${name}' created with plan '${chosenPlanId}'`, now);

    res.json({
      success: true,
      shop: {
        id: shopId,
        slug: cleanSlug,
        name,
        pairingCode,
        pairingExpiresAt
      }
    });
  } catch (err: any) {
    res.status(500).json({ error: 'Failed to create shop', details: err.message });
  }
});

// Regenerate Pairing Code
superadminRouter.post('/shops/:shopId/pairing-code', (req: Request, res: Response) => {
  try {
    const { shopId } = req.params;
    const newCode = `SD-PAIR-${Math.floor(1000 + Math.random() * 9000)}`;
    const expiresAt = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(); // 7 days

    db.prepare(`
      UPDATE shops
      SET pairing_code = ?, pairing_code_expires_at = ?, updated_at = ?
      WHERE id = ?
    `).run(newCode, expiresAt, new Date().toISOString(), shopId);

    res.json({ success: true, pairingCode: newCode, expiresAt });
  } catch (err: any) {
    res.status(500).json({ error: 'Failed to regenerate pairing code' });
  }
});

// Update Subscription (Activate, Renew, Suspend, Revoke, Change Plan)
superadminRouter.post('/shops/:shopId/subscription', (req: Request, res: Response) => {
  try {
    const { shopId } = req.params;
    const { action, planId, durationDays, paymentReference, amountCents } = req.body;

    const currentSub = db.prepare('SELECT * FROM shop_subscriptions WHERE shop_id = ?').get(shopId) as any;
    if (!currentSub) {
      return res.status(404).json({ error: 'Subscription not found for shop' });
    }

    const now = new Date();
    let newStatus = currentSub.status;
    let newExpiry = new Date(currentSub.expiry_date);
    let newPlanId = planId || currentSub.plan_id;

    if (action === 'renew' || action === 'extend') {
      const days = durationDays || 30;
      const baseDate = newExpiry > now ? newExpiry : now;
      newExpiry = new Date(baseDate.getTime() + days * 24 * 60 * 60 * 1000);
      newStatus = 'active';

      if (paymentReference) {
        db.prepare(`
          INSERT INTO subscription_payments (id, shop_id, subscription_id, amount_cents, currency, payment_reference, status, created_at)
          VALUES (?, ?, ?, ?, 'USD', ?, 'verified', ?)
        `).run(
          `pay_${Date.now()}`,
          shopId,
          currentSub.id,
          amountCents || 0,
          paymentReference,
          now.toISOString()
        );
      }
    } else if (action === 'suspend') {
      newStatus = 'suspended';
    } else if (action === 'reactivate') {
      newStatus = 'active';
    } else if (action === 'revoke') {
      newStatus = 'revoked';
    }

    db.prepare(`
      UPDATE shop_subscriptions
      SET plan_id = ?, status = ?, expiry_date = ?, renewal_date = ?, updated_at = ?
      WHERE id = ?
    `).run(newPlanId, newStatus, newExpiry.toISOString(), newExpiry.toISOString(), now.toISOString(), currentSub.id);

    // Audit log
    db.prepare(`
      INSERT INTO audit_events (id, shop_id, actor_type, event_type, description, created_at)
      VALUES (?, ?, 'founder', 'SUBSCRIPTION_UPDATED', ?, ?)
    `).run(
      `evt_${Date.now()}`,
      shopId,
      `Action: ${action}, Plan: ${newPlanId}, Status: ${newStatus}, Expiry: ${newExpiry.toISOString()}`,
      now.toISOString()
    );

    res.json({
      success: true,
      subscription: {
        id: currentSub.id,
        planId: newPlanId,
        status: newStatus,
        expiryDate: newExpiry.toISOString()
      }
    });
  } catch (err: any) {
    res.status(500).json({ error: 'Failed to update subscription', details: err.message });
  }
});

// Device Management
superadminRouter.get('/shops/:shopId/devices', (req: Request, res: Response) => {
  try {
    const devices = db.prepare('SELECT * FROM receiver_devices WHERE shop_id = ? ORDER BY created_at DESC').all(req.params.shopId);
    res.json({ success: true, devices });
  } catch (err: any) {
    res.status(500).json({ error: 'Failed to list devices' });
  }
});

superadminRouter.post('/devices/:deviceId/revoke', (req: Request, res: Response) => {
  try {
    const { deviceId } = req.params;
    const now = new Date().toISOString();

    db.prepare("UPDATE receiver_devices SET status = 'revoked' WHERE id = ?").run(deviceId);

    db.prepare(`
      INSERT INTO audit_events (id, actor_type, event_type, description, created_at)
      VALUES (?, 'founder', 'DEVICE_REVOKED', ?, ?)
    `).run(`evt_${Date.now()}`, `Device ${deviceId} was revoked by administrator`, now);

    res.json({ success: true, message: `Device ${deviceId} has been revoked.` });
  } catch (err: any) {
    res.status(500).json({ error: 'Failed to revoke device' });
  }
});

// Plans
superadminRouter.get('/plans', (req: Request, res: Response) => {
  try {
    const plans = db.prepare('SELECT * FROM subscription_plans').all() as any[];
    const parsed = plans.map(p => ({
      ...p,
      entitlements: JSON.parse(p.entitlements_json)
    }));
    res.json({ success: true, plans: parsed });
  } catch (err: any) {
    res.status(500).json({ error: 'Failed to fetch plans' });
  }
});

// Audit events
superadminRouter.get('/audit-logs', (req: Request, res: Response) => {
  try {
    const logs = db.prepare('SELECT * FROM audit_events ORDER BY created_at DESC LIMIT 100').all();
    res.json({ success: true, logs });
  } catch (err: any) {
    res.status(500).json({ error: 'Failed to fetch audit logs' });
  }
});

