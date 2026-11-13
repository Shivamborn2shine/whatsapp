/**
 * Scan Drop — Shopkeeper PC Local Storage & Cloudflare Tunnel Agent
 *
 * Stores all customer files directly on the shopkeeper's local SSD/HDD
 * inside ./ScanDrop_Local_Inbox/ and exposes a fast local workspace at
 * http://localhost:3838/workspace/ while bridging customer uploads from
 * https://scandropdone.netlify.app directly to this PC via cloudflared.
 */

const http = require('node:http');
const https = require('node:https');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawn, execSync } = require('node:child_process');

const PORT = Number(process.env.PORT || 3838);
const CLOUD_URL = (process.env.SCANDROP_CLOUD_URL || 'https://scandropdone.netlify.app').replace(/\/$/, '');
const INBOX_DIR = path.resolve(__dirname, 'ScanDrop_Local_Inbox');
const STATE_FILE = path.join(INBOX_DIR, 'state.json');
const BIN_DIR = path.resolve(__dirname, 'bin');
const CLOUDFLARED_EXE = path.join(BIN_DIR, process.platform === 'win32' ? 'cloudflared.exe' : 'cloudflared');

const workspaceHtml = require('./workspace_template');

const PLANS = [
  { id: 'plan_free_trial', name: 'Free / Trial', tier: 'free', priceText: '$0 / 30 days', maxDevices: 1, maxMonthlySubmissions: 50, batchConversion: false, pdfAdvanced: false, printerProfiles: false },
  { id: 'plan_pro_monthly', name: 'Pro Monthly', tier: 'pro', priceText: '$29 / month', maxDevices: 1, maxMonthlySubmissions: 1000, batchConversion: true, pdfAdvanced: true, printerProfiles: true },
  { id: 'plan_business_monthly', name: 'Business Multi-PC', tier: 'business', priceText: '$69 / month', maxDevices: 5, maxMonthlySubmissions: 10000, batchConversion: true, pdfAdvanced: true, printerProfiles: true }
];

let activeTunnelUrl = null;
let tunnelProcess = null;
let heartbeatTimer = null;

if (!fs.existsSync(INBOX_DIR)) fs.mkdirSync(INBOX_DIR, { recursive: true });
if (!fs.existsSync(BIN_DIR)) fs.mkdirSync(BIN_DIR, { recursive: true });

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
        id: 'dev_local_pc_01',
        shopId: 'shop_quickprint_01',
        deviceName: process.env.COMPUTERNAME || 'Shop Counter PC',
        windowsVersion: `${process.platform} ${process.arch}`,
        appVersion: '3.1.0-local',
        status: 'active',
        lastHeartbeatAt: now.toISOString(),
        lastIp: '127.0.0.1 (Local SSD)'
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
        id: 'evt_local_init',
        shopId: 'shop_quickprint_01',
        actorType: 'system',
        eventType: 'LOCAL_AGENT_STARTED',
        description: 'Local PC SSD storage initialized',
        createdAt: now.toISOString()
      }
    ]
  };
}

function loadState() {
  let state;
  if (!fs.existsSync(STATE_FILE)) {
    state = getDefaultState();
    saveState(state);
    return state;
  }
  try {
    state = JSON.parse(fs.readFileSync(STATE_FILE, 'utf8'));
  } catch {
    state = getDefaultState();
  }

  // Enforce 24-hour local retention cleanup
  const nowIso = new Date().toISOString();
  let changed = false;
  for (const sub of state.submissions || []) {
    if (sub.status !== 'expired' && sub.expiresAt && sub.expiresAt <= nowIso) {
      sub.status = 'expired';
      const subDir = path.join(INBOX_DIR, sub.id);
      if (fs.existsSync(subDir)) {
        try { fs.rmSync(subDir, { recursive: true, force: true }); } catch {}
      }
      state.auditEvents.unshift({
        id: 'evt_' + Date.now() + '_' + sub.id,
        shopId: sub.shopId,
        actorType: 'system',
        eventType: 'RETENTION_PURGED',
        description: `Submission ${sub.id} reached 24-hour retention limit and was removed from local disk.`,
        createdAt: nowIso
      });
      changed = true;
    }
  }
  if (changed) saveState(state);
  return state;
}

function saveState(state) {
  if (state.auditEvents && state.auditEvents.length > 200) {
    state.auditEvents.length = 200;
  }
  fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
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

function sendJson(res, status, data) {
  const body = JSON.stringify(data);
  res.writeHead(status, {
    'Content-Type': 'application/json',
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
    'Access-Control-Allow-Headers': 'Content-Type, X-Requested-With'
  });
  res.end(body);
}

function readJsonBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on('data', c => chunks.push(c));
    req.on('end', () => {
      try {
        const raw = Buffer.concat(chunks).toString('utf8');
        resolve(raw ? JSON.parse(raw) : {});
      } catch (err) {
        reject(err);
      }
    });
    req.on('error', reject);
  });
}

function readRawBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on('data', c => chunks.push(c));
    req.on('end', () => resolve(Buffer.concat(chunks)));
    req.on('error', reject);
  });
}

/**
 * Minimal, binary-safe multipart/form-data parser using Node Buffers.
 */
function parseMultipart(buffer, contentType) {
  const match = contentType && contentType.match(/boundary=(?:"([^"]+)"|([^;\s]+))/i);
  if (!match) throw new Error('Missing multipart boundary');
  const boundary = '--' + (match[1] || match[2]);
  const boundaryBuf = Buffer.from(boundary);
  const fields = {};
  const files = [];

  let start = buffer.indexOf(boundaryBuf);
  while (start !== -1) {
    start += boundaryBuf.length;
    if (buffer[start] === 45 && buffer[start + 1] === 45) break; // '--' end
    if (buffer[start] === 13 && buffer[start + 1] === 10) start += 2; // CRLF

    const next = buffer.indexOf(boundaryBuf, start);
    if (next === -1) break;

    let partEnd = next;
    if (buffer[partEnd - 2] === 13 && buffer[partEnd - 1] === 10) partEnd -= 2;

    const headerEnd = buffer.indexOf(Buffer.from('\r\n\r\n'), start);
    if (headerEnd === -1 || headerEnd > partEnd) {
      start = next;
      continue;
    }

    const headerStr = buffer.slice(start, headerEnd).toString('utf8');
    const bodyBuf = buffer.slice(headerEnd + 4, partEnd);

    const nameMatch = headerStr.match(/name="([^"]+)"/i);
    const filenameMatch = headerStr.match(/filename="([^"]*)"/i);
    const typeMatch = headerStr.match(/Content-Type:\s*([^\r\n]+)/i);

    if (nameMatch) {
      if (filenameMatch && filenameMatch[1]) {
        files.push({
          fieldName: nameMatch[1],
          originalName: path.basename(filenameMatch[1]),
          mimeType: typeMatch ? typeMatch[1].trim() : 'application/octet-stream',
          buffer: bodyBuf
        });
      } else {
        fields[nameMatch[1]] = bodyBuf.toString('utf8');
      }
    }
    start = next;
  }

  return { fields, files };
}

function sanitizeFileName(name) {
  return path.basename(name || 'file.bin').replace(/[<>:"/\\|?*\x00-\x1f]/g, '_');
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, `http://${req.headers.host || 'localhost'}`);
  const route = url.pathname;

  if (req.method === 'OPTIONS') {
    res.writeHead(204, {
      'Access-Control-Allow-Origin': '*',
      'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
      'Access-Control-Allow-Headers': 'Content-Type, X-Requested-With'
    });
    return res.end();
  }

  try {
    // Workspace UI (served locally at 0 ms latency)
    if ((route === '/' || route === '/workspace' || route === '/workspace/') && req.method === 'GET') {
      if (route === '/') {
        res.writeHead(302, { Location: '/workspace/' });
        return res.end();
      }
      res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
      return res.end(workspaceHtml);
    }

    // Health & Tunnel status
    if (route === '/api/health' && req.method === 'GET') {
      return sendJson(res, 200, {
        success: true,
        storageMode: 'local-pc',
        inboxDir: INBOX_DIR,
        tunnelUrl: activeTunnelUrl,
        tunnelOnline: Boolean(activeTunnelUrl)
      });
    }

    // GET /api/state
    if (route === '/api/state' && req.method === 'GET') {
      const state = loadState();
      if (state.shops[0]) {
        state.shops[0].tunnelUrl = activeTunnelUrl;
        state.shops[0].tunnelOnline = Boolean(activeTunnelUrl);
      }
      return sendJson(res, 200, {
        success: true,
        plans: PLANS,
        state,
        storageMode: 'local-pc',
        tunnelUrl: activeTunnelUrl,
        tunnelOnline: Boolean(activeTunnelUrl)
      });
    }

    // POST /api/direct-upload (Receives FormData directly from customer browser via Tunnel!)
    if (route === '/api/direct-upload' && req.method === 'POST') {
      const contentType = req.headers['content-type'] || '';
      const rawBuf = await readRawBody(req);
      const { fields, files } = parseMultipart(rawBuf, contentType);

      if (!files.length) {
        return sendJson(res, 400, { success: false, error: 'No files attached.' });
      }

      const state = loadState();
      const shop = state.shops.find(s => s.id === fields.shopId || s.slug === fields.shopSlug) || state.shops[0];
      if (!shop) return sendJson(res, 404, { success: false, error: 'Shop not found.' });

      let receiptId = 'SD-' + Math.floor(1000 + Math.random() * 9000);
      while (state.submissions.some(s => s.id === receiptId)) {
        receiptId = 'SD-' + Math.floor(1000 + Math.random() * 9000);
      }

      const subDir = path.join(INBOX_DIR, receiptId);
      fs.mkdirSync(subDir, { recursive: true });

      const now = new Date();
      const expiresAt = new Date(now.getTime() + 24 * 60 * 60 * 1000).toISOString();
      let totalSizeBytes = 0;

      const fileRecords = files.map((f, idx) => {
        const fileId = `f_${Date.now()}_${idx}_${crypto.randomBytes(3).toString('hex')}`;
        const safeName = sanitizeFileName(f.originalName);
        const diskPath = path.join(subDir, `${fileId}_${safeName}`);
        fs.writeFileSync(diskPath, f.buffer);
        const sha256Hash = crypto.createHash('sha256').update(f.buffer).digest('hex');
        totalSizeBytes += f.buffer.length;

        return {
          id: fileId,
          submissionId: receiptId,
          originalName: f.originalName,
          mimeType: f.mimeType || 'application/octet-stream',
          fileExtension: '.' + (f.originalName.split('.').pop() || '').toLowerCase(),
          sizeBytes: f.buffer.length,
          localPath: diskPath,
          sha256Hash,
          status: 'local-ssd',
          createdAt: now.toISOString(),
          expiresAt
        };
      });

      state.submissions.unshift({
        id: receiptId,
        shopId: shop.id,
        customerName: (fields.customerName || '').trim() || 'Anonymous',
        instructions: (fields.instructions || '').trim(),
        status: 'incoming',
        files: fileRecords,
        totalSizeBytes,
        createdAt: now.toISOString(),
        expiresAt
      });

      addAudit(
        state,
        shop.id,
        'customer',
        'DIRECT_TUNNEL_UPLOAD',
        `Direct-to-PC upload ${receiptId} (${fileRecords.length} file(s), ${(totalSizeBytes / 1024).toFixed(1)} KB saved to SSD)`
      );
      saveState(state);
      console.log(`⚡ Received ${receiptId} (${fileRecords.length} files, ${(totalSizeBytes / 1024).toFixed(1)} KB) -> ${subDir}`);

      return sendJson(res, 200, {
        success: true,
        receiptId,
        expiresAt,
        storageMode: 'local-pc',
        fileCount: fileRecords.length
      });
    }

    // GET /api/file?id=... (Streams directly from shopkeeper's local SSD)
    if (route === '/api/file' && req.method === 'GET') {
      const fileId = url.searchParams.get('id');
      const download = url.searchParams.get('download') === '1';
      if (!fileId) return sendJson(res, 400, { error: 'Missing file id' });

      const state = loadState();
      let foundFile = null;
      for (const s of state.submissions) {
        const f = (s.files || []).find(x => x.id === fileId);
        if (f) { foundFile = f; break; }
      }
      if (!foundFile || !foundFile.localPath || !fs.existsSync(foundFile.localPath)) {
        return sendJson(res, 404, { error: 'File not found on local disk' });
      }

      const stat = fs.statSync(foundFile.localPath);
      res.writeHead(200, {
        'Content-Type': foundFile.mimeType || 'application/octet-stream',
        'Content-Length': stat.size,
        'Content-Disposition': `${download ? 'attachment' : 'inline'}; filename="${encodeURIComponent(foundFile.originalName)}"`,
        'Access-Control-Allow-Origin': '*'
      });
      return fs.createReadStream(foundFile.localPath).pipe(res);
    }

    // POST /api/submission/status
    if (route === '/api/submission/status' && req.method === 'POST') {
      const { submissionId, status } = await readJsonBody(req);
      const state = loadState();
      const sub = state.submissions.find(s => s.id === submissionId);
      if (!sub) return sendJson(res, 404, { success: false, error: 'Submission not found' });
      sub.status = status;
      addAudit(state, sub.shopId, 'shopkeeper', 'SUBMISSION_STATUS', `Marked ${submissionId} as ${status}`);
      saveState(state);
      return sendJson(res, 200, { success: true, submission: sub });
    }

    // POST /api/submission/delete
    if (route === '/api/submission/delete' && req.method === 'POST') {
      const { submissionId } = await readJsonBody(req);
      const state = loadState();
      const idx = state.submissions.findIndex(s => s.id === submissionId);
      if (idx === -1) return sendJson(res, 404, { success: false, error: 'Submission not found' });
      const [removed] = state.submissions.splice(idx, 1);
      const subDir = path.join(INBOX_DIR, removed.id);
      if (fs.existsSync(subDir)) {
        try { fs.rmSync(subDir, { recursive: true, force: true }); } catch {}
      }
      addAudit(state, removed.shopId, 'shopkeeper', 'SUBMISSION_DELETED', `Deleted ${submissionId} from local disk`);
      saveState(state);
      return sendJson(res, 200, { success: true });
    }

    // POST /api/pair
    if (route === '/api/pair' && req.method === 'POST') {
      const { pairingCode, deviceName } = await readJsonBody(req);
      const state = loadState();
      const shop = state.shops[0];
      if (pairingCode && shop) shop.pairingCode = pairingCode;
      if (deviceName && state.devices[0]) state.devices[0].deviceName = deviceName;
      saveState(state);
      if (activeTunnelUrl) await registerTunnelWithCloud(activeTunnelUrl);
      return sendJson(res, 200, { success: true, shop, device: state.devices[0] });
    }

    sendJson(res, 404, { error: 'Not Found' });
  } catch (err) {
    console.error('Local Agent Error:', err);
    sendJson(res, 500, { success: false, error: err.message });
  }
});

// ============================================================================
// Cloudflare Tunnel (cloudflared) Auto-Installer & Lifecycle Manager
// ============================================================================

function findCloudflaredCommand() {
  try {
    execSync(process.platform === 'win32' ? 'where.exe cloudflared' : 'which cloudflared', { stdio: 'ignore' });
    return 'cloudflared';
  } catch {}
  if (fs.existsSync(CLOUDFLARED_EXE)) return CLOUDFLARED_EXE;
  return null;
}

function downloadFileFollowRedirects(url, destPath) {
  return new Promise((resolve, reject) => {
    const get = (targetUrl) => {
      https.get(targetUrl, { headers: { 'User-Agent': 'ScanDrop-LocalAgent' } }, (res) => {
        if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
          return get(res.headers.location);
        }
        if (res.statusCode !== 200) {
          return reject(new Error(`HTTP ${res.statusCode} downloading cloudflared`));
        }
        const file = fs.createWriteStream(destPath);
        res.pipe(file);
        file.on('finish', () => file.close(resolve));
        file.on('error', reject);
      }).on('error', reject);
    };
    get(url);
  });
}

async function ensureCloudflared() {
  const existing = findCloudflaredCommand();
  if (existing) return existing;

  if (process.platform === 'win32') {
    const url = 'https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe';
    console.log('⬇️  Downloading cloudflared.exe (one-time setup)...');
    await downloadFileFollowRedirects(url, CLOUDFLARED_EXE);
    console.log('✅ cloudflared.exe downloaded to', CLOUDFLARED_EXE);
    return CLOUDFLARED_EXE;
  }

  throw new Error('cloudflared not found in PATH. Please install cloudflared.');
}

async function registerTunnelWithCloud(tunnelUrl) {
  const state = loadState();
  const shop = state.shops[0];
  if (!shop) return;

  try {
    const res = await fetch(`${CLOUD_URL}/api/tunnel/register`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        shopId: shop.id,
        pairingCode: shop.pairingCode,
        tunnelUrl
      })
    });
    if (res.ok) {
      console.log(`🌐 Registered tunnel with ${CLOUD_URL}: ${tunnelUrl}`);
    } else {
      console.warn(`⚠️  Failed to register tunnel (${res.status})`);
    }
  } catch (err) {
    console.warn('⚠️  Could not reach cloud registry:', err.message);
  }
}

async function startTunnel() {
  try {
    const bin = await ensureCloudflared();
    console.log(`🚇 Starting Cloudflare Tunnel -> http://localhost:${PORT} ...`);
    tunnelProcess = spawn(bin, ['tunnel', '--url', `http://localhost:${PORT}`, '--no-autoupdate'], {
      stdio: ['ignore', 'pipe', 'pipe']
    });

    const handleOutput = (chunk) => {
      const text = chunk.toString('utf8');
      const match = text.match(/https:\/\/[a-z0-9-]+\.trycloudflare\.com/i);
      if (match && match[0] !== activeTunnelUrl) {
        activeTunnelUrl = match[0];
        console.log(`⚡ Tunnel Live: ${activeTunnelUrl}`);
        registerTunnelWithCloud(activeTunnelUrl);
        if (heartbeatTimer) clearInterval(heartbeatTimer);
        heartbeatTimer = setInterval(() => {
          if (activeTunnelUrl) registerTunnelWithCloud(activeTunnelUrl);
        }, 30000);
      }
    };

    tunnelProcess.stdout.on('data', handleOutput);
    tunnelProcess.stderr.on('data', handleOutput);
    tunnelProcess.on('exit', (code) => {
      console.warn(`Tunnel exited with code ${code}. Restarting in 5s...`);
      activeTunnelUrl = null;
      setTimeout(startTunnel, 5000);
    });
  } catch (err) {
    console.warn('⚠️  Tunnel unavailable:', err.message);
  }
}

async function shutdown() {
  if (heartbeatTimer) clearInterval(heartbeatTimer);
  if (tunnelProcess) tunnelProcess.kill();
  try {
    const state = loadState();
    const shop = state.shops[0];
    if (shop) {
      await fetch(`${CLOUD_URL}/api/tunnel/disconnect`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ shopId: shop.id })
      });
    }
  } catch {}
  process.exit(0);
}

process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);

server.listen(PORT, () => {
  console.log('=====================================================');
  console.log(`⚡ Scan Drop Local PC Agent running at http://localhost:${PORT}/workspace/`);
  console.log(`💾 Local SSD Storage Folder: ${INBOX_DIR}`);
  console.log(`🔗 Cloud QR Directory: ${CLOUD_URL}/shop/quickprint`);
  console.log('=====================================================');
  startTunnel();
});
