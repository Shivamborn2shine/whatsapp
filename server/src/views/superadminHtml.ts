export function renderSuperadminHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Scan Drop | Founder Super Admin</title>
  <style>
    :root {
      --primary: #4f46e5;
      --primary-hover: #4338ca;
      --bg: #0f172a;
      --card-bg: #1e293b;
      --card-hover: #26334d;
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
    .card { background: var(--card-bg); border: 1px solid var(--border); border-radius: 14px; padding: 20px; margin-bottom: 24px; }
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
    .btn-sm { padding: 4px 8px; font-size: 11px; border-radius: 6px; }
    .code-box { font-family: monospace; background: #0b1120; padding: 4px 8px; border-radius: 6px; border: 1px solid #1e293b; color: #38bdf8; font-weight: 600; display: inline-flex; align-items: center; gap: 6px; }
    .modal { display: none; position: fixed; inset: 0; background: rgba(0,0,0,0.7); backdrop-filter: blur(4px); align-items: center; justify-content: center; z-index: 100; }
    .modal.open { display: flex; }
    .modal-content { background: var(--card-bg); border: 1px solid var(--border); border-radius: 16px; width: 100%; max-width: 500px; padding: 24px; box-shadow: 0 20px 25px -5px rgba(0,0,0,0.5); }
    .form-group { margin-bottom: 14px; }
    .form-group label { display: block; font-size: 12px; font-weight: 600; color: var(--text-muted); margin-bottom: 6px; }
    .form-group input, .form-group select { width: 100%; padding: 10px 12px; background: #0f172a; border: 1px solid var(--border); border-radius: 8px; color: #fff; font-size: 13px; outline: none; }
    .form-group input:focus, .form-group select:focus { border-color: var(--primary); }
    .modal-actions { display: flex; justify-content: flex-end; gap: 10px; margin-top: 20px; }
  </style>
</head>
<body>
  <header>
    <div class="brand">
      <span class="title">⚡ Scan Drop</span>
      <span class="badge-superadmin">Founder SaaS Console</span>
    </div>
    <div style="font-size: 12px; color: var(--text-muted);">Central Licensing & Device Authority</div>
  </header>

  <div class="container">
    <div class="stats-grid">
      <div class="stat-card">
        <div class="stat-label">Total Registered Shops</div>
        <div class="stat-val" id="statShops">-</div>
      </div>
      <div class="stat-card">
        <div class="stat-label">Active Subscriptions</div>
        <div class="stat-val" id="statSubs" style="color: #34d399;">-</div>
      </div>
      <div class="stat-card">
        <div class="stat-label">Active Windows PCs</div>
        <div class="stat-val" id="statDevices" style="color: #60a5fa;">-</div>
      </div>
      <div class="stat-card">
        <div class="stat-label">Total Submissions</div>
        <div class="stat-val" id="statSubmissions">-</div>
      </div>
      <div class="stat-card">
        <div class="stat-label">Processed Volume</div>
        <div class="stat-val" id="statStorage">-</div>
      </div>
    </div>

    <div class="tabs">
      <button class="tab-btn active" onclick="switchTab('shopsTab', this)">Shops & Subscriptions</button>
      <button class="tab-btn" onclick="switchTab('devicesTab', this)">Windows Device Fleet</button>
      <button class="tab-btn" onclick="switchTab('plansTab', this)">Subscription Plans</button>
      <button class="tab-btn" onclick="switchTab('auditTab', this)">Audit Events</button>
    </div>

    <!-- Shops Tab -->
    <div id="shopsTab" class="tab-content">
      <div class="card">
        <div class="card-header">
          <div class="card-title">Registered Print Shops</div>
          <button class="btn" onclick="openCreateShopModal()">+ Register New Shop</button>
        </div>
        <table>
          <thead>
            <tr>
              <th>Shop Name / Slug</th>
              <th>Owner & Contact</th>
              <th>Plan & Tier</th>
              <th>Status</th>
              <th>Pairing Code</th>
              <th>Active PCs</th>
              <th>Expiry Date</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody id="shopsTableBody">
            <tr><td colspan="8" style="text-align:center;">Loading shops...</td></tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Devices Tab -->
    <div id="devicesTab" class="tab-content" style="display:none;">
      <div class="card">
        <div class="card-header">
          <div class="card-title">Paired Windows Devices Fleet</div>
        </div>
        <table>
          <thead>
            <tr>
              <th>Device Name</th>
              <th>Shop Assigned</th>
              <th>OS / App Version</th>
              <th>Status</th>
              <th>Last Heartbeat</th>
              <th>Last IP</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody id="devicesTableBody">
            <tr><td colspan="7" style="text-align:center;">Loading devices...</td></tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Plans Tab -->
    <div id="plansTab" class="tab-content" style="display:none;">
      <div class="card">
        <div class="card-header">
          <div class="card-title">Subscription Tiers & Entitlements</div>
        </div>
        <table>
          <thead>
            <tr>
              <th>Plan Name</th>
              <th>Tier</th>
              <th>Price</th>
              <th>Max Devices</th>
              <th>Submissions Limit</th>
              <th>Batch Conversion</th>
              <th>Advanced PDF</th>
              <th>Printer Profiles</th>
            </tr>
          </thead>
          <tbody id="plansTableBody">
            <tr><td colspan="8" style="text-align:center;">Loading plans...</td></tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Audit Tab -->
    <div id="auditTab" class="tab-content" style="display:none;">
      <div class="card">
        <div class="card-header">
          <div class="card-title">System Audit & Security Trail</div>
        </div>
        <table>
          <thead>
            <tr>
              <th>Timestamp</th>
              <th>Actor</th>
              <th>Event Type</th>
              <th>Description</th>
            </tr>
          </thead>
          <tbody id="auditTableBody">
            <tr><td colspan="4" style="text-align:center;">Loading audit trail...</td></tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>

  <!-- Create Shop Modal -->
  <div class="modal" id="createShopModal">
    <div class="modal-content">
      <h3 style="margin-bottom: 16px;">Register New Shop</h3>
      <form id="createShopForm">
        <div class="form-group">
          <label>Shop Business Name</label>
          <input type="text" id="newShopName" placeholder="e.g. Metro Print & Graphics" required>
        </div>
        <div class="form-group">
          <label>Shop URL Slug (for QR upload)</label>
          <input type="text" id="newShopSlug" placeholder="e.g. metro-print" required>
        </div>
        <div class="form-group">
          <label>Shop Owner Name</label>
          <input type="text" id="newShopOwner" placeholder="e.g. Anand Patel" required>
        </div>
        <div class="form-group">
          <label>Owner Email</label>
          <input type="email" id="newShopEmail" placeholder="e.g. anand@metroprint.com" required>
        </div>
        <div class="form-group">
          <label>Owner Phone</label>
          <input type="text" id="newShopPhone" placeholder="e.g. +91 98765 00000">
        </div>
        <div class="form-group">
          <label>Initial Subscription Plan</label>
          <select id="newShopPlan">
            <option value="plan_free_trial">Free / 30-Day Trial (1 PC)</option>
            <option value="plan_pro_monthly">Pro Monthly ($29 / 1 PC, Unlimited Batch)</option>
            <option value="plan_business_monthly">Business Multi-PC ($69 / 5 PCs)</option>
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
      <h3 style="margin-bottom: 16px;">Manage Subscription & License</h3>
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
          <option value="plan_pro_monthly">Pro Monthly</option>
          <option value="plan_business_monthly">Business Multi-PC</option>
        </select>
      </div>
      <div class="form-group">
        <label>Payment Reference / Receipt (Optional)</label>
        <input type="text" id="managePaymentRef" placeholder="e.g. UPI-TXN-984321 / STRIPE-INV-102">
      </div>
      <div class="modal-actions">
        <button type="button" class="btn btn-secondary" onclick="closeModal('manageSubModal')">Cancel</button>
        <button type="button" class="btn" onclick="saveSubscriptionChange()">Update License</button>
      </div>
    </div>
  </div>

  <script>
    function switchTab(tabId, btn) {
      document.querySelectorAll('.tab-content').forEach(el => el.style.display = 'none');
      document.querySelectorAll('.tab-btn').forEach(el => el.classList.remove('active'));
      document.getElementById(tabId).style.display = 'block';
      btn.classList.add('active');

      if (tabId === 'devicesTab') loadDevices();
      if (tabId === 'plansTab') loadPlans();
      if (tabId === 'auditTab') loadAuditLogs();
    }

    async function loadOverview() {
      try {
        const res = await fetch('/api/v1/superadmin/overview');
        const data = await res.json();
        if (data.success) {
          document.getElementById('statShops').textContent = data.stats.totalShops;
          document.getElementById('statSubs').textContent = data.stats.activeSubscriptions;
          document.getElementById('statDevices').textContent = data.stats.activeDevices;
          document.getElementById('statSubmissions').textContent = data.stats.totalSubmissions;
          document.getElementById('statStorage').textContent = (data.stats.totalStorageBytes / (1024 * 1024)).toFixed(1) + ' MB';
        }
      } catch (e) {
        console.error('Failed to load stats', e);
      }
    }

    async function loadShops() {
      try {
        const res = await fetch('/api/v1/superadmin/shops');
        const data = await res.json();
        const tbody = document.getElementById('shopsTableBody');
        tbody.innerHTML = '';

        if (!data.shops || data.shops.length === 0) {
          tbody.innerHTML = '<tr><td colspan="8" style="text-align:center;">No shops found</td></tr>';
          return;
        }

        data.shops.forEach(s => {
          const tr = document.createElement('tr');
          const expiryDate = s.expiry_date ? new Date(s.expiry_date).toLocaleDateString() : 'N/A';
          tr.innerHTML = \`
            <td>
              <strong>\${s.name}</strong><br>
              <a href="/shop/\${s.slug}" target="_blank" style="color: #60a5fa; font-size: 11px;">/shop/\${s.slug} ↗</a>
            </td>
            <td>
              \${s.owner_name}<br>
              <span style="font-size: 11px; color: var(--text-muted);">\${s.owner_email}</span>
            </td>
            <td>
              <strong>\${s.plan_name || 'Pro'}</strong><br>
              <span style="font-size: 11px; text-transform: uppercase; color: #818cf8;">\${s.plan_tier || 'PRO'}</span>
            </td>
            <td>
              <span class="status-pill status-\${s.subscription_status || 'active'}">\${s.subscription_status || 'active'}</span>
            </td>
            <td>
              <div class="code-box">
                \${s.pairing_code || 'EXPIRED'}
                <button class="btn btn-sm btn-secondary" onclick="navigator.clipboard.writeText('\${s.pairing_code}'); alert('Pairing code copied!')">📋</button>
              </div>
            </td>
            <td>\${s.active_devices_count || 0}</td>
            <td>\${expiryDate}</td>
            <td>
              <div style="display: flex; gap: 6px;">
                <button class="btn btn-sm btn-secondary" onclick="openManageSub('\${s.id}', '\${s.plan_id}')">License</button>
                <button class="btn btn-sm btn-secondary" onclick="regenPairingCode('\${s.id}')">New Code</button>
              </div>
            </td>
          \`;
          tbody.appendChild(tr);
        });
      } catch (e) {
        console.error('Failed to load shops', e);
      }
    }

    async function loadDevices() {
      try {
        const res = await fetch('/api/v1/superadmin/shops');
        const shopData = await res.json();
        const tbody = document.getElementById('devicesTableBody');
        tbody.innerHTML = '';

        let allDevices = [];
        for (const shop of shopData.shops) {
          const devRes = await fetch(\`/api/v1/superadmin/shops/\${shop.id}/devices\`);
          const devData = await devRes.json();
          if (devData.devices) {
            devData.devices.forEach(d => allDevices.push({ ...d, shopName: shop.name }));
          }
        }

        if (allDevices.length === 0) {
          tbody.innerHTML = '<tr><td colspan="7" style="text-align:center;">No Windows PCs currently paired</td></tr>';
          return;
        }

        allDevices.forEach(d => {
          const tr = document.createElement('tr');
          const lastHeartbeat = new Date(d.last_heartbeat_at).toLocaleTimeString();
          const isRecent = (Date.now() - new Date(d.last_heartbeat_at).getTime()) < 5 * 60 * 1000;
          tr.innerHTML = \`
            <td><strong>\${d.device_name}</strong><br><span style="font-size:11px;color:var(--text-muted);">\${d.id}</span></td>
            <td>\${d.shopName}</td>
            <td>\${d.windows_version || 'Windows 11'} / v\${d.app_version}</td>
            <td>
              <span class="status-pill \${isRecent && d.status === 'active' ? 'status-active' : 'status-revoked'}">
                \${d.status === 'revoked' ? 'Revoked' : (isRecent ? 'Online' : 'Offline')}
              </span>
            </td>
            <td>\${lastHeartbeat}</td>
            <td><code>\${d.last_ip || '127.0.0.1'}</code></td>
            <td>
              \${d.status !== 'revoked' ? \`<button class="btn btn-sm btn-danger" onclick="revokeDevice('\${d.id}')">Revoke PC</button>\` : '<span style="color:var(--text-muted);font-size:11px;">Revoked</span>'}
            </td>
          \`;
          tbody.appendChild(tr);
        });
      } catch (e) {
        console.error('Failed to load devices', e);
      }
    }

    async function loadPlans() {
      try {
        const res = await fetch('/api/v1/superadmin/plans');
        const data = await res.json();
        const tbody = document.getElementById('plansTableBody');
        tbody.innerHTML = '';

        data.plans.forEach(p => {
          const tr = document.createElement('tr');
          tr.innerHTML = \`
            <td><strong>\${p.name}</strong></td>
            <td><span class="status-pill status-active">\${p.tier}</span></td>
            <td>$\${(p.price_cents / 100).toFixed(2)} / \${p.billing_interval}</td>
            <td>\${p.entitlements.max_devices} PC(s)</td>
            <td>\${p.entitlements.max_monthly_submissions} / mo</td>
            <td>\${p.entitlements.batch_conversion_enabled ? '✓ Enabled' : '✕ Disabled'}</td>
            <td>\${p.entitlements.pdf_advanced_enabled ? '✓ Enabled' : '✕ Disabled'}</td>
            <td>\${p.entitlements.printer_profiles_enabled ? '✓ Enabled' : '✕ Disabled'}</td>
          \`;
          tbody.appendChild(tr);
        });
      } catch (e) {
        console.error('Failed to load plans', e);
      }
    }

    async function loadAuditLogs() {
      try {
        const res = await fetch('/api/v1/superadmin/audit-logs');
        const data = await res.json();
        const tbody = document.getElementById('auditTableBody');
        tbody.innerHTML = '';

        data.logs.forEach(l => {
          const tr = document.createElement('tr');
          tr.innerHTML = \`
            <td>\${new Date(l.created_at).toLocaleString()}</td>
            <td><span class="status-pill status-trial">\${l.actor_type}</span></td>
            <td><code>\${l.event_type}</code></td>
            <td>\${l.description}</td>
          \`;
          tbody.appendChild(tr);
        });
      } catch (e) {
        console.error('Failed to load audit logs', e);
      }
    }

    function openCreateShopModal() {
      document.getElementById('createShopModal').classList.add('open');
    }

    function closeModal(id) {
      document.getElementById(id).classList.remove('open');
    }

    document.getElementById('createShopForm').addEventListener('submit', async (e) => {
      e.preventDefault();
      const payload = {
        name: document.getElementById('newShopName').value,
        slug: document.getElementById('newShopSlug').value,
        ownerName: document.getElementById('newShopOwner').value,
        ownerEmail: document.getElementById('newShopEmail').value,
        ownerPhone: document.getElementById('newShopPhone').value,
        planId: document.getElementById('newShopPlan').value
      };

      try {
        const res = await fetch('/api/v1/superadmin/shops', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        });
        const data = await res.json();
        if (data.success) {
          alert('Shop created successfully! Pairing Code: ' + data.shop.pairingCode);
          closeModal('createShopModal');
          loadShops();
          loadOverview();
        } else {
          alert('Error: ' + data.error);
        }
      } catch (err) {
        alert('Network error: ' + err.message);
      }
    });

    async function regenPairingCode(shopId) {
      if (!confirm('Regenerate device pairing code for this shop?')) return;
      try {
        const res = await fetch(\`/api/v1/superadmin/shops/\${shopId}/pairing-code\`, { method: 'POST' });
        const data = await res.json();
        if (data.success) {
          alert('New Pairing Code: ' + data.pairingCode);
          loadShops();
        }
      } catch (e) {
        alert('Failed to regenerate pairing code');
      }
    }

    function openManageSub(shopId, planId) {
      document.getElementById('manageShopId').value = shopId;
      document.getElementById('managePlanId').value = planId || 'plan_pro_monthly';
      document.getElementById('manageSubModal').classList.add('open');
    }

    async function saveSubscriptionChange() {
      const shopId = document.getElementById('manageShopId').value;
      const action = document.getElementById('manageAction').value;
      const planId = document.getElementById('managePlanId').value;
      const paymentReference = document.getElementById('managePaymentRef').value;

      try {
        const res = await fetch(\`/api/v1/superadmin/shops/\${shopId}/subscription\`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ action, planId, paymentReference, durationDays: 30 })
        });
        const data = await res.json();
        if (data.success) {
          alert('Subscription updated!');
          closeModal('manageSubModal');
          loadShops();
          loadOverview();
        } else {
          alert('Error: ' + data.error);
        }
      } catch (e) {
        alert('Failed to update subscription');
      }
    }

    async function revokeDevice(deviceId) {
      if (!confirm('Are you sure you want to revoke this Windows PC? It will immediately lose cloud synchronization.')) return;
      try {
        const res = await fetch(\`/api/v1/superadmin/devices/\${deviceId}/revoke\`, { method: 'POST' });
        const data = await res.json();
        if (data.success) {
          alert('Device revoked');
          loadDevices();
          loadOverview();
        }
      } catch (e) {
        alert('Failed to revoke device');
      }
    }

    // Init
    loadOverview();
    loadShops();
  </script>
</body>
</html>`;
}

