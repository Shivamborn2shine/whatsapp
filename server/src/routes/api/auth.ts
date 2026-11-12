import { Router, Request, Response } from 'express';
import crypto from 'node:crypto';
import { db } from '../../db/database';

export const authRouter = Router();

authRouter.post('/pair', (req: Request, res: Response) => {
  try {
    const { pairingCode, deviceName, windowsVersion, appVersion } = req.body;

    if (!pairingCode) {
      return res.status(400).json({ error: 'Pairing code is required' });
    }

    const shop = db.prepare(`
      SELECT id, name, slug, owner_name, pairing_code_expires_at
      FROM shops
      WHERE pairing_code = ?
    `).get(pairingCode) as any;

    if (!shop) {
      return res.status(404).json({ error: 'Invalid pairing code' });
    }

    if (shop.pairing_code_expires_at && new Date(shop.pairing_code_expires_at) < new Date()) {
      return res.status(400).json({ error: 'Pairing code has expired' });
    }

    // Get current subscription & plan entitlements
    const subscription = db.prepare(`
      SELECT s.*, p.tier, p.entitlements_json
      FROM shop_subscriptions s
      JOIN subscription_plans p ON s.plan_id = p.id
      WHERE s.shop_id = ?
    `).get(shop.id) as any;

    if (!subscription || subscription.status === 'revoked' || subscription.status === 'suspended') {
      return res.status(403).json({ error: 'Shop subscription is not active or suspended' });
    }

    const entitlements = JSON.parse(subscription.entitlements_json);

    // Verify device limit
    const activeDeviceCount = db.prepare(`
      SELECT COUNT(*) as count FROM receiver_devices
      WHERE shop_id = ? AND status = 'active'
    `).get(shop.id) as { count: number };

    if (activeDeviceCount.count >= entitlements.max_devices) {
      return res.status(403).json({
        error: `Device limit reached. Current plan allows ${entitlements.max_devices} active device(s).`
      });
    }

    const deviceId = `dev_${crypto.randomBytes(6).toString('hex')}`;
    const deviceToken = `sdt_${crypto.randomBytes(24).toString('hex')}`;
    const now = new Date().toISOString();
    const clientIp = req.ip || req.socket.remoteAddress || '127.0.0.1';

    db.prepare(`
      INSERT INTO receiver_devices (id, shop_id, device_name, windows_version, app_version, status, device_token, last_heartbeat_at, last_ip, created_at)
      VALUES (?, ?, ?, ?, ?, 'active', ?, ?, ?, ?)
    `).run(
      deviceId,
      shop.id,
      deviceName || 'Windows PC',
      windowsVersion || 'Windows 11',
      appVersion || '3.0.0',
      deviceToken,
      now,
      clientIp,
      now
    );

    // Record entitlement snapshot
    const entitlementExpiry = new Date(Date.now() + 72 * 60 * 60 * 1000).toISOString(); // 72 hours offline grace
    db.prepare(`
      INSERT INTO device_entitlements (id, device_id, shop_id, entitlements_json, version, expires_at, created_at)
      VALUES (?, ?, ?, ?, 1, ?, ?)
    `).run(`ent_${deviceId}`, deviceId, shop.id, subscription.entitlements_json, entitlementExpiry, now);

    // Audit log
    db.prepare(`
      INSERT INTO audit_events (id, shop_id, actor_type, actor_id, event_type, description, created_at)
      VALUES (?, ?, 'shop_staff', ?, 'DEVICE_PAIRED', ?, ?)
    `).run(
      `evt_${Date.now()}`,
      shop.id,
      deviceId,
      `New Windows device '${deviceName}' successfully paired to shop '${shop.name}'.`,
      now
    );

    return res.json({
      success: true,
      deviceId,
      deviceToken,
      shop: {
        id: shop.id,
        name: shop.name,
        slug: shop.slug,
        ownerName: shop.owner_name
      },
      subscription: {
        status: subscription.status,
        tier: subscription.tier,
        expiryDate: subscription.expiry_date,
        entitlements
      }
    });
  } catch (err: any) {
    console.error('Pairing error:', err);
    return res.status(500).json({ error: 'Internal pairing error', details: err.message });
  }
});

authRouter.post('/heartbeat', (req: Request, res: Response) => {
  try {
    const deviceToken = (req.headers['x-device-token'] as string) || req.body.deviceToken;
    const { appVersion, windowsVersion } = req.body;

    if (!deviceToken) {
      return res.status(401).json({ error: 'Device token required' });
    }

    const device = db.prepare(`
      SELECT d.*, s.status as sub_status, s.expiry_date, p.tier, p.entitlements_json, sh.name as shop_name
      FROM receiver_devices d
      JOIN shops sh ON d.shop_id = sh.id
      JOIN shop_subscriptions s ON d.shop_id = s.shop_id
      JOIN subscription_plans p ON s.plan_id = p.id
      WHERE d.device_token = ?
    `).get(deviceToken) as any;

    if (!device) {
      return res.status(401).json({ error: 'Unrecognized device token' });
    }

    if (device.status === 'revoked') {
      return res.status(403).json({ error: 'Device has been revoked by shop or administrator', status: 'revoked' });
    }

    const now = new Date().toISOString();
    const clientIp = req.ip || req.socket.remoteAddress || '127.0.0.1';

    db.prepare(`
      UPDATE receiver_devices
      SET last_heartbeat_at = ?, last_ip = ?, app_version = COALESCE(?, app_version), windows_version = COALESCE(?, windows_version)
      WHERE id = ?
    `).run(now, clientIp, appVersion || null, windowsVersion || null, device.id);

    const entitlements = JSON.parse(device.entitlements_json);

    return res.json({
      success: true,
      deviceId: device.id,
      shopId: device.shop_id,
      shopName: device.shop_name,
      subscription: {
        status: device.sub_status,
        tier: device.tier,
        expiryDate: device.expiry_date,
        entitlements
      },
      serverTime: now
    });
  } catch (err: any) {
    console.error('Heartbeat error:', err);
    return res.status(500).json({ error: 'Heartbeat error', details: err.message });
  }
});

