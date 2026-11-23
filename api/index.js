// Scan Drop — Vercel Serverless Tunnel Registry & Shop Resolver
// Zero-cost local-first bridge: routes customer phones to the shopkeeper's PC tunnel.
// Supports optional Vercel KV / Upstash Redis (KV_REST_API_URL + KV_REST_API_TOKEN),
// with automatic zero-config fallback to retained ntfy.sh registry + warm memory cache.

const DEFAULT_SHOP = {
  id: 'shop_quickprint_01',
  slug: 'quickprint',
  name: 'QuickPrint Xerox & Stationery',
  tagline: 'Scan. Send. Done.',
  planId: 'plan_local_first',
  subscriptionStatus: 'active',
  pairingCode: 'SD-PAIR-7890'
};

// Warm instance cache keyed by shop slug
const memoryRegistry = new Map();
const lastNtfyPublish = new Map();
const TUNNEL_ONLINE_TTL_MS = 15 * 60 * 1000;
const NTFY_REPUBLISH_INTERVAL_MS = 8 * 60 * 1000;

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
  } catch {
    return null;
  }
}

async function kvSet(key, obj) {
  const cfg = getKvConfig();
  if (!cfg) return false;
  try {
    const res = await fetch(`${cfg.url}/set/${encodeURIComponent(key)}`, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${cfg.token}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(obj)
    });
    return res.ok;
  } catch {
    return false;
  }
}

async function ntfyReadLatest(slug) {
  try {
    const topic = getNtfyTopic(slug);
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), 3500);
    const res = await fetch(`https://ntfy.sh/${encodeURIComponent(topic)}/json?poll=1&since=12h`, {
      signal: ctrl.signal
    });
    clearTimeout(timer);
    if (!res.ok) return null;
    const text = await res.text();
    const lines = text.trim().split('\n').filter(Boolean);
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
    const topic = getNtfyTopic(slug);
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), 3500);
    await fetch(`https://ntfy.sh/${encodeURIComponent(topic)}`, {
      method: 'POST',
      signal: ctrl.signal,
      headers: { 'Content-Type': 'text/plain' },
      body: JSON.stringify(record)
    });
    clearTimeout(timer);
  } catch {}
}

async function loadShopRecord(slug) {
  const cleanSlug = (slug || DEFAULT_SHOP.slug).trim().toLowerCase();
  const key = `scandrop:shop:${cleanSlug}`;

  const kvRec = await kvGet(key);
  if (kvRec) {
    memoryRegistry.set(cleanSlug, kvRec);
    return kvRec;
  }

  const cached = memoryRegistry.get(cleanSlug);
  if (cached && cached.tunnelUpdatedAt && (Date.now() - new Date(cached.tunnelUpdatedAt).getTime()) < 25000) {
    return cached;
  }

  const remote = await ntfyReadLatest(cleanSlug);
  if (remote) {
    memoryRegistry.set(cleanSlug, remote);
    return remote;
  }

  return cached || { ...DEFAULT_SHOP, slug: cleanSlug, tunnelUrl: null, tunnelUpdatedAt: null };
}

async function saveShopRecord(slug, record, options = {}) {
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

  try {
    if (route === '/health' && req.method === 'GET') {
      return sendJson(res, 200, { ok: true, success: true, host: 'vercel', registry: getKvConfig() ? 'kv' : 'ntfy' });
    }

    if (route === '/shop' && req.method === 'GET') {
      const slug = (parsedUrl.searchParams.get('slug') || DEFAULT_SHOP.slug).trim().toLowerCase();
      const rec = await loadShopRecord(slug);
      const tunnelOnline = Boolean(
        rec.tunnelUrl &&
        rec.tunnelUpdatedAt &&
        (Date.now() - new Date(rec.tunnelUpdatedAt).getTime()) < TUNNEL_ONLINE_TTL_MS
      );
      return sendJson(res, 200, {
        success: true,
        shop: {
          ...DEFAULT_SHOP,
          ...rec,
          tunnelOnline
        }
      });
    }

    if (route === '/tunnel/register' && req.method === 'POST') {
      const body = await readJsonBody(req);
      const { shopId, pairingCode, tunnelUrl, shopSlug, shopName } = body;
      if (!tunnelUrl || typeof tunnelUrl !== 'string' || !tunnelUrl.startsWith('https://')) {
        return sendJson(res, 400, { success: false, error: 'Valid https:// tunnelUrl is required.' });
      }
      const slug = (shopSlug || DEFAULT_SHOP.slug).trim().toLowerCase();
      const existing = await loadShopRecord(slug);
      const updated = {
        ...DEFAULT_SHOP,
        ...existing,
        id: shopId || existing.id || DEFAULT_SHOP.id,
        slug,
        name: shopName || existing.name || DEFAULT_SHOP.name,
        pairingCode: pairingCode || existing.pairingCode || DEFAULT_SHOP.pairingCode,
        tunnelUrl: tunnelUrl.trim().replace(/\/+$/, ''),
        tunnelUpdatedAt: new Date().toISOString()
      };
      await saveShopRecord(slug, updated);
      return sendJson(res, 200, { success: true, shop: { ...updated, tunnelOnline: true } });
    }

    if (route === '/tunnel/disconnect' && req.method === 'POST') {
      const body = await readJsonBody(req);
      const slug = (body.shopSlug || DEFAULT_SHOP.slug).trim().toLowerCase();
      const existing = await loadShopRecord(slug);
      const updated = {
        ...DEFAULT_SHOP,
        ...existing,
        tunnelUrl: null,
        tunnelUpdatedAt: null
      };
      await saveShopRecord(slug, updated, { forcePublish: true });
      return sendJson(res, 200, { success: true });
    }

    if (route === '/state' && req.method === 'GET') {
      const slug = (parsedUrl.searchParams.get('slug') || DEFAULT_SHOP.slug).trim().toLowerCase();
      const rec = await loadShopRecord(slug);
      const tunnelOnline = Boolean(
        rec.tunnelUrl &&
        rec.tunnelUpdatedAt &&
        (Date.now() - new Date(rec.tunnelUpdatedAt).getTime()) < TUNNEL_ONLINE_TTL_MS
      );
      return sendJson(res, 200, {
        success: true,
        storageMode: 'vercel-registry',
        plans: [{ id: 'plan_local_first', name: 'Local-First PC Edition', priceText: 'Free / Local PC', maxDevices: 1, maxMonthlySubmissions: 999999 }],
        state: {
          shops: [{ ...DEFAULT_SHOP, ...rec, tunnelOnline }],
          submissions: [],
          auditEvents: []
        }
      });
    }

    return sendJson(res, 404, { success: false, error: 'Not Found' });
  } catch (err) {
    return sendJson(res, 500, { success: false, error: err.message || 'Server error' });
  }
};
