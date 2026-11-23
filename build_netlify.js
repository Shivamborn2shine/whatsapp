const fs = require('fs');
const path = require('path');

console.log('🚀 Building Scan Drop Cloud-Connected Portals for Netlify...');

const outDirs = [
  path.resolve(__dirname, 'dist_netlify'),
  path.resolve(__dirname, 'public'),
  path.resolve(__dirname)
];

for (const baseDir of outDirs) {
  fs.mkdirSync(baseDir, { recursive: true });
  fs.mkdirSync(path.join(baseDir, 'superadmin'), { recursive: true });
  fs.mkdirSync(path.join(baseDir, 'workspace'), { recursive: true });
}

// ============================================================================
// 1. CUSTOMER UPLOAD PORTAL (index.html) - REAL CLOUD UPLOAD (/api/upload)
// ============================================================================
const customerHtml = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
  <title>Scan Drop — Send Files</title>
  <link rel="icon" href="data:image/svg+xml,<svg xmlns=%22http://www.w3.org/2000/svg%22 viewBox=%220 0 100 100%22><text y=%22.9em%22 font-size=%2290%22>⚡</text></svg>">
  <style>
    :root {
      --primary: #2563eb;
      --primary-hover: #1d4ed8;
      --bg: #f8fafc;
      --card-bg: #ffffff;
      --text: #0f172a;
      --text-muted: #64748b;
      --border: #e2e8f0;
      --success: #10b981;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; }
    body { background-color: var(--bg); color: var(--text); padding: 16px; min-height: 100vh; display: flex; flex-direction: column; align-items: center; }
    .container { width: 100%; max-width: 480px; margin: 12px auto 0; }
    .header { text-align: center; margin-bottom: 18px; }
    .logo-badge { display: inline-flex; align-items: center; gap: 6px; background: #eff6ff; color: var(--primary); padding: 6px 14px; border-radius: 9999px; font-weight: 700; font-size: 13px; margin-bottom: 10px; }
    .shop-title { font-size: 22px; font-weight: 800; color: #1e293b; margin-bottom: 4px; }
    .shop-tagline { font-size: 13px; color: var(--text-muted); }
    .card { background: var(--card-bg); border-radius: 18px; padding: 22px; box-shadow: 0 4px 20px -2px rgba(0,0,0,0.06); border: 1px solid var(--border); margin-bottom: 16px; }
    .drop-heading { font-size: 18px; font-weight: 800; color: #0f172a; margin-bottom: 12px; text-align: center; }
    .dropzone { border: 2px dashed #cbd5e1; border-radius: 14px; padding: 28px 16px; text-align: center; cursor: pointer; transition: all 0.2s ease; background: #fafafa; }
    .dropzone:hover, .dropzone.dragover { border-color: var(--primary); background: #f0f7ff; }
    .dropzone-icon { width: 44px; height: 44px; margin: 0 auto 8px; color: var(--primary); display: block; }
    .dropzone-text { font-size: 15px; font-weight: 700; color: #334155; }
    .dropzone-sub { font-size: 12px; color: var(--text-muted); margin-top: 6px; line-height: 1.45; }
    .file-list { margin-top: 14px; display: flex; flex-direction: column; gap: 8px; }
    .file-item { display: flex; flex-direction: column; gap: 6px; background: #f8fafc; border: 1px solid var(--border); padding: 10px 12px; border-radius: 10px; font-size: 13px; }
    .file-row-top { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
    .file-name { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; flex: 1; }
    .file-size { font-size: 11px; color: var(--text-muted); white-space: nowrap; }
    .file-remove { color: #ef4444; cursor: pointer; font-weight: bold; padding: 2px 6px; border: none; background: none; font-size: 15px; }
    .file-mini-bar { height: 4px; background: #e2e8f0; border-radius: 99px; overflow: hidden; display: none; }
    .file-mini-fill { height: 100%; background: var(--primary); width: 0%; transition: width 0.15s; }
    .total-summary { display: none; margin-top: 10px; padding: 8px 12px; background: #eff6ff; border: 1px solid #bfdbfe; border-radius: 10px; font-size: 12px; font-weight: 700; color: #1d4ed8; justify-content: space-between; }
    .warn-banner { background: #fef3c7; border: 1px solid #fde68a; color: #92400e; padding: 10px 12px; border-radius: 10px; font-size: 12px; margin-top: 12px; display: none; line-height: 1.4; }
    .form-group { margin-top: 14px; }
    label { display: block; font-size: 13px; font-weight: 600; color: #475569; margin-bottom: 6px; }
    input, textarea { width: 100%; padding: 12px 14px; border: 1px solid var(--border); border-radius: 10px; font-size: 14px; background: #fff; color: var(--text); outline: none; }
    input:focus, textarea:focus { border-color: var(--primary); box-shadow: 0 0 0 3px rgba(37,99,235,0.15); }
    .btn-submit { width: 100%; background: var(--primary); color: white; border: none; border-radius: 12px; padding: 15px; font-size: 16px; font-weight: 700; cursor: pointer; margin-top: 18px; transition: background 0.2s; box-shadow: 0 4px 12px rgba(37,99,235,0.25); }
    .btn-submit:hover { background: var(--primary-hover); }
    .btn-submit:disabled { background: #94a3b8; cursor: not-allowed; box-shadow: none; }
    .progress-bar { width: 100%; height: 9px; background: #e2e8f0; border-radius: 9999px; overflow: hidden; margin-top: 14px; display: none; }
    .progress-fill { height: 100%; background: var(--primary); width: 0%; transition: width 0.2s; }
    .progress-label { font-size: 12px; color: #475569; margin-top: 6px; text-align: center; display: none; font-weight: 600; }
    .receipt-container { display: none; text-align: center; }
    .receipt-badge { font-size: 36px; font-weight: 900; color: var(--primary); letter-spacing: 3px; margin: 12px 0; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .retention-alert { background: #fef3c7; border: 1px solid #fde68a; color: #92400e; padding: 12px; border-radius: 10px; font-size: 12px; line-height: 1.4; margin-top: 16px; text-align: left; }
    .error-banner { background: #fee2e2; border: 1px solid #fecaca; color: #991b1b; padding: 12px; border-radius: 10px; font-size: 13px; margin-bottom: 14px; display: none; line-height: 1.4; }
    .footer { text-align: center; font-size: 11px; color: var(--text-muted); margin-top: auto; padding: 16px 0; }
  </style>
</head>
<body>
  <div class="container">
    <div class="header">
      <div class="logo-badge">⚡ Scan Drop</div>
      <h1 class="shop-title" id="shopTitle">Loading...</h1>
      <p class="shop-tagline" id="shopTagline">Scan. Send. Done.</p>
    </div>

    <div class="error-banner" id="shopUnavailableBanner"></div>

    <div class="card" id="uploadCard">
      <div class="drop-heading">Drop your files here</div>
      <form id="uploadForm">
        <input type="file" id="fileInput" multiple style="display:none;">
        <div class="dropzone" id="dropzone" onclick="document.getElementById('fileInput').click()">
          <svg class="dropzone-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12"/>
          </svg>
          <div class="dropzone-text">Tap to choose files</div>
          <div class="dropzone-sub" id="supportedFormatsHint">Supported: PDF, DOCX, DOC, XLSX, PPTX, TXT, JPG, PNG, WEBP, BMP</div>
        </div>

        <div class="warn-banner" id="inlineWarning"></div>
        <div class="file-list" id="fileList"></div>
        <div class="total-summary" id="totalSummary">
          <span id="totalCountLabel">0 files</span>
          <span id="totalSizeLabel">0 KB</span>
        </div>

        <div class="form-group">
          <label for="customerName">Your Name (Optional)</label>
          <input type="text" id="customerName" placeholder="e.g. Rahul Sharma">
        </div>

        <div class="form-group">
          <label for="instructions">Instructions / Message (Optional)</label>
          <textarea id="instructions" rows="2" placeholder="e.g. Print two copies, color, A4"></textarea>
        </div>

        <div class="progress-bar" id="progressBar">
          <div class="progress-fill" id="progressFill"></div>
        </div>
        <div class="progress-label" id="progressLabel"></div>

        <button type="submit" class="btn-submit" id="submitBtn" disabled>Send Files</button>
      </form>
    </div>

    <!-- Receipt Display -->
    <div class="card receipt-container" id="receiptCard">
      <div style="font-size: 46px; color: var(--success); margin-bottom: 6px;">✓</div>
      <h2 style="font-size: 20px; font-weight: 800;">Files sent successfully!</h2>
      <p style="font-size: 13px; color: var(--text-muted); margin-top: 4px;">Show this receipt code to the shopkeeper</p>
      
      <div class="receipt-badge" id="receiptIdText">------</div>

      <div style="background: #f8fafc; border: 1px solid var(--border); border-radius: 12px; padding: 14px; text-align: left; font-size: 13px;">
        <div style="display: flex; justify-content: space-between; margin-bottom: 6px;">
          <span style="color: var(--text-muted);">Shop:</span>
          <strong id="receiptShopName">-</strong>
        </div>
        <div style="display: flex; justify-content: space-between; margin-bottom: 6px;">
          <span style="color: var(--text-muted);">Files Sent:</span>
          <strong id="receiptFileCount">0</strong>
        </div>
        <div style="display: flex; justify-content: space-between; margin-bottom: 6px;">
          <span style="color: var(--text-muted);">Total Size:</span>
          <strong id="receiptTotalSize">0 KB</strong>
        </div>
        <div style="display: flex; justify-content: space-between; margin-bottom: 6px;">
          <span style="color: var(--text-muted);">Timestamp:</span>
          <strong id="receiptTimestamp">-</strong>
        </div>
        <div style="display: flex; justify-content: space-between;">
          <span style="color: var(--text-muted);">Status:</span>
          <strong id="receiptStatus" style="color: var(--success);">Received on Shop PC</strong>
        </div>
      </div>

      <div class="retention-alert">
        <strong>🔒 24-Hour Privacy Retention:</strong> Your files are stored directly on the shop computer and automatically deleted after retention expiry. No account was created.
      </div>

      <button onclick="resetUploadForm()" class="btn-submit" style="background: #f1f5f9; color: #334155; box-shadow: none; margin-top: 16px;">
        Send More Files
      </button>
    </div>

    <!-- Receipt Status Lookup -->
    <div class="card" id="lookupCard" style="padding: 16px 20px;">
      <div style="font-size: 13px; font-weight: 700; color: #334155; margin-bottom: 8px;">Check Receipt Status</div>
      <div style="display: flex; gap: 8px;">
        <input type="text" id="lookupInput" placeholder="Enter 6-character code (e.g. YG3F9Q)" style="padding: 9px 12px; font-size: 13px; text-transform: uppercase;">
        <button type="button" onclick="trackReceipt()" style="background: #334155; color: #fff; border: none; border-radius: 10px; padding: 0 16px; font-size: 13px; font-weight: 600; cursor: pointer;">Check</button>
      </div>
      <div id="lookupResult" style="margin-top: 10px; font-size: 12px; display: none;"></div>
    </div>

    <div class="footer">
      Powered by <strong>Scan Drop</strong> • Scan. Send. Done.
    </div>
  </div>

  <script>
    const BLOCKED_EXTS = ['.exe', '.dll', '.bat', '.cmd', '.msi', '.vbs', '.js', '.ps1', '.scr', '.com', '.hta', '.jar', '.sh'];
    let allowedExts = ['.pdf', '.docx', '.doc', '.xlsx', '.xls', '.pptx', '.ppt', '.txt', '.csv', '.rtf', '.jpg', '.jpeg', '.png', '.webp', '.bmp', '.gif', '.heic', '.heif', '.mp4', '.mov', '.m4v', '.zip'];
    let maxFileBytes = 500 * 1024 * 1024;
    let maxSubmissionBytes = 500 * 1024 * 1024;
    let receiverChunkSize = 4 * 1024 * 1024; // 4 MiB (< 8 MiB per PRD Sec 8.5)
    let selectedFiles = [];
    let activeShop = null;
    let activeReceiverBase = '';
    let isSubmitting = false;
    let pendingIdempotencyKey = '';

    function fmtSize(bytes) {
      if (bytes < 1024) return bytes + ' B';
      if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
      return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
    }

    function showInlineWarning(msg) {
      const el = document.getElementById('inlineWarning');
      if (!msg) { el.style.display = 'none'; el.textContent = ''; return; }
      el.textContent = msg;
      el.style.display = 'block';
    }

    function applyReceiverPolicy(info) {
      if (!info) return;
      if (info.maxFileSizeBytes) maxFileBytes = Number(info.maxFileSizeBytes);
      if (info.maxSubmissionBytes) maxSubmissionBytes = Number(info.maxSubmissionBytes);
      if (info.chunkSizeBytes) receiverChunkSize = Math.min(Number(info.chunkSizeBytes), 7 * 1024 * 1024);
      if (info.allowedExtensions) {
        allowedExts = String(info.allowedExtensions).split(',').map(x => x.trim().toLowerCase()).filter(Boolean);
      }
      const extLabels = allowedExts.map(x => x.replace('.', '').toUpperCase()).join(', ');
      document.getElementById('supportedFormatsHint').textContent =
        'Supported: ' + extLabels + ' • Max ' + Math.round(maxFileBytes / (1024 * 1024)) + ' MB / file';
    }

    function disableUploadUi(reason) {
      const b = document.getElementById('shopUnavailableBanner');
      b.style.display = 'block';
      b.textContent = reason;
      document.getElementById('submitBtn').disabled = true;
      document.getElementById('dropzone').style.pointerEvents = 'none';
      document.getElementById('dropzone').style.opacity = '0.5';
    }

    async function probeReceiverHealth(baseUrl) {
      const ctrl = new AbortController();
      const timer = setTimeout(() => ctrl.abort(), 5000);
      try {
        const res = await fetch(baseUrl + '/api/health', {
          method: 'GET',
          signal: ctrl.signal,
          headers: { 'X-Pinggy-No-Screen': 'true', 'Bypass-Tunnel-Reminder': 'true' }
        });
        if (!res.ok) return null;
        return await res.json();
      } catch {
        return null;
      } finally {
        clearTimeout(timer);
      }
    }

    async function resolveCurrentShop() {
      try {
        const params = new URLSearchParams(window.location.search);
        let slug = params.get('shop') || '';
        if (!slug && window.location.pathname.startsWith('/shop/')) {
          slug = window.location.pathname.replace('/shop/', '').split('/')[0];
        }

        const res = await fetch('/api/shop?slug=' + encodeURIComponent(slug));
        const data = await res.json();
        activeShop = data.shop;

        if (!activeShop) {
          document.getElementById('shopTitle').textContent = 'Shop Not Found';
          disableUploadUi('Shop not found. Please scan a valid Scan Drop QR code.');
          document.getElementById('uploadCard').style.display = 'none';
          return;
        }

        document.getElementById('shopTitle').textContent = activeShop.name;
        document.getElementById('shopTagline').textContent = 'Scan. Send. Done.';

        if (activeShop.directReceiver) {
          activeReceiverBase = '';
          applyReceiverPolicy(activeShop);
          if (activeShop.acceptingUploads === false) {
            disableUploadUi('This shop is currently paused and not accepting uploads right now.');
          }
          return;
        }

        if (activeShop.subscriptionStatus === 'suspended' || activeShop.subscriptionStatus === 'revoked' || activeShop.subscriptionStatus === 'paused') {
          disableUploadUi('This shop is currently not accepting uploads.');
          return;
        }

        if (activeShop.tunnelOnline && activeShop.tunnelUrl) {
          const base = activeShop.tunnelUrl.replace(/[/]+$/, '');
          const health = await probeReceiverHealth(base);
          if (health && (health.ok || health.success)) {
            activeReceiverBase = base;
            applyReceiverPolicy(health);
            document.getElementById('shopTagline').textContent = 'Scan. Send. Done. • ⚡ Connected to Shop PC';
            if (health.acceptingUploads === false) {
              disableUploadUi('This shop has temporarily paused receiving new uploads. Please ask the shopkeeper.');
            }
            return;
          }
        }

        disableUploadUi('Shop PC is currently offline or unreachable. Please ask the shopkeeper to open Scan Drop.');
      } catch (err) {
        document.getElementById('shopTitle').textContent = 'Scan Drop';
        disableUploadUi('Unable to connect to the shop receiver. Please check your internet connection.');
      }
    }

    const fileInput = document.getElementById('fileInput');
    const dropzone = document.getElementById('dropzone');
    const fileListEl = document.getElementById('fileList');
    const submitBtn = document.getElementById('submitBtn');

    fileInput.addEventListener('change', (e) => { handleFiles(Array.from(e.target.files)); fileInput.value = ''; });
    dropzone.addEventListener('dragover', (e) => { e.preventDefault(); dropzone.classList.add('dragover'); });
    dropzone.addEventListener('dragleave', () => dropzone.classList.remove('dragover'));
    dropzone.addEventListener('drop', (e) => {
      e.preventDefault();
      dropzone.classList.remove('dragover');
      if (e.dataTransfer.files) handleFiles(Array.from(e.dataTransfer.files));
    });

    function handleFiles(files) {
      const warnings = [];
      for (const f of files) {
        const ext = f.name.includes('.') ? '.' + f.name.split('.').pop().toLowerCase() : '';
        if (BLOCKED_EXTS.includes(ext)) {
          warnings.push('Blocked unsafe file: ' + f.name);
          continue;
        }
        if (allowedExts.length > 0 && !allowedExts.includes(ext)) {
          warnings.push('Unsupported format: ' + f.name + ' (' + (ext || 'no ext') + ')');
          continue;
        }
        if (f.size <= 0) {
          warnings.push('Skipped empty file: ' + f.name);
          continue;
        }
        if (f.size > maxFileBytes) {
          warnings.push(f.name + ' exceeds ' + Math.round(maxFileBytes / (1024 * 1024)) + ' MB per-file limit.');
          continue;
        }
        const currentTotal = selectedFiles.reduce((a, b) => a + b.size, 0);
        if (currentTotal + f.size > maxSubmissionBytes) {
          warnings.push('Adding ' + f.name + ' exceeds total ' + Math.round(maxSubmissionBytes / (1024 * 1024)) + ' MB limit.');
          continue;
        }
        if (!selectedFiles.some(item => item.name === f.name && item.size === f.size)) {
          selectedFiles.push(f);
        }
      }
      pendingIdempotencyKey = '';
      showInlineWarning(warnings.length > 0 ? warnings.join(' • ') : '');
      renderFileList();
    }

    function removeFile(idx) {
      if (isSubmitting) return;
      selectedFiles.splice(idx, 1);
      pendingIdempotencyKey = '';
      renderFileList();
    }

    function renderFileList() {
      fileListEl.innerHTML = '';
      const totalBar = document.getElementById('totalSummary');
      if (selectedFiles.length === 0) {
        submitBtn.disabled = true;
        totalBar.style.display = 'none';
        return;
      }
      submitBtn.disabled = false;
      const totalBytes = selectedFiles.reduce((a, b) => a + b.size, 0);
      totalBar.style.display = 'flex';
      document.getElementById('totalCountLabel').textContent = selectedFiles.length + (selectedFiles.length === 1 ? ' file selected' : ' files selected');
      document.getElementById('totalSizeLabel').textContent = 'Total: ' + fmtSize(totalBytes);

      selectedFiles.forEach((file, idx) => {
        const item = document.createElement('div');
        item.className = 'file-item';
        item.innerHTML = \`
          <div class="file-row-top">
            <span class="file-name">\${file.name}</span>
            <span class="file-size">\${fmtSize(file.size)}</span>
            <button type="button" class="file-remove" onclick="removeFile(\${idx})" title="Remove">✕</button>
          </div>
          <div class="file-mini-bar" id="fbar_\${idx}"><div class="file-mini-fill" id="ffill_\${idx}"></div></div>
        \`;
        fileListEl.appendChild(item);
      });
    }

    function sendChunkWithRetry(url, sessionId, token, fileId, chunkIndex, byteOffset, blob, onProgress) {
      const maxAttempts = 3;
      const attemptSend = (attempt) => new Promise((resolve, reject) => {
        const xhr = new XMLHttpRequest();
        xhr.open('POST', url, true);
        xhr.setRequestHeader('Content-Type', 'application/octet-stream');
        xhr.setRequestHeader('X-Session-Id', sessionId);
        xhr.setRequestHeader('X-Upload-Token', token);
        xhr.setRequestHeader('X-File-Id', fileId);
        xhr.setRequestHeader('X-Chunk-Index', String(chunkIndex));
        xhr.setRequestHeader('X-Byte-Offset', String(byteOffset));
        xhr.setRequestHeader('X-Pinggy-No-Screen', 'true');
        xhr.setRequestHeader('Bypass-Tunnel-Reminder', 'true');
        xhr.timeout = 90000;
        xhr.upload.onprogress = (ev) => { if (ev.lengthComputable && onProgress) onProgress(ev.loaded); };
        xhr.onload = () => {
          if (xhr.status >= 200 && xhr.status < 300) return resolve();
          let errMsg = 'Upload error (' + xhr.status + ')';
          try { const j = JSON.parse(xhr.responseText); if (j && j.error) errMsg = j.error; } catch {}
          if (xhr.status >= 400 && xhr.status < 500 && xhr.status !== 408 && xhr.status !== 429) {
            return reject(Object.assign(new Error(errMsg), { fatal: true }));
          }
          reject(new Error(errMsg));
        };
        xhr.onerror = () => reject(new Error('Network interrupted'));
        xhr.ontimeout = () => reject(new Error('Chunk timed out'));
        xhr.send(blob);
      }).catch(err => {
        if (err.fatal || attempt >= maxAttempts) throw err;
        return new Promise(r => setTimeout(r, attempt * 1500)).then(() => attemptSend(attempt + 1));
      });
      return attemptSend(1);
    }

    document.getElementById('uploadForm').addEventListener('submit', async (e) => {
      e.preventDefault();
      if (isSubmitting || selectedFiles.length === 0 || !activeShop) return;

      isSubmitting = true;
      submitBtn.disabled = true;
      showInlineWarning('');
      const progressBar = document.getElementById('progressBar');
      const progressFill = document.getElementById('progressFill');
      const progressLabel = document.getElementById('progressLabel');
      progressBar.style.display = 'block';
      progressLabel.style.display = 'block';
      progressFill.style.width = '3%';
      progressLabel.textContent = 'Starting secure upload session...';
      submitBtn.textContent = 'Sending...';

      if (!pendingIdempotencyKey) {
        pendingIdempotencyKey = 'sd_' + Date.now() + '_' + Math.random().toString(36).slice(2, 10);
      }

      try {
        const base = activeReceiverBase;
        const headers = {
          'Content-Type': 'application/json',
          'X-Pinggy-No-Screen': 'true',
          'Bypass-Tunnel-Reminder': 'true',
          'X-Idempotency-Key': pendingIdempotencyKey
        };

        // 1. Initialize upload session on shop PC
        const initRes = await fetch(base + '/api/upload/sessions', {
          method: 'POST',
          headers,
          body: JSON.stringify({
            customerName: document.getElementById('customerName').value.trim(),
            customerNote: document.getElementById('instructions').value.trim(),
            idempotencyKey: pendingIdempotencyKey,
            files: selectedFiles.map((f, idx) => ({
              clientFileId: 'c_' + idx,
              name: f.name,
              size: f.size,
              contentType: f.type || 'application/octet-stream'
            }))
          })
        });
        const initData = await initRes.json().catch(() => ({}));
        if (!initRes.ok || !initData.success) {
          throw new Error(initData.error || 'Shop PC could not start upload session (HTTP ' + initRes.status + ')');
        }

        // Handle idempotent already-committed submission cleanly
        if (initData.alreadyCompleted) {
          showReceipt(initData);
          return;
        }

        const chunkSz = Math.min(initData.chunkSizeBytes || receiverChunkSize, 7 * 1024 * 1024);
        const totalBytes = selectedFiles.reduce((a, b) => a + b.size, 0);
        let committedBytes = 0;

        // 2. Stream each file in < 8 MiB resumable chunks with automatic retry
        for (let fi = 0; fi < selectedFiles.length; fi++) {
          const f = selectedFiles[fi];
          const serverFile = initData.files[fi];
          const fBar = document.getElementById('fbar_' + fi);
          const fFill = document.getElementById('ffill_' + fi);
          if (fBar) fBar.style.display = 'block';

          const chunkCount = Math.max(1, Math.ceil(f.size / chunkSz));
          let fileSent = 0;

          for (let ci = 0; ci < chunkCount; ci++) {
            const start = ci * chunkSz;
            const end = Math.min(f.size, start + chunkSz);
            const slice = f.slice(start, end);

            await sendChunkWithRetry(
              base + '/api/upload/chunk',
              initData.sessionId,
              initData.uploadToken,
              serverFile.fileId,
              ci,
              start,
              slice,
              (loaded) => {
                const fPct = Math.min(100, Math.round(((fileSent + loaded) / f.size) * 100));
                if (fFill) fFill.style.width = fPct + '%';
                const overall = Math.min(95, Math.round(((committedBytes + loaded) / totalBytes) * 95));
                progressFill.style.width = Math.max(4, overall) + '%';
                progressLabel.textContent = 'Uploading ' + f.name + ' (' + overall + '%)';
              }
            );

            fileSent += (end - start);
            committedBytes += (end - start);
            if (fFill) fFill.style.width = Math.min(100, Math.round((fileSent / f.size) * 100)) + '%';
          }
        }

        // 3. Finalize & verify on shop PC (receipt shown ONLY after receiver confirms commit!)
        progressLabel.textContent = 'Verifying file integrity on Shop PC...';
        progressFill.style.width = '97%';

        const cmpRes = await fetch(base + '/api/upload/complete', {
          method: 'POST',
          headers,
          body: JSON.stringify({
            sessionId: initData.sessionId,
            uploadToken: initData.uploadToken
          })
        });
        const cmpData = await cmpRes.json().catch(() => ({}));
        if (!cmpRes.ok || !cmpData.success) {
          throw new Error(cmpData.error || 'Receiver could not verify uploaded files.');
        }

        progressFill.style.width = '100%';
        showReceipt(cmpData);
      } catch (err) {
        showInlineWarning('Upload failed: ' + err.message);
        isSubmitting = false;
        submitBtn.disabled = false;
        submitBtn.textContent = 'Send Files';
        progressBar.style.display = 'none';
        progressLabel.style.display = 'none';
      }
    });

    function showReceipt(data) {
      isSubmitting = false;
      pendingIdempotencyKey = '';
      document.getElementById('uploadCard').style.display = 'none';
      document.getElementById('receiptCard').style.display = 'block';
      document.getElementById('receiptIdText').textContent = data.receiptCode || data.receiptId || '------';
      document.getElementById('receiptShopName').textContent = data.shopName || (activeShop && activeShop.name) || 'Shop PC';
      document.getElementById('receiptFileCount').textContent = data.fileCount || selectedFiles.length;
      document.getElementById('receiptTotalSize').textContent = fmtSize(data.totalSizeBytes || selectedFiles.reduce((a, b) => a + b.size, 0));
      const ts = data.submittedAt ? new Date(data.submittedAt) : new Date();
      document.getElementById('receiptTimestamp').textContent = ts.toLocaleString();
      document.getElementById('receiptStatus').textContent = 'Received on Shop PC';
    }

    function resetUploadForm() {
      selectedFiles = [];
      pendingIdempotencyKey = '';
      isSubmitting = false;
      fileInput.value = '';
      showInlineWarning('');
      renderFileList();
      document.getElementById('progressBar').style.display = 'none';
      document.getElementById('progressLabel').style.display = 'none';
      document.getElementById('progressFill').style.width = '0%';
      submitBtn.textContent = 'Send Files';
      document.getElementById('uploadCard').style.display = 'block';
      document.getElementById('receiptCard').style.display = 'none';
    }

    async function trackReceipt() {
      const code = document.getElementById('lookupInput').value.trim().toUpperCase();
      const resEl = document.getElementById('lookupResult');
      if (!code) return;
      resEl.style.display = 'block';
      resEl.textContent = 'Checking...';
      try {
        const res = await fetch(activeReceiverBase + '/api/receipt?id=' + encodeURIComponent(code), {
          headers: { 'X-Pinggy-No-Screen': 'true', 'Bypass-Tunnel-Reminder': 'true' }
        });
        const data = await res.json();
        if (!res.ok || !data.success) {
          resEl.innerHTML = '<span style="color:#ef4444;">Receipt not found or expired.</span>';
          return;
        }
        const sub = data.submission;
        const fCount = sub.fileCount !== undefined ? sub.fileCount : (sub.files || []).length;
        const remHrs = Math.max(0, (new Date(sub.expiresAt).getTime() - Date.now()) / 3600000).toFixed(1);
        resEl.innerHTML = \`<div style="background:#f1f5f9;padding:10px;border-radius:8px;">
          <strong>\${sub.receiptCode || sub.id}</strong> • Status: <strong style="text-transform:capitalize;color:#2563eb;">\${sub.status}</strong><br>
          Files: \${fCount} • Auto-deletes in \${remHrs} hrs
        </div>\`;
      } catch (err) {
        resEl.innerHTML = '<span style="color:#ef4444;">Could not check receipt status.</span>';
      }
    }

    resolveCurrentShop();
  </script>
</body>
</html>`;

// ============================================================================
// 2. FOUNDER SUPER ADMIN DASHBOARD (superadmin/index.html) - CLOUD API
// ============================================================================
const superadminHtml = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Scan Drop — Founder Super Admin</title>
  <link rel="icon" href="data:image/svg+xml,<svg xmlns=%22http://www.w3.org/2000/svg%22 viewBox=%220 0 100 100%22><text y=%22.9em%22 font-size=%2290%22>👑</text></svg>">
  <style>
    :root {
      --primary: #4f46e5;
      --primary-hover: #4338ca;
      --bg: #0f172a;
      --card-bg: #1e293b;
      --text: #f8fafc;
      --text-muted: #94a3b8;
      --border: #334155;
      --success: #10b981;
      --warning: #f59e0b;
      --danger: #ef4444;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; }
    body { background-color: var(--bg); color: var(--text); min-height: 100vh; display: flex; flex-direction: column; }
    header { background: #0b1120; border-bottom: 1px solid var(--border); padding: 16px 28px; display: flex; align-items: center; justify-content: space-between; }
    .brand { display: flex; align-items: center; gap: 12px; }
    .badge-superadmin { background: #4f46e522; color: #818cf8; border: 1px solid #6366f144; padding: 4px 10px; border-radius: 9999px; font-size: 11px; font-weight: 700; text-transform: uppercase; }
    .title { font-size: 18px; font-weight: 800; }
    .container { max-width: 1300px; margin: 0 auto; padding: 24px 20px; width: 100%; }
    .stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 16px; margin-bottom: 24px; }
    .stat-card { background: var(--card-bg); border: 1px solid var(--border); border-radius: 12px; padding: 18px; }
    .stat-label { font-size: 12px; font-weight: 600; text-transform: uppercase; color: var(--text-muted); margin-bottom: 6px; }
    .stat-val { font-size: 26px; font-weight: 800; color: #fff; }
    .tabs { display: flex; gap: 8px; border-bottom: 1px solid var(--border); margin-bottom: 20px; }
    .tab-btn { background: none; border: none; color: var(--text-muted); font-size: 14px; font-weight: 600; padding: 10px 18px; cursor: pointer; border-bottom: 2px solid transparent; transition: all 0.2s; }
    .tab-btn.active { color: #818cf8; border-color: #818cf8; }
    .card { background: var(--card-bg); border: 1px solid var(--border); border-radius: 14px; padding: 20px; margin-bottom: 24px; overflow-x: auto; }
    .card-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 18px; }
    .card-title { font-size: 16px; font-weight: 700; }
    table { width: 100%; border-collapse: collapse; text-align: left; font-size: 13px; }
    th { padding: 12px 14px; border-bottom: 1px solid var(--border); color: var(--text-muted); font-weight: 600; }
    td { padding: 14px; border-bottom: 1px solid #1e293b; color: #cbd5e1; }
    tr:hover td { background: #243048; }
    .status-pill { display: inline-block; padding: 3px 10px; border-radius: 9999px; font-size: 11px; font-weight: 700; text-transform: capitalize; }
    .status-active { background: #064e3b; color: #34d399; }
    .status-trial { background: #78350f; color: #fbbf24; }
    .status-suspended { background: #451a03; color: #f97316; }
    .status-revoked { background: #7f1d1d; color: #f87171; }
    .btn { background: var(--primary); color: white; border: none; border-radius: 8px; padding: 8px 14px; font-size: 13px; font-weight: 600; cursor: pointer; transition: background 0.2s; }
    .btn:hover { background: var(--primary-hover); }
    .btn-secondary { background: #334155; color: #f8fafc; }
    .btn-secondary:hover { background: #475569; }
    .btn-danger { background: #dc2626; }
    .btn-danger:hover { background: #b91c1c; }
    .btn-sm { padding: 5px 10px; font-size: 11px; border-radius: 6px; }
    .code-box { font-family: monospace; background: #0b1120; padding: 4px 8px; border-radius: 6px; border: 1px solid #1e293b; color: #38bdf8; font-weight: 600; display: inline-flex; align-items: center; gap: 6px; }
    .modal { display: none; position: fixed; inset: 0; background: rgba(0,0,0,0.7); backdrop-filter: blur(4px); align-items: center; justify-content: center; z-index: 100; }
    .modal.open { display: flex; }
    .modal-content { background: var(--card-bg); border: 1px solid var(--border); border-radius: 16px; width: 100%; max-width: 500px; padding: 24px; box-shadow: 0 20px 25px -5px rgba(0,0,0,0.5); }
    .form-group { margin-bottom: 14px; }
    .form-group label { display: block; font-size: 12px; font-weight: 600; color: var(--text-muted); margin-bottom: 6px; }
    .form-group input, .form-group select { width: 100%; padding: 10px 12px; background: #0f172a; border: 1px solid var(--border); border-radius: 8px; color: #fff; font-size: 13px; outline: none; }
    .modal-actions { display: flex; justify-content: flex-end; gap: 10px; margin-top: 20px; }
  </style>
</head>
<body>
  <header>
    <div class="brand">
      <span class="title">⚡ Scan Drop</span>
      <span class="badge-superadmin">Founder SaaS Console</span>
    </div>
    <div style="font-size: 12px; color: var(--text-muted);">Shopkeeper Subscription & Licensing Authority</div>
  </header>

  <div class="container">
    <div class="stats-grid">
      <div class="stat-card"><div class="stat-label">Registered Shops</div><div class="stat-val" id="statShops">-</div></div>
      <div class="stat-card"><div class="stat-label">Active Subscriptions</div><div class="stat-val" id="statSubs" style="color: #34d399;">-</div></div>
      <div class="stat-card"><div class="stat-label">Active Windows PCs</div><div class="stat-val" id="statDevices" style="color: #60a5fa;">-</div></div>
      <div class="stat-card"><div class="stat-label">Total Submissions</div><div class="stat-val" id="statSubmissions">-</div></div>
      <div class="stat-card"><div class="stat-label">Processed Volume</div><div class="stat-val" id="statStorage">-</div></div>
    </div>

    <div class="tabs">
      <button class="tab-btn active" onclick="switchTab('shopsTab', this)">Shops & Subscriptions</button>
      <button class="tab-btn" onclick="switchTab('devicesTab', this)">Windows Device Fleet</button>
      <button class="tab-btn" onclick="switchTab('plansTab', this)">Shopkeeper Pricing Plans</button>
      <button class="tab-btn" onclick="switchTab('auditTab', this)">Audit Events</button>
    </div>

    <div id="shopsTab" class="tab-content">
      <div class="card">
        <div class="card-header">
          <div class="card-title">Registered Shops (B2B Subscribers)</div>
          <button class="btn" onclick="openCreateShopModal()">+ Register New Shop</button>
        </div>
        <table>
          <thead>
            <tr>
              <th>Shop Name / Customer Upload Link</th>
              <th>Shopkeeper & Contact</th>
              <th>Plan Tier</th>
              <th>Status</th>
              <th>Pairing Code</th>
              <th>Active PCs</th>
              <th>Expiry Date</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody id="shopsTableBody"><tr><td colspan="8" style="text-align:center;">Loading...</td></tr></tbody>
        </table>
      </div>
    </div>

    <div id="devicesTab" class="tab-content" style="display:none;">
      <div class="card">
        <div class="card-header"><div class="card-title">Paired Windows Devices Fleet</div></div>
        <table>
          <thead><tr><th>Device Name</th><th>Shop Assigned</th><th>OS / App Version</th><th>Status</th><th>Last Heartbeat</th><th>Last IP</th><th>Actions</th></tr></thead>
          <tbody id="devicesTableBody"></tbody>
        </table>
      </div>
    </div>

    <div id="plansTab" class="tab-content" style="display:none;">
      <div class="card">
        <div class="card-header"><div class="card-title">Shopkeeper SaaS Pricing Plans & Entitlements</div></div>
        <table>
          <thead><tr><th>Plan Name</th><th>Tier</th><th>Shopkeeper Fee</th><th>Max Devices</th><th>Monthly Submissions</th><th>Batch Conversion</th><th>Advanced PDF</th><th>Printer Profiles</th></tr></thead>
          <tbody id="plansTableBody"></tbody>
        </table>
      </div>
    </div>

    <div id="auditTab" class="tab-content" style="display:none;">
      <div class="card">
        <div class="card-header"><div class="card-title">System Audit & Security Trail</div></div>
        <table>
          <thead><tr><th>Timestamp</th><th>Actor</th><th>Event Type</th><th>Description</th></tr></thead>
          <tbody id="auditTableBody"></tbody>
        </table>
      </div>
    </div>
  </div>

  <!-- Create Shop Modal -->
  <div class="modal" id="createShopModal">
    <div class="modal-content">
      <h3 style="margin-bottom: 16px;">Register New Shop</h3>
      <form id="createShopForm">
        <div class="form-group"><label>Shop Business Name</label><input type="text" id="newShopName" placeholder="e.g. Metro Print & Graphics" required></div>
        <div class="form-group"><label>Shop URL Slug (for customer QR)</label><input type="text" id="newShopSlug" placeholder="e.g. metro-print" required></div>
        <div class="form-group"><label>Shopkeeper Name</label><input type="text" id="newShopOwner" placeholder="e.g. Anand Patel" required></div>
        <div class="form-group"><label>Shopkeeper Email</label><input type="email" id="newShopEmail" placeholder="e.g. anand@metroprint.com" required></div>
        <div class="form-group"><label>Shopkeeper Phone</label><input type="text" id="newShopPhone" placeholder="e.g. +91 98765 00000"></div>
        <div class="form-group">
          <label>Subscription Plan</label>
          <select id="newShopPlan">
            <option value="plan_free_trial">Free / 30-Day Trial (1 PC)</option>
            <option value="plan_pro_monthly">Pro Monthly ($29/mo - 1 PC)</option>
            <option value="plan_business_monthly">Business Multi-PC ($69/mo - 5 PCs)</option>
          </select>
        </div>
        <div class="modal-actions">
          <button type="button" class="btn btn-secondary" onclick="closeModal('createShopModal')">Cancel</button>
          <button type="submit" class="btn">Register & Issue License</button>
        </div>
      </form>
    </div>
  </div>

  <!-- Manage License Modal -->
  <div class="modal" id="manageSubModal">
    <div class="modal-content">
      <h3 style="margin-bottom: 16px;">Manage Shopkeeper License</h3>
      <input type="hidden" id="manageShopId">
      <div class="form-group">
        <label>Action</label>
        <select id="manageAction">
          <option value="renew">Renew / Extend License (+30 Days)</option>
          <option value="suspend">Suspend Shop (Pause receiving)</option>
          <option value="reactivate">Reactivate Shop</option>
          <option value="revoke">Revoke License (Terminal)</option>
        </select>
      </div>
      <div class="form-group">
        <label>Plan Tier</label>
        <select id="managePlanId">
          <option value="plan_free_trial">Free / Trial</option>
          <option value="plan_pro_monthly">Pro Monthly ($29/mo)</option>
          <option value="plan_business_monthly">Business Multi-PC ($69/mo)</option>
        </select>
      </div>
      <div class="form-group"><label>Shopkeeper Billing / Payment Reference (Optional)</label><input type="text" id="managePaymentRef" placeholder="e.g. UPI-TXN-984321 / INV-102"></div>
      <div class="modal-actions">
        <button type="button" class="btn btn-secondary" onclick="closeModal('manageSubModal')">Cancel</button>
        <button type="button" class="btn" onclick="saveSubscriptionChange()">Update License</button>
      </div>
    </div>
  </div>

  <script>
    let cachedData = null;

    function switchTab(tabId, btn) {
      document.querySelectorAll('.tab-content').forEach(el => el.style.display = 'none');
      document.querySelectorAll('.tab-btn').forEach(el => el.classList.remove('active'));
      document.getElementById(tabId).style.display = 'block';
      btn.classList.add('active');
      if (cachedData) renderData(cachedData);
    }

    async function fetchAndRender() {
      try {
        const res = await fetch('/api/state');
        const data = await res.json();
        if (data.success) {
          cachedData = data;
          renderData(data);
        }
      } catch (err) {
        console.error('Failed to fetch cloud state:', err);
      }
    }

    function renderData({ state, plans }) {
      document.getElementById('statShops').textContent = state.shops.length;
      document.getElementById('statSubs').textContent = state.shops.filter(s => s.subscriptionStatus === 'active').length;
      document.getElementById('statDevices').textContent = state.devices.filter(d => d.status === 'active').length;
      document.getElementById('statSubmissions').textContent = state.submissions.length;
      const totalBytes = state.submissions.reduce((acc, s) => acc + (s.totalSizeBytes || 0), 0);
      document.getElementById('statStorage').textContent = (totalBytes / (1024 * 1024)).toFixed(2) + ' MB';

      const shopsBody = document.getElementById('shopsTableBody');
      shopsBody.innerHTML = '';
      state.shops.forEach(s => {
        const plan = plans.find(p => p.id === s.planId) || plans[0];
        const devCount = state.devices.filter(d => d.shopId === s.id && d.status === 'active').length;
        const uploadUrl = window.location.origin + '/shop/' + s.slug;
        const tr = document.createElement('tr');
        tr.innerHTML = \`
          <td>
            <strong>\${s.name}</strong><br>
            <a href="\${uploadUrl}" target="_blank" style="color: #60a5fa; font-size: 11px;">\${uploadUrl} ↗</a>
          </td>
          <td>\${s.ownerName}<br><span style="font-size:11px;color:var(--text-muted);">\${s.ownerEmail}</span></td>
          <td><strong>\${plan.name}</strong><br><span style="font-size:11px;color:#818cf8;">\${plan.priceText}</span></td>
          <td><span class="status-pill status-\${s.subscriptionStatus}">\${s.subscriptionStatus}</span></td>
          <td>
            <div class="code-box">
              \${s.pairingCode}
              <button class="btn btn-sm btn-secondary" onclick="copyCode('\${s.pairingCode}')">Copy</button>
            </div>
          </td>
          <td>\${devCount} / \${plan.maxDevices}</td>
          <td>\${new Date(s.expiryDate).toLocaleDateString()}</td>
          <td>
            <div style="display:flex;gap:6px;">
              <button class="btn btn-sm btn-secondary" onclick="openManageSub('\${s.id}', '\${s.planId}')">License</button>
              <button class="btn btn-sm btn-secondary" onclick="regenPairingCode('\${s.id}')">New Code</button>
            </div>
          </td>
        \`;
        shopsBody.appendChild(tr);
      });

      const devBody = document.getElementById('devicesTableBody');
      devBody.innerHTML = '';
      state.devices.forEach(d => {
        const shop = state.shops.find(s => s.id === d.shopId);
        const tr = document.createElement('tr');
        tr.innerHTML = \`
          <td><strong>\${d.deviceName}</strong><br><span style="font-size:11px;color:var(--text-muted);">\${d.id}</span></td>
          <td>\${shop ? shop.name : 'Unknown'}</td>
          <td>\${d.windowsVersion} / v\${d.appVersion}</td>
          <td><span class="status-pill \${d.status === 'active' ? 'status-active' : 'status-revoked'}">\${d.status}</span></td>
          <td>\${new Date(d.lastHeartbeatAt).toLocaleTimeString()}</td>
          <td><code>\${d.lastIp}</code></td>
          <td>
            \${d.status === 'active'
              ? \`<button class="btn btn-sm btn-danger" onclick="deviceAction('\${d.id}', 'revoke_device')">Revoke PC</button>\`
              : \`<button class="btn btn-sm btn-secondary" onclick="deviceAction('\${d.id}', 'restore_device')">Restore</button>\`}
          </td>
        \`;
        devBody.appendChild(tr);
      });

      const plansBody = document.getElementById('plansTableBody');
      plansBody.innerHTML = '';
      plans.forEach(p => {
        const tr = document.createElement('tr');
        tr.innerHTML = \`
          <td><strong>\${p.name}</strong></td>
          <td><span class="status-pill status-active">\${p.tier}</span></td>
          <td>\${p.priceText}</td>
          <td>\${p.maxDevices} PC(s)</td>
          <td>\${p.maxMonthlySubmissions} / mo</td>
          <td>\${p.batchConversion ? '✓ Enabled' : '✕ Disabled'}</td>
          <td>\${p.pdfAdvanced ? '✓ Enabled' : '✕ Disabled'}</td>
          <td>\${p.printerProfiles ? '✓ Enabled' : '✕ Disabled'}</td>
        \`;
        plansBody.appendChild(tr);
      });

      const auditBody = document.getElementById('auditTableBody');
      auditBody.innerHTML = '';
      state.auditEvents.forEach(l => {
        const tr = document.createElement('tr');
        tr.innerHTML = \`
          <td>\${new Date(l.createdAt).toLocaleString()}</td>
          <td><span class="status-pill status-trial">\${l.actorType}</span></td>
          <td><code>\${l.eventType}</code></td>
          <td>\${l.description}</td>
        \`;
        auditBody.appendChild(tr);
      });
    }

    function copyCode(code) { navigator.clipboard.writeText(code); alert('Copied: ' + code); }
    function openCreateShopModal() { document.getElementById('createShopModal').classList.add('open'); }
    function closeModal(id) { document.getElementById(id).classList.remove('open'); }

    document.getElementById('createShopForm').addEventListener('submit', async (e) => {
      e.preventDefault();
      const res = await fetch('/api/superadmin', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          action: 'create_shop',
          name: document.getElementById('newShopName').value.trim(),
          slug: document.getElementById('newShopSlug').value.trim(),
          ownerName: document.getElementById('newShopOwner').value.trim(),
          ownerEmail: document.getElementById('newShopEmail').value.trim(),
          ownerPhone: document.getElementById('newShopPhone').value.trim(),
          planId: document.getElementById('newShopPlan').value
        })
      });
      const data = await res.json();
      if (data.success) {
        closeModal('createShopModal');
        document.getElementById('createShopForm').reset();
        await fetchAndRender();
        alert('Shop registered! Pairing code: ' + data.shop.pairingCode);
      } else {
        alert('Error: ' + data.error);
      }
    });

    async function regenPairingCode(shopId) {
      const res = await fetch('/api/superadmin', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ action: 'regen_code', shopId })
      });
      const data = await res.json();
      if (data.success) {
        await fetchAndRender();
        alert('New pairing code: ' + data.pairingCode);
      }
    }

    function openManageSub(shopId, planId) {
      document.getElementById('manageShopId').value = shopId;
      document.getElementById('managePlanId').value = planId;
      document.getElementById('manageSubModal').classList.add('open');
    }

    async function saveSubscriptionChange() {
      const res = await fetch('/api/superadmin', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          action: 'update_subscription',
          shopId: document.getElementById('manageShopId').value,
          subAction: document.getElementById('manageAction').value,
          planId: document.getElementById('managePlanId').value,
          paymentRef: document.getElementById('managePaymentRef').value.trim()
        })
      });
      const data = await res.json();
      if (data.success) {
        closeModal('manageSubModal');
        await fetchAndRender();
      } else {
        alert('Error: ' + data.error);
      }
    }

    async function deviceAction(deviceId, action) {
      const res = await fetch('/api/superadmin', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ action, deviceId })
      });
      const data = await res.json();
      if (data.success) await fetchAndRender();
    }

    fetchAndRender();
    setInterval(fetchAndRender, 4000);
  </script>
</body>
</html>`;

// ============================================================================
// 3. SHOPKEEPER WORKSPACE (workspace/index.html) - CLOUD SYNCED PANEL
// ============================================================================
const workspaceHtml = require('./workspace_template');

for (const baseDir of outDirs) {
  fs.writeFileSync(path.join(baseDir, 'index.html'), customerHtml);
  fs.writeFileSync(path.join(baseDir, 'superadmin', 'index.html'), superadminHtml);
  fs.writeFileSync(path.join(baseDir, 'workspace', 'index.html'), workspaceHtml);
  fs.writeFileSync(path.join(baseDir, '_redirects'),
`/superadmin   /superadmin/index.html   200
/superadmin/* /superadmin/index.html   200
/workspace    /workspace/index.html    200
/workspace/*  /workspace/index.html    200
/shop/*       /index.html              200
/*            /index.html              200
`);

  // Publish version.json and ScanDrop.exe for auto-updates & new shop setups
  const versionSrc = path.resolve(__dirname, 'version.json');
  if (fs.existsSync(versionSrc) && baseDir !== __dirname) {
    fs.copyFileSync(versionSrc, path.join(baseDir, 'version.json'));
  }
  const exeSrc = path.resolve(__dirname, 'downloads', 'ScanDrop.exe');
  if (fs.existsSync(exeSrc) && baseDir !== __dirname) {
    fs.mkdirSync(path.join(baseDir, 'downloads'), { recursive: true });
    fs.copyFileSync(exeSrc, path.join(baseDir, 'downloads', 'ScanDrop.exe'));
  }
}

console.log('✅ Built cloud-connected portals for Netlify (/api/* via Netlify Blobs).');
