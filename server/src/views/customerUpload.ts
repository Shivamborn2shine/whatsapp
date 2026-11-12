export function renderCustomerUploadHtml(shop: any, qrDataUrl: string): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
  <title>${shop.name} | Scan Drop</title>
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
    .container { width: 100%; max-width: 480px; margin: 0 auto; }
    .header { text-align: center; margin-bottom: 20px; }
    .logo-badge { display: inline-flex; align-items: center; gap: 6px; background: #eff6ff; color: var(--primary); padding: 6px 14px; border-radius: 9999px; font-weight: 700; font-size: 13px; margin-bottom: 12px; }
    .shop-title { font-size: 22px; font-weight: 800; color: #1e293b; margin-bottom: 4px; }
    .shop-tagline { font-size: 13px; color: var(--text-muted); }
    .card { background: var(--card-bg); border-radius: 18px; padding: 22px; box-shadow: 0 4px 20px -2px rgba(0,0,0,0.06); border: 1px solid var(--border); margin-bottom: 16px; }
    .dropzone { border: 2px dashed #cbd5e1; border-radius: 14px; padding: 32px 16px; text-align: center; cursor: pointer; transition: all 0.2s ease; background: #fafafa; }
    .dropzone:hover, .dropzone.dragover { border-color: var(--primary); background: #f0f7ff; }
    .dropzone-icon { width: 48px; height: 48px; margin-bottom: 10px; color: var(--primary); }
    .dropzone-text { font-size: 15px; font-weight: 600; color: #334155; }
    .dropzone-sub { font-size: 12px; color: var(--text-muted); margin-top: 4px; }
    .file-list { margin-top: 14px; display: flex; flex-direction: column; gap: 8px; }
    .file-item { display: flex; align-items: center; justify-content: space-between; background: #f1f5f9; padding: 10px 14px; border-radius: 10px; font-size: 13px; }
    .file-name { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 240px; }
    .file-remove { color: #ef4444; cursor: pointer; font-weight: bold; padding: 2px 8px; }
    .form-group { margin-top: 16px; }
    label { display: block; font-size: 13px; font-weight: 600; color: #475569; margin-bottom: 6px; }
    input, select, textarea { width: 100%; padding: 12px 14px; border: 1px solid var(--border); border-radius: 10px; font-size: 14px; background: #fff; outline: none; }
    input:focus, select:focus, textarea:focus { border-color: var(--primary); box-shadow: 0 0 0 3px rgba(37,99,235,0.15); }
    .btn-submit { width: 100%; background: var(--primary); color: white; border: none; border-radius: 12px; padding: 15px; font-size: 16px; font-weight: 700; cursor: pointer; margin-top: 20px; transition: background 0.2s; box-shadow: 0 4px 12px rgba(37,99,235,0.25); }
    .btn-submit:hover { background: var(--primary-hover); }
    .btn-submit:disabled { background: #94a3b8; cursor: not-allowed; }
    .progress-bar { width: 100%; height: 8px; background: #e2e8f0; border-radius: 9999px; overflow: hidden; margin-top: 14px; display: none; }
    .progress-fill { height: 100%; background: var(--primary); width: 0%; transition: width 0.3s; }
    .receipt-container { display: none; text-align: center; }
    .receipt-badge { font-size: 32px; font-weight: 900; color: var(--primary); letter-spacing: 1px; margin: 12px 0; }
    .retention-alert { background: #fef3c7; border: 1px solid #fde68a; color: #92400e; padding: 12px; border-radius: 10px; font-size: 12px; line-height: 1.4; margin-top: 16px; }
    .footer { text-align: center; font-size: 11px; color: var(--text-muted); margin-top: auto; padding: 16px 0; }
  </style>
</head>
<body>
  <div class="container">
    <div class="header">
      <div class="logo-badge">⚡ Scan Drop Direct</div>
      <h1 class="shop-title">${shop.name}</h1>
      <p class="shop-tagline">${shop.tagline || 'Scan. Drop. Done.'}</p>
    </div>

    <div class="card" id="uploadCard">
      <form id="uploadForm">
        <input type="file" id="fileInput" multiple accept=".pdf,.jpg,.jpeg,.png,.docx" style="display:none;">
        <div class="dropzone" id="dropzone" onclick="document.getElementById('fileInput').click()">
          <svg class="dropzone-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12"/>
          </svg>
          <div class="dropzone-text">Tap to select or drop files here</div>
          <div class="dropzone-sub">Supports PDF, JPG, PNG, DOCX (Max 100MB)</div>
        </div>

        <div class="file-list" id="fileList"></div>

        <div class="form-group">
          <label for="customerName">Your Name (Optional)</label>
          <input type="text" id="customerName" placeholder="e.g. Rahul Sharma">
        </div>

        <div class="form-group">
          <label for="serviceCategory">Service Type</label>
          <select id="serviceCategory">
            <option value="B&W Print">Black & White Print</option>
            <option value="Color Print">Color Print</option>
            <option value="Document Lamination">Lamination</option>
            <option value="Scanning">Document Scanning</option>
            <option value="Custom Printing">Other / Custom</option>
          </select>
        </div>

        <div class="form-group">
          <label for="instructions">Special Instructions</label>
          <textarea id="instructions" rows="2" placeholder="e.g. 2 copies, double-sided, pages 1 to 5 only"></textarea>
        </div>

        <div class="progress-bar" id="progressBar">
          <div class="progress-fill" id="progressFill"></div>
        </div>

        <button type="submit" class="btn-submit" id="submitBtn" disabled>Upload & Get Receipt</button>
      </form>
    </div>

    <!-- Receipt Display -->
    <div class="card receipt-container" id="receiptCard">
      <div style="font-size: 48px; color: var(--success); margin-bottom: 8px;">✓</div>
      <h2 style="font-size: 20px; font-weight: 800;">Upload Received!</h2>
      <p style="font-size: 13px; color: var(--text-muted); margin-top: 4px;">Show this receipt code to the shopkeeper</p>
      
      <div class="receipt-badge" id="receiptIdText">SD-XXXX</div>

      <div style="background: #f8fafc; border: 1px solid var(--border); border-radius: 12px; padding: 14px; text-align: left; font-size: 13px;">
        <div style="display: flex; justify-content: space-between; margin-bottom: 6px;">
          <span style="color: var(--text-muted);">Shop:</span>
          <strong>${shop.name}</strong>
        </div>
        <div style="display: flex; justify-content: space-between; margin-bottom: 6px;">
          <span style="color: var(--text-muted);">Files Uploaded:</span>
          <strong id="receiptFileCount">0</strong>
        </div>
        <div style="display: flex; justify-content: space-between;">
          <span style="color: var(--text-muted);">Total Size:</span>
          <strong id="receiptTotalSize">0 MB</strong>
        </div>
      </div>

      <div class="retention-alert">
        <strong>🔒 24-Hour Privacy Retention:</strong> Your uploaded files will be delivered straight to the shop computer and automatically permanently purged from our servers in 24 hours.
      </div>

      <button onclick="location.reload()" class="btn-submit" style="background: #f1f5f9; color: #334155; margin-top: 16px;">
        Upload More Files
      </button>
    </div>

    <div class="footer">
      Powered by <strong>Scan Drop</strong> • Privacy First
    </div>
  </div>

  <script>
    const fileInput = document.getElementById('fileInput');
    const dropzone = document.getElementById('dropzone');
    const fileList = document.getElementById('fileList');
    const submitBtn = document.getElementById('submitBtn');
    const uploadForm = document.getElementById('uploadForm');
    const uploadCard = document.getElementById('uploadCard');
    const receiptCard = document.getElementById('receiptCard');
    const progressBar = document.getElementById('progressBar');
    const progressFill = document.getElementById('progressFill');

    let selectedFiles = [];

    fileInput.addEventListener('change', (e) => {
      handleFiles(Array.from(e.target.files));
    });

    dropzone.addEventListener('dragover', (e) => {
      e.preventDefault();
      dropzone.classList.add('dragover');
    });

    dropzone.addEventListener('dragleave', () => dropzone.classList.remove('dragover'));

    dropzone.addEventListener('drop', (e) => {
      e.preventDefault();
      dropzone.classList.remove('dragover');
      if (e.dataTransfer.files) {
        handleFiles(Array.from(e.dataTransfer.files));
      }
    });

    function handleFiles(files) {
      for (const f of files) {
        if (!selectedFiles.some(item => item.name === f.name && item.size === f.size)) {
          selectedFiles.push(f);
        }
      }
      renderFileList();
    }

    function removeFile(index) {
      selectedFiles.splice(index, 1);
      renderFileList();
    }

    function renderFileList() {
      fileList.innerHTML = '';
      if (selectedFiles.length === 0) {
        submitBtn.disabled = true;
        return;
      }
      submitBtn.disabled = false;

      selectedFiles.forEach((file, index) => {
        const item = document.createElement('div');
        item.className = 'file-item';
        const sizeMb = (file.size / (1024 * 1024)).toFixed(2);
        item.innerHTML = \`
          <span class="file-name">\${file.name} (\${sizeMb} MB)</span>
          <span class="file-remove" onclick="removeFile(\${index})">✕</span>
        \`;
        fileList.appendChild(item);
      });
    }

    uploadForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      if (selectedFiles.length === 0) return;

      submitBtn.disabled = true;
      submitBtn.textContent = 'Uploading...';
      progressBar.style.display = 'block';

      const formData = new FormData();
      formData.append('shopSlug', '${shop.slug}');
      formData.append('customerName', document.getElementById('customerName').value);
      formData.append('serviceCategory', document.getElementById('serviceCategory').value);
      formData.append('instructions', document.getElementById('instructions').value);

      for (const file of selectedFiles) {
        formData.append('files', file);
      }

      try {
        const xhr = new XMLHttpRequest();
        xhr.open('POST', '/api/v1/submissions/upload', true);

        xhr.upload.onprogress = (evt) => {
          if (evt.lengthComputable) {
            const percent = Math.round((evt.loaded / evt.total) * 100);
            progressFill.style.width = percent + '%';
          }
        };

        xhr.onload = () => {
          if (xhr.status === 200) {
            const data = JSON.parse(xhr.responseText);
            uploadCard.style.display = 'none';
            receiptCard.style.display = 'block';
            document.getElementById('receiptIdText').textContent = data.receiptId;
            document.getElementById('receiptFileCount').textContent = data.fileCount;
            document.getElementById('receiptTotalSize').textContent = (data.totalSizeBytes / 1024 / 1024).toFixed(2) + ' MB';
          } else {
            const err = JSON.parse(xhr.responseText);
            alert('Upload error: ' + (err.error || 'Failed to upload files'));
            submitBtn.disabled = false;
            submitBtn.textContent = 'Upload & Get Receipt';
            progressBar.style.display = 'none';
          }
        };

        xhr.onerror = () => {
          alert('Network connection error during file upload');
          submitBtn.disabled = false;
          submitBtn.textContent = 'Upload & Get Receipt';
          progressBar.style.display = 'none';
        };

        xhr.send(formData);
      } catch (err) {
        alert('Error: ' + err.message);
        submitBtn.disabled = false;
        submitBtn.textContent = 'Upload & Get Receipt';
      }
    });
  </script>
</body>
</html>`;
}

