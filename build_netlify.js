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
    .header { text-align: center; margin-bottom: 20px; }
    .logo-badge { display: inline-flex; align-items: center; gap: 6px; background: #eff6ff; color: var(--primary); padding: 6px 14px; border-radius: 9999px; font-weight: 700; font-size: 13px; margin-bottom: 10px; }
    .shop-title { font-size: 22px; font-weight: 800; color: #1e293b; margin-bottom: 4px; }
    .shop-tagline { font-size: 13px; color: var(--text-muted); }
    .card { background: var(--card-bg); border-radius: 18px; padding: 22px; box-shadow: 0 4px 20px -2px rgba(0,0,0,0.06); border: 1px solid var(--border); margin-bottom: 16px; }
    .dropzone { border: 2px dashed #cbd5e1; border-radius: 14px; padding: 28px 16px; text-align: center; cursor: pointer; transition: all 0.2s ease; background: #fafafa; }
    .dropzone:hover, .dropzone.dragover { border-color: var(--primary); background: #f0f7ff; }
    .dropzone-icon { width: 44px; height: 44px; margin: 0 auto 8px; color: var(--primary); display: block; }
    .dropzone-text { font-size: 15px; font-weight: 600; color: #334155; }
    .dropzone-sub { font-size: 12px; color: var(--text-muted); margin-top: 4px; }
    .file-list { margin-top: 14px; display: flex; flex-direction: column; gap: 8px; }
    .file-item { display: flex; align-items: center; justify-content: space-between; background: #f1f5f9; padding: 10px 14px; border-radius: 10px; font-size: 13px; }
    .file-name { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 260px; }
    .file-remove { color: #ef4444; cursor: pointer; font-weight: bold; padding: 2px 8px; border: none; background: none; font-size: 15px; }
    .form-group { margin-top: 16px; }
    label { display: block; font-size: 13px; font-weight: 600; color: #475569; margin-bottom: 6px; }
    input, textarea { width: 100%; padding: 12px 14px; border: 1px solid var(--border); border-radius: 10px; font-size: 14px; background: #fff; color: var(--text); outline: none; }
    input:focus, textarea:focus { border-color: var(--primary); box-shadow: 0 0 0 3px rgba(37,99,235,0.15); }
    .btn-submit { width: 100%; background: var(--primary); color: white; border: none; border-radius: 12px; padding: 15px; font-size: 16px; font-weight: 700; cursor: pointer; margin-top: 20px; transition: background 0.2s; box-shadow: 0 4px 12px rgba(37,99,235,0.25); }
    .btn-submit:hover { background: var(--primary-hover); }
    .btn-submit:disabled { background: #94a3b8; cursor: not-allowed; box-shadow: none; }
    .progress-bar { width: 100%; height: 8px; background: #e2e8f0; border-radius: 9999px; overflow: hidden; margin-top: 14px; display: none; }
    .progress-fill { height: 100%; background: var(--primary); width: 0%; transition: width 0.2s; }
    .receipt-container { display: none; text-align: center; }
    .receipt-badge { font-size: 34px; font-weight: 900; color: var(--primary); letter-spacing: 1px; margin: 12px 0; }
    .retention-alert { background: #fef3c7; border: 1px solid #fde68a; color: #92400e; padding: 12px; border-radius: 10px; font-size: 12px; line-height: 1.4; margin-top: 16px; text-align: left; }
    .error-banner { background: #fee2e2; border: 1px solid #fecaca; color: #991b1b; padding: 12px; border-radius: 10px; font-size: 13px; margin-bottom: 14px; display: none; }
    .footer { text-align: center; font-size: 11px; color: var(--text-muted); margin-top: auto; padding: 16px 0; }
  </style>
</head>
<body>
  <div class="container">
    <div class="header">
      <div class="logo-badge">⚡ Scan Drop Direct</div>
      <h1 class="shop-title" id="shopTitle">Loading...</h1>
      <p class="shop-tagline" id="shopTagline">Scan. Drop. Done.</p>
    </div>

    <div class="error-banner" id="shopUnavailableBanner"></div>

    <div class="card" id="uploadCard">
      <form id="uploadForm">
        <input type="file" id="fileInput" multiple style="display:none;">
        <div class="dropzone" id="dropzone" onclick="document.getElementById('fileInput').click()">
          <svg class="dropzone-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12"/>
          </svg>
          <div class="dropzone-text">Tap to select or drop files here</div>
          <div class="dropzone-sub">All Documents, Images, Videos & Archives supported (Max 500MB • Direct PC Tunnel)</div>
        </div>

        <div class="file-list" id="fileList"></div>

        <div class="form-group">
          <label for="customerName">Your Name (Optional)</label>
          <input type="text" id="customerName" placeholder="e.g. Rahul Sharma">
        </div>

        <div class="form-group">
          <label for="instructions">Note / Message (Optional)</label>
          <textarea id="instructions" rows="2" placeholder="e.g. Any note or message (optional)"></textarea>
        </div>

        <div class="progress-bar" id="progressBar">
          <div class="progress-fill" id="progressFill"></div>
        </div>

        <button type="submit" class="btn-submit" id="submitBtn" disabled>Send Files</button>
      </form>
    </div>

    <!-- Receipt Display -->
    <div class="card receipt-container" id="receiptCard">
      <div style="font-size: 46px; color: var(--success); margin-bottom: 6px;">✓</div>
      <h2 style="font-size: 20px; font-weight: 800;">Files Sent Successfully!</h2>
      <p style="font-size: 13px; color: var(--text-muted); margin-top: 4px;">Share this receipt code with the recipient</p>
      
      <div class="receipt-badge" id="receiptIdText">SD-XXXX</div>

      <div style="background: #f8fafc; border: 1px solid var(--border); border-radius: 12px; padding: 14px; text-align: left; font-size: 13px;">
        <div style="display: flex; justify-content: space-between; margin-bottom: 6px;">
          <span style="color: var(--text-muted);">Recipient:</span>
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
        <div style="display: flex; justify-content: space-between;">
          <span style="color: var(--text-muted);">Status:</span>
          <strong style="color: var(--primary);">Delivered to Panel</strong>
        </div>
      </div>

      <div class="retention-alert">
        <strong>🔒 24-Hour Privacy Retention:</strong> Your files are delivered securely over the internet and automatically permanently deleted after 24 hours.
      </div>

      <button onclick="resetUploadForm()" class="btn-submit" style="background: #f1f5f9; color: #334155; box-shadow: none; margin-top: 16px;">
        Send More Files
      </button>
    </div>

    <!-- Receipt Status Lookup -->
    <div class="card" id="lookupCard" style="padding: 16px 20px;">
      <div style="font-size: 13px; font-weight: 700; color: #334155; margin-bottom: 8px;">Track Existing Receipt</div>
      <div style="display: flex; gap: 8px;">
        <input type="text" id="lookupInput" placeholder="Enter Receipt ID (e.g. SD-1042)" style="padding: 9px 12px; font-size: 13px;">
        <button type="button" onclick="trackReceipt()" style="background: #334155; color: #fff; border: none; border-radius: 10px; padding: 0 16px; font-size: 13px; font-weight: 600; cursor: pointer;">Track</button>
      </div>
      <div id="lookupResult" style="margin-top: 10px; font-size: 12px; display: none;"></div>
    </div>

    <div class="footer">
      Powered by <strong>Scan Drop</strong> • Fast & Private File Sharing
    </div>
  </div>

  <script>
    const BLOCKED_EXTS = ['.exe', '.bat', '.cmd', '.msi', '.vbs', '.ps1', '.scr'];
    const MAX_TOTAL_BYTES = 500 * 1024 * 1024; // 500MB via Direct PC Tunnel (10MB chunks)
    const TUNNEL_SINGLE_MAX = 25 * 1024 * 1024; // Direct single-shot up to 25MB
    const TUNNEL_CHUNK_SIZE = 10 * 1024 * 1024; // 10MB chunks (well below Cloudflare 100MB edge limit)
    const CHUNK_SIZE = 2 * 1024 * 1024; // 2MB per chunk for cloud fallback
    let selectedFiles = [];
    let activeShop = null;

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
          document.getElementById('shopUnavailableBanner').style.display = 'block';
          document.getElementById('shopUnavailableBanner').textContent = 'Shop not found. Please scan a valid QR code.';
          document.getElementById('uploadCard').style.display = 'none';
          return;
        }

        document.getElementById('shopTitle').textContent = activeShop.name;
        document.getElementById('shopTagline').textContent = (activeShop.tunnelOnline && activeShop.tunnelUrl)
          ? (activeShop.tagline || 'Scan. Drop. Done.') + ' • ⚡ Direct-to-Shop PC Tunnel'
          : (activeShop.tagline || 'Scan. Drop. Done.');

        if (activeShop.subscriptionStatus === 'suspended' || activeShop.subscriptionStatus === 'revoked') {
          document.getElementById('shopUnavailableBanner').style.display = 'block';
          document.getElementById('shopUnavailableBanner').textContent = 'This recipient is currently not accepting uploads.';
          document.getElementById('submitBtn').disabled = true;
          document.getElementById('dropzone').style.pointerEvents = 'none';
          document.getElementById('dropzone').style.opacity = '0.5';
        }
      } catch (err) {
        document.getElementById('shopTitle').textContent = 'Scan Drop';
        document.getElementById('shopUnavailableBanner').style.display = 'block';
        document.getElementById('shopUnavailableBanner').textContent = 'Could not connect to cloud server.';
      }
    }

    const fileInput = document.getElementById('fileInput');
    const dropzone = document.getElementById('dropzone');
    const fileListEl = document.getElementById('fileList');
    const submitBtn = document.getElementById('submitBtn');

    fileInput.addEventListener('change', (e) => handleFiles(Array.from(e.target.files)));
    dropzone.addEventListener('dragover', (e) => { e.preventDefault(); dropzone.classList.add('dragover'); });
    dropzone.addEventListener('dragleave', () => dropzone.classList.remove('dragover'));
    dropzone.addEventListener('drop', (e) => {
      e.preventDefault();
      dropzone.classList.remove('dragover');
      if (e.dataTransfer.files) handleFiles(Array.from(e.dataTransfer.files));
    });

    function handleFiles(files) {
      for (const f of files) {
        const ext = f.name.includes('.') ? '.' + f.name.split('.').pop().toLowerCase() : '';
        if (BLOCKED_EXTS.includes(ext)) {
          alert('Executable files (' + ext + ') are blocked for security.');
          continue;
        }
        const currentTotal = selectedFiles.reduce((a, b) => a + b.size, 0);
        if (currentTotal + f.size > MAX_TOTAL_BYTES) {
          alert('Total upload size exceeds 500MB limit per transfer.');
          continue;
        }
        if (!selectedFiles.some(item => item.name === f.name && item.size === f.size)) {
          selectedFiles.push(f);
        }
      }
      renderFileList();
    }

    function removeFile(idx) {
      selectedFiles.splice(idx, 1);
      renderFileList();
    }

    function renderFileList() {
      fileListEl.innerHTML = '';
      if (selectedFiles.length === 0) {
        submitBtn.disabled = true;
        return;
      }
      submitBtn.disabled = false;
      selectedFiles.forEach((file, idx) => {
        const item = document.createElement('div');
        item.className = 'file-item';
        const sizeLabel = file.size >= 1024 * 1024
          ? (file.size / (1024 * 1024)).toFixed(2) + ' MB'
          : (file.size / 1024).toFixed(1) + ' KB';
        item.innerHTML = \`
          <span class="file-name">\${file.name} (\${sizeLabel})</span>
          <button type="button" class="file-remove" onclick="removeFile(\${idx})" title="Remove">✕</button>
        \`;
        fileListEl.appendChild(item);
      });
    }

    async function tryDirectTunnelUpload(tunnelUrl, progressFill) {
      const base = tunnelUrl.replace(/[/]+$/, '');
      const totalBytes = selectedFiles.reduce((a, b) => a + b.size, 0);

      // Fast path for <= 25MB: single multipart POST directly to PC
      if (totalBytes <= TUNNEL_SINGLE_MAX) {
        return new Promise((resolve, reject) => {
          const fd = new FormData();
          fd.append('shopId', activeShop.id);
          fd.append('shopSlug', activeShop.slug || 'quickprint');
          fd.append('customerName', document.getElementById('customerName').value.trim());
          fd.append('instructions', document.getElementById('instructions').value.trim());
          selectedFiles.forEach(f => fd.append('files', f, f.name));

          const xhr = new XMLHttpRequest();
          xhr.open('POST', base + '/api/direct-upload', true);
          xhr.timeout = 180000;
          xhr.upload.onprogress = (ev) => {
            if (ev.lengthComputable) {
              progressFill.style.width = Math.max(8, Math.round((ev.loaded / ev.total) * 95)) + '%';
            }
          };
          xhr.onload = () => {
            if (xhr.status >= 200 && xhr.status < 300) {
              try {
                const res = JSON.parse(xhr.responseText);
                if (res && res.success) return resolve(res);
              } catch {}
            }
            reject(new Error('Direct tunnel upload failed (' + xhr.status + ')'));
          };
          xhr.onerror = () => reject(new Error('Tunnel unreachable'));
          xhr.ontimeout = () => reject(new Error('Tunnel timeout'));
          xhr.send(fd);
        });
      }

      // Large-file path (> 25MB up to 500MB): 10MB chunks directly to PC SSD (avoids Cloudflare 100MB per-request limit)
      let uploadedBytes = 0;
      const fileMeta = [];
      for (let fi = 0; fi < selectedFiles.length; fi++) {
        const f = selectedFiles[fi];
        const fileId = 'tf_' + Date.now() + '_' + fi + '_' + Math.floor(Math.random() * 10000);
        const chunkCount = Math.max(1, Math.ceil(f.size / TUNNEL_CHUNK_SIZE));

        for (let ci = 0; ci < chunkCount; ci++) {
          const start = ci * TUNNEL_CHUNK_SIZE;
          const end = Math.min(f.size, start + TUNNEL_CHUNK_SIZE);
          const blob = f.slice(start, end);

          await new Promise((resolve, reject) => {
            const fd = new FormData();
            fd.append('fileId', fileId);
            fd.append('chunkIndex', String(ci));
            fd.append('chunk', blob, f.name);

            const xhr = new XMLHttpRequest();
            xhr.open('POST', base + '/api/direct-upload-chunk', true);
            xhr.timeout = 180000;
            xhr.upload.onprogress = (ev) => {
              if (ev.lengthComputable) {
                const pct = Math.min(95, Math.round(((uploadedBytes + ev.loaded) / totalBytes) * 95));
                progressFill.style.width = Math.max(5, pct) + '%';
                submitBtn.textContent = '⚡ Sending directly to Shop PC (' + pct + '%)...';
              }
            };
            xhr.onload = () => {
              if (xhr.status >= 200 && xhr.status < 300) return resolve();
              reject(new Error('Tunnel chunk failed (' + xhr.status + ')'));
            };
            xhr.onerror = () => reject(new Error('Tunnel unreachable'));
            xhr.ontimeout = () => reject(new Error('Tunnel chunk timeout'));
            xhr.send(fd);
          });

          uploadedBytes += (end - start);
        }

        fileMeta.push({
          id: fileId,
          originalName: f.name,
          mimeType: f.type || 'application/octet-stream',
          sizeBytes: f.size,
          chunkCount
        });
      }

      submitBtn.textContent = '⚡ Finalizing on Shop PC...';
      const cmpRes = await fetch(base + '/api/direct-upload-complete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          shopId: activeShop.id,
          customerName: document.getElementById('customerName').value.trim(),
          instructions: document.getElementById('instructions').value.trim(),
          files: fileMeta
        })
      });
      const cmpData = await cmpRes.json();
      if (!cmpRes.ok || !cmpData.success) {
        throw new Error(cmpData.error || 'Failed to finalize large tunnel upload');
      }
      return cmpData;
    }

    document.getElementById('uploadForm').addEventListener('submit', async (e) => {
      e.preventDefault();
      if (selectedFiles.length === 0 || !activeShop) return;

      submitBtn.disabled = true;
      const progressBar = document.getElementById('progressBar');
      const progressFill = document.getElementById('progressFill');
      progressBar.style.display = 'block';
      progressFill.style.width = '5%';

      try {
        // 1. Try Direct-to-Shop-PC Tunnel first (zero cloud hop, instant local SSD storage)
        if (activeShop.tunnelOnline && activeShop.tunnelUrl) {
          try {
            submitBtn.textContent = '⚡ Sending directly to Shop PC...';
            const totalSizeBytes = selectedFiles.reduce((a, b) => a + b.size, 0);
            const directData = await tryDirectTunnelUpload(activeShop.tunnelUrl, progressFill);
            progressFill.style.width = '100%';
            document.getElementById('uploadCard').style.display = 'none';
            document.getElementById('receiptCard').style.display = 'block';
            document.getElementById('receiptIdText').textContent = directData.receiptId;
            document.getElementById('receiptShopName').textContent = activeShop.name || 'Shop PC (Direct SSD)';
            document.getElementById('receiptFileCount').textContent = directData.fileCount || selectedFiles.length;
            document.getElementById('receiptTotalSize').textContent = totalSizeBytes >= 1024 * 1024
              ? (totalSizeBytes / (1024 * 1024)).toFixed(2) + ' MB'
              : (totalSizeBytes / 1024).toFixed(1) + ' KB';
            return;
          } catch (tunnelErr) {
            console.warn('Direct tunnel failed, falling back to cloud PrintAway:', tunnelErr);
          }
        }

        // 2. Fallback: Cloud PrintAway chunked transfer via Netlify Blobs (max 50MB when shop PC offline)
        const cloudTotalBytes = selectedFiles.reduce((a, b) => a + b.size, 0);
        if (cloudTotalBytes > 50 * 1024 * 1024) {
          throw new Error('Files over 50MB require the Shopkeeper PC Station (ScanDrop.exe) to be online.');
        }
        submitBtn.textContent = 'Transferring via PrintAway...';
        let totalChunks = 0;
        const fileChunkCounts = selectedFiles.map(f => {
          const c = Math.max(1, Math.ceil(f.size / CHUNK_SIZE));
          totalChunks += c;
          return c;
        });

        let uploadedChunks = 0;
        const uploadedMeta = [];

        for (let fi = 0; fi < selectedFiles.length; fi++) {
          const f = selectedFiles[fi];
          const fileId = 'file_' + Date.now() + '_' + fi + '_' + Math.floor(Math.random() * 1000);
          const chunkCount = fileChunkCounts[fi];

          for (let ci = 0; ci < chunkCount; ci++) {
            const start = ci * CHUNK_SIZE;
            const end = Math.min(f.size, start + CHUNK_SIZE);
            const blobChunk = f.slice(start, end);

            const fd = new FormData();
            fd.append('fileId', fileId);
            fd.append('chunkIndex', String(ci));
            fd.append('chunk', blobChunk, f.name);

            const chunkRes = await fetch('/api/upload-chunk', { method: 'POST', body: fd });
            if (!chunkRes.ok) {
              const errData = await chunkRes.json().catch(() => ({}));
              throw new Error(errData.error || 'Chunk upload failed');
            }

            uploadedChunks++;
            progressFill.style.width = Math.round((uploadedChunks / totalChunks) * 90) + '%';
          }

          uploadedMeta.push({
            id: fileId,
            originalName: f.name,
            mimeType: f.type || 'application/octet-stream',
            sizeBytes: f.size,
            chunkCount
          });
        }

        const completeRes = await fetch('/api/upload-complete', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            shopId: activeShop.id,
            customerName: document.getElementById('customerName').value.trim(),
            instructions: document.getElementById('instructions').value.trim(),
            files: uploadedMeta
          })
        });

        const data = await completeRes.json();
        progressFill.style.width = '100%';

        if (completeRes.ok && data.success) {
          document.getElementById('uploadCard').style.display = 'none';
          document.getElementById('receiptCard').style.display = 'block';
          document.getElementById('receiptIdText').textContent = data.receiptId;
          document.getElementById('receiptShopName').textContent = data.shopName;
          document.getElementById('receiptFileCount').textContent = data.fileCount;
          document.getElementById('receiptTotalSize').textContent = data.totalSizeBytes >= 1024 * 1024
            ? (data.totalSizeBytes / (1024 * 1024)).toFixed(2) + ' MB'
            : (data.totalSizeBytes / 1024).toFixed(1) + ' KB';
        } else {
          throw new Error(data.error || 'Failed to finalize transfer');
        }
      } catch (err) {
        alert('Transfer failed: ' + err.message);
        submitBtn.disabled = false;
        submitBtn.textContent = 'Send Files';
        progressBar.style.display = 'none';
      }
    });

    function resetUploadForm() {
      selectedFiles = [];
      fileInput.value = '';
      renderFileList();
      document.getElementById('progressBar').style.display = 'none';
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
        let res, data;
        if (activeShop && activeShop.tunnelOnline && activeShop.tunnelUrl) {
          try {
            res = await fetch(activeShop.tunnelUrl.replace(/[/]+$/, '') + '/api/receipt?id=' + encodeURIComponent(code));
            data = await res.json();
          } catch {}
        }
        if (!res || !res.ok || !data || !data.success) {
          res = await fetch('/api/receipt?id=' + encodeURIComponent(code));
          data = await res.json();
        }
        if (!res.ok || !data.success) {
          resEl.innerHTML = '<span style="color:#ef4444;">Receipt not found or expired.</span>';
          return;
        }
        const sub = data.submission;
        const remHrs = Math.max(0, (new Date(sub.expiresAt).getTime() - Date.now()) / 3600000).toFixed(1);
        resEl.innerHTML = \`<div style="background:#f1f5f9;padding:10px;border-radius:8px;">
          <strong>\${sub.id}</strong> • Status: <strong style="text-transform:capitalize;color:#2563eb;">\${sub.status}</strong><br>
          Files: \${(sub.files || []).length} • Auto-deletes in \${remHrs} hrs
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
