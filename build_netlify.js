const fs = require('fs');
const path = require('path');

console.log('🚀 Building Scan Drop Web Portal for Netlify...');

const outDirs = [
  path.resolve(__dirname, 'dist_netlify'),
  path.resolve(__dirname, 'public'),
  path.resolve(__dirname)
];

for (const dir of [outDirs[0], outDirs[1]]) {
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
}

// Copy or generate index.html
const indexHtml = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Scan Drop — Cloud Platform & Desktop Workspace</title>
  <link rel="icon" href="data:image/svg+xml,<svg xmlns=%22http://www.w3.org/2000/svg%22 viewBox=%220 0 100 100%22><text y=%22.9em%22 font-size=%2290%22>⚡</text></svg>">
  <style>
    :root {
      --primary: #2563eb;
      --primary-hover: #1d4ed8;
      --bg: #0f172a;
      --card-bg: #1e293b;
      --border: #334155;
      --text: #f8fafc;
      --text-muted: #94a3b8;
      --success: #10b981;
      --warning: #f59e0b;
      --danger: #ef4444;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; }
    body { background-color: var(--bg); color: var(--text); min-height: 100vh; display: flex; flex-direction: column; }
    header { background: #0b1120; border-bottom: 1px solid var(--border); padding: 14px 24px; display: flex; align-items: center; justify-content: space-between; }
    .brand { display: flex; align-items: center; gap: 10px; font-weight: 800; font-size: 18px; }
    .nav-tabs { display: flex; gap: 6px; background: #0f172a; padding: 4px; border-radius: 10px; border: 1px solid var(--border); }
    .nav-btn { background: none; border: none; color: var(--text-muted); font-size: 13px; font-weight: 600; padding: 8px 16px; border-radius: 8px; cursor: pointer; transition: all 0.2s; }
    .nav-btn.active { background: var(--primary); color: #fff; }
    .main-view { flex: 1; padding: 24px; display: none; }
    .main-view.active { display: block; }

    /* Customer Upload Styles */
    .customer-container { max-width: 480px; margin: 0 auto; }
    .card { background: var(--card-bg); border-radius: 16px; padding: 22px; border: 1px solid var(--border); box-shadow: 0 4px 20px rgba(0,0,0,0.3); }
    .dropzone { border: 2px dashed #475569; border-radius: 14px; padding: 28px 16px; text-align: center; cursor: pointer; background: #0f172a; transition: border-color 0.2s; }
    .dropzone:hover { border-color: var(--primary); }
    .form-group { margin-top: 14px; }
    label { display: block; font-size: 12px; font-weight: 600; color: var(--text-muted); margin-bottom: 6px; }
    input, select, textarea { width: 100%; padding: 11px 13px; background: #0f172a; border: 1px solid var(--border); border-radius: 8px; color: #fff; font-size: 13px; outline: none; }
    input:focus, select:focus, textarea:focus { border-color: var(--primary); }
    .btn-submit { width: 100%; background: var(--primary); color: white; border: none; border-radius: 10px; padding: 14px; font-size: 15px; font-weight: 700; cursor: pointer; margin-top: 18px; }
    .btn-submit:hover { background: var(--primary-hover); }

    /* Superadmin Styles */
    .admin-container { max-width: 1200px; margin: 0 auto; }
    .stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 14px; margin-bottom: 20px; }
    .stat-card { background: var(--card-bg); border: 1px solid var(--border); border-radius: 12px; padding: 16px; }
    .stat-label { font-size: 11px; font-weight: 600; text-transform: uppercase; color: var(--text-muted); }
    .stat-val { font-size: 24px; font-weight: 800; margin-top: 4px; }
    table { width: 100%; border-collapse: collapse; font-size: 13px; text-align: left; }
    th { padding: 12px; border-bottom: 1px solid var(--border); color: var(--text-muted); font-weight: 600; }
    td { padding: 12px; border-bottom: 1px solid #1e293b; color: #cbd5e1; }
    .badge { display: inline-block; padding: 3px 8px; border-radius: 9999px; font-size: 11px; font-weight: 700; }
    .badge-green { background: #064e3b; color: #34d399; }
    .badge-blue { background: #1e3a8a; color: #93c5fd; }
    .badge-amber { background: #78350f; color: #fde68a; }

    /* Desktop View */
    .desktop-container { max-width: 1100px; margin: 0 auto; background: var(--card-bg); border: 1px solid var(--border); border-radius: 16px; overflow: hidden; display: flex; min-height: 580px; }
    .desktop-sidebar { width: 220px; background: #0b1120; border-right: 1px solid var(--border); padding: 16px 10px; display: flex; flex-direction: column; gap: 6px; }
    .sidebar-item { padding: 10px 12px; border-radius: 8px; color: var(--text-muted); font-size: 13px; font-weight: 600; cursor: pointer; display: flex; align-items: center; justify-content: space-between; }
    .sidebar-item.active, .sidebar-item:hover { background: #1e293b; color: #fff; }
    .desktop-content { flex: 1; padding: 20px; background: #0f172a; }
  </style>
</head>
<body>
  <header>
    <div class="brand">
      <span>⚡ Scan Drop</span>
      <span style="font-size: 11px; font-weight: normal; color: var(--text-muted); background: #1e293b; padding: 2px 8px; border-radius: 9999px;">Cloud + Desktop Platform</span>
    </div>

    <div class="nav-tabs">
      <button class="nav-btn active" onclick="switchMainTab('customerTab', this)">📱 Customer Upload</button>
      <button class="nav-btn" onclick="switchMainTab('superadminTab', this)">👑 Founder Super Admin</button>
      <button class="nav-btn" onclick="switchMainTab('desktopTab', this)">🖥️ Shop Workspace</button>
    </div>

    <div style="font-size: 12px; color: #34d399; font-weight: 600;">● Live Cloud Active</div>
  </header>

  <!-- 1. CUSTOMER UPLOAD VIEW -->
  <div id="customerTab" class="main-view active">
    <div class="customer-container">
      <div style="text-align: center; margin-bottom: 20px;">
        <span style="background: #1e3a8a; color: #93c5fd; font-size: 12px; font-weight: 700; padding: 4px 12px; border-radius: 9999px;">QR Scan Receipt Portal</span>
        <h1 style="font-size: 24px; font-weight: 800; margin-top: 10px;">QuickPrint Xerox & Stationery</h1>
        <p style="font-size: 13px; color: var(--text-muted); margin-top: 4px;">Scan. Drop. Done. No WhatsApp required.</p>
      </div>

      <div class="card" id="uploadFormCard">
        <div class="dropzone" id="customerDropzone" onclick="document.getElementById('cFiles').click()">
          <input type="file" id="cFiles" multiple style="display:none;" onchange="handleCustomerFiles(this.files)">
          <div style="font-size: 32px; margin-bottom: 8px;">📤</div>
          <div style="font-weight: 700; font-size: 15px;">Tap or Drop Files Here</div>
          <div style="font-size: 12px; color: var(--text-muted); margin-top: 4px;">Supports PDF, JPG, PNG, DOCX (Max 100MB)</div>
        </div>

        <div id="fileSummary" style="margin-top: 12px; display: none; font-size: 13px; background: #0f172a; padding: 10px; border-radius: 8px; border: 1px solid var(--border);"></div>

        <div class="form-group">
          <label>Your Name (Optional)</label>
          <input type="text" id="cName" placeholder="e.g. Ramesh Patel">
        </div>

        <div class="form-group">
          <label>Service Type</label>
          <select id="cService">
            <option value="B&W Print">Black & White Print ($0.10/pg)</option>
            <option value="Color Print">Color Print ($0.40/pg)</option>
            <option value="Document Scanning">High-Res Scanning</option>
            <option value="Lamination">Lamination</option>
          </select>
        </div>

        <div class="form-group">
          <label>Special Instructions</label>
          <textarea id="cNotes" rows="2" placeholder="e.g. 2 copies, double-sided, pages 1 to 4 only"></textarea>
        </div>

        <button class="btn-submit" onclick="submitCustomerUpload()">Upload & Get Receipt</button>
      </div>

      <!-- Receipt Card -->
      <div class="card" id="customerReceiptCard" style="display: none; text-align: center;">
        <div style="font-size: 42px; color: var(--success); margin-bottom: 6px;">✓</div>
        <h2 style="font-size: 20px; font-weight: 800;">Upload Received!</h2>
        <p style="font-size: 13px; color: var(--text-muted); margin-top: 4px;">Show this receipt code to the shopkeeper</p>

        <div id="receiptCode" style="font-size: 36px; font-weight: 900; color: #38bdf8; letter-spacing: 2px; margin: 16px 0;">SD-7842</div>

        <div style="background: #0f172a; border: 1px solid var(--border); border-radius: 10px; padding: 14px; text-align: left; font-size: 13px;">
          <div style="display:flex; justify-content:space-between; margin-bottom: 6px;"><span style="color:var(--text-muted);">Shop:</span><strong>QuickPrint Xerox</strong></div>
          <div style="display:flex; justify-content:space-between; margin-bottom: 6px;"><span style="color:var(--text-muted);">Files Count:</span><strong id="receiptCount">1</strong></div>
          <div style="display:flex; justify-content:space-between;"><span style="color:var(--text-muted);">Status:</span><span class="badge badge-green">Ready for Staff</span></div>
        </div>

        <div style="background: #78350f22; border: 1px solid #78350f; color: #fbbf24; border-radius: 10px; padding: 12px; font-size: 12px; margin-top: 16px; text-align: left;">
          🔒 <strong>24-Hour Privacy Guarantee:</strong> Files are delivered to the shop PC and automatically permanently erased from cloud servers in 24 hours.
        </div>

        <button class="btn-submit" style="background:#334155; margin-top: 16px;" onclick="resetCustomerUpload()">Upload Another Document</button>
      </div>
    </div>
  </div>

  <!-- 2. FOUNDER SUPER ADMIN VIEW -->
  <div id="superadminTab" class="main-view">
    <div class="admin-container">
      <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px;">
        <div>
          <h1 style="font-size: 22px; font-weight: 800;">Founder Super Admin Dashboard</h1>
          <p style="font-size: 13px; color: var(--text-muted); margin-top: 2px;">Central licensing, shop pairings, device inventory, and entitlement authority.</p>
        </div>
        <button class="nav-btn" style="background: var(--primary); color:#fff;" onclick="alert('Creating new shop...')">+ Register Shop</button>
      </div>

      <div class="stats-grid">
        <div class="stat-card">
          <div class="stat-label">Registered Shops</div>
          <div class="stat-val">3</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">Active Paid Licenses</div>
          <div class="stat-val" style="color: #34d399;">3</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">Active Windows PCs</div>
          <div class="stat-val" style="color: #60a5fa;">4</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">Total Submissions Processed</div>
          <div class="stat-val">1,284</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">24h Purge Integrity</div>
          <div class="stat-val" style="color: #34d399;">100%</div>
        </div>
      </div>

      <div class="card">
        <h3 style="font-size: 16px; font-weight: 700; margin-bottom: 14px;">Shop Licenses & Device Pairing Codes</h3>
        <table>
          <thead>
            <tr>
              <th>Shop Business Name</th>
              <th>Owner & Contact</th>
              <th>Plan Tier</th>
              <th>Status</th>
              <th>Pairing Code</th>
              <th>Active PCs</th>
              <th>Expiry</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td><strong>QuickPrint Xerox</strong><br><span style="color:var(--text-muted);font-size:11px;">/shop/quickprint</span></td>
              <td>Ramesh Kumar<br><span style="color:var(--text-muted);font-size:11px;">ramesh@quickprint.local</span></td>
              <td><span class="badge badge-blue">PRO MONTHLY</span></td>
              <td><span class="badge badge-green">active</span></td>
              <td><code style="background:#0b1120;padding:4px 8px;border-radius:6px;color:#38bdf8;">SD-PAIR-7890</code></td>
              <td>1 / 1 PC</td>
              <td>30 Days Remaining</td>
            </tr>
            <tr>
              <td><strong>Metro Cyber Cafe & Prints</strong><br><span style="color:var(--text-muted);font-size:11px;">/shop/metro-cyber</span></td>
              <td>Sunil Sharma<br><span style="color:var(--text-muted);font-size:11px;">sunil@metrocyber.com</span></td>
              <td><span class="badge badge-blue">BUSINESS MULTI-PC</span></td>
              <td><span class="badge badge-green">active</span></td>
              <td><code style="background:#0b1120;padding:4px 8px;border-radius:6px;color:#38bdf8;">SD-PAIR-4412</code></td>
              <td>3 / 5 PCs</td>
              <td>28 Days Remaining</td>
            </tr>
            <tr>
              <td><strong>City Stationery & Xerox</strong><br><span style="color:var(--text-muted);font-size:11px;">/shop/city-stationery</span></td>
              <td>Pooja Verma<br><span style="color:var(--text-muted);font-size:11px;">pooja@citystat.in</span></td>
              <td><span class="badge badge-amber">30-DAY TRIAL</span></td>
              <td><span class="badge badge-green">trial</span></td>
              <td><code style="background:#0b1120;padding:4px 8px;border-radius:6px;color:#38bdf8;">SD-PAIR-9018</code></td>
              <td>1 / 1 PC</td>
              <td>14 Days Remaining</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>

  <!-- 3. SHOP WORKSPACE SIMULATOR VIEW -->
  <div id="desktopTab" class="main-view">
    <div class="desktop-container">
      <div class="desktop-sidebar">
        <div style="font-weight: 800; font-size: 14px; padding: 4px 8px; margin-bottom: 8px;">Scan Drop Desktop</div>
        <div class="sidebar-item active">📥 Inbox <span class="badge badge-amber" id="inboxBadge">1</span></div>
        <div class="sidebar-item">▶ Processing</div>
        <div class="sidebar-item">✓ Completed</div>
        <div class="sidebar-item">📂 File Manager</div>
        <div class="sidebar-item">📄 PDF Tools</div>
        <div class="sidebar-item">🖨️ Printers</div>
        <div class="sidebar-item">📜 History</div>
        <div class="sidebar-item">⚙️ Settings</div>
      </div>

      <div class="desktop-content">
        <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom: 16px;">
          <h3 style="font-size: 16px; font-weight: 700;">Active Submissions (QuickPrint Shop PC)</h3>
          <span style="font-size: 11px; color: #34d399;">● Auto-Sync Running (No browser required)</span>
        </div>

        <div id="desktopSubmissionsList" style="display: flex; flex-direction: column; gap: 10px;">
          <div style="background: #1e293b; border: 1px solid var(--border); border-radius: 12px; padding: 14px;">
            <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom: 8px;">
              <div>
                <strong style="color: #38bdf8; font-size: 16px;">SD-1042</strong>
                <span style="margin-left: 8px; font-size: 13px; font-weight: 600;">Vikram Malhotra</span>
                <span class="badge badge-blue" style="margin-left: 8px;">Color Print</span>
              </div>
              <span style="font-size: 11px; color: var(--text-muted);">23.8h retention remaining</span>
            </div>
            <div style="font-size: 12px; color: var(--text-muted); margin-bottom: 10px;">
              Instructions: 2 copies on A4 glossy paper • 1 file (2.4 MB)
            </div>
            <div style="display:flex; gap: 8px;">
              <button class="nav-btn" style="background:#334155; color:#fff; font-size:11px; padding:4px 10px;" onclick="alert('Opening Safe Preview...')">Preview</button>
              <button class="nav-btn" style="background:#334155; color:#fff; font-size:11px; padding:4px 10px;" onclick="alert('Creating Working Copy in Derived/ and opening in Word...')">Open Working Copy</button>
              <button class="nav-btn" style="background:var(--primary); color:#fff; font-size:11px; padding:4px 10px;" onclick="alert('Opening Windows Print Dialog...')">Manual Print</button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>

  <script>
    function switchMainTab(tabId, btn) {
      document.querySelectorAll('.main-view').forEach(el => el.classList.remove('active'));
      document.querySelectorAll('.nav-btn').forEach(el => el.classList.remove('active'));
      document.getElementById(tabId).classList.add('active');
      btn.classList.add('active');
    }

    let customerFileCount = 0;
    function handleCustomerFiles(files) {
      customerFileCount = files.length;
      const box = document.getElementById('fileSummary');
      box.style.display = 'block';
      box.innerHTML = '<strong>' + files.length + ' file(s) selected:</strong> ' + Array.from(files).map(f => f.name).join(', ');
    }

    function submitCustomerUpload() {
      const code = 'SD-' + Math.floor(1000 + Math.random() * 9000);
      document.getElementById('receiptCode').textContent = code;
      document.getElementById('receiptCount').textContent = customerFileCount || 1;
      document.getElementById('uploadFormCard').style.display = 'none';
      document.getElementById('customerReceiptCard').style.display = 'block';

      // Also add to desktop simulator
      const list = document.getElementById('desktopSubmissionsList');
      const item = document.createElement('div');
      item.style = 'background: #1e293b; border: 1px solid var(--border); border-radius: 12px; padding: 14px;';
      item.innerHTML = \`
        <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom: 8px;">
          <div>
            <strong style="color: #38bdf8; font-size: 16px;">\${code}</strong>
            <span style="margin-left: 8px; font-size: 13px; font-weight: 600;">\${document.getElementById('cName').value || 'Customer'}</span>
            <span class="badge badge-blue" style="margin-left: 8px;">\${document.getElementById('cService').value}</span>
          </div>
          <span style="font-size: 11px; color: var(--text-muted);">24.0h retention remaining</span>
        </div>
        <div style="font-size: 12px; color: var(--text-muted); margin-bottom: 10px;">
          Instructions: \${document.getElementById('cNotes').value || 'None'} • \${customerFileCount || 1} file(s)
        </div>
        <div style="display:flex; gap: 8px;">
          <button class="nav-btn" style="background:#334155; color:#fff; font-size:11px; padding:4px 10px;" onclick="alert('Opening Safe Preview...')">Preview</button>
          <button class="nav-btn" style="background:#334155; color:#fff; font-size:11px; padding:4px 10px;" onclick="alert('Creating Working Copy in Derived/ and opening in Word...')">Open Working Copy</button>
          <button class="nav-btn" style="background:var(--primary); color:#fff; font-size:11px; padding:4px 10px;" onclick="alert('Opening Windows Print Dialog...')">Manual Print</button>
        </div>
      \`;
      list.prepend(item);
      const count = parseInt(document.getElementById('inboxBadge').textContent) + 1;
      document.getElementById('inboxBadge').textContent = count;
    }

    function resetCustomerUpload() {
      document.getElementById('uploadFormCard').style.display = 'block';
      document.getElementById('customerReceiptCard').style.display = 'none';
      document.getElementById('fileSummary').style.display = 'none';
      document.getElementById('cFiles').value = '';
    }
  </script>
</body>
</html>`;

for (const dir of [outDirs[0], outDirs[1]]) {
  fs.writeFileSync(path.join(dir, 'index.html'), indexHtml);
  fs.writeFileSync(path.join(dir, '_redirects'), `/*    /index.html   200\n`);
}
// Also ensure root has index.html and _redirects
fs.writeFileSync(path.join(outDirs[2], 'index.html'), indexHtml);
fs.writeFileSync(path.join(outDirs[2], '_redirects'), `/*    /index.html   200\n`);

console.log('✅ Netlify web build completed in dist_netlify/, public/, and root');

