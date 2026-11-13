import { getStore } from '@netlify/blobs';
import crypto from 'node:crypto';

export const config = {
  path: '/api/*'
};

const PLANS = [
  {
    id: 'plan_free_trial',
    name: 'Free / Trial',
    tier: 'free',
    priceText: '$0 / 30 days',
    maxDevices: 1,
    maxMonthlySubmissions: 50,
    batchConversion: false,
    pdfAdvanced: false,
    printerProfiles: false
  },
  {
    id: 'plan_pro_monthly',
    name: 'Pro Monthly',
    tier: 'pro',
    priceText: '$29 / month',
    maxDevices: 1,
    maxMonthlySubmissions: 1000,
    batchConversion: true,
    pdfAdvanced: true,
    printerProfiles: true
  },
  {
    id: 'plan_business_monthly',
    name: 'Business Multi-PC',
    tier: 'business',
    priceText: '$69 / month',
    maxDevices: 5,
    maxMonthlySubmissions: 10000,
    batchConversion: true,
    pdfAdvanced: true,
    printerProfiles: true
  }
];

function getDefaultState() {
  const now = new Date();
  const expiry30 = new Date(now.getTime() + 30 * 24 * 60 * 60 * 1000).toISOString();
  return {
    shops: [
      {
        id: 'shop_quickprint_01',
        slug: 'quickprint',
        name: 'QuickPrint Xerox & Stationery',
        tagline: 'Scan. Drop. Done.',
        ownerName: 'Ramesh Kumar',
        ownerEmail: 'ramesh@quickprint.local',
        ownerPhone: '+91 98765 43210',
        planId: 'plan_pro_monthly',
        subscriptionStatus: 'active',
        pairingCode: 'SD-PAIR-7890',
        expiryDate: expiry30,
        createdAt: now.toISOString()
      }
    ],
    devices: [
      {
        id: 'dev_counter_01',
        shopId: 'shop_quickprint_01',
        deviceName: 'Shop Counter PC 1',
        windowsVersion: 'Windows 11 Pro',
        appVersion: '3.0.0',
        status: 'active',
        lastHeartbeatAt: now.toISOString(),
        lastIp: 'Cloud Sync'
      }
    ],
    submissions: [],
    derivedFiles: [],
    printerProfiles: [
      { id: 'prof_1', name: 'A4 Black & White', paperSize: 'A4', color: false, orientation: 'portrait', duplex: 'simplex' },
      { id: 'prof_2', name: 'A4 Full Color', paperSize: 'A4', color: true, orientation: 'portrait', duplex: 'simplex' },
      { id: 'prof_3', name: 'Photo 4x6 Glossy', paperSize: 'Photo4x6', color: true, orientation: 'portrait', duplex: 'simplex' }
    ],
    auditEvents: [
      {
        id: 'evt_seed_1',
        shopId: 'shop_quickprint_01',
        actorType: 'founder',
        eventType: 'SHOP_CREATED',
        description: 'QuickPrint Xerox & Stationery initialized with Pro Monthly plan',
        createdAt: now.toISOString()
      }
    ]
  };
}

async function loadCloudState() {
  const store = getStore('scandrop-state');
  let state = await store.get('main', { type: 'json' });
  if (!state) {
    state = getDefaultState();
    await store.setJSON('main', state);
    return state;
  }

  // Enforce 24-hour retention cleanup
  const nowIso = new Date().toISOString();
  let changed = false;
  const filesStore = getStore('scandrop-files');

  for (const sub of state.submissions || []) {
    if (sub.status !== 'expired' && sub.expiresAt <= nowIso) {
      sub.status = 'expired';
      for (const f of sub.files || []) {
        try { await filesStore.delete(f.id); } catch {}
      }
      state.auditEvents.unshift({
        id: 'evt_' + Date.now() + '_' + sub.id,
        shopId: sub.shopId,
        actorType: 'system',
        eventType: 'RETENTION_PURGED',
        description: `Submission ${sub.id} reached 24-hour retention limit and was purged.`,
        createdAt: nowIso
      });
      changed = true;
    }
  }

  if (changed) {
    await store.setJSON('main', state);
  }
  return state;
}

async function saveCloudState(state) {
  const store = getStore('scandrop-state');
  if (state.auditEvents && state.auditEvents.length > 200) {
    state.auditEvents.length = 200;
  }
  await store.setJSON('main', state);
}

function addAudit(state, shopId, actorType, eventType, description) {
  state.auditEvents.unshift({
    id: 'evt_' + Date.now() + '_' + Math.floor(Math.random() * 1000),
    shopId: shopId || null,
    actorType,
    eventType,
    description,
    createdAt: new Date().toISOString()
  });
}

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      'Content-Type': 'application/json',
      'Access-Control-Allow-Origin': '*'
    }
  });
}

export default async (req, context) => {
  const url = new URL(req.url);
  const route = url.pathname.replace(/^\/api/, '');

  if (req.method === 'OPTIONS') {
    return new Response(null, {
      status: 204,
      headers: {
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
        'Access-Control-Allow-Headers': 'Content-Type'
      }
    });
  }

  try {
    // 1. GET /api/state
    if (route === '/state' && req.method === 'GET') {
      const state = await loadCloudState();
      return jsonResponse({ success: true, plans: PLANS, state });
    }

    // 2. GET /api/shop?slug=...
    if (route === '/shop' && req.method === 'GET') {
      const slug = url.searchParams.get('slug');
      const state = await loadCloudState();
      const shop = state.shops.find(s => s.slug === slug) || state.shops[0];
      return jsonResponse({ success: true, shop });
    }

    // 3. POST /api/upload (Customer Upload)
    if (route === '/upload' && req.method === 'POST') {
      const formData = await req.formData();
      const shopId = formData.get('shopId');
      const customerName = (formData.get('customerName') || '').toString().trim() || 'Anonymous';
      const instructions = (formData.get('instructions') || '').toString().trim();
      const uploadedFiles = formData.getAll('files');

      if (!uploadedFiles || uploadedFiles.length === 0) {
        return jsonResponse({ success: false, error: 'No files provided.' }, 400);
      }

      const state = await loadCloudState();
      const shop = state.shops.find(s => s.id === shopId) || state.shops[0];
      if (!shop) {
        return jsonResponse({ success: false, error: 'Shop not found.' }, 404);
      }
      if (shop.subscriptionStatus === 'suspended' || shop.subscriptionStatus === 'revoked') {
        return jsonResponse({ success: false, error: 'This shop is currently not accepting file uploads.' }, 403);
      }

      let receiptId = 'SD-' + Math.floor(1000 + Math.random() * 9000);
      while (state.submissions.some(s => s.id === receiptId)) {
        receiptId = 'SD-' + Math.floor(1000 + Math.random() * 9000);
      }

      const now = new Date();
      const expiresAt = new Date(now.getTime() + 24 * 60 * 60 * 1000).toISOString();
      const filesStore = getStore('scandrop-files');
      const fileRecords = [];
      let totalSizeBytes = 0;

      for (let i = 0; i < uploadedFiles.length; i++) {
        const f = uploadedFiles[i];
        const fileId = `file_${receiptId}_${i + 1}_${Date.now()}`;
        const buf = Buffer.from(await f.arrayBuffer());
        const sha256 = crypto.createHash('sha256').update(buf).digest('hex');
        const ext = '.' + (f.name.split('.').pop() || '').toLowerCase();

        await filesStore.set(fileId, buf, {
          metadata: {
            originalName: f.name,
            mimeType: f.type || 'application/octet-stream'
          }
        });

        fileRecords.push({
          id: fileId,
          submissionId: receiptId,
          originalName: f.name,
          mimeType: f.type || 'application/octet-stream',
          fileExtension: ext,
          sizeBytes: buf.length,
          sha256Hash: sha256,
          status: 'synced',
          createdAt: now.toISOString(),
          expiresAt
        });
        totalSizeBytes += buf.length;
      }

      const submission = {
        id: receiptId,
        shopId: shop.id,
        customerName,
        instructions,
        status: 'incoming',
        files: fileRecords,
        totalSizeBytes,
        createdAt: now.toISOString(),
        expiresAt
      };

      state.submissions.unshift(submission);
      addAudit(state, shop.id, 'customer', 'SUBMISSION_RECEIVED',
        `Customer uploaded ${receiptId} (${fileRecords.length} file(s), ${(totalSizeBytes / 1024).toFixed(1)} KB)`);
      await saveCloudState(state);

      return jsonResponse({
        success: true,
        receiptId,
        shopName: shop.name,
        fileCount: fileRecords.length,
        totalSizeBytes,
        expiresAt
      });
    }

    // 4. GET /api/file?id=<fileId>
    if (route === '/file' && req.method === 'GET') {
      const fileId = url.searchParams.get('id');
      if (!fileId) return jsonResponse({ error: 'File ID required' }, 400);

      const filesStore = getStore('scandrop-files');
      const entry = await filesStore.getWithMetadata(fileId, { type: 'arrayBuffer' });
      if (!entry || !entry.data) {
        return jsonResponse({ error: 'File not found or expired' }, 404);
      }

      const meta = entry.metadata || {};
      const filename = meta.originalName || 'download';
      const mime = meta.mimeType || 'application/octet-stream';

      return new Response(entry.data, {
        status: 200,
        headers: {
          'Content-Type': mime,
          'Content-Disposition': `inline; filename="${filename}"`,
          'Access-Control-Allow-Origin': '*'
        }
      });
    }

    // 5. GET /api/receipt?id=<receiptId>
    if (route === '/receipt' && req.method === 'GET') {
      const id = (url.searchParams.get('id') || '').trim().toUpperCase();
      const state = await loadCloudState();
      const sub = state.submissions.find(s => s.id === id);
      if (!sub || sub.status === 'expired') {
        return jsonResponse({ success: false, error: 'Receipt not found or expired.' }, 404);
      }
      return jsonResponse({ success: true, submission: sub });
    }

    // 6. POST /api/submission/status
    if (route === '/submission/status' && req.method === 'POST') {
      const { submissionId, status } = await req.json();
      const state = await loadCloudState();
      const sub = state.submissions.find(s => s.id === submissionId);
      if (!sub) return jsonResponse({ success: false, error: 'Submission not found' }, 404);

      sub.status = status;
      addAudit(state, sub.shopId, 'shop_staff', 'STATUS_UPDATED', `Submission ${submissionId} moved to ${status}`);
      await saveCloudState(state);
      return jsonResponse({ success: true, submission });
    }

    // 7. POST /api/pair
    if (route === '/pair' && req.method === 'POST') {
      const { pairingCode } = await req.json();
      const state = await loadCloudState();
      const shop = state.shops.find(s => s.pairingCode === pairingCode);
      if (!shop) return jsonResponse({ success: false, error: 'Invalid pairing code' }, 404);
      if (shop.subscriptionStatus === 'revoked' || shop.subscriptionStatus === 'suspended') {
        return jsonResponse({ success: false, error: `Shop subscription is ${shop.subscriptionStatus}` }, 403);
      }

      addAudit(state, shop.id, 'shop_staff', 'DEVICE_PAIRED', `Shop workspace paired to ${shop.name}`);
      await saveCloudState(state);
      return jsonResponse({ success: true, shop });
    }

    // 8. POST /api/superadmin
    if (route === '/superadmin' && req.method === 'POST') {
      const body = await req.json();
      const state = await loadCloudState();

      if (body.action === 'create_shop') {
        const slug = (body.slug || '').trim().toLowerCase().replace(/[^a-z0-9-]/g, '-');
        if (!slug || state.shops.some(s => s.slug === slug)) {
          return jsonResponse({ success: false, error: 'Invalid or duplicate shop slug' }, 400);
        }
        const shopId = 'shop_' + Date.now();
        const pairingCode = 'SD-PAIR-' + Math.floor(1000 + Math.random() * 9000);
        const now = new Date();
        const expiryDate = new Date(now.getTime() + 30 * 24 * 60 * 60 * 1000).toISOString();

        const newShop = {
          id: shopId,
          slug,
          name: body.name,
          tagline: 'Scan. Drop. Done.',
          ownerName: body.ownerName,
          ownerEmail: body.ownerEmail,
          ownerPhone: body.ownerPhone || '',
          planId: body.planId || 'plan_free_trial',
          subscriptionStatus: 'active',
          pairingCode,
          expiryDate,
          createdAt: now.toISOString()
        };

        state.shops.unshift(newShop);
        addAudit(state, shopId, 'founder', 'SHOP_CREATED', `Registered shop ${newShop.name} (Pairing: ${pairingCode})`);
        await saveCloudState(state);
        return jsonResponse({ success: true, shop: newShop });
      }

      if (body.action === 'regen_code') {
        const shop = state.shops.find(s => s.id === body.shopId);
        if (!shop) return jsonResponse({ success: false, error: 'Shop not found' }, 404);
        shop.pairingCode = 'SD-PAIR-' + Math.floor(1000 + Math.random() * 9000);
        addAudit(state, shop.id, 'founder', 'PAIRING_CODE_REGENERATED', `Generated new pairing code ${shop.pairingCode}`);
        await saveCloudState(state);
        return jsonResponse({ success: true, pairingCode: shop.pairingCode });
      }

      if (body.action === 'update_subscription') {
        const shop = state.shops.find(s => s.id === body.shopId);
        if (!shop) return jsonResponse({ success: false, error: 'Shop not found' }, 404);

        shop.planId = body.planId || shop.planId;
        if (body.subAction === 'renew') {
          const base = new Date(shop.expiryDate) > new Date() ? new Date(shop.expiryDate) : new Date();
          shop.expiryDate = new Date(base.getTime() + 30 * 24 * 60 * 60 * 1000).toISOString();
          shop.subscriptionStatus = 'active';
        } else if (body.subAction === 'suspend') {
          shop.subscriptionStatus = 'suspended';
        } else if (body.subAction === 'reactivate') {
          shop.subscriptionStatus = 'active';
        } else if (body.subAction === 'revoke') {
          shop.subscriptionStatus = 'revoked';
        }

        addAudit(state, shop.id, 'founder', 'SUBSCRIPTION_UPDATED',
          `Action: ${body.subAction}, Plan: ${shop.planId}${body.paymentRef ? ', Ref: ' + body.paymentRef : ''}`);
        await saveCloudState(state);
        return jsonResponse({ success: true, shop });
      }

      if (body.action === 'revoke_device' || body.action === 'restore_device') {
        const dev = state.devices.find(d => d.id === body.deviceId);
        if (!dev) return jsonResponse({ success: false, error: 'Device not found' }, 404);
        dev.status = body.action === 'revoke_device' ? 'revoked' : 'active';
        addAudit(state, dev.shopId, 'founder', body.action.toUpperCase(), `${dev.deviceName} status set to ${dev.status}`);
        await saveCloudState(state);
        return jsonResponse({ success: true, device: dev });
      }

      return jsonResponse({ success: false, error: 'Unknown action' }, 400);
    }

    return jsonResponse({ error: 'Not Found' }, 404);
  } catch (err) {
    console.error('API Error:', err);
    return jsonResponse({ success: false, error: err.message || 'Server error' }, 500);
  }
};
