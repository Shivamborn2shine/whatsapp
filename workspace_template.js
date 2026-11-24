const fs = require('fs');
const path = require('path');
const { studioCss, studioHtml, studioJs } = require('./studio_module');

function loadDataUri(relPath, mime) {
  try {
    const p = path.resolve(__dirname, relPath);
    if (fs.existsSync(p)) {
      return 'data:' + mime + ';base64,' + fs.readFileSync(p).toString('base64');
    }
  } catch {}
  return '';
}

const FAVICON_URI = loadDataUri('assets/favicon-32.png', 'image/png');
const ICON_URI = loadDataUri('assets/scandrop-icon-96.png', 'image/png');

module.exports = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>ScanDrop — Shopkeeper Workspace</title>
  <link rel="icon" type="image/png" href="${FAVICON_URI || '/assets/favicon-32.png'}">
  <script src="https://unpkg.com/pdf-lib@1.17.1/dist/pdf-lib.min.js"></script>
  <script src="https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.min.js"></script>
  <style>
    :root {
      --primary: #0078FF;
      --primary-hover: #0060D1;
      --primary-soft: #EBF5FF;
      --navy: #0B2545;
      --sky: #58C3FF;
      --gold: #FFC107;
      --bg: #EAF5FF;
      --surface: #FFFFFF;
      --surface-alt: #F4F9FF;
      --border: #D2E6FA;
      --text: #0B2545;
      --text-secondary: #334E68;
      --muted: #627D98;
      --success: #16a34a;
      --warning: #d97706;
      --danger: #dc2626;
      --row-hover: #F0F7FF;
      --row-selected: #DFF0FF;
      --card-shadow: 0 8px 26px rgba(0, 120, 255, 0.085);
    }
    body.dark {
      --primary: #218BFF;
      --primary-hover: #0078FF;
      --primary-soft: rgba(33,139,255,0.16);
      --navy: #061326;
      --sky: #58C3FF;
      --gold: #FFC107;
      --bg: #071426;
      --surface: #0D1F38;
      --surface-alt: #132A4A;
      --border: #1E3A5F;
      --text: #F0F6FF;
      --text-secondary: #C6DCF5;
      --muted: #84A5C8;
      --row-hover: #142D50;
      --row-selected: rgba(0,120,255,0.22);
      --card-shadow: 0 10px 28px rgba(0, 0, 0, 0.35);
    }
    * { box-sizing: border-box; margin: 0; padding: 0; font-family: "Inter", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
    body { background: var(--bg); color: var(--text); height: 100vh; display: flex; overflow: hidden; }

    /* Deep Navy Brand Sidebar with Blue + Golden Bolt Sweep */
    .sidebar {
      width: 228px;
      background:
        radial-gradient(ellipse 140% 42% at 22% 72%, rgba(0, 120, 255, 0.34) 0%, rgba(0, 120, 255, 0) 68%),
        radial-gradient(ellipse 120% 20% at 75% 74%, rgba(255, 193, 7, 0.32) 0%, rgba(255, 193, 7, 0) 70%),
        linear-gradient(180deg, #0B2545 0%, #091E3A 56%, #07162C 100%);
      border-right: 1px solid rgba(88, 195, 255, 0.16);
      display: flex;
      flex-direction: column;
      flex-shrink: 0;
      color: #E2EEFF;
      position: relative;
      overflow: hidden;
    }
    .sidebar::after {
      content: "";
      position: absolute;
      left: -30px; right: -30px; bottom: 118px; height: 90px;
      background: linear-gradient(162deg, transparent 26%, rgba(0,120,255,0.36) 46%, rgba(255,193,7,0.52) 52%, transparent 64%);
      filter: blur(8px);
      pointer-events: none;
    }
    .sidebar-brand { padding: 18px 16px 14px; display: flex; align-items: center; gap: 10px; border-bottom: 1px solid rgba(88, 195, 255, 0.12); position: relative; z-index: 1; }
    .brand-title { font-weight: 900; font-size: 18px; letter-spacing: -0.02em; line-height: 1.1; }
    .brand-title .scan { color: #FFFFFF; }
    .brand-title .drop { color: #58C3FF; }
    .brand-sub { font-size: 10.5px; color: #8FB8E8; font-weight: 600; margin-top: 2px; }
    .nav-list { padding: 12px 10px; flex: 1; display: flex; flex-direction: column; gap: 5px; overflow-y: auto; position: relative; z-index: 1; }
    .nav-item { display: flex; align-items: center; justify-content: space-between; padding: 10px 13px; border-radius: 12px; color: #C6DCF7; font-size: 13px; font-weight: 600; cursor: pointer; border: 1px solid transparent; background: transparent; width: 100%; text-align: left; transition: all 0.16s; }
    .nav-item:hover { background: rgba(88, 195, 255, 0.12); color: #FFFFFF; }
    .nav-item.active {
      background: linear-gradient(90deg, #0078FF 0%, #2496FF 100%);
      color: #FFFFFF;
      box-shadow: 0 6px 18px rgba(0, 120, 255, 0.42);
      border-color: rgba(88, 195, 255, 0.45);
    }
    .nav-left { display: flex; align-items: center; gap: 10px; }
    .badge { padding: 2px 8px; border-radius: 9999px; font-size: 11px; font-weight: 800; white-space: nowrap; }
    .badge-gold { background: #FFC107; color: #0B2545; min-width: 22px; text-align: center; box-shadow: 0 2px 6px rgba(255, 193, 7, 0.35); }
    .badge-red { background: #ef4444; color: #fff; }
    .badge-amber { background: #FFC107; color: #0B2545; }
    .badge-blue { background: #DCEEFF; color: #0060D1; }
    .badge-green { background: #DCFCE7; color: #15803d; }
    .badge-gray { background: var(--surface-alt); color: var(--muted); border: 1px solid var(--border); }
    .nav-divider { height: 1px; background: rgba(88, 195, 255, 0.14); margin: 8px 6px; }
    .station-box {
      margin: 12px;
      padding: 12px 13px;
      background: rgba(8, 25, 48, 0.72);
      border: 1px solid rgba(88, 195, 255, 0.22);
      border-radius: 13px;
      font-size: 11.5px;
      color: #A7C7EA;
      backdrop-filter: blur(8px);
      position: relative;
      z-index: 1;
    }
    .station-dot { display: inline-block; width: 7px; height: 7px; border-radius: 50%; background: #22c55e; margin-right: 5px; box-shadow: 0 0 8px #22c55e; }

    /* Flowing Ice-Blue Wave Main Area */
    .main-area {
      flex: 1;
      display: flex;
      flex-direction: column;
      overflow: hidden;
      min-width: 0;
      background-color: var(--bg);
      background-image:
        radial-gradient(ellipse 70% 45% at 88% 12%, rgba(88, 195, 255, 0.22) 0%, transparent 70%),
        radial-gradient(ellipse 65% 50% at 18% 82%, rgba(0, 120, 255, 0.14) 0%, transparent 70%),
        linear-gradient(155deg, #F3F9FF 0%, #E7F3FF 48%, #D9ECFF 100%);
    }
    body.dark .main-area {
      background-image:
        radial-gradient(ellipse 70% 45% at 88% 12%, rgba(0, 120, 255, 0.16) 0%, transparent 70%),
        radial-gradient(ellipse 60% 50% at 15% 85%, rgba(88, 195, 255, 0.10) 0%, transparent 70%),
        linear-gradient(155deg, #071426 0%, #091B33 55%, #071428 100%);
    }

    .topbar {
      height: 66px;
      background: rgba(255, 255, 255, 0.90);
      backdrop-filter: blur(12px);
      border-bottom: 1px solid rgba(0, 120, 255, 0.14);
      padding: 0 22px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      flex-shrink: 0;
      box-shadow: 0 2px 14px rgba(0, 120, 255, 0.05);
    }
    body.dark .topbar { background: rgba(11, 29, 53, 0.92); border-bottom-color: rgba(88, 195, 255, 0.16); }
    .search-wrap { position: relative; display: flex; align-items: center; }
    .search-input { background: var(--surface); border: 1px solid var(--border); border-radius: 999px; padding: 7px 56px 7px 32px; color: var(--text); font-size: 12.5px; width: 225px; outline: none; transition: all 0.15s; }
    .search-input:focus { border-color: var(--primary); box-shadow: 0 0 0 3px rgba(0, 120, 255, 0.14); }
    .kbd-pill { position: absolute; right: 9px; font-size: 10px; color: var(--muted); background: var(--surface-alt); border: 1px solid var(--border); padding: 2px 6px; border-radius: 999px; font-weight: 700; }

    .btn { background: linear-gradient(180deg, #1687FF 0%, #0070F0 100%); color: #fff; border: none; border-radius: 10px; padding: 8px 14px; font-size: 12px; font-weight: 700; cursor: pointer; display: inline-flex; align-items: center; justify-content: center; gap: 5px; transition: all 0.15s; white-space: nowrap; box-shadow: 0 3px 10px rgba(0, 120, 255, 0.24); }
    .btn:hover { background: linear-gradient(180deg, #0078FF 0%, #005CC8 100%); transform: translateY(-0.5px); }
    .btn:disabled { opacity: 0.45; cursor: not-allowed; transform: none; box-shadow: none; }
    .btn-outline { background: var(--surface); color: var(--text-secondary); border: 1px solid var(--border); box-shadow: 0 1px 3px rgba(11, 37, 69, 0.05); }
    .btn-outline:hover { background: var(--primary-soft); color: var(--primary); border-color: rgba(0, 120, 255, 0.35); transform: none; }
    .btn-sync { background: var(--primary-soft); color: var(--primary); border: 1px solid rgba(0, 120, 255, 0.28); box-shadow: none; }
    .btn-sync:hover { background: #DCEEFF; color: #005CC8; }
    .btn-sync-solid { background: linear-gradient(180deg, #1687FF 0%, #0070F0 100%); color: #fff; border: none; border-radius: 999px; padding: 7px 15px; box-shadow: 0 4px 12px rgba(0, 120, 255, 0.32); }
    .btn-undo-gold { background: rgba(255, 193, 7, 0.18); color: #8A5A00; border: 1px solid rgba(255, 193, 7, 0.55); box-shadow: none; border-radius: 999px; padding: 6px 12px; font-size: 11.5px; }
    body.dark .btn-undo-gold { color: #FFD54F; background: rgba(255, 193, 7, 0.15); }
    .btn-undo-gold:hover { background: #FFC107; color: #0B2545; }
    .btn-danger-soft { background: #FFF1F2; color: #DC2626; border: 1px solid #FECDD3; box-shadow: none; }
    body.dark .btn-danger-soft { background: rgba(220, 38, 38, 0.14); color: #F87171; border-color: rgba(220, 38, 38, 0.35); }
    .btn-danger-soft:hover { background: #FFE4E6; color: #B91C1C; }
    .btn-amber-soft { background: rgba(255, 193, 7, 0.20); color: #92400E; border: 1px solid rgba(255, 193, 7, 0.5); box-shadow: none; }

    .workspace-scroll { flex: 1; overflow-y: auto; padding: 16px 20px; display: flex; flex-direction: column; gap: 16px; }
    .stats-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(165px, 1fr)); gap: 12px; }
    .stat-card { background: rgba(255, 255, 255, 0.94); border: 1px solid var(--border); border-radius: 16px; padding: 14px 16px; display: flex; align-items: center; gap: 12px; box-shadow: var(--card-shadow); }
    body.dark .stat-card { background: var(--surface); }
    .stat-icon { width: 40px; height: 40px; border-radius: 12px; display: flex; align-items: center; justify-content: center; font-size: 18px; background: var(--primary-soft); }
    .stat-num { font-size: 20px; font-weight: 800; line-height: 1.15; color: var(--text); }
    .stat-lbl { font-size: 11.5px; font-weight: 600; color: var(--muted); margin-top: 2px; }

    .split-grid { display: flex; gap: 16px; flex: 1; min-height: 0; align-items: stretch; }
    .inbox-left-col { flex: 1; display: flex; flex-direction: column; gap: 14px; min-width: 0; }
    .list-panel { background: rgba(255, 255, 255, 0.95); border: 1px solid var(--border); border-radius: 18px; display: flex; flex-direction: column; overflow: hidden; min-width: 0; box-shadow: var(--card-shadow); }
    body.dark .list-panel { background: var(--surface); }
    .inbox-brand-stage { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 18px 16px; min-height: 120px; pointer-events: none; user-select: none; }
    .inbox-brand-stage img { max-width: 300px; width: 46%; min-width: 170px; height: auto; opacity: 0.94; filter: drop-shadow(0 12px 24px rgba(0, 120, 255, 0.16)); }
    .inspector-panel { width: 396px; background: rgba(255, 255, 255, 0.96); border: 1px solid var(--border); border-radius: 18px; padding: 16px 18px; flex-shrink: 0; display: flex; flex-direction: column; gap: 11px; box-shadow: var(--card-shadow); align-self: flex-start; max-height: 100%; overflow-y: auto; }
    body.dark .inspector-panel { background: var(--surface); }

    .filter-bar { display: flex; align-items: center; justify-content: space-between; padding: 0 16px; border-bottom: 1px solid var(--border); gap: 8px; flex-wrap: wrap; background: var(--surface); }
    .filter-tabs { display: flex; gap: 16px; }
    .filter-tab { padding: 12px 2px; font-size: 12.5px; font-weight: 700; color: var(--muted); background: none; border: none; border-bottom: 2.5px solid transparent; cursor: pointer; transition: all 0.14s; }
    .filter-tab:hover { color: var(--text); }
    .filter-tab.active { color: var(--primary); border-bottom-color: var(--primary); }

    .sub-row { display: flex; align-items: center; gap: 12px; padding: 12px 16px; border-bottom: 1px solid var(--border); cursor: pointer; transition: background 0.12s; border-left: 3.5px solid transparent; }
    .sub-row:hover { background: var(--row-hover); }
    .sub-row.selected { background: var(--row-selected); border-left-color: #0078FF; }
    .sub-row-main { flex: 1; min-width: 0; }
    .sub-row-meta { font-size: 12px; color: var(--muted); margin-top: 3px; }
    .bulk-bar { padding: 10px 16px; background: var(--surface-alt); border-top: 1px solid var(--border); display: flex; align-items: center; justify-content: space-between; gap: 8px; flex-wrap: wrap; }

    .insp-tabs { display: flex; gap: 16px; border-bottom: 1px solid var(--border); }
    .insp-tab { padding: 7px 2px; font-size: 12.5px; font-weight: 700; color: var(--muted); background: none; border: none; border-bottom: 2.5px solid transparent; cursor: pointer; }
    .insp-tab.active { color: var(--primary); border-bottom-color: var(--primary); }
    .insp-file-card { border: 1px solid var(--border); border-radius: 12px; background: var(--surface); padding: 10px 11px; margin-bottom: 8px; box-shadow: 0 2px 8px rgba(0, 120, 255, 0.05); }
    .insp-file-top { display: flex; align-items: center; gap: 9px; margin-bottom: 8px; }
    .insp-file-actions { display: flex; gap: 5px; flex-wrap: wrap; }
    .mini-btn { padding: 4px 8px; font-size: 11px; border-radius: 7px; border: 1px solid var(--border); background: var(--surface-alt); color: var(--text-secondary); cursor: pointer; font-weight: 700; transition: all 0.12s; }
    .mini-btn:hover { background: var(--primary-soft); color: var(--primary); border-color: rgba(0, 120, 255, 0.35); }
    .mini-btn-edit { background: var(--primary-soft); color: var(--primary); border-color: rgba(0, 120, 255, 0.35); }
    .mini-btn-edit:hover { background: var(--primary); color: #fff; }
    .mini-btn-pages { background: rgba(255, 193, 7, 0.16); color: #8A5A00; border-color: rgba(255, 193, 7, 0.55); }
    body.dark .mini-btn-pages { color: #FFD54F; }
    .mini-btn-pages:hover { background: #FFC107; color: #0B2545; border-color: #FFC107; }
    .file-type-box { width: 34px; height: 34px; border-radius: 9px; display: flex; align-items: center; justify-content: center; font-size: 10px; font-weight: 800; color: #fff; flex-shrink: 0; box-shadow: 0 2px 6px rgba(0,0,0,0.12); }
    .ft-pdf { background: linear-gradient(135deg, #F43F5E, #DC2626); }
    .ft-img { background: linear-gradient(135deg, #10B981, #059669); }
    .ft-doc { background: linear-gradient(135deg, #218BFF, #0060D1); }
    .ft-other { background: #64748b; }

    .field-input { width: 100%; padding: 9px 11px; background: var(--surface-alt); border: 1px solid var(--border); border-radius: 9px; color: var(--text); font-size: 13px; }
    .modal-overlay { display: none; position: fixed; inset: 0; background: rgba(11, 37, 69, 0.62); backdrop-filter: blur(4px); align-items: center; justify-content: center; z-index: 100; }
    .modal-overlay.open { display: flex; }
    .modal-box { background: var(--surface); color: var(--text); border: 1px solid var(--border); border-radius: 16px; padding: 22px; width: 92%; max-width: 500px; box-shadow: 0 20px 35px rgba(11, 37, 69, 0.30); }
    table { width: 100%; border-collapse: collapse; font-size: 12.5px; text-align: left; }
    th { padding: 10px; border-bottom: 1px solid var(--border); color: var(--muted); font-weight: 700; }
    td { padding: 10px; border-bottom: 1px solid var(--border); }
    code { background: var(--surface-alt); padding: 2px 6px; border-radius: 6px; font-size: 11.5px; border: 1px solid var(--border); }
${studioCss}
  </style>
</head>
<body>
  <aside class="sidebar">
    <div class="sidebar-brand">
      <img src="${ICON_URI || '/assets/scandrop-icon-96.png'}" alt="ScanDrop" style="width:38px;height:38px;border-radius:10px;display:block;flex-shrink:0;box-shadow:0 4px 12px rgba(0,120,255,0.4);">
      <div>
        <div class="brand-title"><span class="scan">Scan</span><span class="drop">Drop</span></div>
        <div class="brand-sub">Instant File Drop</div>
      </div>
    </div>

    <nav class="nav-list">
      <button class="nav-item" id="nav-dashboard" onclick="setTab('dashboard', this)">
        <span class="nav-left"><span>📊</span><span>Dashboard</span></span>
      </button>
      <button class="nav-item active" id="nav-inbox" onclick="setTab('inbox', this)">
        <span class="nav-left"><span>📥</span><span>Inbox</span></span>
        <span class="badge badge-gold" id="badgeInbox">0</span>
      </button>
      <button class="nav-item" id="nav-fileManager" onclick="setTab('fileManager', this)">
        <span class="nav-left"><span>📁</span><span>File Manager</span></span>
      </button>
      <button class="nav-item" id="nav-trash" onclick="setTab('trash', this)" ondragover="handleTrashZoneDragOver(event)" ondragleave="this.classList.remove('drag-active')" ondrop="handleTrashZoneDrop(event)">
        <span class="nav-left"><span>🗑️</span><span>Trash</span></span>
        <span class="badge badge-gold" id="badgeTrash" style="display:none;">0</span>
      </button>
      <button class="nav-item" id="nav-qr" onclick="setTab('qr', this)">
        <span class="nav-left"><span>⊞</span><span>QR &amp; Sharing</span></span>
      </button>
      <button class="nav-item" id="nav-activity" onclick="setTab('activity', this)">
        <span class="nav-left"><span>🕒</span><span>Activity</span></span>
      </button>
      <button class="nav-item" id="nav-settings" onclick="setTab('settings', this)">
        <span class="nav-left"><span>⚙️</span><span>Settings</span></span>
      </button>
    </nav>

    <div class="station-box">
      <div style="font-weight:800;color:#FFFFFF;font-size:12.5px;margin-bottom:5px;display:flex;align-items:center;gap:6px;">
        <span>🏪</span><span id="sidebarShopName">Local PC Station</span>
      </div>
      <div><span class="station-dot"></span><span id="sidebarReceiverStatus">Receiver: Checking...</span></div>
      <div id="sidebarStorageMini" style="margin-top:4px;color:#8FB8E8;">Storage: --</div>
    </div>
  </aside>

  <main class="main-area">
    <header class="topbar">
      <div style="display:flex;align-items:center;gap:11px;min-width:0;">
        <div style="width:36px;height:36px;border-radius:11px;background:linear-gradient(135deg,#FFF4CC,#FFE082);display:flex;align-items:center;justify-content:center;font-size:18px;box-shadow:0 3px 10px rgba(255,193,7,0.28);flex-shrink:0;" id="viewIcon">📂</div>
        <div style="min-width:0;">
          <h1 id="viewTitle" style="font-size:17.5px;font-weight:900;letter-spacing:-0.01em;">File Inbox</h1>
          <p id="viewSubtitle" style="font-size:11.5px;color:var(--muted);margin-top:1px;">Customer submissions stored locally on your Windows PC</p>
        </div>
      </div>

      <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;">
        <button id="globalUndoBtn" class="btn btn-undo-gold" style="display:none;" onclick="performGlobalUndo()" title="Undo last action (Ctrl+Z)">↩ Undo</button>
        <span id="storageTunnelBadge" class="badge badge-blue" style="font-size:11px;padding:6px 11px;">Connecting...</span>
        <button id="pauseToggleBtn" class="btn btn-outline" onclick="togglePauseReceiving()" style="padding:6px 11px;font-size:11.5px;border-radius:999px;">⏸ Pause</button>
        <button id="topbarStorageBtn" class="btn btn-outline" onclick="setTab('fileManager')" title="Inspect & Free Up Storage" style="padding:6px 11px;font-size:11.5px;border-radius:999px;">🧹 Storage</button>
        <div class="search-wrap">
          <span style="position:absolute;left:11px;color:var(--primary);font-size:12px;">🔍</span>
          <input type="text" id="searchInput" class="search-input" placeholder="Search receipt, name, file..." oninput="renderCurrentView()">
          <span class="kbd-pill">Ctrl+F</span>
        </div>
        <button id="themeToggleBtn" class="btn btn-outline" onclick="toggleTheme()" title="Toggle Light / Dark Mode" style="padding:7px 10px;border-radius:999px;">🌙</button>
        <button class="btn btn-sync-solid" onclick="fetchCloudState(true)" title="Sync Now (F5)">↻ Sync</button>
      </div>
    </header>

    <div class="workspace-scroll" id="workspaceContent">
      <div style="text-align:center;padding:60px;color:var(--muted);">Loading local Scan Drop workspace...</div>
    </div>

    <!-- DOCKED HOLDING TRAY (Temporary Staging Shelf Across All Tabs & Jobs) -->
    <section id="holdingTrayBar" class="holding-tray"
      ondragover="handleTrayBarDragOver(event)"
      ondragleave="handleTrayBarDragLeave(event)"
      ondrop="handleTrayBarDrop(event)">
      <div class="tray-header">
        <div class="tray-header-left">
          <strong style="font-size:13px;">📌 Holding Tray</strong>
          <span id="trayCountBadge" class="badge badge-gold">0</span>
          <span style="font-size:11px;color:var(--muted);">Keep files temporarily • Drag from any job or PC • Drag out to any job or Merge &amp; Print</span>
        </div>
        <div class="tray-header-actions">
          <input type="file" id="trayAddFileInput" multiple style="display:none;" onchange="if(this.files&&this.files.length){uploadFilesToTray(this.files);this.value='';}">
          <button class="btn btn-outline" style="padding:5px 10px;font-size:11px;border-radius:8px;" onclick="document.getElementById('trayAddFileInput').click()">+ Add Files</button>
          <button class="btn btn-sync" id="trayCopyToSubBtn" style="padding:5px 10px;font-size:11px;border-radius:8px;display:none;" onclick="copyAllTrayToSelectedJob()">📥 Add All to Job</button>
          <button class="btn" id="trayMergeBtn" style="padding:5px 11px;font-size:11px;border-radius:8px;background:linear-gradient(180deg,#22c55e,#16a34a);display:none;" onclick="mergeAndPrintTray()">📑 Merge &amp; Print Tray</button>
          <button class="btn btn-danger-soft" id="trayClearBtn" style="padding:5px 10px;font-size:11px;border-radius:8px;display:none;" onclick="clearHoldingTray()">🧹 Clear</button>
          <button class="btn btn-outline" id="trayToggleBtn" style="padding:5px 9px;font-size:11px;border-radius:8px;" onclick="toggleHoldingTray()">▾ Minimize</button>
        </div>
      </div>
      <div class="tray-shelf-wrap">
        <button class="tray-nav-btn" onclick="scrollTrayShelf(-260)" title="Scroll Left">‹</button>
        <div id="trayShelfItems" class="tray-shelf" style="flex:1;"></div>
        <button class="tray-nav-btn" onclick="scrollTrayShelf(260)" title="Scroll Right">›</button>
      </div>
    </section>
  </main>

  <!-- SAFE PREVIEW MODAL -->
  <div class="modal-overlay" id="previewModal">
    <div class="modal-box" style="max-width:900px;height:86vh;display:flex;flex-direction:column;">
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;gap:8px;">
        <div style="min-width:0;">
          <strong id="previewTitle" style="font-size:15px;">File Preview</strong>
          <span style="font-size:11px;color:var(--muted);margin-left:8px;">Safe Local Viewer</span>
        </div>
        <div style="display:flex;gap:8px;">
          <button class="btn" id="previewPrintBtn">🖨️ Print</button>
          <button class="btn btn-outline" id="previewOpenBtn">↗ Open in App</button>
          <button class="btn btn-outline" onclick="closePreview()">Close (Esc)</button>
        </div>
      </div>
      <div id="previewBody" style="flex:1;background:var(--surface-alt);border:1px solid var(--border);border-radius:10px;overflow:hidden;display:flex;align-items:center;justify-content:center;"></div>
    </div>
  </div>

${studioHtml}

  <!-- Hidden 1px print frame so Chromium activates PDF plugin cleanly for 1-click direct print -->
  <iframe id="directPrintFrame" style="position:fixed;right:0;bottom:0;width:1px;height:1px;border:0;opacity:0.01;pointer-events:none;"></iframe>

  <script>
    let currentTab = 'inbox';
    let subFilter = 'all'; // all, received, processing, printed, archived
    let sortOrder = 'newest';
    let inspectorTab = 'files';
    let selectedSubId = null;
    let checkedSubIds = new Set();
    let cloudState = null;
    let lastMeta = {};
    let latestStorageStats = null;
    let currentSettings = {};
    let lastStateRenderKey = '';

    const TAB_META = {
      dashboard: ['Dashboard', 'Station status, tunnel health, daily volume, and recent uploads', '📊'],
      inbox: ['File Inbox', 'Grouped customer submissions received on your Windows PC', '📂'],
      fileManager: ['File Manager', 'Local storage breakdown, cleanup, and PDF / Word conversion outputs', '📁'],
      trash: ['Trash', 'Individual files dragged out of submissions — restore anytime or delete permanently', '🗑️'],
      qr: ['QR & Sharing', 'Customer QR code and upload links for your shop counter', '⊞'],
      activity: ['Activity Log', 'Operational events recorded in local SQLite database', '🕒'],
      settings: ['Settings', 'Shop name, storage folder, retention, upload limits, printers & updates', '⚙️']
    };

    function setTab(tab, btn) {
      currentTab = tab;
      const meta = TAB_META[tab] || ['Workspace', '', '⚡'];
      document.getElementById('viewTitle').textContent = meta[0];
      document.getElementById('viewSubtitle').textContent = meta[1];
      const ic = document.getElementById('viewIcon');
      if (ic) ic.textContent = meta[2] || '⚡';
      document.querySelectorAll('.nav-item').forEach(el => el.classList.remove('active'));
      const targetBtn = btn || document.getElementById('nav-' + tab);
      if (targetBtn) targetBtn.classList.add('active');
      renderCurrentView();
    }

    function applyTheme(theme) {
      const dark = theme === 'dark';
      document.body.classList.toggle('dark', dark);
      const b = document.getElementById('themeToggleBtn');
      if (b) b.textContent = dark ? '☀️' : '🌙';
      try { localStorage.setItem('scandrop_theme', dark ? 'dark' : 'light'); } catch {}
    }

    async function toggleTheme() {
      const next = document.body.classList.contains('dark') ? 'light' : 'dark';
      applyTheme(next);
      try {
        await fetch('/api/settings', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ theme: next })
        });
      } catch {}
    }

    applyTheme(localStorage.getItem('scandrop_theme') || 'light');

    function openModal(id) { const el = document.getElementById(id); if (el) el.classList.add('open'); }
    function closeModal(id) { const el = document.getElementById(id); if (el) el.classList.remove('open'); }
    function closePreview() { closeModal('previewModal'); document.getElementById('previewBody').innerHTML = ''; }

    function formatSize(bytes) {
      bytes = Number(bytes || 0);
      if (bytes < 1024) return bytes + ' B';
      if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
      if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
      return (bytes / (1024 * 1024 * 1024)).toFixed(2) + ' GB';
    }

    function timeAgo(iso) {
      if (!iso) return '-';
      const diff = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 1000));
      if (diff < 60) return diff + 's ago';
      if (diff < 3600) return Math.floor(diff / 60) + 'm ago';
      if (diff < 86400) return Math.floor(diff / 3600) + 'h ago';
      return Math.floor(diff / 86400) + 'd ago';
    }

    function esc(s) {
      return String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function getFileBadge(ext) {
      const e = (ext || '').toLowerCase();
      if (e === '.pdf') return ['ft-pdf', 'PDF'];
      if (['.jpg', '.jpeg', '.png', '.webp', '.bmp'].includes(e)) return ['ft-img', e.replace('.', '').toUpperCase()];
      if (['.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx', '.txt'].includes(e)) return ['ft-doc', e.replace('.', '').toUpperCase()];
      return ['ft-other', (e || 'FILE').replace('.', '').toUpperCase().slice(0, 4)];
    }

    function statusBadgeHtml(status) {
      const s = (status || 'received').toLowerCase();
      if (s === 'received' || s === 'incoming') return '<span class="badge badge-blue">Received</span>';
      if (s === 'processing') return '<span class="badge badge-amber">Processing</span>';
      if (s === 'printed' || s === 'completed') return '<span class="badge badge-green">Printed</span>';
      if (s === 'archived') return '<span class="badge badge-gray">Archived</span>';
      if (s === 'failed') return '<span class="badge badge-red">Failed</span>';
      if (s === 'expired') return '<span class="badge badge-gray">Expired</span>';
      return '<span class="badge badge-gray">' + esc(s) + '</span>';
    }

    async function fetchCloudState(forceRender = false) {
      try {
        const res = await fetch('/api/state');
        const data = await res.json();
        if (!data || !data.success) return;

        cloudState = data.state || {};
        lastMeta = data;
        currentSettings = data.settings || {};
        if (data.storage) latestStorageStats = data.storage;
        if (currentSettings.theme && !localStorage.getItem('scandrop_theme')) applyTheme(currentSettings.theme);

        // Update persistent topbar & sidebar indicators
        const shop = (cloudState.shops || [])[0] || { name: 'Scan Drop' };
        document.getElementById('sidebarShopName').textContent = shop.name || 'Scan Drop';
        document.getElementById('sidebarReceiverStatus').textContent =
          data.receiverRunning ? ('Receiver: Active (:' + data.receiverPort + ')') : 'Receiver: Local Mode';
        if (latestStorageStats) {
          const used = formatSize(latestStorageStats.totalUsedBytes);
          document.getElementById('sidebarStorageMini').textContent = 'SSD Used: ' + used;
          document.getElementById('topbarStorageBtn').textContent = '🧹 ' + used;
        }

        const badge = document.getElementById('storageTunnelBadge');
        if (badge) {
          const stName = data.tunnelState || (data.tunnelOnline ? 'CONNECTED' : 'CONNECTING');
          if (stName === 'CONNECTED') {
            badge.className = 'badge badge-green';
            badge.textContent = '⚡ ' + (data.tunnelHealth || 'Cloudflare Connected');
            badge.title = (data.tunnelProvider || 'Cloudflare') + ' • ' + (data.tunnelUrl || '');
          } else if (stName === 'DEGRADED') {
            badge.className = 'badge badge-amber';
            badge.textContent = '⚠️ ' + (data.tunnelHealth || 'Cloudflare Degraded — Verifying');
            badge.title = data.tunnelLastFailure || data.tunnelUrl || '';
          } else if (stName === 'COOLDOWN' || stName === 'RECONNECTING' || stName === 'OFFLINE') {
            badge.className = 'badge badge-red';
            badge.textContent = '⚠️ ' + (data.tunnelHealth || 'Cloudflare Offline — Reconnecting');
            badge.title = (data.tunnelLastFailure || '') + (data.tunnelNextRetryUtc ? (' • Next retry: ' + new Date(data.tunnelNextRetryUtc).toLocaleTimeString()) : '');
          } else {
            badge.className = 'badge badge-amber';
            badge.textContent = '⏳ ' + (data.tunnelHealth || 'Connecting Cloudflare...');
            badge.title = data.tunnelProvider || 'Cloudflare Tunnel';
          }
        }

        const pBtn = document.getElementById('pauseToggleBtn');
        if (pBtn) {
          const paused = data.acceptingUploads === false || currentSettings.pauseReceiving;
          pBtn.textContent = paused ? '▶ Paused — Resume' : '⏸ Pause Receiving';
          pBtn.className = paused ? 'btn btn-amber-soft' : 'btn btn-outline';
        }

        const newKey = JSON.stringify({
          subs: (cloudState.submissions || []).map(s => [s.id, s.status, (s.files || []).length, (s.trashedFiles || []).length, s.totalSizeBytes, (s.files || []).map(f => f.id + ':' + f.originalName + ':' + f.conversionStatus + ':' + (f.sortOrder || 0) + ':' + (f.pagePlanJson || '')).join(',')]),
          trash: (cloudState.trashedFiles || []).map(t => t.id).join(','),
          tray: (cloudState.trayItems || []).map(t => t.id + ':' + (t.sortOrder || 0)).join(','),
          conv: (cloudState.convertedFiles || []).length,
          acts: (cloudState.auditEvents || []).length,
          paused: data.acceptingUploads,
          tunnel: [data.tunnelOnline, data.tunnelUrl, data.tunnelState, data.tunnelHealth, data.tunnelPid, data.tunnelRetryCount, data.tunnelNextRetryUtc, data.tunnelLastFailure]
        });

        if (forceRender || newKey !== lastStateRenderKey) {
          lastStateRenderKey = newKey;
          renderCurrentView();
        }
      } catch (err) {
        console.error('Sync error:', err);
      }
    }

    async function togglePauseReceiving() {
      const paused = !(currentSettings.pauseReceiving || lastMeta.acceptingUploads === false);
      await fetch('/api/receiving/pause', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ paused })
      });
      await fetchCloudState(true);
    }

    async function updateSubStatus(subId, newStatus, e, skipUndo) {
      if (e) e.stopPropagation();
      const prevSub = ((cloudState && cloudState.submissions) || []).find(s => s.id === subId || s.receiptCode === subId);
      const prevStatus = prevSub ? prevSub.status : 'received';
      await fetch('/api/submission/status', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ submissionId: subId, status: newStatus })
      });
      if (!skipUndo && prevStatus && prevStatus !== newStatus) {
        pushGlobalUndo('Mark ' + subId + ' as ' + newStatus, () => updateSubStatus(subId, prevStatus, null, true));
        showToast('Updated ' + subId + ' → ' + newStatus, '↩ Undo', () => updateSubStatus(subId, prevStatus, null, true), 5500);
      }
      await fetchCloudState(true);
    }

    async function bulkUpdateSubStatus(ids, newStatus, skipUndo) {
      if (!ids || ids.length === 0) return;
      const prevPairs = ids.map(id => {
        const s = ((cloudState && cloudState.submissions) || []).find(x => x.id === id || x.receiptCode === id);
        return { id, status: s ? s.status : 'received' };
      });
      await Promise.all(ids.map(id => fetch('/api/submission/status', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ submissionId: id, status: newStatus })
      })));
      checkedSubIds.clear();
      if (!skipUndo) {
        const revertAll = async () => {
          await Promise.all(prevPairs.map(p => fetch('/api/submission/status', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ submissionId: p.id, status: p.status })
          })));
          await fetchCloudState(true);
        };
        pushGlobalUndo('Mark ' + ids.length + ' job(s) as ' + newStatus, revertAll);
        showToast('Updated ' + ids.length + ' job(s) → ' + newStatus, '↩ Undo', revertAll, 6000);
      }
      await fetchCloudState(true);
    }

    async function deleteSubmissions(ids) {
      if (!ids || ids.length === 0) return;
      if (!confirm('Permanently delete ' + ids.length + ' submission(s) from local disk?')) return;
      await fetch('/api/submission/delete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ submissionIds: ids })
      });
      checkedSubIds.clear();
      if (ids.includes(selectedSubId)) selectedSubId = null;
      await fetchCloudState(true);
    }

    async function openFileNative(fileId) {
      const res = await fetch('/api/file/open', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ fileId }) });
      if (!res.ok) alert('Could not open file in default Windows app.');
    }

    async function openFileWithNative(fileId) {
      const res = await fetch('/api/file/open-with', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ fileId }) });
      if (!res.ok) alert('Could not launch Open With dialog.');
    }

    async function revealFileInFolder(fileId) {
      await fetch('/api/file/reveal', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ fileId }) });
    }

    async function renameFilePrompt(fileId, currentName) {
      const next = prompt('Rename file display name:', currentName);
      if (!next || next.trim() === currentName) return;
      const res = await fetch('/api/file/rename', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileId, newName: next.trim() })
      });
      const data = await res.json();
      if (!res.ok || !data.success) alert(data.error || 'Rename failed');
      else {
        const revertRename = async () => {
          await fetch('/api/file/rename', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ fileId, newName: currentName }) });
          await fetchCloudState(true);
        };
        pushGlobalUndo('Rename "' + currentName + '"', revertRename);
        await fetchCloudState(true);
        showToast('Renamed to "' + (data.newName || next.trim()) + '"', '↩ Undo', revertRename, 6000);
      }
    }

    async function exportFilePermanent(fileId) {
      const res = await fetch('/api/file/export', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileId })
      });
      const data = await res.json();
      if (!res.ok || !data.success) alert(data.error || 'Export failed');
      else alert(data.message || 'Saved permanent copy outside 24-hour retention!');
    }

    async function convertFileToPdfServer(fileIdOrIds) {
      const payload = Array.isArray(fileIdOrIds) ? { fileIds: fileIdOrIds } : { fileId: fileIdOrIds };
      const res = await fetch('/api/file/convert-pdf', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await res.json();
      if (!res.ok || !data.success) {
        alert('Conversion failed: ' + (data.error || 'Unsupported file'));
        return;
      }
      await fetchCloudState(true);
      if (confirm('PDF created (' + data.outputFilename + ' via ' + data.engine + '). Preview it now?')) {
        previewUrl(data.url, data.outputFilename, '.pdf', null);
      }
    }

    function previewFile(fileId, fileName, ext) {
      const url = '/api/file?id=' + encodeURIComponent(fileId);
      previewUrl(url, fileName, ext, fileId);
    }

    function previewUrl(fileUrl, fileName, ext, fileId) {
      const body = document.getElementById('previewBody');
      document.getElementById('previewTitle').textContent = fileName;
      const e = (ext || '').toLowerCase();
      if (e === '.pdf') {
        body.innerHTML = '<iframe id="previewPdfFrame" src="' + fileUrl + '" style="width:100%;height:100%;border:none;"></iframe>';
      } else if (['.jpg', '.jpeg', '.png', '.webp', '.bmp'].includes(e)) {
        body.innerHTML = '<img src="' + fileUrl + '" style="max-width:100%;max-height:100%;object-fit:contain;">';
      } else {
        body.innerHTML = '<div style="text-align:center;padding:30px;color:var(--muted);">' +
          '<p style="margin-bottom:14px;">Browser preview is not available for <strong>' + esc(e) + '</strong> files.</p>' +
          (fileId ? '<button class="btn" onclick="openFileNative(\\'' + esc(fileId) + '\\')">↗ Open in Default Windows App</button> ' : '') +
          (fileId ? '<button class="btn btn-outline" onclick="convertFileToPdfServer(\\'' + esc(fileId) + '\\')">📄 Convert to PDF</button>' : '') +
          '</div>';
      }
      document.getElementById('previewPrintBtn').onclick = () => directPrint(fileUrl, fileName);
      const openBtn = document.getElementById('previewOpenBtn');
      openBtn.style.display = fileId ? 'inline-flex' : 'none';
      if (fileId) openBtn.onclick = () => openFileNative(fileId);
      openModal('previewModal');
    }

    // 1-Click Direct Print — bypasses any extra modal step and opens system print dialog immediately!
    async function directPrint(fileUrl, fileName) {
      const previewPdf = document.getElementById('previewPdfFrame');
      if (previewPdf && previewPdf.contentWindow && document.getElementById('previewModal').classList.contains('open')) {
        try {
          previewPdf.contentWindow.focus();
          previewPdf.contentWindow.print();
          return;
        } catch {}
      }

      const frame = document.getElementById('directPrintFrame');
      const isImage = /\\.(jpe?g|png|webp|gif|bmp)$/i.test(fileName || '');
      try {
        const res = await fetch(fileUrl);
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const blob = await res.blob();
        const blobUrl = URL.createObjectURL(blob);

        if (isImage) {
          frame.onload = () => {
            try { frame.contentWindow.focus(); frame.contentWindow.print(); } catch { window.open(fileUrl, '_blank'); }
            setTimeout(() => { try { URL.revokeObjectURL(blobUrl); } catch {} }, 60000);
          };
          frame.srcdoc = '<!doctype html><html><head><style>@page{margin:0;}body{margin:0;display:flex;align-items:center;justify-content:center;min-height:100vh;background:#fff;}img{max-width:100%;max-height:100vh;object-fit:contain;}</style></head><body><img src="' + blobUrl + '"></body></html>';
        } else {
          frame.removeAttribute('srcdoc');
          frame.onload = () => {
            setTimeout(() => {
              try { frame.contentWindow.focus(); frame.contentWindow.print(); } catch { window.open(fileUrl, '_blank'); }
              setTimeout(() => { try { URL.revokeObjectURL(blobUrl); } catch {} }, 60000);
            }, 180);
          };
          frame.src = blobUrl;
        }
      } catch {
        const win = window.open(fileUrl, '_blank');
        if (win) win.onload = () => { try { win.focus(); win.print(); } catch {} };
      }
    }

    function openPrintModal(fileId, fileName, directUrl) {
      directPrint(directUrl || ('/api/file?id=' + encodeURIComponent(fileId)), fileName);
    }

    function downloadFile(fileId, fileName, directUrl) {
      const a = document.createElement('a');
      a.href = directUrl ? (directUrl + (directUrl.includes('?') ? '&' : '?') + 'download=1') : ('/api/file?id=' + encodeURIComponent(fileId) + '&download=1');
      a.download = fileName;
      a.click();
    }

    async function clearStorage(scope) {
      const labels = {
        temp: 'Clear abandoned temp upload chunks, expired records, and orphaned files?',
        completed: 'Permanently delete all PRINTED / ARCHIVED jobs plus converted/temp files?',
        all: 'Permanently delete ALL uploaded customer files and temp data? (Settings and permanently exported copies remain safe.)'
      };
      if (!confirm(labels[scope] || labels.temp)) return;
      const res = await fetch('/api/storage/clear', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ scope })
      });
      const data = await res.json();
      if (!res.ok || !data.success) { alert('Cleanup failed: ' + (data.error || 'error')); return; }
      if (scope === 'all') { selectedSubId = null; checkedSubIds.clear(); }
      await fetchCloudState(true);
      alert('Storage cleaned! Freed ' + formatSize(data.freedBytes) + ' (' + data.deletedSubmissions + ' job(s), ' + data.removedTempItems + ' temp item(s)).');
    }

    async function saveShopSettingsForm(e) {
      e.preventDefault();
      const payload = {
        shopName: document.getElementById('cfgShopName').value.trim(),
        storageRoot: document.getElementById('cfgStorageRoot').value.trim(),
        retentionHours: Number(document.getElementById('cfgRetentionHours').value || 24),
        maxFileSizeMb: Number(document.getElementById('cfgMaxFileMb').value || 100),
        allowedExtensions: document.getElementById('cfgAllowedExts').value.trim()
      };
      const res = await fetch('/api/settings', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await res.json();
      if (!res.ok || !data.success) { alert('Error: ' + (data.error || 'Failed saving settings')); return; }
      await fetchCloudState(true);
      alert(data.restartRequiredForStorageMove
        ? 'Settings saved! Restart ScanDrop.exe to switch to the new storage folder.'
        : 'Settings saved cleanly!');
    }

    async function pairShopWithCode(e) {
      e.preventDefault();
      const input = document.getElementById('cfgPairingCode');
      const btn = document.getElementById('pairShopBtn');
      const code = (input ? input.value : '').trim().toUpperCase();
      if (!code) { alert('Enter the Pairing Code provided by your Scan Drop admin.'); return; }
      if (btn) { btn.disabled = true; btn.textContent = 'Pairing...'; }
      try {
        const res = await fetch('/api/pair', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ pairingCode: code })
        });
        const data = await res.json();
        if (!res.ok || !data.success) {
          alert('Pairing failed: ' + (data.error || 'Invalid pairing code'));
          return;
        }
        await fetchCloudState(true);
        alert('Paired! This PC is now linked to ' + data.shop.name + ' (' + data.shop.publicUrl + ').');
      } catch (err) {
        alert('Pairing error: ' + err.message);
      } finally {
        if (btn) { btn.disabled = false; btn.textContent = '🔗 Pair This PC'; }
      }
    }

    async function checkStationUpdates(interactive) {
      const badge = document.getElementById('wsVersionBadge');
      const note = document.getElementById('wsUpdateNote');
      const btn = document.getElementById('wsInstallUpdateBtn');
      if (!badge) return;
      try {
        const res = await fetch('/api/update/check', { method: 'POST' });
        const data = await res.json();
        badge.textContent = 'v' + data.currentVersion + (data.updateAvailable ? ' → v' + data.latestVersion : ' (Latest)');
        badge.className = 'badge ' + (data.updateAvailable ? 'badge-amber' : 'badge-green');
        if (data.updateAvailable) {
          note.textContent = 'Update v' + data.latestVersion + ' ready: ' + (data.releaseNotes || '');
          if (btn) btn.style.display = 'inline-flex';
        } else {
          note.textContent = 'PC station v' + data.currentVersion + ' is running the latest release.';
          if (btn) btn.style.display = 'none';
          if (interactive) alert('Scan Drop v' + data.currentVersion + ' is up to date.');
        }
      } catch {}
    }

    async function installStationUpdate() {
      if (!confirm('Download, verify SHA-256, and restart ScanDrop.exe with the latest update?')) return;
      const res = await fetch('/api/update/apply', { method: 'POST' });
      const data = await res.json();
      alert(data.message || 'Updating ScanDrop.exe...');
    }

    function toggleSubCheck(subId, e) {
      e.stopPropagation();
      if (checkedSubIds.has(subId)) checkedSubIds.delete(subId);
      else checkedSubIds.add(subId);
      renderCurrentView();
    }

    function toggleSelectAll(listIds) {
      if (listIds.length > 0 && listIds.every(id => checkedSubIds.has(id))) listIds.forEach(id => checkedSubIds.delete(id));
      else listIds.forEach(id => checkedSubIds.add(id));
      renderCurrentView();
    }

    function buildStorageMetricsHtml(st) {
      if (!st) return '<span style="color:var(--muted);font-size:12px;">Calculating storage...</span>';
      const tempBytes = Number(st.stagingBytes || 0) + Number(st.orphanBytes || 0);
      return \`
        <div style="display:grid;grid-template-columns:repeat(auto-fit, minmax(145px, 1fr));gap:10px;">
          <div style="background:var(--surface-alt);padding:12px;border-radius:10px;border:1px solid var(--border);">
            <div style="font-size:11px;color:var(--muted);font-weight:600;">Total Used</div>
            <div style="font-size:18px;font-weight:800;margin-top:2px;">\${formatSize(st.totalUsedBytes)}</div>
          </div>
          <div style="background:var(--surface-alt);padding:12px;border-radius:10px;border:1px solid var(--border);">
            <div style="font-size:11px;color:var(--muted);font-weight:600;">Active Jobs (\${st.activeCount || 0})</div>
            <div style="font-size:16px;font-weight:700;color:var(--primary);margin-top:2px;">\${formatSize(st.activeBytes)}</div>
          </div>
          <div style="background:var(--surface-alt);padding:12px;border-radius:10px;border:1px solid var(--border);">
            <div style="font-size:11px;color:var(--muted);font-weight:600;">Printed / Done (\${st.completedCount || 0})</div>
            <div style="font-size:16px;font-weight:700;color:var(--success);margin-top:2px;">\${formatSize(st.completedBytes)}</div>
          </div>
          <div style="background:var(--surface-alt);padding:12px;border-radius:10px;border:1px solid var(--border);">
            <div style="font-size:11px;color:var(--muted);font-weight:600;">Converted PDFs (\${st.convertedCount || 0})</div>
            <div style="font-size:16px;font-weight:700;margin-top:2px;">\${formatSize(st.convertedBytes)}</div>
          </div>
          <div style="background:var(--surface-alt);padding:12px;border-radius:10px;border:1px solid var(--border);">
            <div style="font-size:11px;color:var(--muted);font-weight:600;">Temp / Orphans</div>
            <div style="font-size:16px;font-weight:700;color:var(--warning);margin-top:2px;">\${formatSize(tempBytes)}</div>
          </div>
          <div style="background:var(--surface-alt);padding:12px;border-radius:10px;border:1px solid var(--border);">
            <div style="font-size:11px;color:var(--muted);font-weight:600;">PC Drive Free</div>
            <div style="font-size:16px;font-weight:700;margin-top:2px;">\${formatSize(st.freeDriveBytes)}</div>
          </div>
        </div>
        <div style="font-size:11px;color:var(--muted);margin-top:8px;">Root: <code>\${esc(st.storageRoot || st.inboxDir)}</code></div>
      \`;
    }

    function renderCurrentView() {
      if (!cloudState) return;
      const container = document.getElementById('workspaceContent');
      const prevWsScroll = container ? container.scrollTop : 0;
      const prevListScroll = (document.getElementById('subRowsScroll') || {}).scrollTop || 0;
      const prevInspScroll = (document.getElementById('inspectorFilesScroll') || {}).scrollTop || 0;

      const shop = (cloudState.shops || [])[0] || { id: 'shop_quickprint_01', name: 'Scan Drop Shop', slug: 'quickprint' };
      const allSubs = (cloudState.submissions || []).filter(s => s.status !== 'expired');
      const recSubs = allSubs.filter(s => s.status === 'received' || s.status === 'incoming');
      const procSubs = allSubs.filter(s => s.status === 'processing');
      const printedSubs = allSubs.filter(s => s.status === 'printed' || s.status === 'completed');
      const archivedSubs = allSubs.filter(s => s.status === 'archived');
      document.getElementById('badgeInbox').textContent = recSubs.length;
      const globalTrash = cloudState.trashedFiles || [];
      const bTrash = document.getElementById('badgeTrash');
      if (bTrash) {
        bTrash.textContent = globalTrash.length;
        bTrash.style.display = globalTrash.length > 0 ? 'inline-block' : 'none';
      }

      const q = (document.getElementById('searchInput').value || '').toLowerCase();
      const permanentUrl = (currentSettings.publicBaseUrl || 'https://scandrop-xi.vercel.app').replace(/[/]+$/, '') + '/shop/' + shop.slug;
      const directTunnelUrl = lastMeta.tunnelUrl || '';

      if (currentTab === 'dashboard') {
        const lastCheck = lastMeta.tunnelLastOkUtc ? new Date(lastMeta.tunnelLastOkUtc).toLocaleTimeString() : 'Pending';
        const nextRetry = lastMeta.tunnelNextRetryUtc ? new Date(lastMeta.tunnelNextRetryUtc).toLocaleTimeString() : 'None';
        const tState = lastMeta.tunnelState || (lastMeta.tunnelOnline ? 'CONNECTED' : 'CONNECTING');
        container.innerHTML = \`
          <div class="stats-row">
            <div class="stat-card"><div class="stat-icon">📥</div><div><div class="stat-num">\${recSubs.length}</div><div class="stat-lbl">New / Pending</div></div></div>
            <div class="stat-card"><div class="stat-icon">📅</div><div><div class="stat-num">\${lastMeta.dailyCount || allSubs.length}</div><div class="stat-lbl">Submissions Today</div></div></div>
            <div class="stat-card"><div class="stat-icon">💾</div><div><div class="stat-num">\${formatSize((latestStorageStats || {}).totalUsedBytes)}</div><div class="stat-lbl">Local SSD Used</div></div></div>
            <div class="stat-card"><div class="stat-icon">☁️</div><div><div class="stat-num" style="font-size:14px;">\${esc(lastMeta.tunnelHealth || 'Connecting Cloudflare...')}</div><div class="stat-lbl">\${esc(lastMeta.tunnelProvider || 'Cloudflare Tunnel')}</div></div></div>
          </div>

          <div style="display:grid;grid-template-columns:2fr 1fr;gap:16px;">
            <div class="list-panel" style="padding:18px;">
              <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;">
                <h3>Recent Customer Submissions</h3>
                <button class="btn btn-outline" onclick="setTab('inbox')">Open Full Inbox →</button>
              </div>
              \${allSubs.length === 0 ? '<p style="color:var(--muted);font-size:13px;">No files received yet. Share your QR code with customers.</p>' :
                allSubs.slice(0, 6).map(s => \`<div class="sub-row" onclick="selectedSubId='\${esc(s.id)}';setTab('inbox')">
                  <strong style="min-width:74px;">\${esc(s.receiptCode || s.id)}</strong>
                  \${statusBadgeHtml(s.status)}
                  <div class="sub-row-main"><div style="font-size:13px;font-weight:700;">\${esc(s.customerName)}</div><div class="sub-row-meta">\${(s.files || []).length} file(s) • \${formatSize(s.totalSizeBytes)}</div></div>
                  <span style="font-size:12px;color:var(--muted);">\${timeAgo(s.createdAt)}</span>
                </div>\`).join('')}
            </div>

            <div class="list-panel" style="padding:18px;gap:10px;">
              <h3>Cloudflare Connectivity Supervisor</h3>
              <div style="font-size:12.5px;display:flex;flex-direction:column;gap:7px;margin-top:6px;">
                <div><strong>Receiver:</strong> \${lastMeta.receiverRunning ? 'Running (127.0.0.1:' + lastMeta.receiverPort + ')' : 'Active'}</div>
                <div><strong>State:</strong> <code>\${esc(tState)}</code> — \${esc(lastMeta.tunnelHealth || 'Connecting Cloudflare...')}</div>
                <div><strong>Mode:</strong> \${esc(lastMeta.tunnelProvider || 'Cloudflare Tunnel')}</div>
                <div><strong>Tunnel PID:</strong> \${lastMeta.tunnelPid ? lastMeta.tunnelPid : 'n/a'}</div>
                <div><strong>Last Check:</strong> \${lastCheck}</div>
                \${lastMeta.tunnelRetryCount > 0 ? \`<div><strong>Retries:</strong> \${lastMeta.tunnelRetryCount} • <strong>Next Retry:</strong> \${nextRetry}</div>\` : ''}
                \${lastMeta.tunnelLastFailure ? \`<div style="color:var(--danger);font-size:11.5px;"><strong>Last Failure:</strong> \${esc(lastMeta.tunnelLastFailure)}</div>\` : ''}
                <div><strong>Uploads:</strong> \${lastMeta.acceptingUploads === false ? '<span style="color:var(--warning);font-weight:700;">Paused</span>' : '<span style="color:var(--success);font-weight:700;">Accepting</span>'}</div>
                <div><strong>Office→PDF:</strong> \${esc(lastMeta.officeEngine || (lastMeta.libreOfficeInstalled ? 'LibreOffice Ready' : 'Images & DOCX XML Ready'))}</div>
              </div>
              <div style="display:flex;gap:8px;margin-top:12px;flex-wrap:wrap;">
                <button class="btn" onclick="setTab('qr')">⊞ Show Shop QR</button>
                <button class="btn btn-outline" onclick="fetch('/api/tunnel/restart',{method:'POST'}).then(()=>fetchCloudState(true))">↻ Restart Cloudflare</button>
              </div>
            </div>
          </div>
        \`;
      }
      else if (currentTab === 'inbox') {
        let filtered = allSubs.filter(s => {
          const st = (s.status || '').toLowerCase();
          if (subFilter === 'received' && st !== 'received' && st !== 'incoming') return false;
          if (subFilter === 'processing' && st !== 'processing') return false;
          if (subFilter === 'printed' && st !== 'printed' && st !== 'completed') return false;
          if (subFilter === 'archived' && st !== 'archived') return false;
          if (!q) return true;
          const fileMatch = (s.files || []).some(f => (f.originalName || '').toLowerCase().includes(q));
          return (s.id || '').toLowerCase().includes(q) || (s.customerName || '').toLowerCase().includes(q) || fileMatch;
        });
        if (sortOrder === 'oldest') filtered = [...filtered].reverse();
        if (!selectedSubId && filtered.length > 0) selectedSubId = filtered[0].id;
        const selectedSub = allSubs.find(s => s.id === selectedSubId) || null;
        const allFilteredIds = filtered.map(s => s.id);
        const allChecked = allFilteredIds.length > 0 && allFilteredIds.every(id => checkedSubIds.has(id));

        const rowsHtml = filtered.length === 0
          ? '<div style="padding:56px 20px;text-align:center;color:var(--muted);"><div style="font-size:30px;margin-bottom:8px;">📥</div><strong>No submissions found</strong><p style="font-size:12px;margin-top:4px;">Ask customers to scan your shop QR code.</p></div>'
          : filtered.map(sub => \`
            <div class="sub-row \${selectedSub && selectedSub.id === sub.id ? 'selected' : ''}" onclick="selectedSubId='\${esc(sub.id)}';renderCurrentView()"
              ondragover="handleSubRowDragOver(event)"
              ondragleave="this.classList.remove('tray-drop-hover')"
              ondrop="handleSubRowDrop(event, '\${esc(sub.id)}')">
              <input type="checkbox" \${checkedSubIds.has(sub.id) ? 'checked' : ''} onclick="toggleSubCheck('\${esc(sub.id)}', event)" style="width:15px;height:15px;cursor:pointer;">
              <div style="min-width:130px;display:flex;align-items:center;gap:7px;">
                <strong style="font-size:14px;font-family:ui-monospace,monospace;">\${esc(sub.receiptCode || sub.id)}</strong>
                \${statusBadgeHtml(sub.status)}
              </div>
              <div class="sub-row-main">
                <div style="font-size:13px;font-weight:700;">👤 \${esc(sub.customerName || 'Anonymous')}</div>
                <div class="sub-row-meta">\${(sub.files || []).length} file(s) • \${formatSize(sub.totalSizeBytes)}\${sub.instructions ? ' • ' + esc(sub.instructions) : ''}</div>
              </div>
              <div style="font-size:11.5px;color:var(--muted);min-width:72px;text-align:right;">\${timeAgo(sub.createdAt)}</div>
            </div>
          \`).join('');

        let inspectorHtml = '';
        if (selectedSub) {
          const subTrashed = selectedSub.trashedFiles || [];
          const inspFilesHtml = (selectedSub.files || []).map(f => {
            const [cls, lbl] = getFileBadge(f.fileExtension);
            const fExt = (f.fileExtension || '').toLowerCase();
            const isPdf = fExt === '.pdf';
            const isWord = fExt === '.docx' || fExt === '.doc';
            const isImg = ['.jpg', '.jpeg', '.png', '.webp', '.bmp', '.gif'].includes(fExt);
            const isEdited = f.conversionStatus === 'edited' && !!f.convertedUrl;
            return \`
              <div class="insp-file-card" draggable="true"
                ondragstart="handleFileCardDragStart(event, '\${esc(f.id)}', '\${esc(f.originalName)}', '\${esc(selectedSub.id)}')"
                ondragend="handleFileCardDragEnd(event, '\${esc(f.id)}', '\${esc(f.originalName)}')"
                ondragover="handleFileCardDragOver(event, '\${esc(f.id)}')"
                ondragleave="this.classList.remove('drag-over-card')"
                ondrop="handleFileCardDrop(event, '\${esc(f.id)}', '\${esc(selectedSub.id)}')">
                <div class="insp-file-top">
                  <span class="drag-grip" title="Drag down to Holding Tray, drag out to Trash, or drag up/down to reorder">⋮⋮</span>
                  <div class="file-type-box \${cls}">\${lbl}</div>
                  <div style="flex:1;min-width:0;">
                    <div style="font-size:13px;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" title="\${esc(f.originalName)}">\${esc(f.originalName)}</div>
                    <div style="font-size:11px;color:var(--muted);display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                      <span>\${formatSize(f.sizeBytes)}</span>
                      \${formatFileEditBadge(f)}
                    </div>
                  </div>
                  <button class="card-pin-btn" title="Keep a copy in Holding Tray (or drag card down to Tray)" onclick="pinFileToTray('\${esc(f.id)}', '\${esc(f.originalName)}')">📌</button>
                  <button class="card-trash-btn" title="Move to Trash (or drag card out)" onclick="trashFile('\${esc(f.id)}', '\${esc(f.originalName)}')">🗑</button>
                </div>
                <div class="insp-file-actions">
                  \${isPdf ? \`<button class="mini-btn mini-btn-edit" onclick="convertPdfToWord('\${esc(f.id)}', '\${esc(f.originalName)}', this)" title="Convert PDF to editable Word (.docx) and download">📝 To Word</button>\` : ''}
                  \${isWord ? \`<button class="mini-btn mini-btn-edit" onclick="openFileNative('\${esc(f.id)}')" title="Open directly in Microsoft Word">↗ Edit in Word</button>\` : ''}
                  \${(isPdf || isWord) ? \`<button class="mini-btn mini-btn-pages" onclick="openPageManager('\${esc(f.id)}', false)" title="Choose exact pages, reorder, rotate, duplicate or delete pages">📑 Pages</button>\` : ''}
                  \${isImg ? \`<button class="mini-btn mini-btn-edit" onclick="openSmartEditorById('\${esc(f.id)}', '\${esc(selectedSub.id)}')" title="Resize &amp; arrange on A4 / 4x6 Print Canvas">🖼️ Canvas</button>\` : ''}
                  \${isEdited
                    ? \`<button class="mini-btn" onclick="previewUrl('\${esc(f.convertedUrl)}', '\${esc(f.originalName)} (Edited)', '.pdf', '\${esc(f.id)}')">👁 Preview</button>
                       <button class="mini-btn" onclick="directPrint('\${esc(f.convertedUrl)}', '\${esc(f.originalName)}.pdf')">🖨 Print</button>\`
                    : \`<button class="mini-btn" onclick="previewFile('\${esc(f.id)}', '\${esc(f.originalName)}', '\${esc(f.fileExtension)}')">👁 Preview</button>
                       <button class="mini-btn" onclick="openPrintModal('\${esc(f.id)}', '\${esc(f.originalName)}')">🖨 Print</button>\`}
                  <button class="mini-btn" onclick="downloadFile('\${esc(f.id)}', '\${esc(f.originalName)}')" title="Download file">⬇ Save</button>
                  <button class="mini-btn" onclick="toggleCardMore('\${esc(f.id)}', event)" title="More actions">⋯</button>
                  <div class="more-actions-drawer" id="more_\${esc(f.id)}">
                    \${isEdited ? \`<button class="mini-btn" style="color:var(--warning);" onclick="fetch('/api/file/reset-edited',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({fileId:'\${esc(f.id)}'})}).then(()=>{fetchCloudState(true);showToast('↩ Reverted to original file');})">↩ Revert Original</button>\` : ''}
                    \${!isWord ? \`<button class="mini-btn" onclick="openFileNative('\${esc(f.id)}')">↗ Open</button>\` : ''}
                    <button class="mini-btn" onclick="openFileWithNative('\${esc(f.id)}')">📂 Open With</button>
                    \${!isPdf ? \`<button class="mini-btn" onclick="convertFileToPdfServer('\${esc(f.id)}')">📄 To PDF</button>\` : ''}
                    \${(isPdf || isWord) ? \`<button class="mini-btn" onclick="openSmartEditorById('\${esc(f.id)}', '\${esc(selectedSub.id)}')">✏️ Quick Edit</button>\` : ''}
                    <button class="mini-btn" onclick="renameFilePrompt('\${esc(f.id)}', '\${esc(f.originalName)}')">🏷️ Rename</button>
                    <button class="mini-btn" onclick="exportFilePermanent('\${esc(f.id)}')">💾 Export</button>
                    <button class="mini-btn" onclick="revealFileInFolder('\${esc(f.id)}')">📁 Folder</button>
                  </div>
                </div>
              </div>
            \`;
          }).join('');

          const inspTrashHtml = subTrashed.length === 0
            ? '<div style="padding:26px 12px;text-align:center;color:var(--muted);font-size:12px;">No trashed files for this job.<br><span style="font-size:11px;">Drag any file card out of the Files list (or click 🗑) to move it here.</span></div>'
            : subTrashed.map(tf => {
                const [cls, lbl] = getFileBadge(tf.fileExtension);
                return \`
                  <div class="insp-file-card" style="opacity:0.92;border-style:dashed;">
                    <div class="insp-file-top">
                      <div class="file-type-box \${cls}">\${lbl}</div>
                      <div style="flex:1;min-width:0;">
                        <div style="font-size:12.5px;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" title="\${esc(tf.originalName)}">\${esc(tf.originalName)}</div>
                        <div style="font-size:11px;color:var(--muted);">\${formatSize(tf.sizeBytes)} • Trashed \${timeAgo(tf.trashedAt)}</div>
                      </div>
                    </div>
                    <div class="insp-file-actions">
                      <button class="mini-btn mini-btn-edit" onclick="restoreFile('\${esc(tf.id)}', '\${esc(tf.originalName)}')">↩ Restore</button>
                      <button class="mini-btn" style="color:var(--danger);" onclick="deleteFilePermanent('\${esc(tf.id)}', '\${esc(tf.originalName)}')">🗑 Delete Forever</button>
                    </div>
                  </div>
                \`;
              }).join('') + \`<button class="btn btn-danger-soft" style="width:100%;margin-top:6px;padding:7px;" onclick="emptyTrash('\${esc(selectedSub.id)}')">🧹 Empty This Job's Trash</button>\`;

          const isArchivedSub = (selectedSub.status || '').toLowerCase() === 'archived';
          const remHrs = Math.max(0, (new Date(selectedSub.expiresAt).getTime() - Date.now()) / 3600000).toFixed(1);
          const printableExts = ['.pdf', '.docx', '.doc', '.rtf', '.txt', '.jpg', '.jpeg', '.png', '.webp', '.bmp', '.gif'];
          const printableCount = (selectedSub.files || []).filter(f => printableExts.includes((f.fileExtension || '').toLowerCase())).length;
          inspectorHtml = \`
            <aside class="inspector-panel">
              <div style="display:flex;justify-content:space-between;align-items:center;">
                <div style="display:flex;align-items:center;gap:8px;">
                  <strong style="font-size:17px;font-family:ui-monospace,monospace;">\${esc(selectedSub.receiptCode || selectedSub.id)}</strong>
                  \${statusBadgeHtml(selectedSub.status)}
                </div>
                <button onclick="selectedSubId=null;renderCurrentView()" style="border:none;background:none;font-size:16px;color:var(--muted);cursor:pointer;">✕</button>
              </div>

              <div class="insp-tabs">
                <button class="insp-tab \${inspectorTab === 'files' ? 'active' : ''}" onclick="inspectorTab='files';renderCurrentView()">Files (\${(selectedSub.files || []).length})</button>
                <button class="insp-tab insp-trash-tab \${inspectorTab === 'trash' ? 'active' : ''}" onclick="inspectorTab='trash';renderCurrentView()" ondragover="handleTrashZoneDragOver(event)" ondragleave="this.classList.remove('drag-active')" ondrop="handleTrashZoneDrop(event)">🗑️ Trash (\${subTrashed.length})</button>
                <button class="insp-tab \${inspectorTab === 'details' ? 'active' : ''}" onclick="inspectorTab='details';renderCurrentView()">Details</button>
              </div>

              <div id="inspectorFilesScroll" style="max-height:330px;overflow-y:auto;padding-right:4px;"
                ondragover="if(!draggingFileState){event.preventDefault();this.classList.add('ext-drag-over');}"
                ondragleave="this.classList.remove('ext-drag-over')"
                ondrop="this.classList.remove('ext-drag-over');handleAddZoneDrop(event, '\${esc(selectedSub.id)}', false)">
                \${inspectorTab === 'files'
                  ? (inspFilesHtml || '<p style="font-size:12px;color:var(--muted);padding:14px 4px;text-align:center;">No active files. Drop edited files below or restore from Trash.</p>')
                  : inspectorTab === 'trash'
                    ? inspTrashHtml
                    : \`<div style="font-size:12.5px;display:flex;flex-direction:column;gap:9px;padding:6px 0;">
                        <div><span style="color:var(--muted);">Customer:</span> <strong>\${esc(selectedSub.customerName)}</strong></div>
                        <div><span style="color:var(--muted);">Received:</span> <strong>\${new Date(selectedSub.createdAt).toLocaleString()}</strong></div>
                        <div><span style="color:var(--muted);">Auto-Purge:</span> \${isArchivedSub ? '<strong style="color:var(--success);">Paused (Archived)</strong>' : '<strong style="color:var(--warning);">' + remHrs + ' hours left</strong>'}</div>
                        <div><span style="color:var(--muted);">Instructions:</span><div style="background:var(--surface-alt);padding:8px;border-radius:8px;border:1px solid var(--border);margin-top:4px;">\${esc(selectedSub.instructions || 'None')}</div></div>
                      </div>\`}
              </div>

              \${inspectorTab === 'files' ? \`
                <div id="inspectorTrashDropZone" class="trash-drop-zone"
                  ondragover="handleTrashZoneDragOver(event)"
                  ondragleave="this.classList.remove('drag-active')"
                  ondrop="handleTrashZoneDrop(event)">
                  🗑️ Drop here — or drag outside list — to move to Trash
                </div>
                <div class="drop-in-zone"
                  onclick="document.getElementById('inspAddFileInput').click()"
                  ondragover="handleAddZoneDragOver(event)"
                  ondragleave="this.classList.remove('drag-active')"
                  ondrop="handleAddZoneDrop(event, '\${esc(selectedSub.id)}', false)">
                  📥 Drop edited Word / PDF / Image files here to add for merging (or click)
                  <input type="file" id="inspAddFileInput" multiple style="display:none;" onchange="if(this.files&&this.files.length){uploadLocalFilesToSubmission('\${esc(selectedSub.id)}',this.files,false);this.value='';}">
                </div>
              \` : ''}

              \${printableCount > 0 ? \`<button class="btn" style="width:100%;background:#16a34a;" onclick="openMergeModal('\${esc(selectedSub.id)}')">📑 Merge &amp; Print All (\${printableCount} \${printableCount === 1 ? 'File' : 'Files'})</button>\` : ''}

              <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;">
                <button class="btn" onclick="updateSubStatus('\${esc(selectedSub.id)}', 'printed')">✓ Mark Printed</button>
                \${isArchivedSub
                  ? \`<button class="btn btn-outline" onclick="updateSubStatus('\${esc(selectedSub.id)}', 'received')">📤 Unarchive</button>\`
                  : \`<button class="btn btn-outline" onclick="updateSubStatus('\${esc(selectedSub.id)}', 'archived')">📦 Archive</button>\`}
              </div>
              <button class="btn btn-danger-soft" style="width:100%;" onclick="deleteSubmissions(['\${esc(selectedSub.id)}'])">🗑 Delete Submission</button>
            </aside>
          \`;
        }

        container.innerHTML = \`
          <div class="split-grid">
            <div class="inbox-left-col" ondragover="handleTrashZoneDragOver(event)" ondrop="handleTrashZoneDrop(event)">
              <div class="list-panel" style="\${filtered.length > 5 ? 'flex:1;' : ''}">
                <div class="filter-bar">
                  <div class="filter-tabs">
                    <button class="filter-tab \${subFilter === 'all' ? 'active' : ''}" onclick="subFilter='all';renderCurrentView()">All (\${allSubs.length})</button>
                    <button class="filter-tab \${subFilter === 'received' ? 'active' : ''}" onclick="subFilter='received';renderCurrentView()">Received (\${recSubs.length})</button>
                    <button class="filter-tab \${subFilter === 'processing' ? 'active' : ''}" onclick="subFilter='processing';renderCurrentView()">Processing (\${procSubs.length})</button>
                    <button class="filter-tab \${subFilter === 'printed' ? 'active' : ''}" onclick="subFilter='printed';renderCurrentView()">Printed (\${printedSubs.length})</button>
                    <button class="filter-tab \${subFilter === 'archived' ? 'active' : ''}" onclick="subFilter='archived';renderCurrentView()">Archived (\${archivedSubs.length})</button>
                  </div>
                  <select onchange="sortOrder=this.value;renderCurrentView()" style="padding:5px 9px;border:1px solid var(--border);border-radius:9px;font-size:12px;background:var(--surface);color:var(--text);font-weight:600;">
                    <option value="newest" \${sortOrder === 'newest' ? 'selected' : ''}>Newest First</option>
                    <option value="oldest" \${sortOrder === 'oldest' ? 'selected' : ''}>Oldest First</option>
                  </select>
                </div>
                <div id="subRowsScroll" style="\${filtered.length > 5 ? 'flex:1;' : ''}overflow-y:auto;">\${rowsHtml}</div>
                <div class="bulk-bar">
                  <label style="display:flex;align-items:center;gap:7px;font-size:12px;font-weight:700;cursor:pointer;">
                    <input type="checkbox" \${allChecked ? 'checked' : ''} onchange='toggleSelectAll(\${JSON.stringify(allFilteredIds)})'> Select All
                  </label>
                  <div style="display:flex;gap:7px;flex-wrap:wrap;">
                    <button class="btn btn-outline" onclick="bulkUpdateSubStatus(Array.from(checkedSubIds),'printed')">✓ Mark Printed</button>
                    \${subFilter !== 'archived' ? \`<button class="btn btn-outline" onclick="bulkUpdateSubStatus(Array.from(checkedSubIds),'archived')">📦 Archive</button>\` : ''}
                    \${(subFilter === 'archived' || subFilter === 'all') ? \`<button class="btn btn-outline" onclick="bulkUpdateSubStatus(Array.from(checkedSubIds),'received')">📤 Unarchive</button>\` : ''}
                    <button class="btn btn-danger-soft" onclick="deleteSubmissions(Array.from(checkedSubIds))">🗑 Delete Selected</button>
                  </div>
                </div>
              </div>
              \${filtered.length <= 5 ? \`
                <div class="inbox-brand-stage">
                  <img src="/assets/scandrop-logo-hero.png" alt="ScanDrop — Instant File Drop for Shops">
                </div>
              \` : ''}
            </div>
            \${inspectorHtml}
          </div>
        \`;
      }
      else if (currentTab === 'fileManager') {
        const converted = cloudState.convertedFiles || [];
        container.innerHTML = \`
          <div class="list-panel" style="padding:20px;">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-wrap:wrap;gap:8px;">
              <div><h3>💾 Local PC Storage &amp; Cleanup</h3><p style="font-size:12px;color:var(--muted);">Files stay on your PC and auto-delete after \${currentSettings.retentionHours || 24} hours unless permanently exported.</p></div>
              <button class="btn btn-outline" onclick="fetchCloudState(true)">↻ Refresh</button>
            </div>
            <div style="margin-bottom:14px;">\${buildStorageMetricsHtml(latestStorageStats)}</div>
            <div style="display:flex;gap:8px;flex-wrap:wrap;">
              <button class="btn btn-outline" onclick="clearStorage('temp')">🧹 Clear Temp &amp; Orphans</button>
              <button class="btn btn-outline" onclick="clearStorage('completed')">✅ Clear Printed &amp; Converted</button>
              <button class="btn btn-danger-soft" onclick="clearStorage('all')">🗑️ Free Up All Storage</button>
            </div>
          </div>

          <div class="list-panel" style="padding:20px;">
            <h3 style="margin-bottom:12px;">📄 Converted PDF &amp; Word (.docx) Outputs</h3>
            \${converted.length === 0 ? '<p style="font-size:13px;color:var(--muted);">No converted files yet. Use "To Word" on any PDF or "To PDF" on any image/Word doc in Inbox.</p>' :
              converted.map(c => {
                const isDocx = (c.ext === '.docx') || (c.name || '').toLowerCase().endsWith('.docx');
                return \`<div style="padding:10px 12px;border:1px solid var(--border);border-radius:10px;margin-bottom:8px;display:flex;justify-content:space-between;align-items:center;gap:8px;">
                  <div><strong>\${esc(c.name)}</strong> <span style="font-size:11px;color:var(--muted);">(\${formatSize(c.sizeBytes)})</span></div>
                  <div style="display:flex;gap:6px;">
                    \${isDocx
                      ? \`<button class="btn" onclick="openConvertedNative('\${esc(c.name)}')">↗ Open in Word</button>\`
                      : \`<button class="btn btn-outline" onclick="previewUrl('\${esc(c.url)}','\${esc(c.name)}','.pdf',null)">👁 Preview</button>
                         <button class="btn" onclick="directPrint('\${esc(c.url)}','\${esc(c.name)}')">🖨 Print</button>\`}
                    <button class="btn btn-outline" onclick="downloadFile(null,'\${esc(c.name)}','\${esc(c.url)}')">⬇ Download</button>
                  </div>
                </div>\`;
              }).join('')}
          </div>
        \`;
      }
      else if (currentTab === 'trash') {
        const items = globalTrash.filter(t => !q || (t.originalName || '').toLowerCase().includes(q) || (t.receiptCode || '').toLowerCase().includes(q) || (t.customerName || '').toLowerCase().includes(q));
        container.innerHTML = \`
          <div class="list-panel" style="padding:20px;">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:14px;flex-wrap:wrap;gap:10px;">
              <div>
                <h3>🗑️ Trash (\${items.length} file\${items.length === 1 ? '' : 's'})</h3>
                <p style="font-size:12px;color:var(--muted);margin-top:2px;">Files dragged out of customer submissions land here and are excluded from Merge &amp; Print. Restore anytime or permanently delete.</p>
              </div>
              \${globalTrash.length > 0 ? '<button class="btn btn-danger-soft" onclick="emptyTrash(null)">🧹 Empty All Trash</button>' : ''}
            </div>
            \${items.length === 0
              ? '<div style="padding:48px 20px;text-align:center;color:var(--muted);"><div style="font-size:32px;margin-bottom:8px;">🗑️</div><strong>Trash is empty</strong><p style="font-size:12px;margin-top:4px;">Drag any unwanted file out of a submission in Inbox to move it here.</p></div>'
              : items.map(t => {
                  const [cls, lbl] = getFileBadge(t.fileExtension);
                  return \`<div style="padding:11px 13px;border:1px solid var(--border);border-radius:10px;margin-bottom:8px;display:flex;justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap;">
                    <div style="display:flex;align-items:center;gap:10px;min-width:0;flex:1;">
                      <div class="file-type-box \${cls}">\${lbl}</div>
                      <div style="min-width:0;">
                        <div style="font-size:13px;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">\${esc(t.originalName)}</div>
                        <div style="font-size:11.5px;color:var(--muted);">Job <strong>\${esc(t.receiptCode)}</strong> (\${esc(t.customerName)}) • \${formatSize(t.sizeBytes)} • Trashed \${timeAgo(t.trashedAt)}</div>
                      </div>
                    </div>
                    <div style="display:flex;gap:7px;">
                      <button class="btn" onclick="restoreFile('\${esc(t.id)}','\${esc(t.originalName)}')">↩ Restore</button>
                      <button class="btn btn-danger-soft" onclick="deleteFilePermanent('\${esc(t.id)}','\${esc(t.originalName)}')">🗑 Delete Forever</button>
                    </div>
                  </div>\`;
                }).join('')}
          </div>
        \`;
      }
      else if (currentTab === 'qr') {
        const displayQrUrl = permanentUrl;
        const devQrUrl = directTunnelUrl || permanentUrl;
        container.innerHTML = \`
          <div style="display:grid;grid-template-columns:repeat(auto-fit, minmax(320px, 1fr));gap:16px;">
            <div class="list-panel" style="padding:22px;text-align:center;">
              <h3 style="margin-bottom:4px;">Permanent Shop QR Code</h3>
              <p style="font-size:12px;color:var(--muted);margin-bottom:14px;">Print this once for your counter — automatically routes to your PC tunnel.</p>
              <img src="https://api.qrserver.com/v1/create-qr-code/?size=220x220&data=\${encodeURIComponent(displayQrUrl)}" style="width:220px;height:220px;border:1px solid var(--border);border-radius:12px;padding:10px;background:#fff;margin:0 auto 12px;display:block;">
              <input class="field-input" value="\${esc(displayQrUrl)}" readonly style="text-align:center;color:var(--primary);margin-bottom:12px;">
              <div style="display:flex;gap:8px;justify-content:center;">
                <button class="btn" onclick="navigator.clipboard.writeText('\${esc(displayQrUrl)}');alert('Copied shop link!')">📋 Copy Link</button>
                <button class="btn btn-outline" onclick="window.print()">🖨 Print Poster</button>
              </div>
            </div>

            <div class="list-panel" style="padding:22px;text-align:center;">
              <h3 style="margin-bottom:4px;">Direct Tunnel QR (Live Now)</h3>
              <p style="font-size:12px;color:var(--muted);margin-bottom:14px;">Connects customer smartphone directly to this PC's isolated port :\${lastMeta.receiverPort || 8787}.</p>
              \${directTunnelUrl
                ? \`<img src="https://api.qrserver.com/v1/create-qr-code/?size=220x220&data=\${encodeURIComponent(devQrUrl)}" style="width:220px;height:220px;border:1px solid var(--border);border-radius:12px;padding:10px;background:#fff;margin:0 auto 12px;display:block;">
                   <input class="field-input" value="\${esc(directTunnelUrl)}" readonly style="text-align:center;margin-bottom:12px;">
                   <button class="btn btn-outline" onclick="navigator.clipboard.writeText('\${esc(directTunnelUrl)}');alert('Copied direct tunnel URL!')">📋 Copy Direct URL</button>\`
                : '<div style="padding:60px 20px;color:var(--muted);font-size:13px;">Tunnel connecting...</div>'}
            </div>
          </div>
        \`;
      }
      else if (currentTab === 'activity') {
        container.innerHTML = \`<div class="list-panel" style="padding:20px;">
          <h3 style="margin-bottom:12px;">Local SQLite Operational Activity Log</h3>
          <table>
            <thead><tr><th>Time</th><th>Actor</th><th>Event</th><th>Details</th></tr></thead>
            <tbody>
              \${(cloudState.auditEvents || []).map(e => \`<tr>
                <td style="font-size:11px;color:var(--muted);white-space:nowrap;">\${new Date(e.createdAt).toLocaleString()}</td>
                <td><span class="badge badge-blue">\${esc(e.actorType)}</span></td>
                <td><code>\${esc(e.eventType)}</code></td>
                <td>\${esc(e.description)}</td>
              </tr>\`).join('')}
            </tbody>
          </table>
        </div>\`;
      }
      else if (currentTab === 'settings') {
        const printers = cloudState.printerProfiles || [];
        container.innerHTML = \`
          <div style="display:grid;grid-template-columns:repeat(auto-fit, minmax(360px, 1fr));gap:16px;">
            <form class="list-panel" style="padding:22px;gap:12px;" onsubmit="saveShopSettingsForm(event)">
              <h3>Shop &amp; Receiver Configuration</h3>
              <div><label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Shop Name</label><input id="cfgShopName" class="field-input" value="\${esc(currentSettings.shopName || shop.name)}"></div>
              <div><label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Local Storage Root Folder</label><input id="cfgStorageRoot" class="field-input" value="\${esc(currentSettings.storageRoot || '')}"></div>
              <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;">
                <div><label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Retention (Hours)</label><input id="cfgRetentionHours" type="number" min="1" max="720" class="field-input" value="\${currentSettings.retentionHours || 24}"></div>
                <div><label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Max File Size (MB)</label><input id="cfgMaxFileMb" type="number" min="5" max="500" class="field-input" value="\${currentSettings.maxFileSizeMb || 100}"></div>
              </div>
              <div><label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Allowed File Extensions</label><input id="cfgAllowedExts" class="field-input" value="\${esc(currentSettings.allowedExtensions || '')}"></div>
              <div style="display:flex;gap:8px;margin-top:4px;">
                <button type="submit" class="btn">💾 Save Settings</button>
                <button type="button" class="btn btn-outline" onclick="toggleTheme()">🌓 Switch Theme</button>
              </div>
            </form>

            <div style="display:flex;flex-direction:column;gap:16px;">
              <form class="list-panel" style="padding:22px;gap:10px;" onsubmit="pairShopWithCode(event)">
                <div style="display:flex;justify-content:space-between;align-items:center;">
                  <h3>🔗 Shop Pairing (Admin Code)</h3>
                  <span class="badge \${currentSettings.isPaired ? 'badge-green' : 'badge-amber'}">\${currentSettings.isPaired ? 'Paired' : 'Not Paired'}</span>
                </div>
                <p style="font-size:12px;color:var(--muted);">Link this Windows PC to your shop using the Pairing Code issued in Scan Drop Superadmin.</p>
                <div style="font-size:12px;background:var(--surface-alt);border:1px solid var(--border);border-radius:8px;padding:9px 11px;">
                  <div><strong>Shop URL:</strong> <code>\${esc(permanentUrl)}</code></div>
                  <div><strong>Device ID:</strong> <code>\${esc(currentSettings.deviceId || 'Legacy / Unpaired')}</code></div>
                </div>
                <div style="display:flex;gap:8px;">
                  <input id="cfgPairingCode" class="field-input" placeholder="e.g. SD-7K9P-3M2X" value="\${esc(currentSettings.pairingCode || '')}" style="text-transform:uppercase;font-family:ui-monospace,monospace;">
                  <button id="pairShopBtn" type="submit" class="btn" style="white-space:nowrap;">🔗 Pair This PC</button>
                </div>
              </form>

              <div class="list-panel" style="padding:22px;">
                <h3 style="margin-bottom:10px;">🖨️ Installed Windows Printers</h3>
                \${printers.length === 0 ? '<p style="font-size:12px;color:var(--muted);">No Windows printers detected.</p>' :
                  printers.map(p => \`<div style="padding:8px 10px;border:1px solid var(--border);border-radius:8px;margin-bottom:6px;display:flex;justify-content:space-between;font-size:12.5px;">
                    <span>\${esc(p.name)}</span>
                    \${p.isDefault ? '<span class="badge badge-green">Default</span>' : ''}
                  </div>\`).join('')}
                <div style="font-size:11.5px;color:var(--muted);margin-top:6px;">Printing is always manual — Scan Drop never auto-prints customer files.</div>
              </div>

              <div class="list-panel" style="padding:22px;">
                <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px;">
                  <h3>PC Station Software Updates</h3>
                  <span class="badge badge-blue" id="wsVersionBadge">v\${esc(lastMeta.appVersion || '4.0.0')}</span>
                </div>
                <p style="font-size:12px;color:var(--muted);margin-bottom:12px;" id="wsUpdateNote">SHA-256 verified updates for ScanDrop.exe &amp; Workspace UI.</p>
                <div style="display:flex;gap:8px;flex-wrap:wrap;">
                  <button class="btn btn-outline" onclick="checkStationUpdates(true)">🔄 Check for Updates</button>
                  <button class="btn" id="wsInstallUpdateBtn" style="display:none;background:#16a34a;" onclick="installStationUpdate()">⚡ Install Update &amp; Restart</button>
                </div>
              </div>
            </div>
          </div>
        \`;
        checkStationUpdates(false);
      }

      const restoreScroll = () => {
        if (container) container.scrollTop = prevWsScroll;
        const listEl = document.getElementById('subRowsScroll');
        if (listEl) listEl.scrollTop = prevListScroll;
        const inspEl = document.getElementById('inspectorFilesScroll');
        if (inspEl) inspEl.scrollTop = prevInspScroll;
      };
      restoreScroll();
      if (typeof requestAnimationFrame === 'function') requestAnimationFrame(restoreScroll);
      if (typeof renderHoldingTray === 'function') renderHoldingTray();
    }

${studioJs}

    window.addEventListener('keydown', (e) => {
      const tag = (document.activeElement && document.activeElement.tagName || '').toLowerCase();
      const typing = tag === 'input' || tag === 'textarea' || tag === 'select';
      if (e.ctrlKey && e.key.toLowerCase() === 'f') { e.preventDefault(); document.getElementById('searchInput').focus(); }
      else if (e.key === 'F5') { e.preventDefault(); fetchCloudState(true); }
      else if (e.ctrlKey && !e.shiftKey && e.key.toLowerCase() === 'z' && !typing) {
        if (document.getElementById('pageManagerModal').classList.contains('open')) { e.preventDefault(); pmUndo(); }
        else if (document.getElementById('imageStudioModal').classList.contains('open')) { e.preventDefault(); icUndo(); }
        else if (document.getElementById('pdfStudioModal').classList.contains('open')) { e.preventDefault(); pdfStudioUndo(); }
        else if (document.getElementById('mergePrintModal').classList.contains('open')) { e.preventDefault(); mergeUndo(); }
        else if (globalUndoStack.length > 0) { e.preventDefault(); performGlobalUndo(); }
      }
      else if (e.ctrlKey && (e.key.toLowerCase() === 'y' || (e.shiftKey && e.key.toLowerCase() === 'z')) && !typing) {
        if (document.getElementById('pageManagerModal').classList.contains('open')) { e.preventDefault(); pmRedo(); }
        else if (document.getElementById('imageStudioModal').classList.contains('open')) { e.preventDefault(); icRedo(); }
      }
      else if (e.key === 'Escape') {
        const zoom = document.getElementById('pmZoomOverlay');
        if (zoom && zoom.classList.contains('open')) { zoom.classList.remove('open'); return; }
        closePreview();
        closeStudioModal('mergePrintModal');
      }
    });

    fetchCloudState(true);
    setInterval(() => fetchCloudState(false), 3500);
  </script>
</body>
</html>`;
