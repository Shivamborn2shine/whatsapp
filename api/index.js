// Scan Drop — Multi-Shop Vercel Control Plane & Tunnel Registry
// Customer files NEVER touch Vercel or Supabase — they stay 100% local on each shopkeeper's PC.
// Supabase Postgres stores ONLY shop metadata, pairing codes, hashed PC device secrets,
// live tunnel URLs, subscription statuses, and superadmin audit events.

const crypto = require('node:crypto');

const PLANS = [
  { id: 'plan_free_trial', name: 'Free / Trial', priceText: '₹0 / 30 days', maxDevices: 1 },
  { id: 'plan_pro_monthly', name: 'Pro Shop (1 PC)', priceText: '₹499 / month', maxDevices: 1 },
  { id: 'plan_business_monthly', name: 'Business Multi-Counter (5 PCs)', priceText: '₹1,499 / month', maxDevices: 5 }
];

const DEFAULT_SHOP = {
  id: 'shop_quickprint_01',
  slug: 'quickprint',
  name: 'QuickPrint Xerox & Stationery',
  tagline: 'Scan. Send. Done.',
  planId: 'plan_pro_monthly',
  subscriptionStatus: 'active',
  maxDevices: 1,
  pairingCode: 'SD-PAIR-7890'
};

const memoryRegistry = new Map();
const lastNtfyPublish = new Map();
const TUNNEL_ONLINE_TTL_MS = 15 * 60 * 1000;
const NTFY_REPUBLISH_INTERVAL_MS = 8 * 60 * 1000;

function sha256Hex(str) {
  return crypto.createHash('sha256').update(String(str || ''), 'utf8').digest('hex');
}

function generatePairingCode() {
  const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  const bytes = crypto.randomBytes(8);
  let part1 = '';
  let part2 = '';
  for (let i = 0; i < 4; i++) part1 += alphabet[bytes[i] % alphabet.length];
  for (let i = 4; i < 8; i++) part2 += alphabet[bytes[i] % alphabet.length];
  return `SD-${part1}-${part2}`;
}

function normalizeSlug(raw) {
  return String(raw || '')
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9-]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 48);
}

function isTunnelOnline(tunnelUrl, tunnelUpdatedAt, subscriptionStatus) {
  if (!tunnelUrl || !tunnelUpdatedAt) return false;
  if (subscriptionStatus === 'suspended' || subscriptionStatus === 'revoked' || subscriptionStatus === 'paused') return false;
  return (Date.now() - new Date(tunnelUpdatedAt).getTime()) < TUNNEL_ONLINE_TTL_MS;
}

// ============================================================================
// Supabase Configuration & Helpers (supports both sb_secret_ & legacy JWT keys)
// ============================================================================
function getSupabaseConfig() {
  const url = (process.env.SUPABASE_URL || process.env.NEXT_PUBLIC_SUPABASE_URL || '').trim().replace(/\/+$/, '');
  const publishableKey = (process.env.SUPABASE_PUBLISHABLE_KEY || process.env.SUPABASE_ANON_KEY || process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY || '').trim();
  const secretKey = (process.env.SUPABASE_SECRET_KEY || process.env.SUPABASE_SERVICE_ROLE_KEY || '').trim();
  return {
    url,
    publishableKey,
    secretKey,
    enabled: Boolean(url && secretKey)
  };
}

function supabaseHeaders(key, extra = {}) {
  const headers = {
    apikey: key,
    'Content-Type': 'application/json',
    ...extra
  };
  // Legacy JWT keys (eyJ...) also require Authorization: Bearer; new opaque sb_* keys must NOT be sent as JWT Bearer.
  if (key && !key.startsWith('sb_')) {
    headers.Authorization = `Bearer ${key}`;
  }
  return headers;
}

async function sbRest(pathAndQuery, options = {}) {
  const cfg = getSupabaseConfig();
  if (!cfg.enabled) throw new Error('Supabase is not configured on this deployment.');
  const res = await fetch(`${cfg.url}/rest/v1/${pathAndQuery}`, {
    method: options.method || 'GET',
    headers: supabaseHeaders(cfg.secretKey, options.headers || {}),
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined
  });
  const text = await res.text();
  let json = null;
  if (text) {
    try { json = JSON.parse(text); } catch { json = text; }
  }
  if (!res.ok) {
    const msg = (json && (json.message || json.error || json.details)) || `Supabase REST error (${res.status})`;
    const err = new Error(msg);
    err.status = res.status;
    err.details = json;
    throw err;
  }
  return json;
}

async function verifySuperadminRequest(req) {
  const cfg = getSupabaseConfig();
  if (!cfg.enabled) {
    return { ok: false, status: 503, error: 'Supabase environment variables (SUPABASE_URL, SUPABASE_PUBLISHABLE_KEY, SUPABASE_SECRET_KEY) are not configured on Vercel yet.' };
  }
  const authHeader = req.headers.authorization || req.headers.Authorization || '';
  const token = authHeader.startsWith('Bearer ') ? authHeader.slice(7).trim() : '';
  if (!token) {
    return { ok: false, status: 401, error: 'Sign in required.' };
  }

  const authKey = cfg.publishableKey || cfg.secretKey;
  const userRes = await fetch(`${cfg.url}/auth/v1/user`, {
    method: 'GET',
    headers: {
      apikey: authKey,
      Authorization: `Bearer ${token}`
    }
  });
  if (!userRes.ok) {
    return { ok: false, status: 401, error: 'Session expired or invalid. Please sign in again.' };
  }
  const user = await userRes.json();
  if (!user || !user.id || !user.email) {
    return { ok: false, status: 401, error: 'Unable to verify authenticated user.' };
  }

  const emailLower = String(user.email).trim().toLowerCase();
  const rows = await sbRest(`superadmins?user_id=eq.${encodeURIComponent(user.id)}&select=user_id,email&limit=1`);
  if (Array.isArray(rows) && rows.length > 0) {
    return { ok: true, user: { id: user.id, email: emailLower } };
  }

  const allowedEmails = String(process.env.SUPERADMIN_EMAILS || '')
    .split(',')
    .map(e => e.trim().toLowerCase())
    .filter(Boolean);
  if (allowedEmails.includes(emailLower)) {
    try {
      await sbRest('superadmins', {
        method: 'POST',
        headers: { Prefer: 'resolution=merge-duplicates,return=minimal' },
        body: { user_id: user.id, email: emailLower }
      });
    } catch {}
    return { ok: true, user: { id: user.id, email: emailLower } };
  }

  return {
    ok: false,
    status: 403,
    error: `Signed in as ${emailLower}, which is not authorized as a Scan Drop Superadmin.`
  };
}

async function sbLogAudit(shopDbId, actorEmail, eventType, description) {
  try {
    await sbRest('audit_events', {
      method: 'POST',
      headers: { Prefer: 'return=minimal' },
      body: {
        shop_id: shopDbId || null,
        actor_email: actorEmail || 'system',
        event_type: eventType,
        description
      }
    });
  } catch {}
}

function mapShopPublic(row) {
  const blocked = row.subscription_status === 'suspended' || row.subscription_status === 'revoked' || row.subscription_status === 'paused';
  const online = isTunnelOnline(row.tunnel_url, row.tunnel_updated_at, row.subscription_status);
  return {
    id: row.shop_uid,
    slug: row.slug,
    name: row.name,
    tagline: row.tagline || 'Scan. Send. Done.',
    subscriptionStatus: row.subscription_status,
    tunnelUrl: blocked ? null : (row.tunnel_url || null),
    tunnelUpdatedAt: blocked ? null : (row.tunnel_updated_at || null),
    tunnelOnline: online
  };
}

function mapShopAdmin(row, deviceCount) {
  const online = isTunnelOnline(row.tunnel_url, row.tunnel_updated_at, row.subscription_status);
  return {
    dbId: row.id,
    id: row.shop_uid,
    slug: row.slug,
    name: row.name,
    tagline: row.tagline || 'Scan. Send. Done.',
    ownerName: row.owner_name || '',
    ownerEmail: row.owner_email || '',
    ownerPhone: row.owner_phone || '',
    planId: row.plan_id || 'plan_pro_monthly',
    subscriptionStatus: row.subscription_status || 'active',
    maxDevices: row.max_devices || 1,
    pairingCode: row.pairing_code,
    pairingCodeUsedAt: row.pairing_code_used_at,
    tunnelUrl: row.tunnel_url || null,
    tunnelUpdatedAt: row.tunnel_updated_at || null,
    tunnelOnline: online,
    deviceCount: deviceCount || 0,
    expiryDate: row.expiry_date,
    createdAt: row.created_at
  };
}

// ============================================================================
// Fallback Registry (KV / ntfy.sh) when Supabase env vars are not set yet
// ============================================================================
function getKvConfig() {
  const url = process.env.KV_REST_API_URL || process.env.UPSTASH_REDIS_REST_URL;
  const token = process.env.KV_REST_API_TOKEN || process.env.UPSTASH_REDIS_REST_TOKEN;
  return url && token ? { url: url.replace(/\/+$/, ''), token } : null;
}

function getNtfyTopic(slug) {
  const clean = String(slug || 'quickprint').toLowerCase().replace(/[^a-z0-9_-]/g, '');
  return `scandrop_tunnel_reg_${clean || 'quickprint'}_v4_9a8b7c`;
}

async function kvGet(key) {
  const cfg = getKvConfig();
  if (!cfg) return null;
  try {
    const res = await fetch(`${cfg.url}/get/${encodeURIComponent(key)}`, {
      headers: { Authorization: `Bearer ${cfg.token}` }
    });
    if (!res.ok) return null;
    const data = await res.json();
    if (!data || !data.result) return null;
    return typeof data.result === 'string' ? JSON.parse(data.result) : data.result;
  } catch { return null; }
}

async function kvSet(key, obj) {
  const cfg = getKvConfig();
  if (!cfg) return false;
  try {
    const res = await fetch(`${cfg.url}/set/${encodeURIComponent(key)}`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${cfg.token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify(obj)
    });
    return res.ok;
  } catch { return false; }
}

async function ntfyReadLatest(slug) {
  try {
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), 3500);
    const res = await fetch(`https://ntfy.sh/${encodeURIComponent(getNtfyTopic(slug))}/json?poll=1&since=12h`, { signal: ctrl.signal });
    clearTimeout(timer);
    if (!res.ok) return null;
    const lines = (await res.text()).trim().split('\n').filter(Boolean);
    for (let i = lines.length - 1; i >= 0; i--) {
      try {
        const evt = JSON.parse(lines[i]);
        if (evt && evt.event === 'message' && evt.message) {
          const payload = JSON.parse(evt.message);
          if (payload && typeof payload === 'object') return payload;
        }
      } catch {}
    }
  } catch {}
  return null;
}

async function ntfyPublish(slug, record) {
  try {
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), 3500);
    await fetch(`https://ntfy.sh/${encodeURIComponent(getNtfyTopic(slug))}`, {
      method: 'POST',
      signal: ctrl.signal,
      headers: { 'Content-Type': 'text/plain' },
      body: JSON.stringify(record)
    });
    clearTimeout(timer);
  } catch {}
}

async function fallbackLoadShop(slug) {
  const cleanSlug = (slug || DEFAULT_SHOP.slug).trim().toLowerCase();
  const key = `scandrop:shop:${cleanSlug}`;
  const kvRec = await kvGet(key);
  if (kvRec) { memoryRegistry.set(cleanSlug, kvRec); return kvRec; }
  const cached = memoryRegistry.get(cleanSlug);
  if (cached && cached.tunnelUpdatedAt && (Date.now() - new Date(cached.tunnelUpdatedAt).getTime()) < 25000) return cached;
  const remote = await ntfyReadLatest(cleanSlug);
  if (remote) { memoryRegistry.set(cleanSlug, remote); return remote; }
  if (cleanSlug === DEFAULT_SHOP.slug) {
    return cached || { ...DEFAULT_SHOP, tunnelUrl: null, tunnelUpdatedAt: null };
  }
  return cached || null;
}

async function fallbackSaveShop(slug, record, options = {}) {
  const cleanSlug = (slug || DEFAULT_SHOP.slug).trim().toLowerCase();
  const key = `scandrop:shop:${cleanSlug}`;
  const prev = memoryRegistry.get(cleanSlug);
  memoryRegistry.set(cleanSlug, record);
  const savedKv = await kvSet(key, record);
  if (!savedKv) {
    const now = Date.now();
    const lastPub = lastNtfyPublish.get(cleanSlug) || 0;
    const urlChanged = !prev || prev.tunnelUrl !== record.tunnelUrl;
    if (options.forcePublish || urlChanged || (now - lastPub) >= NTFY_REPUBLISH_INTERVAL_MS) {
      await ntfyPublish(cleanSlug, record);
      lastNtfyPublish.set(cleanSlug, now);
    }
  }
}

// ============================================================================
// HTTP Utilities
// ============================================================================
async function readJsonBody(req) {
  if (req.body && typeof req.body === 'object') return req.body;
  if (typeof req.body === 'string') {
    try { return JSON.parse(req.body); } catch { return {}; }
  }
  const chunks = [];
  for await (const chunk of req) chunks.push(chunk);
  const raw = Buffer.concat(chunks).toString('utf8');
  if (!raw.trim()) return {};
  try { return JSON.parse(raw); } catch { return {}; }
}

function sendJson(res, status, data) {
  res.statusCode = status;
  res.setHeader('Content-Type', 'application/json; charset=utf-8');
  res.setHeader('Cache-Control', 'no-store, max-age=0');
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-Requested-With');
  res.end(JSON.stringify(data));
}

// ============================================================================
// Main Vercel Serverless Handler
// ============================================================================
module.exports = async function handler(req, res) {
  if (req.method === 'OPTIONS') {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-Requested-With');
    res.statusCode = 204;
    res.end();
    return;
  }

  const parsedUrl = new URL(req.url, 'http://localhost');
  let route = parsedUrl.searchParams.get('route') || parsedUrl.pathname.replace(/^\/api/, '');
  if (!route.startsWith('/')) route = '/' + route;
  route = route.replace(/\/+$/, '') || '/';

  const sb = getSupabaseConfig();
  const registryMode = sb.enabled ? 'supabase' : (getKvConfig() ? 'kv' : 'ntfy');

  try {
    // 1. Health check
    if (route === '/health' && req.method === 'GET') {
      return sendJson(res, 200, {
        ok: true,
        success: true,
        host: 'vercel',
        registry: registryMode,
        authConfigured: Boolean(sb.url && sb.publishableKey && sb.secretKey)
      });
    }

    // 2. Public client config for Superadmin Auth login (NEVER returns secretKey)
    if (route === '/config' && req.method === 'GET') {
      return sendJson(res, 200, {
        success: true,
        registry: registryMode,
        supabaseUrl: sb.url || null,
        supabasePublishableKey: sb.publishableKey || null
      });
    }

    // 3. Public shop resolution (strict slug lookup, zero sensitive fields)
    if (route === '/shop' && req.method === 'GET') {
      const slug = normalizeSlug(parsedUrl.searchParams.get('slug') || '');
      if (!slug) {
        return sendJson(res, 400, { success: false, error: 'Shop slug is required.' });
      }

      if (sb.enabled) {
        const rows = await sbRest(`shops?slug=eq.${encodeURIComponent(slug)}&select=shop_uid,slug,name,tagline,subscription_status,tunnel_url,tunnel_updated_at&limit=1`);
        if (!Array.isArray(rows) || rows.length === 0) {
          return sendJson(res, 404, { success: false, error: 'Shop not found.', shop: null });
        }
        return sendJson(res, 200, { success: true, shop: mapShopPublic(rows[0]) });
      }

      const rec = await fallbackLoadShop(slug);
      if (!rec) {
        return sendJson(res, 404, { success: false, error: 'Shop not found.', shop: null });
      }
      const online = isTunnelOnline(rec.tunnelUrl, rec.tunnelUpdatedAt, rec.subscriptionStatus);
      return sendJson(res, 200, {
        success: true,
        shop: {
          id: rec.id || DEFAULT_SHOP.id,
          slug: rec.slug || slug,
          name: rec.name || DEFAULT_SHOP.name,
          tagline: rec.tagline || 'Scan. Send. Done.',
          subscriptionStatus: rec.subscriptionStatus || 'active',
          tunnelUrl: rec.tunnelUrl || null,
          tunnelUpdatedAt: rec.tunnelUpdatedAt || null,
          tunnelOnline: online
        }
      });
    }

    // 4. Shop PC Pairing (exchanges admin-issued pairingCode for DPAPI-stored deviceSecret)
    if (route === '/pair' && req.method === 'POST') {
      const body = await readJsonBody(req);
      const pairingCode = String(body.pairingCode || '').trim().toUpperCase();
      const deviceName = String(body.deviceName || 'Windows Counter PC').trim().slice(0, 80);
      const machineFingerprint = String(body.machineFingerprint || '').trim().slice(0, 128);
      const windowsVersion = String(body.windowsVersion || 'Windows').trim().slice(0, 80);
      const appVersion = String(body.appVersion || '4.1.0').trim().slice(0, 32);

      if (!pairingCode || pairingCode.length < 4) {
        return sendJson(res, 400, { success: false, error: 'Please enter a valid Pairing Code.' });
      }

      if (sb.enabled) {
        const shops = await sbRest(`shops?pairing_code=eq.${encodeURIComponent(pairingCode)}&limit=1`);
        if (!Array.isArray(shops) || shops.length === 0) {
          return sendJson(res, 404, { success: false, error: 'Invalid pairing code. Ask your Scan Drop administrator for a valid code.' });
        }
        const shop = shops[0];
        if (shop.subscription_status === 'suspended' || shop.subscription_status === 'revoked') {
          return sendJson(res, 403, { success: false, error: `This shop account is currently ${shop.subscription_status}. Contact Scan Drop admin.` });
        }

        const existingDevices = await sbRest(`devices?shop_id=eq.${shop.id}&status=eq.active&select=id,device_uid,machine_fingerprint`);
        const matched = machineFingerprint
          ? (existingDevices || []).find(d => d.machine_fingerprint && d.machine_fingerprint === machineFingerprint)
          : null;

        if (!matched && (existingDevices || []).length >= (shop.max_devices || 1)) {
          return sendJson(res, 403, {
            success: false,
            error: `Device limit reached (${shop.max_devices || 1} PC max on this plan). Ask your Scan Drop admin to revoke an old PC or upgrade your plan.`
          });
        }

        const deviceSecret = crypto.randomBytes(32).toString('hex');
        const secretHash = sha256Hex(deviceSecret);
        const nowIso = new Date().toISOString();
        let deviceUid;

        if (matched) {
          deviceUid = matched.device_uid;
          await sbRest(`devices?id=eq.${matched.id}`, {
            method: 'PATCH',
            headers: { Prefer: 'return=minimal' },
            body: {
              device_name: deviceName,
              device_secret_hash: secretHash,
              windows_version: windowsVersion,
              app_version: appVersion,
              status: 'active',
              last_heartbeat_at: nowIso
            }
          });
        } else {
          deviceUid = 'dev_' + crypto.randomBytes(6).toString('hex');
          await sbRest('devices', {
            method: 'POST',
            headers: { Prefer: 'return=minimal' },
            body: {
              device_uid: deviceUid,
              shop_id: shop.id,
              device_name: deviceName,
              machine_fingerprint: machineFingerprint,
              device_secret_hash: secretHash,
              windows_version: windowsVersion,
              app_version: appVersion,
              status: 'active',
              last_heartbeat_at: nowIso
            }
          });
        }

        await sbRest(`shops?id=eq.${shop.id}`, {
          method: 'PATCH',
          headers: { Prefer: 'return=minimal' },
          body: {
            pairing_code_used_at: nowIso,
            device_secret_hash: secretHash
          }
        });

        await sbLogAudit(shop.id, 'shop-pc', 'DEVICE_PAIRED', `Paired PC "${deviceName}" (${deviceUid}) to /shop/${shop.slug}`);

        return sendJson(res, 200, {
          success: true,
          deviceId: deviceUid,
          deviceSecret,
          shop: {
            id: shop.shop_uid,
            slug: shop.slug,
            name: shop.name,
            tagline: shop.tagline || 'Scan. Send. Done.',
            subscriptionStatus: shop.subscription_status,
            planId: shop.plan_id,
            maxDevices: shop.max_devices
          }
        });
      }

      // Fallback mode before Supabase env vars are added
      if (pairingCode !== DEFAULT_SHOP.pairingCode) {
        return sendJson(res, 404, { success: false, error: 'Invalid pairing code (connect Supabase in Vercel to enable multi-shop codes).' });
      }
      const deviceSecret = crypto.randomBytes(32).toString('hex');
      return sendJson(res, 200, {
        success: true,
        deviceId: 'dev_default_01',
        deviceSecret,
        shop: {
          id: DEFAULT_SHOP.id,
          slug: DEFAULT_SHOP.slug,
          name: DEFAULT_SHOP.name,
          tagline: DEFAULT_SHOP.tagline,
          subscriptionStatus: 'active',
          planId: DEFAULT_SHOP.planId,
          maxDevices: 1
        }
      });
    }

    // 5. Authenticated PC Tunnel Heartbeat / Registration (anti-hijack protected)
    if (route === '/tunnel/register' && req.method === 'POST') {
      const body = await readJsonBody(req);
      const { shopId, pairingCode, tunnelUrl, shopSlug, shopName, deviceId, deviceSecret, appVersion } = body;
      if (!tunnelUrl || typeof tunnelUrl !== 'string' || !tunnelUrl.startsWith('https://')) {
        return sendJson(res, 400, { success: false, error: 'Valid https:// tunnelUrl is required.' });
      }
      const cleanTunnel = tunnelUrl.trim().replace(/\/+$/, '');
      const cleanSlug = normalizeSlug(shopSlug || '');

      if (sb.enabled) {
        let shop = null;
        if (cleanSlug) {
          const rows = await sbRest(`shops?slug=eq.${encodeURIComponent(cleanSlug)}&limit=1`);
          if (rows && rows.length) shop = rows[0];
        }
        if (!shop && shopId) {
          const rows = await sbRest(`shops?shop_uid=eq.${encodeURIComponent(String(shopId))}&limit=1`);
          if (rows && rows.length) shop = rows[0];
        }
        if (!shop && pairingCode) {
          const rows = await sbRest(`shops?pairing_code=eq.${encodeURIComponent(String(pairingCode).trim().toUpperCase())}&limit=1`);
          if (rows && rows.length) shop = rows[0];
        }
        if (!shop) {
          return sendJson(res, 404, { success: false, error: 'Shop not registered. Enter a valid pairing code in Scan Drop.' });
        }

        // Authenticate caller so no PC can hijack another shop's slug
        let authorized = false;
        let matchedDeviceDbId = null;

        if (deviceSecret) {
          const hash = sha256Hex(deviceSecret);
          const devs = await sbRest(`devices?shop_id=eq.${shop.id}&device_secret_hash=eq.${encodeURIComponent(hash)}&status=eq.active&limit=1`);
          if (devs && devs.length > 0) {
            authorized = true;
            matchedDeviceDbId = devs[0].id;
          } else if (shop.device_secret_hash && shop.device_secret_hash === hash) {
            authorized = true;
          }
        }

        // Backward-compatible pairingCode auth ONLY when pairing_code matches this exact shop
        // AND either no device has paired yet or no revoked conflict exists
        if (!authorized && pairingCode && String(pairingCode).trim().toUpperCase() === String(shop.pairing_code || '').trim().toUpperCase()) {
          if (!shop.device_secret_hash || !deviceSecret) {
            authorized = true;
          }
        }

        if (!authorized) {
          return sendJson(res, 401, {
            success: false,
            error: 'Unauthorized PC or revoked device. Re-pair with a valid Pairing Code in Settings.'
          });
        }

        if (shop.subscription_status === 'suspended' || shop.subscription_status === 'revoked') {
          return sendJson(res, 403, { success: false, error: `Shop subscription is ${shop.subscription_status}.` });
        }

        const nowIso = new Date().toISOString();
        const patchShop = {
          tunnel_url: cleanTunnel,
          tunnel_updated_at: nowIso
        };
        if (shopName && typeof shopName === 'string' && shopName.trim().length >= 2) {
          patchShop.name = shopName.trim().slice(0, 100);
        }
        await sbRest(`shops?id=eq.${shop.id}`, {
          method: 'PATCH',
          headers: { Prefer: 'return=minimal' },
          body: patchShop
        });

        if (matchedDeviceDbId) {
          await sbRest(`devices?id=eq.${matchedDeviceDbId}`, {
            method: 'PATCH',
            headers: { Prefer: 'return=minimal' },
            body: {
              tunnel_url: cleanTunnel,
              last_heartbeat_at: nowIso,
              ...(appVersion ? { app_version: String(appVersion).slice(0, 32) } : {})
            }
          });
        }

        return sendJson(res, 200, {
          success: true,
          shop: mapShopPublic({ ...shop, ...patchShop })
        });
      }

      // Fallback mode
      const slug = cleanSlug || DEFAULT_SHOP.slug;
      const existing = (await fallbackLoadShop(slug)) || { ...DEFAULT_SHOP, slug };
      const updated = {
        ...DEFAULT_SHOP,
        ...existing,
        id: shopId || existing.id || DEFAULT_SHOP.id,
        slug,
        name: shopName || existing.name || DEFAULT_SHOP.name,
        pairingCode: pairingCode || existing.pairingCode || DEFAULT_SHOP.pairingCode,
        tunnelUrl: cleanTunnel,
        tunnelUpdatedAt: new Date().toISOString()
      };
      await fallbackSaveShop(slug, updated);
      return sendJson(res, 200, { success: true, shop: { ...updated, tunnelOnline: true } });
    }

    // 6. Authenticated PC Tunnel Disconnect
    if (route === '/tunnel/disconnect' && req.method === 'POST') {
      const body = await readJsonBody(req);
      const { shopId, shopSlug, pairingCode, deviceSecret } = body;
      const cleanSlug = normalizeSlug(shopSlug || '');

      if (sb.enabled) {
        let shop = null;
        if (cleanSlug) {
          const rows = await sbRest(`shops?slug=eq.${encodeURIComponent(cleanSlug)}&limit=1`);
          if (rows && rows.length) shop = rows[0];
        } else if (shopId) {
          const rows = await sbRest(`shops?shop_uid=eq.${encodeURIComponent(String(shopId))}&limit=1`);
          if (rows && rows.length) shop = rows[0];
        }
        if (!shop) return sendJson(res, 200, { success: true });

        let authorized = false;
        if (deviceSecret) {
          const hash = sha256Hex(deviceSecret);
          const devs = await sbRest(`devices?shop_id=eq.${shop.id}&device_secret_hash=eq.${encodeURIComponent(hash)}&limit=1`);
          if ((devs && devs.length > 0) || shop.device_secret_hash === hash) authorized = true;
        }
        if (!authorized && pairingCode && String(pairingCode).trim().toUpperCase() === String(shop.pairing_code || '').trim().toUpperCase()) {
          authorized = true;
        }
        if (!authorized) return sendJson(res, 401, { success: false, error: 'Unauthorized disconnect request.' });

        await sbRest(`shops?id=eq.${shop.id}`, {
          method: 'PATCH',
          headers: { Prefer: 'return=minimal' },
          body: { tunnel_url: null, tunnel_updated_at: null }
        });
        return sendJson(res, 200, { success: true });
      }

      const slug = cleanSlug || DEFAULT_SHOP.slug;
      const existing = (await fallbackLoadShop(slug)) || { ...DEFAULT_SHOP, slug };
      await fallbackSaveShop(slug, { ...DEFAULT_SHOP, ...existing, tunnelUrl: null, tunnelUpdatedAt: null }, { forcePublish: true });
      return sendJson(res, 200, { success: true });
    }

    // 7. Protected Superadmin Control Plane (Supabase Auth JWT verified)
    if (route === '/superadmin' && req.method === 'GET') {
      const auth = await verifySuperadminRequest(req);
      if (!auth.ok) return sendJson(res, auth.status, { success: false, error: auth.error });

      const [shops, devices, events] = await Promise.all([
        sbRest('shops?select=*&order=created_at.desc'),
        sbRest('devices?select=id,device_uid,shop_id,device_name,windows_version,app_version,status,tunnel_url,last_heartbeat_at,created_at&order=last_heartbeat_at.desc'),
        sbRest('audit_events?select=id,shop_id,actor_email,event_type,description,created_at&order=created_at.desc&limit=80')
      ]);

      const countByShop = new Map();
      const shopByDbId = new Map();
      for (const s of (shops || [])) shopByDbId.set(s.id, s);
      for (const d of (devices || [])) {
        if (d.status === 'active') countByShop.set(d.shop_id, (countByShop.get(d.shop_id) || 0) + 1);
      }

      return sendJson(res, 200, {
        success: true,
        admin: auth.user,
        plans: PLANS,
        shops: (shops || []).map(s => mapShopAdmin(s, countByShop.get(s.id) || 0)),
        devices: (devices || []).map(d => {
          const parent = shopByDbId.get(d.shop_id);
          return {
            dbId: d.id,
            id: d.device_uid,
            shopId: parent ? parent.shop_uid : String(d.shop_id),
            shopName: parent ? parent.name : '',
            shopSlug: parent ? parent.slug : '',
            deviceName: d.device_name,
            windowsVersion: d.windows_version,
            appVersion: d.app_version,
            status: d.status,
            tunnelUrl: d.tunnel_url,
            lastHeartbeatAt: d.last_heartbeat_at,
            createdAt: d.created_at
          };
        }),
        auditEvents: (events || []).map(e => ({
          id: e.id,
          actor: e.actor_email,
          eventType: e.event_type,
          description: e.description,
          createdAt: e.created_at
        }))
      });
    }

    if (route === '/superadmin' && req.method === 'POST') {
      const auth = await verifySuperadminRequest(req);
      if (!auth.ok) return sendJson(res, auth.status, { success: false, error: auth.error });

      const body = await readJsonBody(req);
      const action = String(body.action || '').trim();

      if (action === 'create_shop') {
        const name = String(body.name || '').trim();
        const slug = normalizeSlug(body.slug || name);
        if (name.length < 2) return sendJson(res, 400, { success: false, error: 'Shop name must be at least 2 characters.' });
        if (!/^[a-z0-9][a-z0-9-]{1,48}$/.test(slug)) {
          return sendJson(res, 400, { success: false, error: 'Shop slug must be 2–49 lowercase letters, numbers, or hyphens (e.g. sharma-xerox).' });
        }
        const reserved = ['api', 'superadmin', 'workspace', 'downloads', 'shop', 'admin', 'login', 'health'];
        if (reserved.includes(slug)) {
          return sendJson(res, 400, { success: false, error: `Slug "${slug}" is reserved. Pick another name.` });
        }

        const existing = await sbRest(`shops?slug=eq.${encodeURIComponent(slug)}&select=id&limit=1`);
        if (existing && existing.length > 0) {
          return sendJson(res, 409, { success: false, error: `Shop URL /shop/${slug} is already taken.` });
        }

        const plan = PLANS.find(p => p.id === body.planId) || PLANS[1];
        const pairingCode = generatePairingCode();
        const shopUid = 'shop_' + crypto.randomBytes(6).toString('hex');
        const expiryDays = plan.id === 'plan_free_trial' ? 14 : 30;
        const expiryDate = new Date(Date.now() + expiryDays * 86400000).toISOString();

        const created = await sbRest('shops', {
          method: 'POST',
          headers: { Prefer: 'return=representation' },
          body: {
            shop_uid: shopUid,
            slug,
            name,
            tagline: String(body.tagline || 'Scan. Send. Done.').trim().slice(0, 100),
            owner_name: String(body.ownerName || '').trim().slice(0, 100),
            owner_email: String(body.ownerEmail || '').trim().slice(0, 120),
            owner_phone: String(body.ownerPhone || '').trim().slice(0, 40),
            plan_id: plan.id,
            subscription_status: plan.id === 'plan_free_trial' ? 'trial' : 'active',
            max_devices: plan.maxDevices,
            pairing_code: pairingCode,
            expiry_date: expiryDate
          }
        });
        const shopRow = Array.isArray(created) ? created[0] : created;
        await sbLogAudit(shopRow.id, auth.user.email, 'SHOP_CREATED', `Created shop "${name}" (/shop/${slug}) with pairing code ${pairingCode}`);
        return sendJson(res, 200, { success: true, shop: mapShopAdmin(shopRow, 0) });
      }

      if (action === 'regen_code') {
        const shopUid = String(body.shopId || '').trim();
        const rows = await sbRest(`shops?shop_uid=eq.${encodeURIComponent(shopUid)}&limit=1`);
        if (!rows || !rows.length) return sendJson(res, 404, { success: false, error: 'Shop not found.' });
        const newCode = generatePairingCode();
        await sbRest(`shops?id=eq.${rows[0].id}`, {
          method: 'PATCH',
          headers: { Prefer: 'return=minimal' },
          body: { pairing_code: newCode, pairing_code_used_at: null }
        });
        await sbLogAudit(rows[0].id, auth.user.email, 'PAIRING_CODE_REGENERATED', `Regenerated pairing code for /shop/${rows[0].slug}`);
        return sendJson(res, 200, { success: true, pairingCode: newCode });
      }

      if (action === 'update_subscription') {
        const shopUid = String(body.shopId || '').trim();
        const subAction = String(body.subAction || '').trim();
        const rows = await sbRest(`shops?shop_uid=eq.${encodeURIComponent(shopUid)}&limit=1`);
        if (!rows || !rows.length) return sendJson(res, 404, { success: false, error: 'Shop not found.' });
        const shop = rows[0];
        const plan = PLANS.find(p => p.id === body.planId) || PLANS.find(p => p.id === shop.plan_id) || PLANS[1];
        const patch = { plan_id: plan.id, max_devices: plan.maxDevices };

        if (subAction === 'activate') {
          patch.subscription_status = 'active';
        } else if (subAction === 'extend_30d') {
          patch.subscription_status = 'active';
          const baseMs = Math.max(Date.now(), new Date(shop.expiry_date || Date.now()).getTime());
          patch.expiry_date = new Date(baseMs + 30 * 86400000).toISOString();
        } else if (subAction === 'pause') {
          patch.subscription_status = 'paused';
        } else if (subAction === 'suspend') {
          patch.subscription_status = 'suspended';
          patch.tunnel_url = null;
          patch.tunnel_updated_at = null;
        } else if (subAction === 'revoke') {
          patch.subscription_status = 'revoked';
          patch.tunnel_url = null;
          patch.tunnel_updated_at = null;
        }

        await sbRest(`shops?id=eq.${shop.id}`, {
          method: 'PATCH',
          headers: { Prefer: 'return=minimal' },
          body: patch
        });
        const note = body.paymentRef ? ` (ref: ${String(body.paymentRef).slice(0, 60)})` : '';
        await sbLogAudit(shop.id, auth.user.email, 'SUBSCRIPTION_UPDATED', `Updated /shop/${shop.slug}: ${subAction || 'plan'} -> ${plan.name}${note}`);
        return sendJson(res, 200, { success: true });
      }

      if (action === 'revoke_device') {
        const deviceUid = String(body.deviceId || '').trim();
        const devs = await sbRest(`devices?device_uid=eq.${encodeURIComponent(deviceUid)}&limit=1`);
        if (!devs || !devs.length) return sendJson(res, 404, { success: false, error: 'Device not found.' });
        const dev = devs[0];
        await sbRest(`devices?id=eq.${dev.id}`, {
          method: 'PATCH',
          headers: { Prefer: 'return=minimal' },
          body: { status: 'revoked', device_secret_hash: 'revoked_' + crypto.randomBytes(16).toString('hex'), tunnel_url: null }
        });
        await sbRest(`shops?id=eq.${dev.shop_id}`, {
          method: 'PATCH',
          headers: { Prefer: 'return=minimal' },
          body: { device_secret_hash: null, tunnel_url: null, tunnel_updated_at: null }
        });
        await sbLogAudit(dev.shop_id, auth.user.email, 'DEVICE_REVOKED', `Revoked device "${dev.device_name}" (${deviceUid})`);
        return sendJson(res, 200, { success: true });
      }

      if (action === 'delete_shop') {
        const shopUid = String(body.shopId || '').trim();
        const rows = await sbRest(`shops?shop_uid=eq.${encodeURIComponent(shopUid)}&limit=1`);
        if (!rows || !rows.length) return sendJson(res, 404, { success: false, error: 'Shop not found.' });
        const shop = rows[0];
        await sbLogAudit(null, auth.user.email, 'SHOP_DELETED', `Deleted shop "${shop.name}" (/shop/${shop.slug})`);
        await sbRest(`shops?id=eq.${shop.id}`, {
          method: 'DELETE',
          headers: { Prefer: 'return=minimal' }
        });
        return sendJson(res, 200, { success: true });
      }

      return sendJson(res, 400, { success: false, error: 'Unknown superadmin action.' });
    }

    // 8. Legacy compatibility state endpoint (safe minimal metadata only)
    if (route === '/state' && req.method === 'GET') {
      const slug = normalizeSlug(parsedUrl.searchParams.get('slug') || DEFAULT_SHOP.slug);
      if (sb.enabled) {
        const rows = await sbRest(`shops?slug=eq.${encodeURIComponent(slug)}&select=shop_uid,slug,name,tagline,subscription_status,tunnel_url,tunnel_updated_at&limit=1`);
        return sendJson(res, 200, {
          success: true,
          storageMode: 'supabase-registry',
          plans: PLANS,
          state: { shops: (rows || []).map(mapShopPublic), submissions: [], auditEvents: [] }
        });
      }
      const rec = (await fallbackLoadShop(slug)) || { ...DEFAULT_SHOP, slug };
      return sendJson(res, 200, {
        success: true,
        storageMode: 'vercel-registry',
        plans: PLANS,
        state: {
          shops: [{ ...DEFAULT_SHOP, ...rec, tunnelOnline: isTunnelOnline(rec.tunnelUrl, rec.tunnelUpdatedAt, rec.subscriptionStatus) }],
          submissions: [],
          auditEvents: []
        }
      });
    }

    return sendJson(res, 404, { success: false, error: 'Not Found' });
  } catch (err) {
    return sendJson(res, err.status || 500, { success: false, error: err.message || 'Server error' });
  }
};
