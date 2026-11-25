// ScanDrop — Local Document, PDF, Image Canvas & Merge-Print Studio
// Injected cleanly into workspace_template.js (runs 100% locally on 127.0.0.1:3838).

const studioCss = `
    .studio-overlay { display:none; position:fixed; inset:0; background:rgba(15,23,42,0.72); backdrop-filter:blur(4px); z-index:200; align-items:center; justify-content:center; padding:16px; }
    .studio-overlay.open { display:flex; }
    .studio-shell { width:96vw; height:93vh; max-width:1440px; background:var(--surface); color:var(--text); border:1px solid var(--border); border-radius:16px; display:flex; flex-direction:column; overflow:hidden; box-shadow:0 24px 60px rgba(0,0,0,0.35); }
    .studio-topbar { padding:12px 18px; border-bottom:1px solid var(--border); display:flex; align-items:center; justify-content:space-between; gap:10px; background:var(--surface); flex-shrink:0; flex-wrap:wrap; }
    .studio-body { flex:1; display:flex; overflow:hidden; min-height:0; }
    .studio-side { width:360px; border-right:1px solid var(--border); overflow-y:auto; padding:14px; display:flex; flex-direction:column; gap:12px; background:var(--surface-alt); flex-shrink:0; }
    .studio-side.wide { width:430px; }
    .studio-stage { flex:1; overflow:auto; padding:18px; display:flex; flex-direction:column; align-items:center; justify-content:flex-start; background:var(--bg); position:relative; min-width:0; }
    .studio-card { background:var(--surface); border:1px solid var(--border); border-radius:12px; padding:12px; display:flex; flex-direction:column; gap:8px; }
    .studio-card h4 { font-size:12.5px; font-weight:800; color:var(--text); display:flex; align-items:center; justify-content:space-between; gap:6px; }
    .studio-lbl { font-size:11px; font-weight:700; color:var(--muted); display:block; margin-bottom:3px; }
    .pdf-page-wrap { position:relative; background:#fff; box-shadow:0 8px 28px rgba(0,0,0,0.22); border-radius:4px; overflow:hidden; user-select:none; }
    .pdf-text-hit { position:absolute; cursor:pointer; border:1px solid transparent; border-radius:2px; transition:background 0.1s, border-color 0.1s; }
    .pdf-text-hit:hover { background:rgba(37,99,235,0.16); border-color:rgba(37,99,235,0.65); }
    .pdf-text-hit.header-hit { border-bottom:1px dashed rgba(245,158,11,0.75); }
    .pdf-overlay-img { position:absolute; border:2px dashed #2563eb; cursor:move; box-sizing:border-box; }
    .pdf-overlay-img .resize-handle { position:absolute; right:-7px; bottom:-7px; width:14px; height:14px; background:#2563eb; border:2px solid #fff; border-radius:50%; cursor:nwse-resize; }
    .canvas-paper { position:relative; background:#fff; box-shadow:0 10px 30px rgba(0,0,0,0.25); overflow:visible; user-select:none; transition:box-shadow 0.15s; }
    .canvas-paper.drop-target-active { box-shadow:0 0 0 3px #0078FF, 0 12px 34px rgba(0,120,255,0.28); }
    .canvas-margin-guide { position:absolute; inset:14px; border:1px dashed rgba(148,163,184,0.45); pointer-events:none; }
    .canvas-item { position:absolute; cursor:move; box-sizing:border-box; border:2px solid transparent; overflow:visible; }
    .canvas-item.selected { border-color:#0078FF; box-shadow:0 0 0 2px rgba(0,120,255,0.22); z-index:20; }
    .canvas-item img { width:100%; height:100%; display:block; pointer-events:none; }
    .canvas-handle { position:absolute; right:-8px; bottom:-8px; width:16px; height:16px; background:#0078FF; border:2px solid #fff; border-radius:50%; cursor:nwse-resize; display:none; box-shadow:0 2px 6px rgba(0,0,0,0.25); }
    .canvas-item.selected .canvas-handle { display:block; }
    .canvas-dim-tag { position:absolute; left:4px; top:4px; background:rgba(15,23,42,0.82); color:#fff; font-size:10px; font-weight:700; padding:2px 6px; border-radius:4px; pointer-events:none; display:none; white-space:nowrap; }
    .canvas-item.selected .canvas-dim-tag { display:block; }
    .ic-rotate-handle { position:absolute; right:-34px; top:50%; transform:translateY(-50%); width:26px; height:26px; border-radius:50%; background:#ffffff; border:2px solid #0078FF; color:#0078FF; display:none; align-items:center; justify-content:center; font-size:14px; font-weight:900; cursor:grab; box-shadow:0 3px 10px rgba(0,0,0,0.22); z-index:30; }
    .ic-rotate-handle:before { content:''; position:absolute; left:-10px; top:50%; width:8px; height:2px; background:#0078FF; transform:translateY(-50%); pointer-events:none; }
    .ic-rotate-handle:hover { background:#0078FF; color:#fff; }
    .ic-rotate-handle:active { cursor:grabbing; }
    .canvas-item.selected.single-selected .ic-rotate-handle { display:flex; }
    .ic-canva-bar { position:absolute; transform:translateX(-50%); background:#0B2545; color:#fff; border:1px solid rgba(255,255,255,0.16); border-radius:999px; padding:4px 7px; display:flex; align-items:center; gap:4px; box-shadow:0 8px 24px rgba(11,37,69,0.35); z-index:60; white-space:nowrap; pointer-events:auto; }
    .ic-canva-btn { border:none; background:transparent; color:#f8fafc; width:27px; height:26px; border-radius:999px; cursor:pointer; font-size:13px; font-weight:800; display:inline-flex; align-items:center; justify-content:center; transition:background 0.12s, color 0.12s; }
    .ic-canva-btn:hover { background:rgba(255,255,255,0.16); }
    .ic-canva-btn.danger:hover { background:#ef4444; color:#fff; }
    .ic-canva-pill-count { font-size:10.5px; font-weight:800; color:#FCD34D; padding:0 6px 0 4px; border-right:1px solid rgba(255,255,255,0.2); margin-right:2px; }
    .ic-marquee-box { position:absolute; display:none; border:1.5px solid #0078FF; background:rgba(0,120,255,0.14); border-radius:3px; pointer-events:none; z-index:55; }
    .ic-group-outline { position:absolute; border:1.5px dashed #0078FF; background:rgba(0,120,255,0.04); pointer-events:none; z-index:15; border-radius:4px; }
    .docx-block-row { border:1px solid var(--border); border-radius:8px; padding:8px; background:var(--surface-alt); display:flex; flex-direction:column; gap:5px; }
    .merge-row { display:flex; align-items:center; gap:10px; padding:10px 12px; border:1px solid var(--border); border-radius:10px; background:var(--surface); }
    .fr-pair-row { display:grid; grid-template-columns:1fr 1fr auto; gap:6px; align-items:center; }

    /* Drag-to-Trash, Drag-in-to-Add, Reorder & Toast UX */
    .insp-file-card[draggable="true"] { cursor:grab; transition:transform 0.12s, border-color 0.15s, opacity 0.15s, box-shadow 0.15s; }
    .insp-file-card[draggable="true"]:active { cursor:grabbing; }
    .insp-file-card.dragging { opacity:0.42; transform:scale(0.97); border:1.5px dashed var(--danger); }
    .insp-file-card.drag-over-card { border-top:3px solid var(--primary); }
    .drag-grip { color:var(--muted); font-size:13px; letter-spacing:-2px; cursor:grab; user-select:none; padding:2px 3px 2px 0; flex-shrink:0; opacity:0.75; }
    .insp-file-card:hover .drag-grip { color:var(--primary); opacity:1; }
    .card-trash-btn { border:none; background:transparent; color:var(--muted); cursor:pointer; font-size:13px; padding:3px 6px; border-radius:6px; transition:all 0.12s; flex-shrink:0; }
    .card-trash-btn:hover { background:rgba(220,38,38,0.12); color:#dc2626; }
    .drop-in-zone { border:1.5px dashed rgba(37,99,235,0.45); background:var(--primary-soft); color:var(--primary); border-radius:10px; padding:10px 12px; text-align:center; font-size:11.5px; font-weight:700; cursor:pointer; transition:all 0.15s; }
    .drop-in-zone:hover, .drop-in-zone.drag-active { border-color:var(--primary); background:rgba(37,99,235,0.18); box-shadow:0 0 0 3px rgba(37,99,235,0.16); }
    #inspectorFilesScroll.ext-drag-over { outline:2px dashed var(--primary); outline-offset:2px; border-radius:10px; background:var(--primary-soft); }
    .trash-drop-zone { display:none; border:2px dashed #ef4444; background:rgba(239,68,68,0.09); color:#dc2626; border-radius:10px; padding:11px 12px; text-align:center; font-size:12px; font-weight:800; transition:all 0.15s; }
    body.dragging-file-active .trash-drop-zone { display:block; animation:sdPulseTrash 1.2s ease-in-out infinite; }
    .trash-drop-zone.drag-active { background:rgba(239,68,68,0.22); border-style:solid; transform:scale(1.01); animation:none; }
    body.dragging-file-active #nav-trash, body.dragging-file-active .insp-trash-tab { background:rgba(239,68,68,0.14); color:#dc2626 !important; border-radius:8px; }
    body.dragging-file-active .list-panel { outline:2px dashed rgba(239,68,68,0.45); outline-offset:-3px; }
    @keyframes sdPulseTrash { 0%,100% { border-color:#ef4444; } 50% { border-color:#fca5a5; } }
    .insp-file-actions-grid { display:grid; grid-template-columns:repeat(4, minmax(0, 1fr)); gap:6px; margin-top:2px; }
    .insp-file-actions-grid .mini-btn { width:100%; justify-content:center; padding:6px 6px; font-size:11.5px; border-radius:8px; display:inline-flex; align-items:center; gap:4px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
    .mini-btn-quick { color:#0284c7; border-color:rgba(2,132,199,0.32); background:rgba(2,132,199,0.08); }
    body.dark .mini-btn-quick { color:#38bdf8; border-color:rgba(56,189,248,0.32); background:rgba(56,189,248,0.10); }
    .mini-btn-quick:hover { background:#0284c7; color:#fff; border-color:#0284c7; }
    .more-trigger-btn.open { border-color:#38bdf8 !important; color:#38bdf8 !important; background:rgba(56,189,248,0.12) !important; box-shadow:0 0 0 2px rgba(56,189,248,0.22); }
    .card-more-popover { position:fixed; display:none; min-width:176px; background:#0F233C; color:#F8FAFC; border:1px solid rgba(88,195,255,0.32); border-radius:12px; padding:5px 0; box-shadow:0 16px 36px rgba(4,12,24,0.55); z-index:450; backdrop-filter:blur(10px); }
    .card-more-popover.open { display:block; animation:sdPopIn 0.12s ease-out; }
    @keyframes sdPopIn { from { opacity:0; transform:translateY(-4px) scale(0.98); } to { opacity:1; transform:translateY(0) scale(1); } }
    .card-more-popover::before { content:''; position:absolute; top:-7px; right:26px; width:12px; height:12px; background:#0F233C; border-left:1px solid rgba(88,195,255,0.32); border-top:1px solid rgba(88,195,255,0.32); transform:rotate(45deg); }
    .card-more-item { width:100%; border:none; background:transparent; color:#E2E8F0; padding:9px 14px; font-size:12.5px; font-weight:700; text-align:left; cursor:pointer; display:flex; align-items:center; justify-content:space-between; gap:10px; transition:background 0.12s, color 0.12s; }
    .card-more-item + .card-more-item { border-top:1px solid rgba(148,163,184,0.14); }
    .card-more-item:hover { background:rgba(0,120,255,0.24); color:#FFFFFF; }
    .card-more-item-left { display:flex; align-items:center; gap:10px; }
    .card-more-item-icon { width:18px; text-align:center; font-size:13px; }
    .card-more-chevron { color:#94A3B8; font-size:13px; font-weight:800; }
    .sd-toast-box { position:fixed; right:20px; bottom:20px; z-index:400; display:flex; flex-direction:column; gap:8px; max-width:410px; pointer-events:none; }
    .sd-toast { pointer-events:auto; background:#0f172a; color:#f8fafc; border:1px solid rgba(148,163,184,0.28); border-radius:12px; padding:11px 14px; font-size:12.5px; font-weight:600; box-shadow:0 12px 30px rgba(0,0,0,0.35); display:flex; align-items:center; justify-content:space-between; gap:10px; }
    .sd-toast-btn { background:#3b82f6; color:#fff; border:none; border-radius:7px; padding:5px 10px; font-size:11.5px; font-weight:700; cursor:pointer; white-space:nowrap; }
    .sd-toast-btn:hover { background:#2563eb; }

    /* Docked Holding Tray (Distinct Warm Golden-Yellow Staging Shelf) */
    .holding-tray { flex-shrink:0; background:linear-gradient(180deg,#FFFBEB 0%,#FEF3C7 100%); border-top:3px solid #F59E0B; padding:10px 18px 12px; display:flex; flex-direction:column; gap:8px; transition:border-color 0.15s, background 0.15s, box-shadow 0.15s; z-index:40; box-shadow:0 -6px 22px rgba(245,158,11,0.16); }
    body.dark .holding-tray { background:linear-gradient(180deg,#2C2109 0%,#1F1706 100%); border-top-color:#F59E0B; box-shadow:0 -6px 22px rgba(0,0,0,0.45); }
    .holding-tray.collapsed { padding:8px 18px; gap:0; }
    .holding-tray.collapsed .tray-shelf-wrap { display:none; }
    body.dragging-file-active .holding-tray { border-top-color:#D97706; background:linear-gradient(180deg,#FEF3C7 0%,#FDE68A 100%); }
    body.dark.dragging-file-active .holding-tray { background:linear-gradient(180deg,#3E2E0C 0%,#2C2109 100%); }
    body.dragging-file-active .holding-tray.collapsed .tray-shelf-wrap { display:flex; }
    .holding-tray.tray-drag-over { border-top-color:#D97706; background:linear-gradient(180deg,#FDE68A 0%,#FCD34D 100%); box-shadow:inset 0 0 0 2.5px #D97706; }
    .tray-header { display:flex; align-items:center; justify-content:space-between; gap:10px; flex-wrap:wrap; }
    .tray-header-left { display:flex; align-items:center; gap:8px; min-width:0; }
    .tray-header-actions { display:flex; align-items:center; gap:6px; flex-wrap:wrap; }
    .tray-shelf { display:flex; align-items:stretch; gap:8px; overflow-x:auto; padding:2px 0 4px; min-height:74px; scroll-behavior:smooth; }
    .tray-empty-hint { flex:1; border:2px dashed #F59E0B; background:rgba(255,255,255,0.68); border-radius:10px; display:flex; align-items:center; justify-content:center; padding:12px 16px; font-size:12px; color:#92400E; font-weight:700; cursor:pointer; transition:all 0.15s; text-align:center; }
    body.dark .tray-empty-hint { background:rgba(44,33,9,0.7); color:#FDE68A; border-color:#D97706; }
    .tray-empty-hint:hover, .holding-tray.tray-drag-over .tray-empty-hint { border-color:#B45309; color:#78350F; background:#FFFBEB; }
    .tray-chip { width:222px; flex-shrink:0; background:#FFFFFF; border:1.5px solid #FCD34D; border-radius:10px; padding:7px 9px; display:flex; flex-direction:column; gap:5px; cursor:grab; transition:transform 0.12s, border-color 0.15s, opacity 0.15s, box-shadow 0.15s; box-shadow:0 2px 8px rgba(180,83,9,0.09); }
    body.dark .tray-chip { background:#2D230E; border-color:#92400E; }
    .tray-chip:hover { border-color:#D97706; box-shadow:0 4px 12px rgba(217,119,6,0.2); }
    .tray-chip.dragging { opacity:0.45; transform:scale(0.96); }
    .tray-chip.tray-chip-over { border-left:3.5px solid #D97706; }
    .tray-chip-top { display:flex; align-items:center; gap:7px; }
    .tray-chip-actions { display:flex; gap:4px; align-items:center; }
    .card-pin-btn { border:none; background:transparent; color:var(--muted); cursor:pointer; font-size:12.5px; padding:3px 5px; border-radius:6px; transition:all 0.12s; flex-shrink:0; }
    .card-pin-btn:hover { background:var(--primary-soft); color:var(--primary); }
    body.dragging-tray-active .drop-in-zone, body.dragging-tray-active #inspectorFilesScroll { outline:2px dashed #0078FF; background:var(--primary-soft); border-radius:10px; }
    .sub-row.tray-drop-hover { background:rgba(0,120,255,0.16) !important; outline:2px dashed #0078FF; outline-offset:-2px; }
    .tray-shelf-wrap { display:flex; align-items:center; gap:10px; }
    .tray-nav-btn { width:48px; min-width:48px; height:74px; flex-shrink:0; border:2px solid #D97706; background:linear-gradient(180deg,#FCD34D 0%,#F59E0B 100%); color:#0B2545; border-radius:12px; cursor:pointer; font-size:22px; font-weight:900; display:flex; align-items:center; justify-content:center; transition:all 0.14s; box-shadow:0 4px 12px rgba(217,119,6,0.28); user-select:none; }
    .tray-nav-btn:hover { background:linear-gradient(180deg,#FDE68A 0%,#FBBF24 100%); border-color:#B45309; transform:translateY(-1px); box-shadow:0 6px 16px rgba(217,119,6,0.36); }
    .tray-nav-btn:active { transform:scale(0.95); }

    /* Image Print Canvas — Visual Submission Thumbnail Picker */
    .ic-scope-row { display:flex; align-items:center; justify-content:space-between; gap:6px; margin-bottom:2px; }
    .ic-scope-tabs { display:inline-flex; background:var(--surface-alt); border:1px solid var(--border); border-radius:8px; padding:2px; gap:2px; }
    .ic-scope-btn { border:none; background:transparent; color:var(--muted); font-size:10.5px; font-weight:700; padding:3px 8px; border-radius:6px; cursor:pointer; transition:all 0.12s; }
    .ic-scope-btn.active { background:#0078FF; color:#fff; box-shadow:0 2px 6px rgba(0,120,255,0.28); }
    .ic-thumb-strip { display:grid; grid-template-columns:repeat(3, minmax(0, 1fr)); grid-auto-rows:94px; align-content:start; gap:8px; height:212px; max-height:212px; min-height:112px; overflow-y:auto; overflow-x:hidden; padding:7px; background:var(--surface-alt); border:1px solid var(--border); border-radius:10px; flex-shrink:0; }
    .ic-thumb-tile { position:relative; height:94px; min-height:94px; border-radius:9px; overflow:hidden; border:2px solid var(--border); background:var(--surface); cursor:grab; display:flex; flex-direction:column; transition:border-color 0.14s, transform 0.14s, box-shadow 0.14s; user-select:none; }
    .ic-thumb-tile:active { cursor:grabbing; }
    .ic-thumb-tile:hover { border-color:#0078FF; transform:translateY(-1px); box-shadow:0 4px 12px rgba(0,120,255,0.18); }
    .ic-thumb-tile.on-sheet { border-color:#16a34a; }
    .ic-thumb-img-wrap { width:100%; height:68px; flex:0 0 68px; background:#e2e8f0; display:flex; align-items:center; justify-content:center; overflow:hidden; position:relative; }
    .ic-thumb-img-wrap img { width:100%; height:100%; object-fit:cover; display:block; pointer-events:none; }
    .ic-thumb-name { font-size:9.5px; font-weight:700; padding:2px 5px; height:22px; line-height:18px; flex:0 0 22px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; color:var(--text); background:var(--surface); }
    .ic-thumb-badge { position:absolute; top:4px; right:4px; background:rgba(11,37,69,0.86); color:#fff; font-size:9.5px; font-weight:800; padding:1px 6px; border-radius:999px; line-height:1.35; }
    .ic-thumb-tile.on-sheet .ic-thumb-badge { background:#16a34a; }
    .ic-thumb-empty { grid-column:1/-1; text-align:center; padding:24px 8px; font-size:11.5px; color:var(--muted); }

    /* File Manager — Local PC Downloads Browser */
    .local-folder-tabs { display:flex; gap:6px; flex-wrap:wrap; align-items:center; }
    .local-folder-pill { border:1px solid var(--border); background:var(--surface-alt); color:var(--text); font-size:12px; font-weight:700; padding:6px 12px; border-radius:999px; cursor:pointer; transition:all 0.14s; display:inline-flex; align-items:center; gap:5px; }
    .local-folder-pill:hover { border-color:#0078FF; color:#0078FF; background:var(--primary-soft); }
    .local-folder-pill.active { background:#0078FF; border-color:#0078FF; color:#fff; box-shadow:0 3px 10px rgba(0,120,255,0.28); }
    .local-file-list { max-height:390px; overflow-y:auto; display:flex; flex-direction:column; gap:7px; padding-right:2px; }
    .local-file-row { padding:9px 12px; border:1px solid var(--border); border-radius:10px; display:flex; justify-content:space-between; align-items:center; gap:8px; background:var(--surface); transition:border-color 0.14s, background 0.14s; flex-wrap:wrap; }
    .local-file-row:hover { border-color:rgba(0,120,255,0.45); background:var(--surface-alt); }
    .local-file-row.is-dir { cursor:pointer; background:var(--surface-alt); }

    /* Visual Page Manager Studio (PDF & Word Docs) */
    .pm-toolbar { padding:10px 18px; border-bottom:1px solid var(--border); background:var(--surface-alt); display:flex; align-items:center; justify-content:space-between; gap:10px; flex-wrap:wrap; flex-shrink:0; }
    .pm-grid { flex:1; overflow-y:auto; padding:18px; display:grid; grid-template-columns:repeat(auto-fill, minmax(172px, 1fr)); gap:14px; align-content:start; background:var(--bg); }
    .pm-card { background:var(--surface); border:2px solid var(--border); border-radius:12px; padding:9px; display:flex; flex-direction:column; gap:7px; cursor:grab; transition:all 0.14s; position:relative; box-shadow:0 4px 12px rgba(0,120,255,0.06); }
    .pm-card:hover { border-color:rgba(0,120,255,0.55); transform:translateY(-1px); }
    .pm-card.selected { border-color:#0078FF; box-shadow:0 6px 18px rgba(0,120,255,0.16); }
    .pm-card.excluded { opacity:0.46; border-style:dashed; border-color:var(--border); filter:grayscale(0.5); }
    .pm-card.pm-drag-over { border-left:4px solid #FFC107; }
    .pm-thumb-box { width:100%; aspect-ratio:210/297; background:#fff; border:1px solid var(--border); border-radius:8px; overflow:hidden; display:flex; align-items:center; justify-content:center; position:relative; cursor:pointer; }
    .pm-thumb-box canvas { max-width:100%; max-height:100%; display:block; transition:transform 0.16s; }
    .pm-page-pill { position:absolute; left:6px; top:6px; background:#0B2545; color:#fff; font-size:10px; font-weight:800; padding:2px 7px; border-radius:999px; }
    .pm-card.selected .pm-page-pill { background:#0078FF; }
    .pm-excl-pill { position:absolute; right:6px; top:6px; background:#ef4444; color:#fff; font-size:9.5px; font-weight:800; padding:2px 6px; border-radius:999px; display:none; }
    .pm-card.excluded .pm-excl-pill { display:inline-block; }
    .pm-card-bar { display:flex; align-items:center; justify-content:space-between; gap:4px; }
    .pm-icon-btn { border:1px solid var(--border); background:var(--surface-alt); color:var(--text-secondary); border-radius:6px; padding:3px 6px; font-size:11px; cursor:pointer; font-weight:700; }
    .pm-icon-btn:hover { background:var(--primary-soft); color:var(--primary); border-color:rgba(0,120,255,0.35); }
    .pm-zoom-overlay { display:none; position:fixed; inset:0; background:rgba(11,37,69,0.82); backdrop-filter:blur(4px); z-index:320; align-items:center; justify-content:center; padding:20px; }
    .pm-zoom-overlay.open { display:flex; }
`;

const studioHtml = `
  <!-- 1. PDF EDITOR STUDIO MODAL -->
  <div id="pdfStudioModal" class="studio-overlay">
    <div class="studio-shell">
      <div class="studio-topbar">
        <div style="display:flex;align-items:center;gap:10px;min-width:0;">
          <strong style="font-size:15px;">📄 PDF Studio</strong>
          <span id="pdfStudioFileName" class="badge badge-blue" style="max-width:280px;overflow:hidden;text-overflow:ellipsis;"></span>
          <span id="pdfStudioStatus" style="font-size:12px;color:var(--muted);"></span>
        </div>
        <div style="display:flex;gap:7px;flex-wrap:wrap;align-items:center;">
          <button class="btn btn-outline" onclick="pdfStudioUndo()">↩ Undo</button>
          <button class="btn btn-outline" onclick="pdfStudioReset()">↻ Original</button>
          <button class="btn" onclick="pdfStudioSave(false)">💾 Save Edited PDF</button>
          <button class="btn" style="background:#16a34a;" onclick="pdfStudioSave(true)">🖨 Save &amp; Direct Print</button>
          <button class="btn btn-outline" onclick="closeStudioModal('pdfStudioModal')">✕ Close</button>
        </div>
      </div>
      <div class="studio-body">
        <div class="studio-side">
          <div class="studio-card">
            <h4>🎓 Detected Page Headers <span class="badge badge-amber" id="pdfHeaderCount">0</span></h4>
            <p style="font-size:11px;color:var(--muted);">Click any detected header (Name / Roll No.) to replace across all pages.</p>
            <div id="pdfDetectedHeaders" style="display:flex;flex-direction:column;gap:5px;max-height:140px;overflow-y:auto;"></div>
          </div>

          <div class="studio-card">
            <h4>🔍 Find &amp; Replace Text</h4>
            <div><label class="studio-lbl">Find Text (e.g. Old Student Name or Roll No.)</label><input id="pdfFindInput" class="field-input" placeholder="Exact or partial text on PDF"></div>
            <div><label class="studio-lbl">Replace With (e.g. New Name / Roll No.)</label><input id="pdfReplaceInput" class="field-input" placeholder="Replacement text"></div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;">
              <div>
                <label class="studio-lbl">Scope</label>
                <select id="pdfReplaceScope" class="field-input">
                  <option value="all">All Pages</option>
                  <option value="headers">Headers Only (Top 18%)</option>
                  <option value="current">Current Page Only</option>
                </select>
              </div>
              <div>
                <label class="studio-lbl">Font Size</label>
                <select id="pdfReplaceFontSize" class="field-input">
                  <option value="auto">Auto Match</option>
                  <option value="9">9 pt</option>
                  <option value="10">10 pt</option>
                  <option value="11">11 pt</option>
                  <option value="12">12 pt</option>
                  <option value="14">14 pt</option>
                  <option value="16">16 pt</option>
                  <option value="20">20 pt</option>
                </select>
              </div>
            </div>
            <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;">
              <label style="font-size:11.5px;display:flex;align-items:center;gap:5px;cursor:pointer;"><input type="checkbox" id="pdfReplaceBold"> Bold text</label>
              <button class="btn" onclick="pdfApplyFindReplace()">✨ Replace Now</button>
            </div>
          </div>

          <div class="studio-card">
            <h4>✏️ Click-to-Edit &amp; Add Text</h4>
            <p style="font-size:11px;color:var(--muted);">Click any text box on the PDF page to edit it directly, or toggle Add Text / Whiteout below.</p>
            <div style="display:flex;gap:6px;flex-wrap:wrap;">
              <button id="pdfModeSelectBtn" class="btn" style="font-size:11px;padding:6px 9px;" onclick="setPdfToolMode('edit')">👆 Edit Text</button>
              <button id="pdfModeAddTextBtn" class="btn btn-outline" style="font-size:11px;padding:6px 9px;" onclick="setPdfToolMode('addText')">➕ Add Text</button>
              <button id="pdfModeWhiteoutBtn" class="btn btn-outline" style="font-size:11px;padding:6px 9px;" onclick="setPdfToolMode('whiteout')">⬜ Cover Box</button>
            </div>
            <div id="pdfInlineEditor" style="display:none;border-top:1px solid var(--border);padding-top:8px;margin-top:4px;">
              <label class="studio-lbl" id="pdfInlineLabel">Edit Selected Text</label>
              <input id="pdfInlineText" class="field-input" style="margin-bottom:6px;">
              <div style="display:flex;gap:6px;align-items:center;">
                <input id="pdfInlineSize" type="number" min="6" max="48" value="11" class="field-input" style="width:72px;" title="Font size (pt)">
                <label style="font-size:11px;display:flex;align-items:center;gap:4px;"><input type="checkbox" id="pdfInlineBold"> Bold</label>
                <button class="btn" style="margin-left:auto;" onclick="pdfCommitInlineEdit()">Apply</button>
              </div>
            </div>
          </div>

          <div class="studio-card">
            <h4>🖼️ Adjust / Overlay Images</h4>
            <p style="font-size:11px;color:var(--muted);">Cover an old image with Cover Box, then place and resize a replacement or new image on the page.</p>
            <div style="display:flex;gap:6px;flex-wrap:wrap;">
              <input type="file" id="pdfOverlayFileInput" accept="image/*" style="display:none;" onchange="pdfAddOverlayFromFile(this)">
              <button class="btn btn-outline" style="font-size:11px;" onclick="document.getElementById('pdfOverlayFileInput').click()">📁 Pick Image</button>
              <select id="pdfSubImgPicker" class="field-input" style="flex:1;font-size:11px;padding:5px 7px;" onchange="if(this.value){pdfAddOverlayFromUrl(this.value);this.value='';}">
                <option value="">+ From Submission...</option>
              </select>
            </div>
            <div id="pdfOverlayList" style="display:flex;flex-direction:column;gap:5px;"></div>
          </div>
        </div>

        <div class="studio-stage">
          <div style="display:flex;align-items:center;gap:10px;margin-bottom:12px;background:var(--surface);border:1px solid var(--border);padding:7px 14px;border-radius:10px;flex-wrap:wrap;">
            <button class="btn btn-outline" style="padding:5px 10px;" onclick="pdfChangePage(-1)">◀ Prev</button>
            <strong id="pdfPageLabel" style="font-size:12.5px;">Page 1 / 1</strong>
            <button class="btn btn-outline" style="padding:5px 10px;" onclick="pdfChangePage(1)">Next ▶</button>
            <span style="color:var(--border);">|</span>
            <label style="font-size:12px;color:var(--muted);">Zoom:</label>
            <select id="pdfZoomSelect" class="field-input" style="width:92px;padding:4px 8px;" onchange="pdfRenderCurrentPage()">
              <option value="1.0">100%</option>
              <option value="1.35" selected>135%</option>
              <option value="1.7">170%</option>
            </select>
            <span id="pdfToolHint" style="font-size:11.5px;color:var(--primary);font-weight:600;">Click any text on the page to edit</span>
          </div>
          <div id="pdfPageContainer" class="pdf-page-wrap">
            <canvas id="pdfRenderCanvas"></canvas>
            <div id="pdfTextLayer" style="position:absolute;inset:0;"></div>
            <div id="pdfOverlayLayer" style="position:absolute;inset:0;pointer-events:none;"></div>
          </div>
        </div>
      </div>
    </div>
  </div>

  <!-- 2. DOCX EDITOR STUDIO MODAL -->
  <div id="docxStudioModal" class="studio-overlay">
    <div class="studio-shell">
      <div class="studio-topbar">
        <div style="display:flex;align-items:center;gap:10px;min-width:0;">
          <strong style="font-size:15px;">📝 Word (DOCX) Studio</strong>
          <span id="docxStudioFileName" class="badge badge-blue"></span>
          <span id="docxStudioEngine" class="badge badge-green"></span>
        </div>
        <div style="display:flex;gap:7px;flex-wrap:wrap;">
          <button class="btn btn-outline" onclick="docxInspectLoad(true)">↻ Load Original</button>
          <button class="btn" id="docxApplyBtn" onclick="docxSaveAndPreview(false)">✨ Apply Changes &amp; Preview</button>
          <a id="docxDownloadLink" class="btn btn-outline" style="display:none;text-decoration:none;" download>⬇ .docx</a>
          <button class="btn" style="background:#16a34a;" onclick="docxSaveAndPreview(true)">🖨 Save &amp; Direct Print</button>
          <button class="btn btn-outline" onclick="closeStudioModal('docxStudioModal')">✕ Close</button>
        </div>
      </div>
      <div class="studio-body">
        <div class="studio-side wide">
          <div class="studio-card">
            <h4>🔍 Find &amp; Replace (Headers + Body + Tables) <button class="mini-btn" onclick="docxAddFrRow()">+ Pair</button></h4>
            <p style="font-size:11px;color:var(--muted);">Preserves Word bold/fonts/tables even across split runs — ideal for Name, Roll No., Section.</p>
            <div id="docxFrPairs" style="display:flex;flex-direction:column;gap:6px;"></div>
            <div style="display:flex;gap:8px;align-items:center;margin-top:2px;">
              <select id="docxFrScope" class="field-input" style="flex:1;font-size:12px;">
                <option value="all">Replace Everywhere (Headers + Footers + Body)</option>
                <option value="headers">Headers &amp; Footers Only</option>
                <option value="body">Document Body &amp; Tables Only</option>
              </select>
              <label style="font-size:11px;display:flex;align-items:center;gap:4px;white-space:nowrap;"><input type="checkbox" id="docxFrCase"> Case</label>
            </div>
          </div>

          <div class="studio-card">
            <h4>🎓 Headers &amp; Footers <span class="badge badge-amber" id="docxHdrBadge">0</span></h4>
            <div id="docxHeadersList" style="display:flex;flex-direction:column;gap:7px;max-height:190px;overflow-y:auto;"></div>
          </div>

          <div class="studio-card">
            <h4>🖼️ Embedded Images Resize <span class="badge badge-blue" id="docxImgBadge">0</span></h4>
            <div id="docxImagesList" style="display:flex;flex-direction:column;gap:7px;max-height:160px;overflow-y:auto;"></div>
          </div>

          <div class="studio-card">
            <h4>📄 Body &amp; Cover Page Text <input id="docxBodyFilter" class="field-input" placeholder="Filter paragraphs..." style="width:150px;padding:4px 8px;font-size:11px;" oninput="renderDocxBodyList()"></h4>
            <div id="docxBodyList" style="display:flex;flex-direction:column;gap:7px;max-height:260px;overflow-y:auto;"></div>
          </div>
        </div>

        <div class="studio-stage" style="padding:12px;">
          <div id="docxPreviewBanner" style="width:100%;margin-bottom:8px;font-size:12px;color:var(--muted);display:flex;justify-content:space-between;align-items:center;">
            <span id="docxPreviewStatus">Edit headers, text, or image sizes on the left, then click "Apply Changes &amp; Preview".</span>
          </div>
          <div id="docxPreviewFrameWrap" style="width:100%;flex:1;background:var(--surface);border:1px solid var(--border);border-radius:12px;overflow:hidden;display:flex;align-items:center;justify-content:center;">
            <div style="text-align:center;padding:30px;color:var(--muted);font-size:13px;">Live PDF Preview will appear here after clicking <strong>Apply Changes &amp; Preview</strong>.</div>
          </div>
        </div>
      </div>
    </div>
  </div>

  <!-- 3. IMAGE PRINT CANVAS STUDIO MODAL -->
  <div id="imageStudioModal" class="studio-overlay">
    <div class="studio-shell">
      <div class="studio-topbar">
        <div style="display:flex;align-items:center;gap:10px;">
          <strong style="font-size:15px;">🖼️ Image Print Canvas Studio</strong>
          <span id="imgCanvasStatus" class="badge badge-blue">A4 Portrait</span>
        </div>
        <div style="display:flex;gap:7px;flex-wrap:wrap;">
          <button class="btn btn-outline" id="icUndoBtn" onclick="icUndo()" disabled>↩ Undo</button>
          <button class="btn btn-outline" id="icRedoBtn" onclick="icRedo()" disabled>↪ Redo</button>
          <button class="btn" onclick="imgCanvasExport(false)">💾 Save PDF to Job</button>
          <button class="btn" style="background:#16a34a;" onclick="imgCanvasExport(true)">🖨 Direct Print Canvas</button>
          <button class="btn btn-outline" onclick="closeStudioModal('imageStudioModal')">✕ Close</button>
        </div>
      </div>
      <div class="studio-body">
        <div class="studio-side">
          <div class="studio-card">
            <h4>📐 Paper &amp; Orientation</h4>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;">
              <div>
                <label class="studio-lbl">Paper Size</label>
                <select id="icPaperSize" class="field-input" onchange="imgCanvasRenderPaper()">
                  <option value="A4">A4 (21 × 29.7 cm)</option>
                  <option value="Letter">Letter (21.6 × 27.9 cm)</option>
                  <option value="A5">A5 (14.8 × 21 cm)</option>
                  <option value="Photo4x6">4×6 Photo (10.2 × 15.2 cm)</option>
                </select>
              </div>
              <div>
                <label class="studio-lbl">Orientation</label>
                <select id="icOrientation" class="field-input" onchange="imgCanvasRenderPaper()">
                  <option value="portrait">Portrait</option>
                  <option value="landscape">Landscape</option>
                </select>
              </div>
            </div>
          </div>

          <div class="studio-card">
            <h4>⚡ Quick Print Layouts</h4>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:6px;">
              <button class="btn btn-outline" style="font-size:11px;padding:7px;" onclick="icPreset('fit')">Fit Full Page</button>
              <button class="btn btn-outline" style="font-size:11px;padding:7px;" onclick="icPreset('half')">Half Page Top</button>
              <button class="btn btn-outline" style="font-size:11px;padding:7px;" onclick="icPreset('idcard')">ID Card (8.6×5.4cm)</button>
              <button class="btn btn-outline" style="font-size:11px;padding:7px;" onclick="icPreset('2up')">2-Up Duplicate</button>
              <button class="btn btn-outline" style="font-size:11px;padding:7px;grid-column:1/-1;" onclick="icPreset('passport8')">🪪 8× Passport Photo Sheet (3.5×4.5cm)</button>
            </div>
          </div>

          <div class="studio-card">
            <h4>
              <span>➕ Add More Images to This Sheet</span>
              <button class="btn" style="font-size:11px;padding:5px 11px;border-radius:8px;" onclick="document.getElementById('icLocalImgInput').click()" title="Upload one or more images from your PC">⬆ Upload</button>
            </h4>
            <input type="file" id="icLocalImgInput" accept="image/*" multiple style="display:none;" onchange="icAddLocalFile(this)">
            <div class="ic-scope-row">
              <div class="ic-scope-tabs">
                <button id="icScopeJobBtn" class="ic-scope-btn active" onclick="icSetPickerScope('job')">This Job</button>
                <button id="icScopeAllBtn" class="ic-scope-btn" onclick="icSetPickerScope('all')">All Inbox</button>
              </div>
              <span style="font-size:10px;color:var(--muted);">Click or drag onto sheet</span>
            </div>
            <div id="icSubThumbGrid" class="ic-thumb-strip"></div>
          </div>

          <div class="studio-card" id="icSelectedControls">
            <h4>🎛️ Selected Image Adjustments</h4>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;">
              <div><label class="studio-lbl">Width (cm)</label><input id="icWidthCm" type="number" step="0.1" min="1" max="30" class="field-input" onchange="icApplyDimInput('w')"></div>
              <div><label class="studio-lbl">Height (cm)</label><input id="icHeightCm" type="number" step="0.1" min="1" max="30" class="field-input" onchange="icApplyDimInput('h')"></div>
            </div>
            <div>
              <label class="studio-lbl">Rotate: <span id="icRotVal">0°</span></label>
              <div style="display:flex;gap:6px;align-items:center;">
                <button class="mini-btn" onclick="icRotateStep(-90)">↺ -90°</button>
                <input id="icRotSlider" type="range" min="-180" max="180" value="0" style="flex:1;" oninput="icSetRotation(Number(this.value))">
                <button class="mini-btn" onclick="icRotateStep(90)">↻ +90°</button>
              </div>
            </div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;">
              <div><label class="studio-lbl">Brightness</label><input id="icBright" type="range" min="60" max="160" value="100" oninput="icSetFilter()"></div>
              <div><label class="studio-lbl">Contrast</label><input id="icContrast" type="range" min="60" max="180" value="100" oninput="icSetFilter()"></div>
            </div>
            <label style="font-size:11.5px;display:flex;align-items:center;gap:6px;cursor:pointer;"><input type="checkbox" id="icGrayscale" onchange="icSetFilter()"> Black &amp; White (B&amp;W Print Mode)</label>
            <div style="display:flex;gap:6px;flex-wrap:wrap;margin-top:4px;">
              <button class="mini-btn" onclick="icCenterSelected()">⊕ Center</button>
              <button class="mini-btn" onclick="icDuplicateSelected()">⧉ Duplicate</button>
              <button class="mini-btn" style="color:var(--danger);" onclick="icDeleteSelected()">🗑 Remove</button>
            </div>
          </div>
        </div>

        <div class="studio-stage" style="justify-content:center;">
          <div id="icPaperEl" class="canvas-paper"
            onmousedown="icPaperMouseDown(event)"
            ondragover="icPaperDragOver(event)"
            ondragleave="icPaperDragLeave(event)"
            ondrop="icPaperDrop(event)">
            <div class="canvas-margin-guide"></div>
            <div id="icItemsLayer" style="position:absolute;inset:0;"></div>
            <div id="icSelectionUiLayer" style="position:absolute;inset:0;pointer-events:none;z-index:50;"></div>
            <div id="icMarqueeBox" class="ic-marquee-box"></div>
          </div>
          <div style="margin-top:12px;font-size:11.5px;color:var(--muted);">Drag thumbnails onto sheet • Click &amp; drag empty paper to multi-select • Use floating toolbar &amp; side ⟳ handle to rotate/duplicate/delete</div>
        </div>
      </div>
    </div>
  </div>

  <!-- 4. MERGE & PRINT ALL MODAL -->
  <div id="mergePrintModal" class="studio-overlay">
    <div class="studio-shell" style="max-width:860px;height:auto;max-height:88vh;">
      <div class="studio-topbar">
        <div>
          <strong style="font-size:16px;">📑 Merge &amp; Print All Files (1 Print Job)</strong>
          <div id="mergeSubTitle" style="font-size:12px;color:var(--muted);"></div>
        </div>
        <div style="display:flex;gap:7px;align-items:center;">
          <button class="btn btn-outline" id="mergeUndoBtn" onclick="mergeUndo()" disabled>↩ Undo</button>
          <button class="btn btn-outline" onclick="closeStudioModal('mergePrintModal')">✕ Close</button>
        </div>
      </div>
      <div style="padding:18px;overflow-y:auto;display:flex;flex-direction:column;gap:12px;">
        <p style="font-size:12.5px;color:var(--text-secondary);">
          Reorder, pick exact page ranges per file (e.g. <code>1-3, 5</code>), or move unwanted files to Trash. Drop any newly edited Word (.docx), PDF, or Image files below to include them in this single print job.
        </p>
        <div id="mergeDropZone" class="drop-in-zone"
          onclick="document.getElementById('mergeAddFileInput').click()"
          ondragover="handleAddZoneDragOver(event)"
          ondragleave="this.classList.remove('drag-active')"
          ondrop="handleAddZoneDrop(event, mergeQueue.submissionId, true)">
          📥 Drag &amp; drop edited Word / PDF / Image files here to add to this merge (or click to browse)
          <input type="file" id="mergeAddFileInput" multiple style="display:none;" onchange="if(this.files&&this.files.length){uploadLocalFilesToSubmission(mergeQueue.submissionId,this.files,true);this.value='';}">
        </div>
        <div id="mergeQueueList" style="display:flex;flex-direction:column;gap:8px;"></div>
        <div id="mergeProgressMsg" style="font-size:12.5px;font-weight:700;color:var(--primary);display:none;"></div>
        <div style="display:flex;justify-content:flex-end;gap:10px;padding-top:8px;border-top:1px solid var(--border);flex-wrap:wrap;">
          <button class="btn btn-outline" id="mergeSaveOnlyBtn" onclick="executeMergeAndPrint(false)">💾 Merge &amp; Preview PDF</button>
          <button class="btn" id="mergeDirectPrintBtn" style="background:#16a34a;" onclick="executeMergeAndPrint(true)">🖨 Merge All &amp; Direct Print (1 Job)</button>
        </div>
      </div>
    </div>
  </div>

  <!-- 5. VISUAL PAGE MANAGER STUDIO (PDF & WORD DOCS — Select, Reorder, Rotate, Duplicate, Delete) -->
  <div id="pageManagerModal" class="studio-overlay">
    <div class="studio-shell">
      <div class="studio-topbar">
        <div style="display:flex;align-items:center;gap:10px;min-width:0;flex-wrap:wrap;">
          <strong style="font-size:15.5px;">📑 Page Manager</strong>
          <span id="pmFileName" class="badge badge-blue" style="max-width:260px;overflow:hidden;text-overflow:ellipsis;"></span>
          <span id="pmSelectionBadge" class="badge badge-gold">0 / 0 selected</span>
        </div>
        <div style="display:flex;gap:7px;flex-wrap:wrap;align-items:center;">
          <button class="btn btn-outline" id="pmUndoBtn" onclick="pmUndo()" disabled>↩ Undo</button>
          <button class="btn btn-outline" id="pmRedoBtn" onclick="pmRedo()" disabled>↪ Redo</button>
          <button class="btn btn-outline" onclick="pmResetAll()" title="Restore all original pages">↻ Reset</button>
          <button class="btn btn-sync" id="pmToTrayBtn" onclick="pmSendToTray()">📌 Send to Tray</button>
          <button class="btn" id="pmSaveBtn" onclick="pmSaveOutput(false)">💾 Save Pages</button>
          <button class="btn" id="pmPrintBtn" style="background:#16a34a;" onclick="pmSaveOutput(true)">🖨 Print Selected</button>
          <button class="btn btn-outline" onclick="closeStudioModal('pageManagerModal')">✕ Close</button>
        </div>
      </div>

      <div class="pm-toolbar">
        <div style="display:flex;align-items:center;gap:7px;flex-wrap:wrap;">
          <strong style="font-size:12px;">Page Range:</strong>
          <input id="pmRangeInput" class="field-input" placeholder="e.g. 1-3, 5, 8-12" style="width:175px;padding:6px 10px;font-size:12px;" onkeydown="if(event.key==='Enter'){event.preventDefault();pmApplyRange();}">
          <button class="btn" style="padding:6px 11px;font-size:11.5px;" onclick="pmApplyRange()">Apply Range</button>
          <span style="color:var(--border);">|</span>
          <button class="mini-btn" onclick="pmPreset('all')">✓ All</button>
          <button class="mini-btn" onclick="pmPreset('none')">✕ None</button>
          <button class="mini-btn" onclick="pmPreset('odd')">Odd (1,3,5)</button>
          <button class="mini-btn" onclick="pmPreset('even')">Even (2,4,6)</button>
          <button class="mini-btn" onclick="pmPreset('first')">1st Page</button>
          <button class="mini-btn" onclick="pmPreset('invert')">⇄ Invert</button>
          <button class="mini-btn" style="color:var(--danger);" onclick="pmRemoveUnselected()" title="Delete all unchecked pages from grid">🗑 Remove Unchecked</button>
        </div>
        <span id="pmStatusHint" style="font-size:11.5px;color:var(--muted);">Click pages to include/exclude • Drag thumbnails to reorder • Use ↺↻⧉🗑 per page</span>
      </div>

      <div id="pmGrid" class="pm-grid">
        <div style="grid-column:1/-1;text-align:center;padding:60px;color:var(--muted);">Loading document pages...</div>
      </div>
    </div>
  </div>

  <!-- PAGE MANAGER FULL-SIZE ZOOM LIGHTBOX -->
  <div id="pmZoomOverlay" class="pm-zoom-overlay" onclick="if(event.target===this)this.classList.remove('open')">
    <div style="background:var(--surface);border-radius:14px;padding:14px;max-width:90vw;max-height:90vh;display:flex;flex-direction:column;gap:10px;">
      <div style="display:flex;justify-content:space-between;align-items:center;gap:12px;">
        <strong id="pmZoomTitle" style="font-size:14px;">Page Preview</strong>
        <button class="btn btn-outline" style="padding:5px 10px;" onclick="document.getElementById('pmZoomOverlay').classList.remove('open')">✕ Close</button>
      </div>
      <div style="overflow:auto;display:flex;justify-content:center;background:#e2e8f0;padding:12px;border-radius:10px;max-height:78vh;">
        <canvas id="pmZoomCanvas" style="background:#fff;box-shadow:0 6px 24px rgba(0,0,0,0.25);"></canvas>
      </div>
    </div>
  </div>

  <!-- NON-BLOCKING TOAST NOTIFICATIONS (with Undo / Open in Word) -->
  <div id="sdToastBox" class="sd-toast-box"></div>
`;

const studioJs = `
    // ========================================================================
    // DOCUMENT, PDF, IMAGE CANVAS & MERGE-PRINT STUDIO
    // ========================================================================
    function closeStudioModal(id) {
      const el = document.getElementById(id);
      if (el) el.classList.remove('open');
    }

    function openSmartEditorById(fileId, submissionId) {
      let targetFile = null;
      let resolvedSubId = submissionId || null;
      for (const s of (cloudState && cloudState.submissions) || []) {
        const hit = (s.files || []).find(f => f.id === fileId);
        if (hit) { targetFile = hit; if (!resolvedSubId) resolvedSubId = s.id; break; }
      }
      if (!targetFile) { alert('File record not found. Click Sync and try again.'); return; }
      openSmartEditor(targetFile.id, targetFile.originalName, targetFile.fileExtension, targetFile.convertedUrl || '', resolvedSubId, targetFile.conversionStatus || '');
    }

    function openSmartEditor(fileId, fileName, ext, convertedUrl, submissionId, conversionStatus) {
      const e = (ext || '').toLowerCase();
      if (e === '.pdf') {
        const origUrl = '/api/file?id=' + encodeURIComponent(fileId);
        const activeUrl = (conversionStatus === 'edited' && convertedUrl) ? convertedUrl : origUrl;
        openPdfStudio(fileId, fileName, activeUrl, submissionId, origUrl);
      } else if (e === '.docx' || e === '.doc') {
        openDocxStudio(fileId, fileName);
      } else if (['.jpg', '.jpeg', '.png', '.webp', '.bmp', '.gif'].includes(e)) {
        openImageCanvas(fileId, fileName, submissionId);
      } else {
        alert('Direct editing supports PDF, DOCX/DOC, and Images. Use "Open" for ' + e + ' files.');
      }
    }

    // ------------------------------------------------------------------------
    // 1. PDF STUDIO (pdf.js text extraction + pdf-lib editing & image overlay)
    // ------------------------------------------------------------------------
    let pdfStudio = {
      fileId: null,
      fileName: '',
      submissionId: null,
      originalUrl: null,
      originalBytes: null,
      currentBytes: null,
      undoStack: [],
      pdfJsDoc: null,
      pageNum: 1,
      numPages: 1,
      scale: 1.35,
      toolMode: 'edit', // edit | addText | whiteout
      pageTextItems: [],
      allHeaders: [],
      selectedInlineTarget: null,
      overlays: [] // { id, pageNum, imgUrl, bytes, mime, xPct, yPct, wPct, hPct }
    };

    function ensurePdfJsWorker() {
      if (window.pdfjsLib && window.pdfjsLib.GlobalWorkerOptions) {
        if (!window.pdfjsLib.GlobalWorkerOptions.workerSrc) {
          window.pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js';
        }
        return true;
      }
      return false;
    }

    async function openPdfStudio(fileId, fileName, pdfUrl, submissionId, originalUrl) {
      if (!ensurePdfJsWorker() || !window.PDFLib) {
        alert('PDF Studio libraries are still loading. Check internet access or refresh the page.');
        return;
      }
      pdfStudio.fileId = fileId;
      pdfStudio.fileName = fileName;
      pdfStudio.submissionId = submissionId;
      pdfStudio.originalUrl = originalUrl || pdfUrl;
      pdfStudio.undoStack = [];
      pdfStudio.overlays = [];
      pdfStudio.pageNum = 1;
      pdfStudio.toolMode = 'edit';
      document.getElementById('pdfStudioFileName').textContent = fileName;
      document.getElementById('pdfStudioStatus').textContent = 'Loading PDF...';
      document.getElementById('pdfInlineEditor').style.display = 'none';
      document.getElementById('pdfStudioModal').classList.add('open');

      // Populate submission image picker for PDF overlays
      const picker = document.getElementById('pdfSubImgPicker');
      picker.innerHTML = '<option value="">+ From Submission...</option>';
      const sub = (cloudState.submissions || []).find(s => s.id === submissionId || s.receiptCode === submissionId);
      if (sub) {
        (sub.files || []).filter(f => ['.jpg','.jpeg','.png','.webp','.bmp'].includes((f.fileExtension || '').toLowerCase())).forEach(f => {
          const opt = document.createElement('option');
          opt.value = '/api/file?id=' + encodeURIComponent(f.id);
          opt.textContent = f.originalName;
          picker.appendChild(opt);
        });
      }

      try {
        const res = await fetch(pdfUrl + (pdfUrl.includes('?') ? '&' : '?') + 't=' + Date.now());
        if (!res.ok) throw new Error('Could not load PDF file.');
        const buf = new Uint8Array(await res.arrayBuffer());
        pdfStudio.currentBytes = buf.slice();
        if (pdfStudio.originalUrl && pdfStudio.originalUrl !== pdfUrl) {
          try {
            const origRes = await fetch(pdfStudio.originalUrl);
            pdfStudio.originalBytes = origRes.ok ? new Uint8Array(await origRes.arrayBuffer()) : buf.slice();
          } catch { pdfStudio.originalBytes = buf.slice(); }
        } else {
          pdfStudio.originalBytes = buf.slice();
        }
        await pdfStudioReloadDoc();
        setPdfToolMode('edit');
      } catch (err) {
        alert('PDF Studio failed to load: ' + err.message);
        closeStudioModal('pdfStudioModal');
      }
    }

    async function pdfStudioReloadDoc() {
      const loadingTask = window.pdfjsLib.getDocument({ data: pdfStudio.currentBytes.slice() });
      pdfStudio.pdfJsDoc = await loadingTask.promise;
      pdfStudio.numPages = pdfStudio.pdfJsDoc.numPages;
      if (pdfStudio.pageNum > pdfStudio.numPages) pdfStudio.pageNum = pdfStudio.numPages;
      await pdfScanHeaders();
      await pdfRenderCurrentPage();
      document.getElementById('pdfStudioStatus').textContent = pdfStudio.numPages + ' page(s) ready';
    }

    async function pdfScanHeaders() {
      const seen = new Map();
      for (let p = 1; p <= pdfStudio.numPages; p++) {
        const page = await pdfStudio.pdfJsDoc.getPage(p);
        const vp = page.getViewport({ scale: 1 });
        const tc = await page.getTextContent();
        const headerThresholdY = vp.height * 0.82; // top 18% in PDF bottom-left coords
        for (const item of tc.items) {
          const txt = (item.str || '').trim();
          if (txt.length < 2) continue;
          const ty = item.transform[5];
          if (ty >= headerThresholdY) {
            if (!seen.has(txt)) seen.set(txt, { text: txt, pages: [p] });
            else if (!seen.get(txt).pages.includes(p)) seen.get(txt).pages.push(p);
          }
        }
      }
      pdfStudio.allHeaders = Array.from(seen.values()).slice(0, 30);
      document.getElementById('pdfHeaderCount').textContent = pdfStudio.allHeaders.length;
      const box = document.getElementById('pdfDetectedHeaders');
      if (pdfStudio.allHeaders.length === 0) {
        box.innerHTML = '<div style="font-size:11px;color:var(--muted);">No selectable header text detected (may be a scanned PDF — use Cover Box + Add Text).</div>';
      } else {
        box.innerHTML = pdfStudio.allHeaders.map(h =>
          '<button type="button" class="mini-btn" style="text-align:left;display:flex;justify-content:space-between;align-items:center;gap:6px;padding:6px 8px;" onclick="pdfSelectHeaderToReplace(' + JSON.stringify(h.text).replace(/"/g, '&quot;') + ')">' +
          '<span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">' + esc(h.text) + '</span>' +
          '<span style="color:var(--muted);font-size:10px;white-space:nowrap;">' + h.pages.length + 'p</span></button>'
        ).join('');
      }
    }

    function pdfSelectHeaderToReplace(txt) {
      document.getElementById('pdfFindInput').value = txt;
      document.getElementById('pdfReplaceScope').value = 'headers';
      const rep = document.getElementById('pdfReplaceInput');
      rep.focus();
      rep.select();
    }

    function setPdfToolMode(mode) {
      pdfStudio.toolMode = mode;
      document.getElementById('pdfModeSelectBtn').className = mode === 'edit' ? 'btn' : 'btn btn-outline';
      document.getElementById('pdfModeAddTextBtn').className = mode === 'addText' ? 'btn' : 'btn btn-outline';
      document.getElementById('pdfModeWhiteoutBtn').className = mode === 'whiteout' ? 'btn' : 'btn btn-outline';
      const hints = {
        edit: 'Click any text on the page to edit it in place',
        addText: 'Click anywhere on the page to place new text (e.g. Name / Roll No.)',
        whiteout: 'Click and drag on the page to cover an old image, logo, or text'
      };
      document.getElementById('pdfToolHint').textContent = hints[mode] || '';
      const layer = document.getElementById('pdfTextLayer');
      if (layer) layer.style.cursor = mode === 'edit' ? 'default' : 'crosshair';
    }

    function pdfChangePage(delta) {
      const next = pdfStudio.pageNum + delta;
      if (next < 1 || next > pdfStudio.numPages) return;
      pdfStudio.pageNum = next;
      pdfRenderCurrentPage();
    }

    async function pdfRenderCurrentPage() {
      if (!pdfStudio.pdfJsDoc) return;
      pdfStudio.scale = Number(document.getElementById('pdfZoomSelect').value || 1.35);
      const page = await pdfStudio.pdfJsDoc.getPage(pdfStudio.pageNum);
      const vp = page.getViewport({ scale: pdfStudio.scale });
      const vp1 = page.getViewport({ scale: 1 });
      const canvas = document.getElementById('pdfRenderCanvas');
      canvas.width = vp.width;
      canvas.height = vp.height;
      const ctx = canvas.getContext('2d');
      await page.render({ canvasContext: ctx, viewport: vp }).promise;

      const wrap = document.getElementById('pdfPageContainer');
      wrap.style.width = vp.width + 'px';
      wrap.style.height = vp.height + 'px';
      document.getElementById('pdfPageLabel').textContent = 'Page ' + pdfStudio.pageNum + ' / ' + pdfStudio.numPages;

      // Render clickable text boxes
      const tc = await page.getTextContent();
      const textLayer = document.getElementById('pdfTextLayer');
      textLayer.innerHTML = '';
      pdfStudio.pageTextItems = [];

      tc.items.forEach((item, idx) => {
        const str = (item.str || '');
        if (!str.trim()) return;
        const tx = item.transform[4];
        const ty = item.transform[5];
        const fontSize = Math.max(7, Math.hypot(item.transform[0], item.transform[1]) || item.height || 11);
        const wPdf = Math.max(item.width || (str.length * fontSize * 0.52), fontSize);
        const hPdf = Math.max(item.height || fontSize * 1.15, fontSize);

        const rec = { idx, str, xPdf: tx, yPdf: ty, wPdf, hPdf, fontSize, pageW: vp1.width, pageH: vp1.height };
        pdfStudio.pageTextItems.push(rec);

        const div = document.createElement('div');
        div.className = 'pdf-text-hit' + (ty >= vp1.height * 0.82 ? ' header-hit' : '');
        div.style.left = (tx * pdfStudio.scale) + 'px';
        div.style.top = ((vp1.height - ty - hPdf * 0.88) * pdfStudio.scale) + 'px';
        div.style.width = (wPdf * pdfStudio.scale) + 'px';
        div.style.height = (hPdf * 1.12 * pdfStudio.scale) + 'px';
        div.title = 'Click to edit: ' + str;
        div.onclick = (ev) => {
          if (pdfStudio.toolMode !== 'edit') return;
          ev.stopPropagation();
          pdfStudio.selectedInlineTarget = { kind: 'replaceItem', rec };
          document.getElementById('pdfInlineLabel').textContent = 'Edit Text on Page ' + pdfStudio.pageNum;
          document.getElementById('pdfInlineText').value = str;
          document.getElementById('pdfInlineSize').value = Math.round(fontSize);
          document.getElementById('pdfInlineEditor').style.display = 'block';
          document.getElementById('pdfInlineText').focus();
        };
        textLayer.appendChild(div);
      });

      // Handle Add Text click & Whiteout drag on textLayer
      textLayer.onmousedown = (ev) => {
        if (pdfStudio.toolMode === 'addText') {
          const rect = textLayer.getBoundingClientRect();
          const xPdf = (ev.clientX - rect.left) / pdfStudio.scale;
          const yPdf = vp1.height - ((ev.clientY - rect.top) / pdfStudio.scale);
          pdfStudio.selectedInlineTarget = { kind: 'addText', xPdf, yPdf, pageW: vp1.width, pageH: vp1.height };
          document.getElementById('pdfInlineLabel').textContent = 'Place New Text on Page ' + pdfStudio.pageNum;
          document.getElementById('pdfInlineText').value = '';
          document.getElementById('pdfInlineSize').value = 12;
          document.getElementById('pdfInlineEditor').style.display = 'block';
          document.getElementById('pdfInlineText').focus();
        } else if (pdfStudio.toolMode === 'whiteout') {
          const rect = textLayer.getBoundingClientRect();
          const startX = ev.clientX - rect.left;
          const startY = ev.clientY - rect.top;
          const box = document.createElement('div');
          box.style.cssText = 'position:absolute;background:rgba(255,255,255,0.85);border:2px dashed #2563eb;pointer-events:none;';
          box.style.left = startX + 'px'; box.style.top = startY + 'px';
          textLayer.appendChild(box);
          const onMove = (mv) => {
            const curX = mv.clientX - rect.left;
            const curY = mv.clientY - rect.top;
            box.style.left = Math.min(startX, curX) + 'px';
            box.style.top = Math.min(startY, curY) + 'px';
            box.style.width = Math.abs(curX - startX) + 'px';
            box.style.height = Math.abs(curY - startY) + 'px';
          };
          const onUp = async (up) => {
            window.removeEventListener('mousemove', onMove);
            window.removeEventListener('mouseup', onUp);
            const endX = up.clientX - rect.left;
            const endY = up.clientY - rect.top;
            box.remove();
            const wPx = Math.abs(endX - startX);
            const hPx = Math.abs(endY - startY);
            if (wPx < 6 || hPx < 6) return;
            const xPdf = Math.min(startX, endX) / pdfStudio.scale;
            const yTopPdf = Math.min(startY, endY) / pdfStudio.scale;
            const wPdf = wPx / pdfStudio.scale;
            const hPdf = hPx / pdfStudio.scale;
            await pdfApplyWhiteoutBox(pdfStudio.pageNum, xPdf, vp1.height - yTopPdf - hPdf, wPdf, hPdf);
          };
          window.addEventListener('mousemove', onMove);
          window.addEventListener('mouseup', onUp);
        }
      };

      pdfRenderOverlayLayer(vp.width, vp.height);
    }

    async function pdfApplyWhiteoutBox(pageNum, x, y, w, h) {
      pdfStudio.undoStack.push(pdfStudio.currentBytes.slice());
      const doc = await window.PDFLib.PDFDocument.load(pdfStudio.currentBytes, { ignoreEncryption: true });
      const page = doc.getPage(pageNum - 1);
      page.drawRectangle({ x, y, width: w, height: h, color: window.PDFLib.rgb(1, 1, 1) });
      pdfStudio.currentBytes = await doc.save();
      await pdfStudioReloadDoc();
    }

    async function pdfCommitInlineEdit() {
      const target = pdfStudio.selectedInlineTarget;
      if (!target) return;
      const newText = document.getElementById('pdfInlineText').value;
      const size = Math.max(6, Math.min(48, Number(document.getElementById('pdfInlineSize').value || 11)));
      const bold = document.getElementById('pdfInlineBold').checked;

      pdfStudio.undoStack.push(pdfStudio.currentBytes.slice());
      const doc = await window.PDFLib.PDFDocument.load(pdfStudio.currentBytes, { ignoreEncryption: true });
      const font = await doc.embedFont(bold ? window.PDFLib.StandardFonts.HelveticaBold : window.PDFLib.StandardFonts.Helvetica);
      const page = doc.getPage(pdfStudio.pageNum - 1);

      if (target.kind === 'replaceItem') {
        const r = target.rec;
        page.drawRectangle({
          x: r.xPdf - 1.5,
          y: r.yPdf - r.fontSize * 0.28,
          width: Math.max(r.wPdf + 4, font.widthOfTextAtSize(newText || ' ', size) + 2),
          height: r.hPdf * 1.25,
          color: window.PDFLib.rgb(1, 1, 1)
        });
        if (newText) {
          page.drawText(newText, { x: r.xPdf, y: r.yPdf, size, font, color: window.PDFLib.rgb(0.06, 0.09, 0.16) });
        }
      } else if (target.kind === 'addText' && newText) {
        page.drawText(newText, { x: target.xPdf, y: target.yPdf, size, font, color: window.PDFLib.rgb(0.06, 0.09, 0.16) });
      }

      pdfStudio.currentBytes = await doc.save();
      document.getElementById('pdfInlineEditor').style.display = 'none';
      pdfStudio.selectedInlineTarget = null;
      await pdfStudioReloadDoc();
    }

    async function pdfApplyFindReplace() {
      const findStr = (document.getElementById('pdfFindInput').value || '').trim();
      const replStr = document.getElementById('pdfReplaceInput').value || '';
      const scope = document.getElementById('pdfReplaceScope').value;
      const fontChoice = document.getElementById('pdfReplaceFontSize').value;
      const bold = document.getElementById('pdfReplaceBold').checked;
      if (!findStr) { alert('Enter text to find in the PDF.'); return; }

      const findLower = findStr.toLowerCase();
      const escapedFind = findStr.replace(/[.*+?^\${}()|[\\]\\\\]/g, '\\\\$&');
      const findRe = new RegExp(escapedFind, 'gi');
      const hits = [];
      const startPage = scope === 'current' ? pdfStudio.pageNum : 1;
      const endPage = scope === 'current' ? pdfStudio.pageNum : pdfStudio.numPages;

      for (let p = startPage; p <= endPage; p++) {
        const page = await pdfStudio.pdfJsDoc.getPage(p);
        const vp1 = page.getViewport({ scale: 1 });
        const tc = await page.getTextContent();
        const pageDirectHits = [];
        const lineBuckets = [];

        for (const item of tc.items) {
          const s = item.str || '';
          if (!s.trim()) continue;
          const ty = item.transform[5];
          if (scope === 'headers' && ty < vp1.height * 0.82) continue;
          const fontSize = Math.max(7, Math.hypot(item.transform[0], item.transform[1]) || item.height || 11);
          const tx = item.transform[4];
          const w = Math.max(item.width || (s.length * fontSize * 0.55), fontSize);
          const h = Math.max(item.height || fontSize * 1.15, fontSize);

          if (s.toLowerCase().includes(findLower)) {
            pageDirectHits.push({
              pageNum: p, x: tx, y: ty, w, h, fontSize,
              newText: s.replace(findRe, replStr)
            });
          }

          let bucket = lineBuckets.find(b => Math.abs(b.y - ty) <= Math.max(3, fontSize * 0.45));
          if (!bucket) { bucket = { y: ty, fontSize, items: [] }; lineBuckets.push(bucket); }
          bucket.items.push({ x: tx, y: ty, w, h, fontSize, str: s });
        }

        if (pageDirectHits.length > 0) {
          pageDirectHits.forEach(h => hits.push(h));
        } else {
          for (const b of lineBuckets) {
            b.items.sort((a, c) => a.x - c.x);
            let joined = '';
            let prevRight = null;
            for (const it of b.items) {
              if (prevRight !== null && (it.x - prevRight) > it.fontSize * 0.2 && !joined.endsWith(' ') && !it.str.startsWith(' ')) joined += ' ';
              joined += it.str;
              prevRight = it.x + it.w;
            }
            if (joined.toLowerCase().includes(findLower)) {
              const first = b.items[0];
              const last = b.items[b.items.length - 1];
              const maxH = Math.max(...b.items.map(it => it.h));
              hits.push({
                pageNum: p,
                x: first.x,
                y: first.y,
                w: Math.max((last.x + last.w) - first.x, first.w),
                h: maxH,
                fontSize: first.fontSize,
                newText: joined.replace(findRe, replStr)
              });
            }
          }
        }
      }

      if (hits.length === 0) {
        alert('No matching selectable text found for "' + findStr + '". If this PDF is a photo/scan, use Cover Box + Add Text.');
        return;
      }

      pdfStudio.undoStack.push(pdfStudio.currentBytes.slice());
      const doc = await window.PDFLib.PDFDocument.load(pdfStudio.currentBytes, { ignoreEncryption: true });
      const font = await doc.embedFont(bold ? window.PDFLib.StandardFonts.HelveticaBold : window.PDFLib.StandardFonts.Helvetica);

      for (const h of hits) {
        const page = doc.getPage(h.pageNum - 1);
        const drawSize = fontChoice === 'auto' ? Math.max(7, Math.min(28, h.fontSize * 0.96)) : Number(fontChoice);
        const newW = h.newText ? font.widthOfTextAtSize(h.newText, drawSize) : 0;
        page.drawRectangle({
          x: h.x - 1.5,
          y: h.y - h.fontSize * 0.28,
          width: Math.max(h.w + 3, newW + 2),
          height: h.h * 1.25,
          color: window.PDFLib.rgb(1, 1, 1)
        });
        if (h.newText) {
          page.drawText(h.newText, { x: h.x, y: h.y, size: drawSize, font, color: window.PDFLib.rgb(0.06, 0.09, 0.16) });
        }
      }

      pdfStudio.currentBytes = await doc.save();
      await pdfStudioReloadDoc();
      document.getElementById('pdfStudioStatus').textContent = 'Replaced ' + hits.length + ' match(es)!';
    }

    async function pdfAddOverlayFromFile(input) {
      if (!input.files || !input.files[0]) return;
      const f = input.files[0];
      const url = URL.createObjectURL(f);
      await pdfAddOverlayFromUrl(url, f.name);
      input.value = '';
    }

    async function pdfAddOverlayFromUrl(url, label) {
      const img = new Image();
      img.crossOrigin = 'anonymous';
      await new Promise((res, rej) => { img.onload = res; img.onerror = rej; img.src = url; });
      const aspect = img.naturalHeight / Math.max(1, img.naturalWidth);
      const wPct = 32;
      const hPct = Math.min(45, wPct * aspect * 0.72);
      pdfStudio.overlays.push({
        id: 'ov_' + Date.now() + '_' + Math.random().toString(36).slice(2, 5),
        pageNum: pdfStudio.pageNum,
        imgUrl: url,
        label: label || 'Overlay Image',
        xPct: 30, yPct: 22, wPct, hPct
      });
      pdfRenderCurrentPage();
    }

    function pdfRenderOverlayLayer(vpW, vpH) {
      const layer = document.getElementById('pdfOverlayLayer');
      const list = document.getElementById('pdfOverlayList');
      layer.innerHTML = '';
      const pageOvs = pdfStudio.overlays.filter(o => o.pageNum === pdfStudio.pageNum);
      list.innerHTML = pdfStudio.overlays.map(o =>
        '<div style="display:flex;justify-content:space-between;align-items:center;font-size:11px;padding:4px 7px;background:var(--surface-alt);border-radius:6px;">' +
        '<span>Page ' + o.pageNum + ': ' + esc(o.label) + '</span>' +
        '<button class="mini-btn" style="color:var(--danger);" onclick="pdfRemoveOverlay(\\'' + o.id + '\\')">✕</button></div>'
      ).join('');

      pageOvs.forEach(o => {
        const el = document.createElement('div');
        el.className = 'pdf-overlay-img';
        el.style.pointerEvents = 'auto';
        el.style.left = (o.xPct / 100 * vpW) + 'px';
        el.style.top = (o.yPct / 100 * vpH) + 'px';
        el.style.width = (o.wPct / 100 * vpW) + 'px';
        el.style.height = (o.hPct / 100 * vpH) + 'px';
        el.innerHTML = '<img src="' + o.imgUrl + '" style="width:100%;height:100%;object-fit:contain;pointer-events:none;"><div class="resize-handle"></div>';

        el.onmousedown = (ev) => {
          ev.stopPropagation();
          const isResize = ev.target.classList.contains('resize-handle');
          const sx = ev.clientX, sy = ev.clientY;
          const ox = o.xPct, oy = o.yPct, ow = o.wPct, oh = o.hPct;
          const onMove = (mv) => {
            const dxPct = ((mv.clientX - sx) / vpW) * 100;
            const dyPct = ((mv.clientY - sy) / vpH) * 100;
            if (isResize) {
              o.wPct = Math.max(4, Math.min(98 - ox, ow + dxPct));
              o.hPct = Math.max(4, Math.min(98 - oy, oh + dyPct));
              el.style.width = (o.wPct / 100 * vpW) + 'px';
              el.style.height = (o.hPct / 100 * vpH) + 'px';
            } else {
              o.xPct = Math.max(0, Math.min(100 - o.wPct, ox + dxPct));
              o.yPct = Math.max(0, Math.min(100 - o.hPct, oy + dyPct));
              el.style.left = (o.xPct / 100 * vpW) + 'px';
              el.style.top = (o.yPct / 100 * vpH) + 'px';
            }
          };
          const onUp = () => { window.removeEventListener('mousemove', onMove); window.removeEventListener('mouseup', onUp); };
          window.addEventListener('mousemove', onMove);
          window.addEventListener('mouseup', onUp);
        };
        layer.appendChild(el);
      });
    }

    function pdfRemoveOverlay(id) {
      pdfStudio.overlays = pdfStudio.overlays.filter(o => o.id !== id);
      pdfRenderCurrentPage();
    }

    async function pdfBakeOverlaysIntoBytes() {
      if (pdfStudio.overlays.length === 0) return pdfStudio.currentBytes;
      const doc = await window.PDFLib.PDFDocument.load(pdfStudio.currentBytes, { ignoreEncryption: true });
      for (const o of pdfStudio.overlays) {
        const img = new Image();
        img.crossOrigin = 'anonymous';
        await new Promise((res, rej) => { img.onload = res; img.onerror = rej; img.src = o.imgUrl; });
        const tmp = document.createElement('canvas');
        tmp.width = img.naturalWidth; tmp.height = img.naturalHeight;
        tmp.getContext('2d').drawImage(img, 0, 0);
        const pngDataUrl = tmp.toDataURL('image/png');
        const pngBytes = Uint8Array.from(atob(pngDataUrl.split(',')[1]), c => c.charCodeAt(0));
        const embedded = await doc.embedPng(pngBytes);
        const page = doc.getPage(o.pageNum - 1);
        const sz = page.getSize();
        const w = (o.wPct / 100) * sz.width;
        const h = (o.hPct / 100) * sz.height;
        const x = (o.xPct / 100) * sz.width;
        const y = sz.height - ((o.yPct / 100) * sz.height) - h;
        page.drawImage(embedded, { x, y, width: w, height: h });
      }
      return await doc.save();
    }

    async function pdfStudioUndo() {
      if (pdfStudio.undoStack.length === 0) return;
      pdfStudio.currentBytes = pdfStudio.undoStack.pop();
      await pdfStudioReloadDoc();
    }

    async function pdfStudioReset() {
      if (!pdfStudio.originalBytes) return;
      pdfStudio.undoStack = [];
      pdfStudio.overlays = [];
      pdfStudio.currentBytes = pdfStudio.originalBytes.slice();
      await pdfStudioReloadDoc();
    }

    async function pdfStudioSave(andPrint) {
      document.getElementById('pdfStudioStatus').textContent = 'Saving edited PDF...';
      try {
        const finalBytes = await pdfBakeOverlaysIntoBytes();
        const baseName = (pdfStudio.fileName || 'document').replace(/\\.pdf$/i, '') + '_edited_' + Date.now().toString().slice(-4) + '.pdf';
        const res = await fetch('/api/file/save-edited-pdf', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/pdf',
            'X-File-Id': pdfStudio.fileId || '',
            'X-Output-Name': baseName
          },
          body: finalBytes
        });
        const data = await res.json();
        if (!res.ok || !data.success) throw new Error(data.error || 'Save failed');
        await fetchCloudState(true);
        document.getElementById('pdfStudioStatus').textContent = '✓ Saved (' + data.filename + ')';
        if (andPrint) {
          directPrint(data.url, data.filename);
        } else {
          alert('Edited PDF saved! It is now marked ✓ Edited and will be used automatically in Merge & Print.');
        }
      } catch (err) {
        alert('Could not save PDF: ' + err.message);
      }
    }

    // ------------------------------------------------------------------------
    // 2. DOCX STUDIO (Native C# OpenXML Header/Body/Image Editor + Word/LO PDF)
    // ------------------------------------------------------------------------
    let docxStudio = {
      fileId: null,
      fileName: '',
      headers: [],
      footers: [],
      bodyBlocks: [],
      images: [],
      editedTexts: {}, // key "partPath|pIdx" -> newText
      imageResizes: {}, // key "partPath|dIdx" -> { partPath, drawingIndex, scalePercent, widthCm, heightCm }
      lastPdfUrl: null
    };

    async function openDocxStudio(fileId, fileName) {
      docxStudio.fileId = fileId;
      docxStudio.fileName = fileName;
      docxStudio.editedTexts = {};
      docxStudio.imageResizes = {};
      docxStudio.lastPdfUrl = null;
      document.getElementById('docxStudioFileName').textContent = fileName;
      document.getElementById('docxFrPairs').innerHTML = '';
      docxAddFrRow();
      docxAddFrRow();
      document.getElementById('docxStudioModal').classList.add('open');
      await docxInspectLoad(false);
    }

    function docxAddFrRow() {
      const box = document.getElementById('docxFrPairs');
      const row = document.createElement('div');
      row.className = 'fr-pair-row';
      row.innerHTML = '<input class="field-input docx-find" placeholder="Find (e.g. Old Name / Roll)" style="font-size:12px;padding:6px 8px;">' +
        '<input class="field-input docx-repl" placeholder="Replace with..." style="font-size:12px;padding:6px 8px;">' +
        '<button type="button" class="mini-btn" onclick="this.parentElement.remove()">✕</button>';
      box.appendChild(row);
    }

    async function docxInspectLoad(useOriginal) {
      document.getElementById('docxPreviewStatus').textContent = 'Reading DOCX headers, text & images...';
      const res = await fetch('/api/docx/inspect', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileId: docxStudio.fileId, useOriginal: !!useOriginal })
      });
      const data = await res.json();
      if (!res.ok || !data.success) {
        alert('Could not open DOCX Studio: ' + (data.error || 'Error'));
        closeStudioModal('docxStudioModal');
        return;
      }

      docxStudio.headers = (data.headers || []).concat(data.footers || []);
      docxStudio.bodyBlocks = data.bodyBlocks || [];
      docxStudio.images = data.images || [];
      docxStudio.editedTexts = {};
      docxStudio.imageResizes = {};
      document.getElementById('docxStudioEngine').textContent = data.officeEngine || 'DOCX Ready';
      document.getElementById('docxHdrBadge').textContent = docxStudio.headers.length;
      document.getElementById('docxImgBadge').textContent = docxStudio.images.length;

      const hdrEl = document.getElementById('docxHeadersList');
      hdrEl.innerHTML = docxStudio.headers.length === 0
        ? '<div style="font-size:11.5px;color:var(--muted);">No headers/footers in this document. Check Body &amp; Cover Page Text below!</div>'
        : docxStudio.headers.map(b => {
            const k = b.partPath + '|' + b.paragraphIndex;
            return '<div class="docx-block-row">' +
              '<div style="font-size:10px;color:var(--muted);display:flex;justify-content:space-between;"><span>' + esc(b.partType.toUpperCase()) + (b.inTable ? ' • Table' : '') + '</span><span>#' + b.paragraphIndex + '</span></div>' +
              '<input class="field-input" style="font-size:12px;padding:6px 8px;" value="' + esc(b.text) + '" oninput="docxStudio.editedTexts[' + JSON.stringify(k).replace(/"/g, '&quot;') + ']=this.value">' +
              '</div>';
          }).join('');

      const imgEl = document.getElementById('docxImagesList');
      imgEl.innerHTML = docxStudio.images.length === 0
        ? '<div style="font-size:11.5px;color:var(--muted);">No embedded images found in this document.</div>'
        : docxStudio.images.map((im, idx) => {
            const k = im.partPath + '|' + im.drawingIndex;
            return '<div class="docx-block-row">' +
              '<div style="font-size:11.5px;font-weight:700;">' + esc(im.name) + ' <span style="font-weight:400;color:var(--muted);">(' + im.widthCm + ' × ' + im.heightCm + ' cm)</span></div>' +
              '<div style="display:flex;gap:6px;align-items:center;">' +
              '<span style="font-size:11px;color:var(--muted);">Scale:</span>' +
              '<input type="range" min="25" max="200" value="100" style="flex:1;" oninput="document.getElementById(\\'dimg_lbl_' + idx + '\\').textContent=this.value+\\'%\\';docxSetImgScale(' + JSON.stringify(k).replace(/"/g, '&quot;') + ',' + JSON.stringify(im.partPath).replace(/"/g, '&quot;') + ',' + im.drawingIndex + ',Number(this.value))">' +
              '<span id="dimg_lbl_' + idx + '" style="font-size:11px;font-weight:700;min-width:36px;">100%</span>' +
              '</div></div>';
          }).join('');

      renderDocxBodyList();

      if (data.convertedUrl) {
        docxStudio.lastPdfUrl = data.convertedUrl;
        document.getElementById('docxPreviewFrameWrap').innerHTML = '<iframe id="docxPdfIframe" src="' + data.convertedUrl + '?t=' + Date.now() + '" style="width:100%;height:100%;border:none;"></iframe>';
        document.getElementById('docxPreviewStatus').textContent = 'Showing saved PDF preview.';
      } else {
        document.getElementById('docxPreviewFrameWrap').innerHTML = '<div style="text-align:center;padding:30px;color:var(--muted);font-size:13px;">Make your edits on the left and click <strong>✨ Apply Changes &amp; Preview</strong> to render live PDF.</div>';
      }
    }

    function docxSetImgScale(key, partPath, drawingIndex, scalePercent) {
      docxStudio.imageResizes[key] = { partPath, drawingIndex, scalePercent };
    }

    function renderDocxBodyList() {
      const q = (document.getElementById('docxBodyFilter').value || '').toLowerCase();
      const box = document.getElementById('docxBodyList');
      const items = docxStudio.bodyBlocks.filter(b => !q || (b.text || '').toLowerCase().includes(q)).slice(0, 60);
      box.innerHTML = items.length === 0
        ? '<div style="font-size:11.5px;color:var(--muted);">No matching paragraphs.</div>'
        : items.map(b => {
            const k = b.partPath + '|' + b.paragraphIndex;
            const val = docxStudio.editedTexts[k] !== undefined ? docxStudio.editedTexts[k] : b.text;
            return '<div class="docx-block-row">' +
              '<div style="font-size:10px;color:var(--muted);">Paragraph #' + b.paragraphIndex + (b.inTable ? ' • Table Cell' : '') + '</div>' +
              '<input class="field-input" style="font-size:12px;padding:6px 8px;" value="' + esc(val) + '" oninput="docxStudio.editedTexts[' + JSON.stringify(k).replace(/"/g, '&quot;') + ']=this.value">' +
              '</div>';
          }).join('');
    }

    async function docxSaveAndPreview(andPrint) {
      const btn = document.getElementById('docxApplyBtn');
      if (btn) { btn.disabled = true; btn.textContent = 'Applying & Rendering PDF...'; }
      document.getElementById('docxPreviewStatus').textContent = 'Updating DOCX XML and rendering live PDF...';
      try {
        const findReplaces = [];
        const scope = document.getElementById('docxFrScope').value;
        const caseSensitive = document.getElementById('docxFrCase').checked;
        document.querySelectorAll('#docxFrPairs .fr-pair-row').forEach(r => {
          const f = (r.querySelector('.docx-find').value || '');
          const rep = (r.querySelector('.docx-repl').value || '');
          if (f) findReplaces.push({ find: f, replace: rep, caseSensitive, scope });
        });

        const paragraphEdits = Object.entries(docxStudio.editedTexts).map(([k, newText]) => {
          const idx = k.lastIndexOf('|');
          return { partPath: k.slice(0, idx), paragraphIndex: Number(k.slice(idx + 1)), newText };
        });
        const imageResizes = Object.values(docxStudio.imageResizes);

        const res = await fetch('/api/docx/edit', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            fileId: docxStudio.fileId,
            findReplaces,
            paragraphEdits,
            imageResizes,
            convertToPdf: true
          })
        });
        const data = await res.json();
        if (!res.ok || !data.success) throw new Error(data.error || 'DOCX edit failed');

        await fetchCloudState(true);
        const dl = document.getElementById('docxDownloadLink');
        if (data.editedDocxUrl) { dl.href = data.editedDocxUrl; dl.style.display = 'inline-flex'; }

        const summary = '✓ Saved (' + data.replacementsCount + ' find/replace, ' + data.paragraphEditsCount + ' text edit, ' + data.imagesResizedCount + ' image resize)';
        document.getElementById('docxPreviewStatus').textContent = summary + (data.pdfEngine ? ' • PDF via ' + data.pdfEngine : '');

        if (data.pdfUrl) {
          docxStudio.lastPdfUrl = data.pdfUrl;
          document.getElementById('docxPreviewFrameWrap').innerHTML = '<iframe id="docxPdfIframe" src="' + data.pdfUrl + '?t=' + Date.now() + '" style="width:100%;height:100%;border:none;"></iframe>';
          if (andPrint) directPrint(data.pdfUrl, data.pdfFilename || docxStudio.fileName);
        } else if (data.pdfError) {
          document.getElementById('docxPreviewFrameWrap').innerHTML = '<div style="padding:24px;text-align:center;color:var(--warning);font-size:13px;"><strong>DOCX edits saved!</strong><br>' + esc(data.pdfError) + '</div>';
          if (andPrint) alert('DOCX saved, but PDF conversion needs Microsoft Word or LibreOffice to direct-print.');
        }
      } catch (err) {
        alert('DOCX Studio error: ' + err.message);
      } finally {
        if (btn) { btn.disabled = false; btn.textContent = '✨ Apply Changes & Preview'; }
      }
    }

    // ------------------------------------------------------------------------
    // 3. IMAGE PRINT CANVAS STUDIO (A4/4x6 Drag, Resize, Rotate, Multi-Image)
    // ------------------------------------------------------------------------
    const PAPER_CM = {
      A4: { w: 21.0, h: 29.7 },
      Letter: { w: 21.59, h: 27.94 },
      A5: { w: 14.8, h: 21.0 },
      Photo4x6: { w: 10.16, h: 15.24 }
    };

    let icState = {
      primaryFileId: null,
      primaryFileName: '',
      submissionId: null,
      items: [], // { id, url, name, natW, natH, xCm, yCm, wCm, hCm, rot, bright, contrast, gray }
      selectedId: null,
      selectedIds: [],
      pxPerCm: 22,
      undoStack: [],
      redoStack: []
    };

    let icDraggingThumb = null;

    function icSetSelection(ids) {
      const valid = new Set((icState.items || []).map(i => i.id));
      const clean = [];
      (ids || []).forEach(id => { if (valid.has(id) && !clean.includes(id)) clean.push(id); });
      icState.selectedIds = clean;
      icState.selectedId = clean.length > 0 ? clean[clean.length - 1] : null;
    }

    function icGetSelectedItems() {
      const ids = (icState.selectedIds && icState.selectedIds.length)
        ? icState.selectedIds
        : (icState.selectedId ? [icState.selectedId] : []);
      const set = new Set(ids);
      return (icState.items || []).filter(i => set.has(i.id));
    }

    function icSnapshot() { return JSON.stringify({ items: icState.items, selectedId: icState.selectedId, selectedIds: icState.selectedIds }); }
    function icPushUndo() {
      icState.undoStack.push(icSnapshot());
      if (icState.undoStack.length > 35) icState.undoStack.shift();
      icState.redoStack = [];
      icUpdateUndoButtons();
    }
    function icUpdateUndoButtons() {
      const u = document.getElementById('icUndoBtn');
      const r = document.getElementById('icRedoBtn');
      if (u) { u.disabled = icState.undoStack.length === 0; u.textContent = icState.undoStack.length > 0 ? ('↩ Undo (' + icState.undoStack.length + ')') : '↩ Undo'; }
      if (r) { r.disabled = icState.redoStack.length === 0; }
    }
    function icUndo() {
      if (icState.undoStack.length === 0) return;
      icState.redoStack.push(icSnapshot());
      const prev = JSON.parse(icState.undoStack.pop());
      icState.items = prev.items || [];
      icSetSelection(prev.selectedIds || (prev.selectedId ? [prev.selectedId] : []));
      icUpdateUndoButtons();
      imgCanvasRenderPaper();
    }
    function icRedo() {
      if (icState.redoStack.length === 0) return;
      icState.undoStack.push(icSnapshot());
      const next = JSON.parse(icState.redoStack.pop());
      icState.items = next.items || [];
      icSetSelection(next.selectedIds || (next.selectedId ? [next.selectedId] : []));
      icUpdateUndoButtons();
      imgCanvasRenderPaper();
    }

    function icGetPaperCm() {
      const sz = PAPER_CM[document.getElementById('icPaperSize').value] || PAPER_CM.A4;
      const land = document.getElementById('icOrientation').value === 'landscape';
      return land ? { w: sz.h, h: sz.w } : { w: sz.w, h: sz.h };
    }

    function icSetPickerScope(scope) {
      icState.pickerScope = scope === 'all' ? 'all' : 'job';
      const jb = document.getElementById('icScopeJobBtn');
      const ab = document.getElementById('icScopeAllBtn');
      if (jb) jb.classList.toggle('active', icState.pickerScope === 'job');
      if (ab) ab.classList.toggle('active', icState.pickerScope === 'all');
      renderIcSubmissionThumbs();
    }

    function renderIcSubmissionThumbs() {
      const grid = document.getElementById('icSubThumbGrid');
      if (!grid) return;
      const imgExts = ['.jpg', '.jpeg', '.png', '.webp', '.bmp', '.gif'];
      const subs = (cloudState && cloudState.submissions) || [];
      const curSub = subs.find(s => s.id === icState.submissionId || s.receiptCode === icState.submissionId);
      const jobImgs = curSub ? (curSub.files || []).filter(f => imgExts.includes((f.fileExtension || '').toLowerCase())).map(f => ({ f, tag: curSub.receiptCode || curSub.id })) : [];
      const allImgs = [];
      subs.forEach(s => {
        (s.files || []).filter(f => imgExts.includes((f.fileExtension || '').toLowerCase())).forEach(f => {
          allImgs.push({ f, tag: s.receiptCode || s.id });
        });
      });

      const jb = document.getElementById('icScopeJobBtn');
      const ab = document.getElementById('icScopeAllBtn');
      if (jb) jb.textContent = 'This Job (' + jobImgs.length + ')';
      if (ab) ab.textContent = 'All Inbox (' + allImgs.length + ')';

      const list = icState.pickerScope === 'all' ? allImgs : (jobImgs.length > 0 ? jobImgs : allImgs);
      if (list.length === 0) {
        grid.innerHTML = '<div class="ic-thumb-empty">No submission images yet — click <strong>⬆ Upload</strong> above to pick from PC.</div>';
        return;
      }

      grid.innerHTML = '';
      list.forEach(({ f, tag }) => {
        const url = '/api/file?id=' + encodeURIComponent(f.id);
        const countOnSheet = (icState.items || []).filter(it => it.url === url).length;
        const tile = document.createElement('div');
        tile.className = 'ic-thumb-tile' + (countOnSheet > 0 ? ' on-sheet' : '');
        tile.draggable = true;
        tile.title = f.originalName + (icState.pickerScope === 'all' ? (' (' + tag + ')') : '') + ' — Click or drag onto sheet';
        tile.innerHTML =
          '<div class="ic-thumb-img-wrap">' +
            '<img src="' + url + '" loading="lazy" alt="' + esc(f.originalName) + '">' +
            '<span class="ic-thumb-badge">' + (countOnSheet > 0 ? ('✓ ×' + countOnSheet) : '+') + '</span>' +
          '</div>' +
          '<div class="ic-thumb-name">' + esc(f.originalName) + '</div>';
        tile.ondragstart = (ev) => {
          icDraggingThumb = { url, name: f.originalName };
          try { ev.dataTransfer.setData('text/plain', f.originalName); ev.dataTransfer.effectAllowed = 'copy'; } catch {}
        };
        tile.ondragend = () => {
          icDraggingThumb = null;
          const pEl = document.getElementById('icPaperEl');
          if (pEl) pEl.classList.remove('drop-target-active');
        };
        tile.onclick = () => {
          icPushUndo();
          icAddImageUrl(url, f.originalName);
        };
        grid.appendChild(tile);
      });
    }

    function icPaperDragOver(ev) {
      ev.preventDefault();
      if (ev.dataTransfer) ev.dataTransfer.dropEffect = 'copy';
      const pEl = document.getElementById('icPaperEl');
      if (pEl) pEl.classList.add('drop-target-active');
    }

    function icPaperDragLeave(ev) {
      const pEl = document.getElementById('icPaperEl');
      if (pEl && !pEl.contains(ev.relatedTarget)) pEl.classList.remove('drop-target-active');
    }

    async function icPaperDrop(ev) {
      ev.preventDefault();
      const pEl = document.getElementById('icPaperEl');
      if (pEl) pEl.classList.remove('drop-target-active');
      const rect = pEl.getBoundingClientRect();
      const dropXCm = (ev.clientX - rect.left) / Math.max(1, icState.pxPerCm);
      const dropYCm = (ev.clientY - rect.top) / Math.max(1, icState.pxPerCm);

      if (icDraggingThumb) {
        const t = icDraggingThumb;
        icDraggingThumb = null;
        icPushUndo();
        await icAddImageUrl(t.url, t.name, { xCm: dropXCm, yCm: dropYCm });
        return;
      }

      if (ev.dataTransfer && ev.dataTransfer.files && ev.dataTransfer.files.length > 0) {
        const imgs = Array.from(ev.dataTransfer.files).filter(f => (f.type || '').startsWith('image/') || /\\.(jpg|jpeg|png|webp|bmp|gif)$/i.test(f.name || ''));
        if (imgs.length > 0) {
          icPushUndo();
          for (let i = 0; i < imgs.length; i++) {
            await icAddImageUrl(URL.createObjectURL(imgs[i]), imgs[i].name, { xCm: dropXCm + i * 0.8, yCm: dropYCm + i * 0.8 });
          }
        }
      }
    }

    function icPaperMouseDown(ev) {
      if (ev.button !== 0) return;
      // Only start marquee when clicking empty paper (not an image item or toolbar button)
      if (ev.target.closest('.canvas-item') || ev.target.closest('.ic-canva-bar')) return;
      const pEl = document.getElementById('icPaperEl');
      const mq = document.getElementById('icMarqueeBox');
      if (!pEl || !mq) return;
      const rect = pEl.getBoundingClientRect();
      const startX = ev.clientX - rect.left;
      const startY = ev.clientY - rect.top;
      let dragged = false;

      const onMove = (mv) => {
        const curX = Math.max(0, Math.min(rect.width, mv.clientX - rect.left));
        const curY = Math.max(0, Math.min(rect.height, mv.clientY - rect.top));
        const rx = Math.min(startX, curX);
        const ry = Math.min(startY, curY);
        const rw = Math.abs(curX - startX);
        const rh = Math.abs(curY - startY);
        if (rw > 4 || rh > 4) dragged = true;
        if (!dragged) return;
        mq.style.display = 'block';
        mq.style.left = rx + 'px';
        mq.style.top = ry + 'px';
        mq.style.width = rw + 'px';
        mq.style.height = rh + 'px';

        // Live-highlight intersecting items
        const x0Cm = rx / icState.pxPerCm;
        const y0Cm = ry / icState.pxPerCm;
        const x1Cm = (rx + rw) / icState.pxPerCm;
        const y1Cm = (ry + rh) / icState.pxPerCm;
        const hits = [];
        icState.items.forEach(it => {
          const overlaps = it.xCm < x1Cm && (it.xCm + it.wCm) > x0Cm && it.yCm < y1Cm && (it.yCm + it.hCm) > y0Cm;
          if (overlaps) hits.push(it.id);
        });
        icSetSelection(hits);
        icRefreshSelectionStyles();
      };

      const onUp = () => {
        window.removeEventListener('mousemove', onMove);
        window.removeEventListener('mouseup', onUp);
        mq.style.display = 'none';
        if (!dragged && !ev.shiftKey) {
          icSetSelection([]);
        }
        icRefreshSelectionStyles();
      };
      window.addEventListener('mousemove', onMove);
      window.addEventListener('mouseup', onUp);
    }

    function icRefreshSelectionStyles() {
      const selSet = new Set(icState.selectedIds || []);
      const isSingle = selSet.size === 1;
      document.querySelectorAll('#icItemsLayer .canvas-item').forEach(node => {
        const id = node.dataset.itemId;
        const isSel = selSet.has(id);
        node.classList.toggle('selected', isSel);
        node.classList.toggle('single-selected', isSel && isSingle);
      });
      renderIcSelectionUi();
      icSyncControls();
    }

    function renderIcSelectionUi() {
      const ui = document.getElementById('icSelectionUiLayer');
      if (!ui) return;
      ui.innerHTML = '';
      const selItems = icGetSelectedItems();
      if (selItems.length === 0) return;

      const paper = icGetPaperCm();
      const paperWpx = paper.w * icState.pxPerCm;
      const paperHpx = paper.h * icState.pxPerCm;

      let minX = 1e9, minY = 1e9, maxX = -1e9, maxY = -1e9;
      selItems.forEach(it => {
        const x0 = it.xCm * icState.pxPerCm;
        const y0 = it.yCm * icState.pxPerCm;
        const x1 = (it.xCm + it.wCm) * icState.pxPerCm;
        const y1 = (it.yCm + it.hCm) * icState.pxPerCm;
        if (x0 < minX) minX = x0;
        if (y0 < minY) minY = y0;
        if (x1 > maxX) maxX = x1;
        if (y1 > maxY) maxY = y1;
      });

      if (selItems.length > 1) {
        const grp = document.createElement('div');
        grp.className = 'ic-group-outline';
        grp.style.left = (minX - 4) + 'px';
        grp.style.top = (minY - 4) + 'px';
        grp.style.width = (maxX - minX + 8) + 'px';
        grp.style.height = (maxY - minY + 8) + 'px';
        ui.appendChild(grp);
      }

      const bar = document.createElement('div');
      bar.className = 'ic-canva-bar';
      const cx = Math.max(85, Math.min(paperWpx - 85, (minX + maxX) / 2));
      const topPos = minY >= 44 ? (minY - 40) : Math.min(paperHpx + 6, maxY + 10);
      bar.style.left = cx + 'px';
      bar.style.top = topPos + 'px';
      bar.onmousedown = (e) => e.stopPropagation();

      bar.innerHTML =
        (selItems.length > 1 ? '<span class="ic-canva-pill-count">' + selItems.length + ' selected</span>' : '') +
        '<button class="ic-canva-btn" title="Duplicate (Ctrl+D)" onclick="icDuplicateSelected()">⧉</button>' +
        '<button class="ic-canva-btn" title="Rotate Left -90°" onclick="icRotateStep(-90)">↺</button>' +
        '<button class="ic-canva-btn" title="Rotate Right +90°" onclick="icRotateStep(90)">↻</button>' +
        '<button class="ic-canva-btn danger" title="Delete (Del)" onclick="icDeleteSelected()">🗑</button>';
      ui.appendChild(bar);
    }

    async function openImageCanvas(fileId, fileName, submissionId) {
      icState.primaryFileId = fileId;
      icState.primaryFileName = fileName;
      icState.submissionId = submissionId;
      icState.pickerScope = 'job';
      icState.items = [];
      icState.selectedId = null;
      icState.selectedIds = [];
      icState.undoStack = [];
      icState.redoStack = [];
      icUpdateUndoButtons();

      const jb = document.getElementById('icScopeJobBtn');
      const ab = document.getElementById('icScopeAllBtn');
      if (jb) jb.classList.add('active');
      if (ab) ab.classList.remove('active');

      document.getElementById('imageStudioModal').classList.add('open');
      imgCanvasRenderPaper();
      await icAddImageUrl('/api/file?id=' + encodeURIComponent(fileId), fileName);
      icPreset('fit');
      renderIcSubmissionThumbs();
    }

    async function icAddLocalFile(input) {
      if (!input.files || !input.files.length) return;
      icPushUndo();
      const files = Array.from(input.files);
      for (const f of files) {
        await icAddImageUrl(URL.createObjectURL(f), f.name);
      }
      input.value = '';
    }

    async function icAddImageUrl(url, name, dropPosCm) {
      const img = new Image();
      img.crossOrigin = 'anonymous';
      await new Promise((res, rej) => { img.onload = res; img.onerror = rej; img.src = url; });
      const paper = icGetPaperCm();
      const aspect = img.naturalHeight / Math.max(1, img.naturalWidth);
      let wCm = Math.min(paper.w - 2.4, dropPosCm ? 9.5 : 14);
      let hCm = wCm * aspect;
      if (hCm > paper.h - 2.4) { hCm = paper.h - 2.4; wCm = hCm / aspect; }
      const xCm = dropPosCm
        ? Math.max(0.2, Math.min(paper.w - wCm * 0.5, dropPosCm.xCm - wCm / 2))
        : Math.max(0.8, (paper.w - wCm) / 2);
      const yCm = dropPosCm
        ? Math.max(0.2, Math.min(paper.h - hCm * 0.5, dropPosCm.yCm - hCm / 2))
        : Math.max(0.8, (paper.h - hCm) / 2);
      const item = {
        id: 'ic_' + Date.now() + '_' + Math.random().toString(36).slice(2, 5),
        url, name: name || 'Image',
        natW: img.naturalWidth, natH: img.naturalHeight,
        xCm, yCm,
        wCm, hCm, rot: 0, bright: 100, contrast: 100, gray: false
      };
      icState.items.push(item);
      icSetSelection([item.id]);
      imgCanvasRenderPaper();
      renderIcSubmissionThumbs();
    }

    function imgCanvasRenderPaper() {
      const paper = icGetPaperCm();
      const maxH = Math.max(460, window.innerHeight * 0.72);
      const maxW = Math.max(460, window.innerWidth - 460);
      icState.pxPerCm = Math.min(maxW / paper.w, maxH / paper.h);

      const paperEl = document.getElementById('icPaperEl');
      paperEl.style.width = Math.round(paper.w * icState.pxPerCm) + 'px';
      paperEl.style.height = Math.round(paper.h * icState.pxPerCm) + 'px';
      const selCount = (icState.selectedIds || []).length;
      document.getElementById('imgCanvasStatus').textContent =
        document.getElementById('icPaperSize').value + ' ' + document.getElementById('icOrientation').value + ' • ' + icState.items.length + ' image(s)' + (selCount > 1 ? (' • ' + selCount + ' selected') : '');

      const selSet = new Set(icState.selectedIds || []);
      const isSingle = selSet.size === 1;
      const layer = document.getElementById('icItemsLayer');
      layer.innerHTML = '';
      icState.items.forEach(item => {
        const isSel = selSet.has(item.id);
        const div = document.createElement('div');
        div.dataset.itemId = item.id;
        div.className = 'canvas-item' + (isSel ? ' selected' : '') + (isSel && isSingle ? ' single-selected' : '');
        div.style.left = (item.xCm * icState.pxPerCm) + 'px';
        div.style.top = (item.yCm * icState.pxPerCm) + 'px';
        div.style.width = (item.wCm * icState.pxPerCm) + 'px';
        div.style.height = (item.hCm * icState.pxPerCm) + 'px';
        div.style.transform = 'rotate(' + item.rot + 'deg)';
        const filter = 'brightness(' + item.bright + '%) contrast(' + item.contrast + '%)' + (item.gray ? ' grayscale(100%)' : '');
        div.innerHTML = '<img src="' + item.url + '" style="filter:' + filter + ';">' +
          '<div class="canvas-dim-tag">' + item.wCm.toFixed(1) + ' × ' + item.hCm.toFixed(1) + ' cm' + (item.rot ? (' • ' + item.rot + '°') : '') + '</div>' +
          '<div class="ic-rotate-handle" title="Drag to rotate">⟳</div>' +
          '<div class="canvas-handle" title="Drag to resize"></div>';

        div.onmousedown = (ev) => {
          ev.stopPropagation();
          if (ev.button !== 0) return;

          const isResize = ev.target.classList.contains('canvas-handle');
          const isRotate = ev.target.classList.contains('ic-rotate-handle');

          const liveSelSet = new Set(icState.selectedIds || []);
          if (ev.shiftKey && !isResize && !isRotate) {
            if (liveSelSet.has(item.id)) liveSelSet.delete(item.id); else liveSelSet.add(item.id);
            icSetSelection(Array.from(liveSelSet));
            icRefreshSelectionStyles();
            return;
          }

          if (!liveSelSet.has(item.id)) {
            icSetSelection([item.id]);
            icRefreshSelectionStyles();
          }

          const sx = ev.clientX, sy = ev.clientY;
          const ox = item.xCm, oy = item.yCm, ow = item.wCm, oh = item.hCm;
          const aspect = oh / Math.max(0.1, ow);

          // Snapshot starting positions & dimensions for ALL selected items (multi-select move, resize & rotate!)
          const movingItems = icGetSelectedItems();
          const startPos = movingItems.map(mi => ({
            item: mi,
            ox: mi.xCm,
            oy: mi.yCm,
            ow: mi.wCm,
            oh: mi.hCm,
            aspect: mi.hCm / Math.max(0.1, mi.wCm),
            origRot: mi.rot || 0,
            el: layer.querySelector('[data-item-id="' + mi.id + '"]')
          }));

          // Center of image in screen coordinates for smooth Canva drag-to-rotate
          const itemRect = div.getBoundingClientRect();
          const cxScr = itemRect.left + itemRect.width / 2;
          const cyScr = itemRect.top + itemRect.height / 2;
          const startAngleRad = Math.atan2(sy - cyScr, sx - cxScr);
          const origRot = item.rot || 0;

          let movedOnce = false;
          const onMove = (mv) => {
            if (!movedOnce) { icPushUndo(); movedOnce = true; }
            if (isRotate) {
              const curRad = Math.atan2(mv.clientY - cyScr, mv.clientX - cxScr);
              const deltaDeg = Math.round((curRad - startAngleRad) * (180 / Math.PI));
              startPos.forEach(sp => {
                let deg = sp.origRot + deltaDeg;
                while (deg > 180) deg -= 360;
                while (deg < -180) deg += 360;
                if (!mv.shiftKey) {
                  for (const snap of [-180, -90, 0, 90, 180]) {
                    if (Math.abs(deg - snap) <= 5) { deg = snap; break; }
                  }
                }
                sp.item.rot = deg;
                if (sp.el) {
                  sp.el.style.transform = 'rotate(' + sp.item.rot + 'deg)';
                  const tag = sp.el.querySelector('.canvas-dim-tag');
                  if (tag) tag.textContent = sp.item.wCm.toFixed(1) + ' × ' + sp.item.hCm.toFixed(1) + ' cm' + (sp.item.rot ? (' • ' + sp.item.rot + '°') : '');
                }
              });
            } else if (isResize) {
              const dxCm = (mv.clientX - sx) / icState.pxPerCm;
              const dyCm = (mv.clientY - sy) / icState.pxPerCm;
              const targetW = Math.max(1.2, Math.min(paper.w, ow + dxCm));
              const targetH = mv.shiftKey ? Math.max(1.2, Math.min(paper.h, oh + dyCm)) : (targetW * aspect);
              const scaleX = targetW / Math.max(0.1, ow);
              const scaleY = targetH / Math.max(0.1, oh);

              startPos.forEach(sp => {
                sp.item.wCm = Math.max(1.2, Math.min(paper.w, sp.ow * scaleX));
                sp.item.hCm = mv.shiftKey ? Math.max(1.2, Math.min(paper.h, sp.oh * scaleY)) : (sp.item.wCm * sp.aspect);
                if (sp.el) {
                  sp.el.style.width = (sp.item.wCm * icState.pxPerCm) + 'px';
                  sp.el.style.height = (sp.item.hCm * icState.pxPerCm) + 'px';
                  const tag = sp.el.querySelector('.canvas-dim-tag');
                  if (tag) tag.textContent = sp.item.wCm.toFixed(1) + ' × ' + sp.item.hCm.toFixed(1) + ' cm' + (sp.item.rot ? (' • ' + sp.item.rot + '°') : '');
                }
              });
            } else {
              const dxCm = (mv.clientX - sx) / icState.pxPerCm;
              const dyCm = (mv.clientY - sy) / icState.pxPerCm;
              startPos.forEach(sp => {
                sp.item.xCm = Math.max(-sp.item.wCm * 0.4, Math.min(paper.w - sp.item.wCm * 0.4, sp.ox + dxCm));
                sp.item.yCm = Math.max(-sp.item.hCm * 0.4, Math.min(paper.h - sp.item.hCm * 0.4, sp.oy + dyCm));
                if (sp.el) {
                  sp.el.style.left = (sp.item.xCm * icState.pxPerCm) + 'px';
                  sp.el.style.top = (sp.item.yCm * icState.pxPerCm) + 'px';
                }
              });
            }
            renderIcSelectionUi();
            icSyncControls();
          };
          const onUp = () => { window.removeEventListener('mousemove', onMove); window.removeEventListener('mouseup', onUp); };
          window.addEventListener('mousemove', onMove);
          window.addEventListener('mouseup', onUp);
        };
        layer.appendChild(div);
      });
      renderIcSelectionUi();
      icSyncControls();
      renderIcSubmissionThumbs();
    }

    function icGetSelected() { return icState.items.find(i => i.id === icState.selectedId) || icState.items[0] || null; }

    function icSyncControls() {
      const it = icGetSelected();
      if (!it) return;
      document.getElementById('icWidthCm').value = it.wCm.toFixed(1);
      document.getElementById('icHeightCm').value = it.hCm.toFixed(1);
      document.getElementById('icRotSlider').value = it.rot;
      document.getElementById('icRotVal').textContent = it.rot + '°';
      document.getElementById('icBright').value = it.bright;
      document.getElementById('icContrast').value = it.contrast;
      document.getElementById('icGrayscale').checked = !!it.gray;
    }

    function icApplyDimInput(axis) {
      const targets = icGetSelectedItems();
      if (!targets.length) return;
      icPushUndo();
      const val = Math.max(1, Number(document.getElementById(axis === 'w' ? 'icWidthCm' : 'icHeightCm').value || 1));
      targets.forEach(it => {
        const ratio = it.natH / Math.max(1, it.natW);
        if (axis === 'w') {
          it.wCm = val;
          it.hCm = it.wCm * ratio;
        } else {
          it.hCm = val;
          it.wCm = it.hCm / ratio;
        }
      });
      imgCanvasRenderPaper();
    }

    function icSetRotation(deg) {
      const targets = icGetSelectedItems();
      if (!targets.length) return;
      icPushUndo();
      targets.forEach(it => { it.rot = deg; });
      imgCanvasRenderPaper();
    }
    function icRotateStep(delta) {
      const targets = icGetSelectedItems();
      if (!targets.length) return;
      icPushUndo();
      targets.forEach(it => { it.rot = ((it.rot + delta + 540) % 360) - 180; });
      imgCanvasRenderPaper();
    }
    function icSetFilter() {
      const targets = icGetSelectedItems();
      if (!targets.length) return;
      icPushUndo();
      const br = Number(document.getElementById('icBright').value || 100);
      const co = Number(document.getElementById('icContrast').value || 100);
      const gr = document.getElementById('icGrayscale').checked;
      targets.forEach(it => { it.bright = br; it.contrast = co; it.gray = gr; });
      imgCanvasRenderPaper();
    }
    function icCenterSelected() {
      const targets = icGetSelectedItems();
      if (!targets.length) return;
      icPushUndo();
      const paper = icGetPaperCm();
      targets.forEach(it => {
        it.xCm = (paper.w - it.wCm) / 2;
        it.yCm = (paper.h - it.hCm) / 2;
      });
      imgCanvasRenderPaper();
    }
    function icDuplicateSelected() {
      const targets = icGetSelectedItems();
      if (!targets.length) return;
      icPushUndo();
      const newIds = [];
      targets.forEach((it, idx) => {
        const copy = {
          ...it,
          id: 'ic_' + Date.now() + '_' + idx + '_' + Math.random().toString(36).slice(2, 5),
          xCm: it.xCm + 1.0,
          yCm: it.yCm + 1.0
        };
        icState.items.push(copy);
        newIds.push(copy.id);
      });
      icSetSelection(newIds);
      imgCanvasRenderPaper();
    }
    function icDeleteSelected() {
      const targets = icGetSelectedItems();
      if (!targets.length) return;
      icPushUndo();
      const delSet = new Set(targets.map(t => t.id));
      icState.items = icState.items.filter(i => !delSet.has(i.id));
      icSetSelection(icState.items[0] ? [icState.items[0].id] : []);
      imgCanvasRenderPaper();
    }

    // Keyboard shortcuts when Image Print Canvas modal is open (Delete, Ctrl+D)
    window.addEventListener('keydown', (e) => {
      const modal = document.getElementById('imageStudioModal');
      if (!modal || !modal.classList.contains('open')) return;
      if (e.target && ['INPUT', 'TEXTAREA', 'SELECT'].includes(e.target.tagName)) return;
      if (e.key === 'Delete' || e.key === 'Backspace') {
        if (icGetSelectedItems().length > 0) { e.preventDefault(); icDeleteSelected(); }
      } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'd') {
        if (icGetSelectedItems().length > 0) { e.preventDefault(); icDuplicateSelected(); }
      }
    });

    function icPreset(kind) {
      const it = icGetSelected(); if (!it) return;
      if (icState.items.length > 0 && icState.undoStack.length > 0 || kind !== 'fit') icPushUndo();
      const paper = icGetPaperCm();
      const aspect = it.natH / Math.max(1, it.natW);
      if (kind === 'fit') {
        let w = paper.w - 1.6, h = w * aspect;
        if (h > paper.h - 1.6) { h = paper.h - 1.6; w = h / aspect; }
        it.wCm = w; it.hCm = h; it.xCm = (paper.w - w) / 2; it.yCm = (paper.h - h) / 2; it.rot = 0;
      } else if (kind === 'half') {
        let w = paper.w - 2.0, h = w * aspect;
        if (h > (paper.h / 2) - 1.5) { h = (paper.h / 2) - 1.5; w = h / aspect; }
        it.wCm = w; it.hCm = h; it.xCm = (paper.w - w) / 2; it.yCm = 1.0;
      } else if (kind === 'idcard') {
        it.wCm = 8.56; it.hCm = 5.4; it.xCm = (paper.w - it.wCm) / 2; it.yCm = 1.6; it.rot = 0;
      } else if (kind === '2up') {
        let w = paper.w - 2.4, h = w * aspect;
        if (h > (paper.h / 2) - 1.6) { h = (paper.h / 2) - 1.6; w = h / aspect; }
        it.wCm = w; it.hCm = h; it.xCm = (paper.w - w) / 2; it.yCm = 1.0; it.rot = 0;
        const copy = { ...it, id: 'ic_' + Date.now() + '_2', yCm: (paper.h / 2) + 0.5 };
        icState.items = [it, copy];
        icState.selectedId = it.id;
      } else if (kind === 'passport8') {
        const pw = 3.5, ph = 4.5, gap = 0.6;
        const cols = 4, rows = 2;
        const startX = (paper.w - (cols * pw + (cols - 1) * gap)) / 2;
        const startY = 1.5;
        const grid = [];
        for (let r = 0; r < rows; r++) {
          for (let c = 0; c < cols; c++) {
            grid.push({
              ...it,
              id: 'ic_pp_' + r + '_' + c + '_' + Date.now(),
              wCm: pw, hCm: ph, rot: 0,
              xCm: startX + c * (pw + gap),
              yCm: startY + r * (ph + gap)
            });
          }
        }
        icState.items = grid;
        icState.selectedId = grid[0].id;
      }
      imgCanvasRenderPaper();
    }

    async function imgCanvasExport(andPrint) {
      if (icState.items.length === 0) { alert('Add at least one image to the canvas.'); return; }
      const paper = icGetPaperCm();
      const dpi = 200; // crisp high-DPI print render
      const pxPerCmExport = dpi / 2.54;
      const outCanvas = document.createElement('canvas');
      outCanvas.width = Math.round(paper.w * pxPerCmExport);
      outCanvas.height = Math.round(paper.h * pxPerCmExport);
      const ctx = outCanvas.getContext('2d');
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(0, 0, outCanvas.width, outCanvas.height);

      for (const it of icState.items) {
        const img = new Image();
        img.crossOrigin = 'anonymous';
        await new Promise((res, rej) => { img.onload = res; img.onerror = rej; img.src = it.url; });
        const cx = (it.xCm + it.wCm / 2) * pxPerCmExport;
        const cy = (it.yCm + it.hCm / 2) * pxPerCmExport;
        const w = it.wCm * pxPerCmExport;
        const h = it.hCm * pxPerCmExport;
        ctx.save();
        ctx.translate(cx, cy);
        ctx.rotate((it.rot * Math.PI) / 180);
        ctx.filter = 'brightness(' + it.bright + '%) contrast(' + it.contrast + '%)' + (it.gray ? ' grayscale(100%)' : '');
        ctx.drawImage(img, -w / 2, -h / 2, w, h);
        ctx.restore();
      }

      const jpegUrl = outCanvas.toDataURL('image/jpeg', 0.93);
      const jpegBytes = Uint8Array.from(atob(jpegUrl.split(',')[1]), c => c.charCodeAt(0));
      const doc = await window.PDFLib.PDFDocument.create();
      const ptPerCm = 72 / 2.54;
      const page = doc.addPage([paper.w * ptPerCm, paper.h * ptPerCm]);
      const embedded = await doc.embedJpg(jpegBytes);
      page.drawImage(embedded, { x: 0, y: 0, width: paper.w * ptPerCm, height: paper.h * ptPerCm });
      const pdfBytes = await doc.save();

      const base = (icState.primaryFileName || 'canvas').replace(/\\.[^.]+$/, '') + '_canvas_' + Date.now().toString().slice(-4) + '.pdf';
      // 1. Save a copy in File Manager (Converted Outputs)
      const res = await fetch('/api/file/save-edited-pdf', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/pdf',
          'X-Output-Name': base
        },
        body: pdfBytes
      });
      const data = await res.json();
      if (!res.ok || !data.success) { alert('Save failed: ' + (data.error || 'Error')); return; }

      // 2. Also add the generated PDF directly into the currently opened submission!
      const targetSubId = icState.submissionId || selectedSubId;
      let addedFileId = null;
      if (targetSubId) {
        try {
          const addRes = await fetch('/api/submission/add-file', {
            method: 'POST',
            headers: {
              'Content-Type': 'application/pdf',
              'X-Submission-Id': targetSubId,
              'X-File-Name': encodeURIComponent(data.filename || base)
            },
            body: pdfBytes
          });
          const addData = await addRes.json();
          if (addRes.ok && addData.success && addData.file) {
            addedFileId = addData.file.id;
            pushGlobalUndo('Add canvas PDF "' + (data.filename || base) + '"', () => trashFile(addedFileId, data.filename || base, true));
          }
        } catch {}
      }

      await fetchCloudState(true);
      if (andPrint) {
        directPrint(data.url, data.filename);
        showToast('🖨 Printing & added "' + data.filename + '"' + (targetSubId ? (' to ' + targetSubId) : ''), null, null, 5500);
      } else {
        closeStudioModal('imageStudioModal');
        showToast(
          '✓ Added "' + data.filename + '"' + (targetSubId ? (' to ' + targetSubId + ' & File Manager') : ' to File Manager'),
          addedFileId ? '↩ Undo' : null,
          addedFileId ? (() => trashFile(addedFileId, data.filename, true)) : null,
          7000
        );
      }
    }

    // ------------------------------------------------------------------------
    // 4. MERGE & PRINT ALL QUEUE (Mixed Edited/Original DOCX + PDF + Images + Per-File Pages)
    // ------------------------------------------------------------------------
    let mergeQueue = { submissionId: null, receiptCode: '', items: [], undoStack: [] };

    function mergePushUndo() {
      mergeQueue.undoStack.push(JSON.stringify(mergeQueue.items.map(i => ({ fileId: i.file.id, checked: i.checked, pageRange: i.pageRange || '' }))));
      if (mergeQueue.undoStack.length > 25) mergeQueue.undoStack.shift();
      mergeUpdateUndoBtn();
    }
    function mergeUpdateUndoBtn() {
      const b = document.getElementById('mergeUndoBtn');
      if (b) {
        b.disabled = mergeQueue.undoStack.length === 0;
        b.textContent = mergeQueue.undoStack.length > 0 ? ('↩ Undo (' + mergeQueue.undoStack.length + ')') : '↩ Undo';
      }
    }
    function mergeUndo() {
      if (mergeQueue.undoStack.length === 0) return;
      const snap = JSON.parse(mergeQueue.undoStack.pop());
      const sub = (cloudState.submissions || []).find(s => s.id === mergeQueue.submissionId || s.receiptCode === mergeQueue.submissionId);
      const map = new Map(((sub && sub.files) || []).map(f => [f.id, f]));
      mergeQueue.items = snap.map(x => map.get(x.fileId) ? { file: map.get(x.fileId), checked: x.checked, pageRange: x.pageRange } : null).filter(Boolean);
      mergeUpdateUndoBtn();
      renderMergeQueue();
    }

    function openMergeModal(submissionId) {
      const sub = (cloudState.submissions || []).find(s => s.id === submissionId || s.receiptCode === submissionId);
      if (!sub) return;
      const printableExts = ['.pdf', '.docx', '.doc', '.rtf', '.txt', '.jpg', '.jpeg', '.png', '.webp', '.bmp', '.gif'];
      const prevRanges = new Map((mergeQueue.submissionId === sub.id ? mergeQueue.items : []).map(i => [i.file.id, i.pageRange || '']));
      mergeQueue.submissionId = sub.id;
      mergeQueue.receiptCode = sub.receiptCode || sub.id;
      mergeQueue.undoStack = [];
      mergeQueue.items = (sub.files || [])
        .filter(f => printableExts.includes((f.fileExtension || '').toLowerCase()))
        .map(f => ({ file: f, checked: true, pageRange: prevRanges.get(f.id) || '' }));

      document.getElementById('mergeSubTitle').textContent =
        (sub.receiptCode || sub.id) + ' • ' + (sub.customerName || 'Customer') + ' • ' + mergeQueue.items.length + ' printable file(s)';
      document.getElementById('mergeProgressMsg').style.display = 'none';
      mergeUpdateUndoBtn();
      renderMergeQueue();
      document.getElementById('mergePrintModal').classList.add('open');
    }

    function renderMergeQueue() {
      const box = document.getElementById('mergeQueueList');
      if (mergeQueue.items.length === 0) {
        box.innerHTML = '<div style="padding:24px;text-align:center;color:var(--muted);">No printable PDF, Word, or Image files in this submission.</div>';
        return;
      }
      box.innerHTML = mergeQueue.items.map((entry, idx) => {
        const f = entry.file;
        const fExt = (f.fileExtension || '').toLowerCase();
        const isEdited = f.conversionStatus === 'edited' && f.convertedUrl;
        const canPickPages = ['.pdf', '.docx', '.doc'].includes(fExt);
        const [cls, lbl] = getFileBadge(f.fileExtension);
        return '<div class="merge-row">' +
          '<input type="checkbox" ' + (entry.checked ? 'checked' : '') + ' onchange="mergePushUndo();mergeQueue.items[' + idx + '].checked=this.checked" style="width:16px;height:16px;cursor:pointer;">' +
          '<div class="file-type-box ' + cls + '">' + lbl + '</div>' +
          '<div style="flex:1;min-width:0;">' +
            '<div style="font-size:13px;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">' + esc(f.originalName) + '</div>' +
            '<div style="font-size:11px;color:var(--muted);">' + formatSize(f.sizeBytes) + ' • ' +
              (isEdited ? (formatFileEditBadge(f) || '<span style="color:var(--success);font-weight:700;">✓ Using Saved Pages / Edited PDF</span>') : 'Original (auto-converted if needed)') +
            '</div>' +
          '</div>' +
          (canPickPages
            ? '<input class="field-input" style="width:128px;padding:5px 8px;font-size:11.5px;" placeholder="All (e.g. 1-3, 5)" title="Optional page range for this file" value="' + esc(entry.pageRange || '') + '" onchange="mergePushUndo();mergeQueue.items[' + idx + '].pageRange=this.value.trim()">' +
              '<button class="mini-btn mini-btn-edit" onclick="closeStudioModal(\\'mergePrintModal\\');openPageManager(\\'' + esc(f.id) + '\\',true)" title="Open Visual Page Picker">📑 Pages</button>'
            : '') +
          '<div style="display:flex;gap:4px;">' +
            '<button class="mini-btn" onclick="mergeMove(' + idx + ',-1)" title="Move Up">↑</button>' +
            '<button class="mini-btn" onclick="mergeMove(' + idx + ',1)" title="Move Down">↓</button>' +
            '<button class="mini-btn" style="color:var(--danger);" onclick="mergeTrashFile(\\'' + esc(f.id) + '\\',\\'' + esc(f.originalName) + '\\')" title="Move to Trash">🗑</button>' +
          '</div>' +
        '</div>';
      }).join('');
    }

    async function mergeTrashFile(fileId, fileName) {
      await trashFile(fileId, fileName);
      if (mergeQueue.submissionId) openMergeModal(mergeQueue.submissionId);
    }

    // ------------------------------------------------------------------------
    // 5. PDF -> WORD (.docx), DRAG-OUT-TO-TRASH, DRAG-IN-TO-ADD & TOASTS
    // ------------------------------------------------------------------------
    function showToast(message, actionLabel, actionFn, durationMs) {
      const box = document.getElementById('sdToastBox');
      if (!box) return;
      const el = document.createElement('div');
      el.className = 'sd-toast';
      const txt = document.createElement('span');
      txt.textContent = message;
      el.appendChild(txt);
      if (actionLabel && typeof actionFn === 'function') {
        const btn = document.createElement('button');
        btn.className = 'sd-toast-btn';
        btn.textContent = actionLabel;
        btn.onclick = () => { el.remove(); actionFn(); };
        el.appendChild(btn);
      }
      box.appendChild(el);
      setTimeout(() => { try { el.remove(); } catch {} }, durationMs || 6500);
    }

    function rgbToHex6(r, g, b) {
      const h = v => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0');
      return (h(r) + h(g) + h(b)).toUpperCase();
    }

    function hexColorDist(c1, c2) {
      const p = c => [parseInt(c.slice(0, 2), 16) || 0, parseInt(c.slice(2, 4), 16) || 0, parseInt(c.slice(4, 6), 16) || 0];
      const a = p(c1 || '111827');
      const b = p(c2 || '111827');
      return Math.abs(a[0] - b[0]) + Math.abs(a[1] - b[1]) + Math.abs(a[2] - b[2]);
    }

    function sampleItemColorAndScrub(fullImg, bgImg, bgCtx, px, py, pw, ph) {
      const W = fullImg.width;
      const H = fullImg.height;
      const x0 = Math.max(0, Math.floor(px - 1));
      const y0 = Math.max(0, Math.floor(py - 1));
      const x1 = Math.min(W - 1, Math.ceil(px + Math.max(4, pw) + 1));
      const y1 = Math.min(H - 1, Math.ceil(py + Math.max(4, ph) + 1));
      if (x1 <= x0 || y1 <= y0) return '111827';

      const fd = fullImg.data;
      const bd = bgImg.data;
      let inkR = 0, inkG = 0, inkB = 0, inkW = 0, inkCount = 0;
      const stepX = Math.max(1, Math.floor((x1 - x0) / 48));
      const stepY = Math.max(1, Math.floor((y1 - y0) / 24));

      for (let y = y0; y <= y1; y += stepY) {
        const rowOff = y * W * 4;
        for (let x = x0; x <= x1; x += stepX) {
          const i = rowOff + x * 4;
          const diff = Math.abs(fd[i] - bd[i]) + Math.abs(fd[i + 1] - bd[i + 1]) + Math.abs(fd[i + 2] - bd[i + 2]);
          if (diff > 36) {
            const wt = diff * diff;
            inkR += fd[i] * wt;
            inkG += fd[i + 1] * wt;
            inkB += fd[i + 2] * wt;
            inkW += wt;
            inkCount++;
          }
        }
      }

      if (inkCount >= 2 && inkW > 0) {
        return rgbToHex6(inkR / inkW, inkG / inkW, inkB / inkW);
      }

      // Rare fallback: text was drawn as vector paths instead of fillText -> sample perimeter bg & scrub glyph cleanly
      const perimR = [], perimG = [], perimB = [];
      const samplePt = (sx, sy) => {
        if (sx < 0 || sy < 0 || sx >= W || sy >= H) return;
        const idx = (sy * W + sx) * 4;
        perimR.push(bd[idx]); perimG.push(bd[idx + 1]); perimB.push(bd[idx + 2]);
      };
      const pyTop = Math.max(0, y0 - 2);
      const pyBot = Math.min(H - 1, y1 + 2);
      const pxL = Math.max(0, x0 - 2);
      const pxR = Math.min(W - 1, x1 + 2);
      for (let x = x0; x <= x1; x += Math.max(2, stepX * 2)) { samplePt(x, pyTop); samplePt(x, pyBot); }
      for (let y = y0; y <= y1; y += Math.max(2, stepY * 2)) { samplePt(pxL, y); samplePt(pxR, y); }

      const med = arr => { if (!arr.length) return 255; arr.sort((a, b) => a - b); return arr[Math.floor(arr.length / 2)]; };
      const bgR = med(perimR), bgG = med(perimG), bgB = med(perimB);

      let fR = 0, fG = 0, fB = 0, fW = 0;
      for (let y = y0; y <= y1; y += stepY) {
        const rowOff = y * W * 4;
        for (let x = x0; x <= x1; x += stepX) {
          const i = rowOff + x * 4;
          const dist = Math.abs(fd[i] - bgR) + Math.abs(fd[i + 1] - bgG) + Math.abs(fd[i + 2] - bgB);
          if (dist > 45) {
            const wt = dist * dist;
            fR += fd[i] * wt; fG += fd[i + 1] * wt; fB += fd[i + 2] * wt; fW += wt;
          }
        }
      }

      bgCtx.fillStyle = 'rgb(' + bgR + ',' + bgG + ',' + bgB + ')';
      bgCtx.fillRect(Math.max(0, x0 - 1), Math.max(0, y0 - 1), (x1 - x0) + 3, (y1 - y0) + 3);

      if (fW > 0) return rgbToHex6(fR / fW, fG / fW, fB / fW);
      const lum = (bgR * 299 + bgG * 587 + bgB * 114) / 1000;
      return lum < 128 ? 'FFFFFF' : '111827';
    }

    function mapPdfFontFamily(fontKey, cssFamily) {
      const f = ((fontKey || '') + ' ' + (cssFamily || '')).toLowerCase();
      if (/mono|courier|consolas|code/.test(f)) return 'Consolas';
      if (/serif|slab|zilla|times|georgia|garamond|cambria|palatino|merriweather|playfair/.test(f)) return 'Georgia';
      return 'Calibri';
    }

    function buildVisualBoxesForPage(items, widthPt, heightPt) {
      if (!items || !items.length) return [];
      // 1. Group items into same-baseline, same-column, same-color visual lines
      const sorted = items.slice().sort((a, b) => (a.baselineY - b.baselineY) || (a.x - b.x));
      const lines = [];
      for (const it of sorted) {
        let bestLine = null;
        let bestGap = 1e9;
        for (let i = lines.length - 1; i >= Math.max(0, lines.length - 20); i--) {
          const ln = lines[i];
          const yTol = Math.max(2.6, Math.min(ln.fontSize, it.fontSize) * 0.45);
          if (Math.abs(ln.baselineY - it.baselineY) > yTol) continue;
          if (hexColorDist(ln.color, it.color) > 75) continue;
          const gap = it.x - ln.right;
          const maxGap = Math.max(22, Math.max(ln.fontSize, it.fontSize) * 2.2);
          if (gap >= -it.fontSize * 0.6 && gap <= maxGap && gap < bestGap) {
            bestGap = gap;
            bestLine = ln;
          }
        }
        if (bestLine) {
          bestLine.items.push(it);
          bestLine.right = Math.max(bestLine.right, it.x + it.w);
          bestLine.topY = Math.min(bestLine.topY, it.topY);
          bestLine.bottomY = Math.max(bestLine.bottomY, it.topY + it.h);
          bestLine.fontSize = Math.max(bestLine.fontSize, it.fontSize);
        } else {
          lines.push({
            x: it.x,
            right: it.x + it.w,
            baselineY: it.baselineY,
            topY: it.topY,
            bottomY: it.topY + it.h,
            fontSize: it.fontSize,
            color: it.color,
            fontFamily: it.fontFamily,
            items: [it]
          });
        }
      }

      // Finalize text & style for each visual line (fixing tight word-space gaps like ZillaSlab!)
      const builtLines = [];
      for (const ln of lines) {
        ln.items.sort((a, b) => a.x - b.x);
        let text = '';
        let prevEnd = null;
        let boldChars = 0, italicChars = 0, totalChars = 0;
        for (const it of ln.items) {
          const s = it.str || '';
          if (!s.length) continue;
          if (prevEnd !== null) {
            const gap = it.x - prevEnd;
            if (gap > Math.max(0.75, it.fontSize * 0.09) && !text.endsWith(' ') && !s.startsWith(' ')) {
              text += ' ';
            }
          }
          text += s;
          prevEnd = it.x + Math.max(it.w, s.length * it.fontSize * 0.36);
          const cLen = s.trim().length;
          totalChars += cLen;
          if (it.bold) boldChars += cLen;
          if (it.italic) italicChars += cLen;
        }
        const cleaned = text.replace(/\\s{2,}/g, ' ').trim();
        if (!cleaned) continue;
        builtLines.push({
          x: ln.x,
          right: ln.right,
          w: Math.max(10, ln.right - ln.x),
          topY: ln.topY,
          bottomY: ln.bottomY,
          baselineY: ln.baselineY,
          text: cleaned,
          fontSize: Math.round(ln.fontSize * 10) / 10,
          bold: totalChars > 0 && (boldChars / totalChars) >= 0.5,
          italic: totalChars > 0 && (italicChars / totalChars) >= 0.5,
          color: ln.color,
          fontFamily: ln.fontFamily
        });
      }

      // 2. Group tightly stacked lines in the same column & style into multi-line editable Word Text Boxes
      builtLines.sort((a, b) => (a.topY - b.topY) || (a.x - b.x));
      const boxes = [];
      for (const ln of builtLines) {
        let targetBox = null;
        for (let i = boxes.length - 1; i >= Math.max(0, boxes.length - 16); i--) {
          const bx = boxes[i];
          const last = bx.rawLines[bx.rawLines.length - 1];
          const dy = ln.baselineY - last.baselineY;
          if (dy < last.fontSize * 0.80 || dy > last.fontSize * 1.65) continue;
          if (Math.abs(ln.x - bx.x) > 9) continue;
          if (Math.abs(ln.fontSize - last.fontSize) > 1.2) continue;
          if (ln.bold !== last.bold || ln.italic !== last.italic) continue;
          if (ln.fontFamily !== last.fontFamily) continue;
          if (hexColorDist(ln.color, last.color) > 65) continue;
          if (ln.x > bx.right + 12 || ln.right < bx.x - 12) continue;
          targetBox = bx;
          break;
        }
        if (targetBox) {
          targetBox.rawLines.push(ln);
          targetBox.x = Math.min(targetBox.x, ln.x);
          targetBox.right = Math.max(targetBox.right, ln.right);
          targetBox.bottomY = Math.max(targetBox.bottomY, ln.bottomY);
        } else {
          boxes.push({
            x: ln.x,
            right: ln.right,
            topY: ln.topY,
            bottomY: ln.bottomY,
            rawLines: [ln]
          });
        }
      }

      // 3. Compute safe non-overlapping box widths so Word never wraps headings unexpectedly
      const result = [];
      for (let i = 0; i < boxes.length; i++) {
        const bx = boxes[i];
        const firstFs = bx.rawLines[0].fontSize || 10.5;
        let maxRightLimit = widthPt - 4;
        for (let j = 0; j < boxes.length; j++) {
          if (i === j) continue;
          const other = boxes[j];
          const vOverlap = other.topY < bx.bottomY && other.bottomY > bx.topY;
          if (vOverlap && other.x > bx.right - 2) {
            maxRightLimit = Math.min(maxRightLimit, other.x - 4);
          }
        }
        const rawW = bx.right - bx.x;
        const paddedW = Math.min(maxRightLimit - bx.x, rawW + Math.max(14, rawW * 0.08, firstFs * 1.1));
        const finalW = Math.max(rawW + 4, paddedW);
        const finalH = Math.max(firstFs * 1.25, (bx.bottomY - bx.topY) + 4);

        const outLines = [];
        for (let k = 0; k < bx.rawLines.length; k++) {
          const cur = bx.rawLines[k];
          const next = k + 1 < bx.rawLines.length ? bx.rawLines[k + 1] : null;
          const lhPt = next ? Math.max(cur.fontSize * 1.0, Math.min(cur.fontSize * 1.85, next.baselineY - cur.baselineY)) : (cur.fontSize * 1.18);
          outLines.push({
            text: cur.text,
            fontSize: cur.fontSize,
            bold: cur.bold,
            italic: cur.italic,
            color: cur.color,
            fontFamily: cur.fontFamily,
            lineHeightPt: Math.round(lhPt * 10) / 10
          });
        }

        result.push({
          x: Math.round(Math.max(0, bx.x - 1) * 10) / 10,
          y: Math.round(Math.max(0, bx.topY - 1) * 10) / 10,
          w: Math.round(finalW * 10) / 10,
          h: Math.round(finalH * 10) / 10,
          align: 'left',
          lines: outLines
        });
      }
      return result;
    }

    async function convertPdfToDocxViaPdfJs(fileId, fileName, fallbackReason, btnEl) {
      if (!window.pdfjsLib) {
        throw new Error('Built-in Visual PDF Engine (pdf.js) is unavailable offline. Install Microsoft Word 2013+ or LibreOffice.');
      }
      ensurePdfJsWorker();
      if (btnEl) btnEl.textContent = '⏳ Extracting Layout...';
      const pdfRes = await fetch('/api/file?id=' + encodeURIComponent(fileId));
      if (!pdfRes.ok) throw new Error('Could not read source PDF from local storage.');
      const pdfBytes = new Uint8Array(await pdfRes.arrayBuffer());
      const pdfDoc = await window.pdfjsLib.getDocument({ data: pdfBytes }).promise;

      const visualPages = [];
      let totalBoxes = 0;
      for (let p = 1; p <= pdfDoc.numPages; p++) {
        if (btnEl && pdfDoc.numPages > 1) btnEl.textContent = '⏳ Page ' + p + '/' + pdfDoc.numPages + '...';
        const page = await pdfDoc.getPage(p);
        const vp1 = page.getViewport({ scale: 1.0 });
        const scale = Math.min(2.0, Math.max(1.35, 1400 / Math.max(vp1.width, vp1.height)));
        const vpR = page.getViewport({ scale });

        // 1. Render full visual page (for exact ink color sampling)
        const fullCanvas = document.createElement('canvas');
        fullCanvas.width = Math.ceil(vpR.width);
        fullCanvas.height = Math.ceil(vpR.height);
        const fullCtx = fullCanvas.getContext('2d', { willReadFrequently: true });
        fullCtx.fillStyle = '#FFFFFF';
        fullCtx.fillRect(0, 0, fullCanvas.width, fullCanvas.height);
        await page.render({ canvasContext: fullCtx, viewport: vpR }).promise;

        // 2. Render text-free graphics/photo/sidebar/icons background layer
        const bgCanvas = document.createElement('canvas');
        bgCanvas.width = fullCanvas.width;
        bgCanvas.height = fullCanvas.height;
        const bgCtx = bgCanvas.getContext('2d', { willReadFrequently: true });
        bgCtx.fillStyle = '#FFFFFF';
        bgCtx.fillRect(0, 0, bgCanvas.width, bgCanvas.height);
        const bgCtxProxy = new Proxy(bgCtx, {
          get(target, prop) {
            if (prop === 'fillText' || prop === 'strokeText') return function() {};
            const val = target[prop];
            return typeof val === 'function' ? val.bind(target) : val;
          },
          set(target, prop, value) {
            target[prop] = value;
            return true;
          }
        });
        await page.render({ canvasContext: bgCtxProxy, viewport: vpR }).promise;

        const fullImg = fullCtx.getImageData(0, 0, fullCanvas.width, fullCanvas.height);
        const bgImg = bgCtx.getImageData(0, 0, bgCanvas.width, bgCanvas.height);

        // 3. Extract positioned text items with exact sampled ink colors + path-glyph scrub
        const tc = await page.getTextContent();
        const styles = tc.styles || {};
        const items = [];
        for (const raw of (tc.items || [])) {
          const str = (raw && typeof raw.str === 'string') ? raw.str : '';
          if (!str.trim()) continue;
          const tr = raw.transform || [11, 0, 0, 11, 0, 0];
          const vtr = window.pdfjsLib.Util.transform(vp1.transform, tr);
          const fontSize = Math.max(5.5, Math.min(72, Math.hypot(vtr[2], vtr[3]) || Math.hypot(vtr[0], vtr[1]) || raw.height || 10.5));
          const x = vtr[4] || 0;
          const baselineY = vtr[5] || 0;
          const topY = baselineY - fontSize * 0.90;
          const w = (raw.width && raw.width > 0) ? raw.width : (str.length * fontSize * 0.50);
          const h = fontSize * 1.15;

          const fontKey = raw.fontName || '';
          const cssFam = (styles[fontKey] && styles[fontKey].fontFamily) ? styles[fontKey].fontFamily : '';
          const isBold = /bold|black|heavy|semibold|demi/i.test(fontKey + ' ' + cssFam);
          const isItalic = /italic|oblique/i.test(fontKey + ' ' + cssFam);
          const fontFamily = mapPdfFontFamily(fontKey, cssFam);
          const color = sampleItemColorAndScrub(fullImg, bgImg, bgCtx, x * scale, topY * scale, w * scale, h * scale);

          items.push({ str, x, baselineY, topY, w, h, fontSize, bold: isBold, italic: isItalic, fontFamily, color });
        }

        const boxes = buildVisualBoxesForPage(items, vp1.width, vp1.height);
        totalBoxes += boxes.length;

        let bgDataUrl = bgCanvas.toDataURL('image/png');
        if (bgDataUrl.length > 480000) {
          bgDataUrl = bgCanvas.toDataURL('image/jpeg', 0.91);
        }

        visualPages.push({
          widthPt: Math.round(vp1.width * 10) / 10,
          heightPt: Math.round(vp1.height * 10) / 10,
          bgDataUrl,
          boxes
        });
      }

      if (btnEl) btnEl.textContent = '⏳ Building .docx...';
      const buildRes = await fetch('/api/file/build-docx-from-pages', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileId, fileName, visualPages })
      });
      const buildData = await buildRes.json();
      if (!buildRes.ok || !buildData.success) {
        throw new Error(buildData.error || 'Could not generate visual editable Word (.docx) file.');
      }
      buildData._scannedOnly = (totalBoxes === 0);
      return buildData;
    }

    async function convertPdfToWord(fileId, fileName, btnEl) {
      const origText = btnEl ? btnEl.textContent : '';
      if (btnEl) { btnEl.disabled = true; btnEl.textContent = '⏳ Converting...'; }
      showToast('Converting "' + fileName + '" to editable Word (.docx)...', null, null, 4500);
      try {
        const res = await fetch('/api/file/convert-docx', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ fileId })
        });
        let data = await res.json();
        let usedBuiltinFallback = false;
        let fallbackNote = '';

        if (!res.ok || !data.success) {
          if (data && data.fallbackSupported) {
            fallbackNote = data.error || 'Office PDF Reflow unavailable';
            showToast('Building full visual layout Word doc (' + fallbackNote + ')...', null, null, 5000);
            data = await convertPdfToDocxViaPdfJs(fileId, fileName, fallbackNote, btnEl);
            usedBuiltinFallback = true;
          } else {
            alert('PDF to Word failed: ' + (data.error || 'Ensure Microsoft Word 2013+ or LibreOffice is installed.'));
            return;
          }
        }

        await fetchCloudState(true);
        // Trigger immediate download so shopkeeper can open & edit in Word, then drag back in!
        const a = document.createElement('a');
        a.href = data.downloadUrl || (data.url + '&download=1');
        a.download = data.outputFilename || 'converted.docx';
        document.body.appendChild(a);
        a.click();
        a.remove();

        const toastMsg = usedBuiltinFallback
          ? (data._scannedOnly
              ? ('✓ Downloaded ' + data.outputFilename + ' (scanned PDF pages embedded in Word).')
              : ('✓ Downloaded ' + data.outputFilename + ' (Built-in Visual Layout Engine). Open in Word to edit text directly in place!'))
          : ('✓ Downloaded ' + data.outputFilename + ' (' + data.engine + '). Edit in Word, then drag it back here!');

        showToast(
          toastMsg,
          '↗ Open in Word',
          () => openConvertedNative(data.outputFilename),
          12000
        );
      } catch (err) {
        alert('PDF to Word error: ' + err.message);
      } finally {
        if (btnEl) { btnEl.disabled = false; btnEl.textContent = origText; }
      }
    }

    async function openConvertedNative(name) {
      try {
        const res = await fetch('/api/converted/open', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ name })
        });
        if (!res.ok) alert('Could not open converted file.');
      } catch {}
    }

    // Global Undo Stack (Trash, Restore, Status, Rename, Reorder, Pin, Page Save)
    const globalUndoStack = [];

    function pushGlobalUndo(label, undoFn) {
      globalUndoStack.push({ label, undoFn });
      if (globalUndoStack.length > 25) globalUndoStack.shift();
      updateGlobalUndoBtn();
    }

    function updateGlobalUndoBtn() {
      const btn = document.getElementById('globalUndoBtn');
      if (!btn) return;
      if (globalUndoStack.length === 0) {
        btn.style.display = 'none';
      } else {
        const top = globalUndoStack[globalUndoStack.length - 1];
        btn.style.display = 'inline-flex';
        btn.title = 'Undo: ' + top.label + ' (Ctrl+Z)';
        btn.textContent = '↩ Undo';
      }
    }

    async function performGlobalUndo() {
      if (globalUndoStack.length === 0) return;
      const top = globalUndoStack.pop();
      updateGlobalUndoBtn();
      try {
        await top.undoFn();
        showToast('↩ Undid: ' + top.label, null, null, 3200);
      } catch (err) {
        alert('Undo failed: ' + err.message);
      }
    }

    async function trashFile(fileId, fileName, skipUndoPush) {
      const res = await fetch('/api/file/trash', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileId })
      });
      const data = await res.json();
      if (!res.ok || !data.success) { alert('Could not move file to Trash: ' + (data.error || 'error')); return; }
      if (!skipUndoPush) pushGlobalUndo('Trash "' + (fileName || 'file') + '"', () => restoreFile(fileId, fileName, true));
      await fetchCloudState(true);
      showToast('🗑️ Moved "' + (fileName || 'file') + '" to Trash', '↩ Undo', () => restoreFile(fileId, fileName, true), 7000);
    }

    async function restoreFile(fileId, fileName, skipUndoPush) {
      const res = await fetch('/api/file/restore', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileId })
      });
      const data = await res.json();
      if (!res.ok || !data.success) { alert('Restore failed: ' + (data.error || 'error')); return; }
      if (!skipUndoPush) pushGlobalUndo('Restore "' + (fileName || 'file') + '"', () => trashFile(fileId, fileName, true));
      await fetchCloudState(true);
      showToast('↩ Restored "' + (fileName || 'file') + '" back to submission', null, null, 3500);
    }

    async function deleteFilePermanent(fileId, fileName) {
      if (!confirm('Permanently delete "' + (fileName || 'this file') + '" from disk?')) return;
      const res = await fetch('/api/file/delete-permanent', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileId })
      });
      const data = await res.json();
      if (!res.ok || !data.success) { alert('Delete failed: ' + (data.error || 'error')); return; }
      await fetchCloudState(true);
      showToast('Deleted permanently (freed ' + formatSize(data.freedBytes) + ')', null, null, 3500);
    }

    async function emptyTrash(submissionId) {
      const msg = submissionId
        ? 'Permanently delete all trashed files in this submission?'
        : 'Permanently empty ALL files in Trash across all submissions?';
      if (!confirm(msg)) return;
      const res = await fetch('/api/trash/empty', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(submissionId ? { submissionId } : {})
      });
      const data = await res.json();
      if (!res.ok || !data.success) { alert('Empty Trash failed: ' + (data.error || 'error')); return; }
      await fetchCloudState(true);
      showToast('🧹 Emptied Trash (' + data.deletedCount + ' file(s), freed ' + formatSize(data.freedBytes) + ')', null, null, 4500);
    }

    async function uploadLocalFilesToSubmission(submissionId, fileList, refreshMerge) {
      if (!submissionId || !fileList || fileList.length === 0) return;
      const files = Array.from(fileList);
      showToast('Adding ' + files.length + ' file(s) to ' + submissionId + '...', null, null, 3000);
      let added = 0;
      const errs = [];
      for (const f of files) {
        try {
          const res = await fetch('/api/submission/add-file', {
            method: 'POST',
            headers: {
              'Content-Type': f.type || 'application/octet-stream',
              'X-Submission-Id': submissionId,
              'X-File-Name': encodeURIComponent(f.name)
            },
            body: f
          });
          const data = await res.json();
          if (!res.ok || !data.success) errs.push(f.name + ': ' + (data.error || 'failed'));
          else added++;
        } catch (err) {
          errs.push(f.name + ': ' + err.message);
        }
      }
      await fetchCloudState(true);
      if (refreshMerge && mergeQueue.submissionId) openMergeModal(mergeQueue.submissionId);
      if (added > 0) showToast('✓ Added ' + added + ' file(s) to ' + submissionId + ' — ready to Merge & Print!', null, null, 5000);
      if (errs.length > 0) alert('Some files could not be added:\\n' + errs.join('\\n'));
    }

    let draggingFileState = null;

    function handleFileCardDragStart(ev, fileId, fileName, subId) {
      draggingFileState = { fileId, fileName, subId, handled: false };
      ev.dataTransfer.effectAllowed = 'move';
      try { ev.dataTransfer.setData('application/x-scandrop-file', fileId); } catch {}
      try { ev.dataTransfer.setData('text/plain', fileName); } catch {}
      if (ev.currentTarget) ev.currentTarget.classList.add('dragging');
      document.body.classList.add('dragging-file-active');
    }

    function handleFileCardDragEnd(ev, fileId, fileName) {
      if (ev.currentTarget) ev.currentTarget.classList.remove('dragging');
      document.body.classList.remove('dragging-file-active');
      document.querySelectorAll('.drag-over-card,.drag-active').forEach(n => n.classList.remove('drag-over-card', 'drag-active'));
      const st = draggingFileState;
      draggingFileState = null;
      if (!st || st.handled) return;

      // Intuitive "drag out of the list" -> automatically move to Trash (unless released over Holding Tray!)
      const trayEl = document.getElementById('holdingTrayBar');
      if (trayEl && (ev.clientX !== 0 || ev.clientY !== 0)) {
        const tr = trayEl.getBoundingClientRect();
        if (ev.clientX >= tr.left && ev.clientX <= tr.right && ev.clientY >= tr.top && ev.clientY <= tr.bottom) {
          pinFileToTray(fileId, fileName);
          return;
        }
      }

      const listEl = document.getElementById('inspectorFilesScroll');
      if (listEl && (ev.clientX !== 0 || ev.clientY !== 0)) {
        const r = listEl.getBoundingClientRect();
        const pad = 18;
        const outside = ev.clientX < (r.left - pad) || ev.clientX > (r.right + pad) || ev.clientY < (r.top - pad) || ev.clientY > (r.bottom + pad);
        if (outside) {
          trashFile(fileId, fileName);
        }
      }
    }

    function handleFileCardDragOver(ev, targetFileId) {
      if (draggingTrayState) {
        ev.preventDefault();
        ev.dataTransfer.dropEffect = 'copy';
        return;
      }
      if (draggingFileState && draggingFileState.fileId !== targetFileId) {
        ev.preventDefault();
        ev.dataTransfer.dropEffect = 'move';
        if (ev.currentTarget) ev.currentTarget.classList.add('drag-over-card');
      } else if (ev.dataTransfer && ev.dataTransfer.types && Array.from(ev.dataTransfer.types).includes('Files')) {
        ev.preventDefault();
        ev.dataTransfer.dropEffect = 'copy';
      }
    }

    async function handleFileCardDrop(ev, targetFileId, subId) {
      if (ev.currentTarget) ev.currentTarget.classList.remove('drag-over-card');
      if (draggingTrayState) {
        ev.preventDefault();
        ev.stopPropagation();
        const t = draggingTrayState;
        draggingTrayState = null;
        document.body.classList.remove('dragging-tray-active');
        await copyTrayItemsToSubmission([t.trayId], subId, false);
        return;
      }
      if (ev.dataTransfer && ev.dataTransfer.files && ev.dataTransfer.files.length > 0) {
        ev.preventDefault();
        ev.stopPropagation();
        await uploadLocalFilesToSubmission(subId, ev.dataTransfer.files, false);
        return;
      }
      if (!draggingFileState || draggingFileState.fileId === targetFileId) return;
      ev.preventDefault();
      ev.stopPropagation();
      draggingFileState.handled = true;
      const sub = (cloudState.submissions || []).find(s => s.id === subId || s.receiptCode === subId);
      if (!sub) return;
      const prevIds = (sub.files || []).map(f => f.id);
      const ids = prevIds.slice();
      const fromIdx = ids.indexOf(draggingFileState.fileId);
      const toIdx = ids.indexOf(targetFileId);
      if (fromIdx < 0 || toIdx < 0) return;
      ids.splice(fromIdx, 1);
      ids.splice(toIdx, 0, draggingFileState.fileId);
      await fetch('/api/submission/reorder-files', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ submissionId: sub.id, fileIds: ids })
      });
      pushGlobalUndo('Reorder files in ' + (sub.receiptCode || sub.id), async () => {
        await fetch('/api/submission/reorder-files', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ submissionId: sub.id, fileIds: prevIds })
        });
        await fetchCloudState(true);
      });
      await fetchCloudState(true);
    }

    function handleTrashZoneDragOver(ev) {
      if (!draggingFileState && !draggingTrayState) return;
      ev.preventDefault();
      ev.dataTransfer.dropEffect = 'move';
      if (ev.currentTarget && ev.currentTarget.classList) ev.currentTarget.classList.add('drag-active');
    }

    async function handleTrashZoneDrop(ev) {
      if (ev.currentTarget && ev.currentTarget.classList) ev.currentTarget.classList.remove('drag-active');
      if (draggingTrayState) {
        ev.preventDefault();
        ev.stopPropagation();
        const t = draggingTrayState;
        draggingTrayState = null;
        document.body.classList.remove('dragging-tray-active');
        await removeTrayItem(t.trayId, t.fileName);
        return;
      }
      if (!draggingFileState) return;
      ev.preventDefault();
      ev.stopPropagation();
      const st = draggingFileState;
      st.handled = true;
      await trashFile(st.fileId, st.fileName);
    }

    function handleAddZoneDragOver(ev) {
      if (draggingFileState) return;
      ev.preventDefault();
      ev.dataTransfer.dropEffect = 'copy';
      if (ev.currentTarget && ev.currentTarget.classList) ev.currentTarget.classList.add('drag-active');
    }

    async function handleAddZoneDrop(ev, subId, refreshMerge) {
      if (ev.currentTarget && ev.currentTarget.classList) ev.currentTarget.classList.remove('drag-active');
      if (draggingFileState) return;
      if (draggingTrayState) {
        ev.preventDefault();
        ev.stopPropagation();
        const t = draggingTrayState;
        draggingTrayState = null;
        document.body.classList.remove('dragging-tray-active');
        await copyTrayItemsToSubmission([t.trayId], subId, !!refreshMerge);
        return;
      }
      if (ev.dataTransfer && ev.dataTransfer.files && ev.dataTransfer.files.length > 0) {
        ev.preventDefault();
        ev.stopPropagation();
        await uploadLocalFilesToSubmission(subId, ev.dataTransfer.files, !!refreshMerge);
      }
    }

    let activeMoreFileId = null;

    function closeAllCardMoreMenus() {
      activeMoreFileId = null;
      document.querySelectorAll('.card-more-popover.open').forEach(el => el.classList.remove('open'));
      document.querySelectorAll('.more-trigger-btn.open').forEach(btn => {
        btn.classList.remove('open');
        const arrow = btn.querySelector('.more-chevron-char');
        if (arrow) arrow.textContent = '▾';
      });
    }

    function toggleCardMore(fileId, ev) {
      if (ev) ev.stopPropagation();
      const menu = document.getElementById('more_' + fileId);
      const btn = document.getElementById('more_btn_' + fileId);
      if (!menu || !btn) return;
      const wasOpen = menu.classList.contains('open');
      closeAllCardMoreMenus();
      if (wasOpen) return;

      activeMoreFileId = fileId;
      btn.classList.add('open');
      const arrow = btn.querySelector('.more-chevron-char');
      if (arrow) arrow.textContent = '▴';

      menu.classList.add('open');
      const r = btn.getBoundingClientRect();
      const menuW = Math.max(176, menu.offsetWidth || 176);
      const menuH = menu.offsetHeight || 170;
      let left = r.right - menuW + 6;
      if (left < 12) left = 12;
      if (left + menuW > window.innerWidth - 12) left = window.innerWidth - menuW - 12;
      let top = r.bottom + 8;
      if (top + menuH > window.innerHeight - 12 && r.top - menuH - 8 > 12) {
        top = r.top - menuH - 8;
      }
      menu.style.left = Math.round(left) + 'px';
      menu.style.top = Math.round(top) + 'px';
    }

    window.addEventListener('click', (ev) => {
      if (!activeMoreFileId) return;
      if (ev.target && (ev.target.closest('.card-more-popover') || ev.target.closest('.more-trigger-btn'))) return;
      closeAllCardMoreMenus();
    });
    window.addEventListener('keydown', (ev) => {
      if (ev.key === 'Escape' && activeMoreFileId) closeAllCardMoreMenus();
    });

    // ------------------------------------------------------------------------
    // 6. PERSISTENT HOLDING TRAY (Temporary Staging Shelf Across All Jobs)
    // ------------------------------------------------------------------------
    let trayCollapsed = false;
    let draggingTrayState = null;

    function toggleHoldingTray() {
      trayCollapsed = !trayCollapsed;
      renderHoldingTray();
    }

    function renderHoldingTray() {
      const bar = document.getElementById('holdingTrayBar');
      if (!bar) return;
      const items = (cloudState && cloudState.trayItems) || [];
      bar.classList.toggle('collapsed', trayCollapsed);

      const countEl = document.getElementById('trayCountBadge');
      if (countEl) {
        countEl.textContent = items.length;
        countEl.className = 'badge ' + (items.length > 0 ? 'badge-gold' : 'badge-gray');
      }
      const toggleBtn = document.getElementById('trayToggleBtn');
      if (toggleBtn) toggleBtn.textContent = trayCollapsed ? '▴ Expand' : '▾ Minimize';

      const copyBtn = document.getElementById('trayCopyToSubBtn');
      if (copyBtn) {
        copyBtn.style.display = (items.length > 0 && selectedSubId) ? 'inline-flex' : 'none';
        if (selectedSubId) copyBtn.textContent = '📥 Add All to ' + selectedSubId;
      }
      const mergeBtn = document.getElementById('trayMergeBtn');
      if (mergeBtn) {
        mergeBtn.style.display = items.length > 0 ? 'inline-flex' : 'none';
        mergeBtn.textContent = '📑 Merge & Print Tray (' + items.length + ')';
      }
      const clearBtn = document.getElementById('trayClearBtn');
      if (clearBtn) clearBtn.style.display = items.length > 0 ? 'inline-flex' : 'none';

      const shelf = document.getElementById('trayShelfItems');
      if (!shelf) return;
      if (items.length === 0) {
        shelf.innerHTML = '<div class="tray-empty-hint" onclick="document.getElementById(\\'trayAddFileInput\\').click()">📌 Holding Tray is empty — drag any file from a customer job or Windows Explorer here to keep temporarily, move between jobs, or Merge &amp; Print</div>';
        return;
      }

      shelf.innerHTML = items.map(it => {
        const [cls, lbl] = getFileBadge(it.fileExtension);
        const safeName = esc(it.originalName);
        const safeId = esc(it.id);
        return '<div class="tray-chip" draggable="true" ' +
          'ondragstart="handleTrayChipDragStart(event,\\'' + safeId + '\\',\\'' + safeName + '\\')" ' +
          'ondragend="handleTrayChipDragEnd(event)" ' +
          'ondragover="handleTrayChipDragOver(event,\\'' + safeId + '\\')" ' +
          'ondragleave="this.classList.remove(\\'tray-chip-over\\')" ' +
          'ondrop="handleTrayChipDrop(event,\\'' + safeId + '\\')">' +
          '<div class="tray-chip-top">' +
            '<div class="file-type-box ' + cls + '" style="width:26px;height:26px;font-size:9px;border-radius:6px;">' + lbl + '</div>' +
            '<div style="flex:1;min-width:0;">' +
              '<div style="font-size:11.5px;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" title="' + safeName + '">' + safeName + '</div>' +
              '<div style="font-size:10px;color:var(--muted);">' + formatSize(it.sizeBytes) + ' • ' + esc(it.sourceLabel || 'Local PC') + '</div>' +
            '</div>' +
            '<button class="card-trash-btn" style="font-size:11px;padding:2px 4px;" title="Remove from Tray" onclick="removeTrayItem(\\'' + safeId + '\\',\\'' + safeName + '\\')">✕</button>' +
          '</div>' +
          '<div class="tray-chip-actions">' +
            '<button class="mini-btn" style="padding:2px 6px;font-size:10px;" onclick="previewUrl(\\'' + esc(it.url) + '\\',\\'' + safeName + '\\',\\'' + esc(it.fileExtension) + '\\',null)" title="Preview">👁</button>' +
            (['.pdf', '.docx', '.doc'].includes((it.fileExtension || '').toLowerCase()) ? '<button class="mini-btn" style="padding:2px 6px;font-size:10px;" onclick="openTrayPageManager(\\'' + safeId + '\\')" title="Pick / Manage Pages">📑</button>' : '') +
            '<button class="mini-btn" style="padding:2px 6px;font-size:10px;" onclick="openTrayFileNative(\\'' + safeId + '\\')" title="Open in Windows App">↗</button>' +
            '<button class="mini-btn" style="padding:2px 6px;font-size:10px;" onclick="directPrint(\\'' + esc(it.url) + '\\',\\'' + safeName + '\\')" title="Print">🖨</button>' +
            '<button class="mini-btn" style="padding:2px 6px;font-size:10px;" onclick="downloadFile(null,\\'' + safeName + '\\',\\'' + esc(it.url) + '\\')" title="Download">⬇</button>' +
            (selectedSubId ? '<button class="mini-btn mini-btn-edit" style="padding:2px 6px;font-size:10px;margin-left:auto;" onclick="copyTrayItemsToSubmission([\\'' + safeId + '\\'],\\'' + esc(selectedSubId) + '\\',false)" title="Copy into ' + esc(selectedSubId) + '">+ ' + esc(selectedSubId) + '</button>' : '') +
          '</div>' +
        '</div>';
      }).join('');
    }

    async function pinFileToTray(fileId, fileName) {
      const res = await fetch('/api/tray/add-from-file', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileId })
      });
      const data = await res.json();
      if (!res.ok || !data.success) { alert('Could not add to Holding Tray: ' + (data.error || 'error')); return; }
      trayCollapsed = false;
      await fetchCloudState(true);
      showToast('📌 Kept "' + (fileName || 'file') + '" in Holding Tray', null, null, 3500);
    }

    async function uploadFilesToTray(fileList) {
      if (!fileList || fileList.length === 0) return;
      const files = Array.from(fileList);
      let added = 0;
      for (const f of files) {
        try {
          const res = await fetch('/api/tray/add-upload', {
            method: 'POST',
            headers: {
              'Content-Type': f.type || 'application/octet-stream',
              'X-File-Name': encodeURIComponent(f.name)
            },
            body: f
          });
          const data = await res.json();
          if (res.ok && data.success) added++;
        } catch {}
      }
      trayCollapsed = false;
      await fetchCloudState(true);
      if (added > 0) showToast('📌 Added ' + added + ' file(s) to Holding Tray', null, null, 3500);
    }

    async function removeTrayItem(trayId, fileName) {
      await fetch('/api/tray/remove', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ trayId })
      });
      await fetchCloudState(true);
      showToast('Removed "' + (fileName || 'item') + '" from Holding Tray', null, null, 2500);
    }

    async function clearHoldingTray() {
      const items = (cloudState && cloudState.trayItems) || [];
      if (items.length === 0) return;
      if (!confirm('Clear all ' + items.length + ' temporary item(s) from Holding Tray? (Original submission files remain safe.)')) return;
      await fetch('/api/tray/clear', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });
      await fetchCloudState(true);
      showToast('🧹 Cleared Holding Tray', null, null, 3000);
    }

    async function openTrayFileNative(trayId) {
      const res = await fetch('/api/tray/open', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ trayId })
      });
      if (!res.ok) alert('Could not open tray file in default Windows app.');
    }

    async function copyTrayItemsToSubmission(trayIds, submissionId, refreshMerge) {
      if (!submissionId || !trayIds || trayIds.length === 0) return;
      const res = await fetch('/api/tray/copy-to-submission', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ trayIds, submissionId })
      });
      const data = await res.json();
      if (!res.ok || !data.success) { alert('Could not add tray file(s): ' + (data.error || 'error')); return; }
      await fetchCloudState(true);
      if (refreshMerge && mergeQueue.submissionId) openMergeModal(mergeQueue.submissionId);
      showToast('📥 Added ' + data.copiedCount + ' file(s) from Tray into ' + submissionId, null, null, 4000);
    }

    async function copyAllTrayToSelectedJob() {
      if (!selectedSubId) { alert('Select a customer submission in Inbox first.'); return; }
      const ids = ((cloudState && cloudState.trayItems) || []).map(i => i.id);
      if (ids.length === 0) return;
      await copyTrayItemsToSubmission(ids, selectedSubId, false);
    }

    async function mergeAndPrintTray() {
      const items = (cloudState && cloudState.trayItems) || [];
      if (items.length === 0) return;
      const btn = document.getElementById('trayMergeBtn');
      const origTxt = btn ? btn.textContent : '';
      if (btn) { btn.disabled = true; btn.textContent = '⏳ Merging...'; }
      try {
        const mergedPdf = await window.PDFLib.PDFDocument.create();
        let totalPages = 0;
        for (let i = 0; i < items.length; i++) {
          const it = items[i];
          showToast('Preparing Tray (' + (i + 1) + '/' + items.length + '): ' + it.originalName, null, null, 2500);
          const prepRes = await fetch('/api/tray/prepare-pdf', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ trayId: it.id })
          });
          const prep = await prepRes.json();
          if (!prepRes.ok || !prep.success) throw new Error(it.originalName + ': ' + (prep.error || 'Convert failed'));
          const bytes = await (await fetch(prep.url + (prep.url.includes('?') ? '&' : '?') + 't=' + Date.now())).arrayBuffer();
          const srcDoc = await window.PDFLib.PDFDocument.load(bytes, { ignoreEncryption: true });
          const pages = await mergedPdf.copyPages(srcDoc, srcDoc.getPageIndices());
          pages.forEach(p => mergedPdf.addPage(p));
          totalPages += pages.length;
        }
        const mergedBytes = await mergedPdf.save();
        const outName = 'Merged_Tray_' + items.length + 'files_' + Date.now().toString().slice(-4) + '.pdf';
        const saveRes = await fetch('/api/file/save-edited-pdf', {
          method: 'POST',
          headers: { 'Content-Type': 'application/pdf', 'X-Output-Name': outName },
          body: mergedBytes
        });
        const saved = await saveRes.json();
        if (!saveRes.ok || !saved.success) throw new Error(saved.error || 'Save failed');
        await fetchCloudState(true);
        showToast('✓ Merged ' + items.length + ' tray file(s) (' + totalPages + ' pages) — sending to print!', null, null, 5000);
        directPrint(saved.url, saved.filename);
      } catch (err) {
        alert('Merge Tray failed: ' + err.message);
      } finally {
        if (btn) { btn.disabled = false; btn.textContent = origTxt; }
      }
    }

    function handleTrayBarDragOver(ev) {
      ev.preventDefault();
      ev.stopPropagation();
      ev.dataTransfer.dropEffect = draggingTrayState ? 'move' : 'copy';
      const bar = document.getElementById('holdingTrayBar');
      if (bar) bar.classList.add('tray-drag-over');
    }

    function handleTrayBarDragLeave(ev) {
      const bar = document.getElementById('holdingTrayBar');
      if (bar && !bar.contains(ev.relatedTarget)) bar.classList.remove('tray-drag-over');
    }

    async function handleTrayBarDrop(ev) {
      ev.preventDefault();
      ev.stopPropagation();
      const bar = document.getElementById('holdingTrayBar');
      if (bar) bar.classList.remove('tray-drag-over');

      if (draggingFileState) {
        const st = draggingFileState;
        st.handled = true;
        await pinFileToTray(st.fileId, st.fileName);
        return;
      }
      if (draggingTrayState) return;
      if (ev.dataTransfer && ev.dataTransfer.files && ev.dataTransfer.files.length > 0) {
        await uploadFilesToTray(ev.dataTransfer.files);
      }
    }

    function handleTrayChipDragStart(ev, trayId, fileName) {
      draggingTrayState = { trayId, fileName };
      ev.dataTransfer.effectAllowed = 'copyMove';
      try { ev.dataTransfer.setData('application/x-scandrop-tray', trayId); } catch {}
      try { ev.dataTransfer.setData('text/plain', fileName); } catch {}
      if (ev.currentTarget) ev.currentTarget.classList.add('dragging');
      document.body.classList.add('dragging-tray-active');
    }

    function handleTrayChipDragEnd(ev) {
      if (ev.currentTarget) ev.currentTarget.classList.remove('dragging');
      document.body.classList.remove('dragging-tray-active');
      document.querySelectorAll('.tray-chip-over,.tray-drop-hover').forEach(n => n.classList.remove('tray-chip-over', 'tray-drop-hover'));
      draggingTrayState = null;
    }

    function handleTrayChipDragOver(ev, targetTrayId) {
      if (draggingTrayState && draggingTrayState.trayId !== targetTrayId) {
        ev.preventDefault();
        ev.stopPropagation();
        ev.dataTransfer.dropEffect = 'move';
        if (ev.currentTarget) ev.currentTarget.classList.add('tray-chip-over');
      }
    }

    async function handleTrayChipDrop(ev, targetTrayId) {
      if (ev.currentTarget) ev.currentTarget.classList.remove('tray-chip-over');
      if (!draggingTrayState || draggingTrayState.trayId === targetTrayId) return;
      ev.preventDefault();
      ev.stopPropagation();
      const items = (cloudState && cloudState.trayItems) || [];
      const ids = items.map(i => i.id);
      const fromIdx = ids.indexOf(draggingTrayState.trayId);
      const toIdx = ids.indexOf(targetTrayId);
      if (fromIdx < 0 || toIdx < 0) return;
      ids.splice(fromIdx, 1);
      ids.splice(toIdx, 0, draggingTrayState.trayId);
      draggingTrayState = null;
      document.body.classList.remove('dragging-tray-active');
      await fetch('/api/tray/reorder', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ trayIds: ids })
      });
      await fetchCloudState(true);
    }

    function handleSubRowDragOver(ev) {
      if (draggingTrayState || (ev.dataTransfer && ev.dataTransfer.types && Array.from(ev.dataTransfer.types).includes('Files'))) {
        ev.preventDefault();
        ev.stopPropagation();
        ev.dataTransfer.dropEffect = 'copy';
        if (ev.currentTarget) ev.currentTarget.classList.add('tray-drop-hover');
      }
    }

    async function handleSubRowDrop(ev, subId) {
      if (ev.currentTarget) ev.currentTarget.classList.remove('tray-drop-hover');
      if (draggingTrayState) {
        ev.preventDefault();
        ev.stopPropagation();
        const t = draggingTrayState;
        draggingTrayState = null;
        document.body.classList.remove('dragging-tray-active');
        await copyTrayItemsToSubmission([t.trayId], subId, false);
        return;
      }
      if (ev.dataTransfer && ev.dataTransfer.files && ev.dataTransfer.files.length > 0) {
        ev.preventDefault();
        ev.stopPropagation();
        await uploadLocalFilesToSubmission(subId, ev.dataTransfer.files, false);
      }
    }

    function scrollTrayShelf(delta) {
      const shelf = document.getElementById('trayShelfItems');
      if (shelf) shelf.scrollBy({ left: delta, behavior: 'smooth' });
    }

    function mergeMove(idx, delta) {
      const target = idx + delta;
      if (target < 0 || target >= mergeQueue.items.length) return;
      mergePushUndo();
      const tmp = mergeQueue.items[idx];
      mergeQueue.items[idx] = mergeQueue.items[target];
      mergeQueue.items[target] = tmp;
      renderMergeQueue();
    }

    function parsePageRangeSpec(spec, maxPages) {
      const clean = String(spec || '').trim();
      if (!clean) return Array.from({ length: maxPages }, (_, i) => i);
      const out = [];
      for (const part of clean.split(',')) {
        const p = part.trim();
        if (!p) continue;
        if (p.includes('-')) {
          const [aRaw, bRaw] = p.split('-');
          let a = parseInt(aRaw, 10), b = parseInt(bRaw, 10);
          if (isNaN(a)) a = 1;
          if (isNaN(b)) b = maxPages;
          const step = a <= b ? 1 : -1;
          for (let n = a; step > 0 ? n <= b : n >= b; n += step) {
            if (n >= 1 && n <= maxPages) out.push(n - 1);
          }
        } else {
          const n = parseInt(p, 10);
          if (!isNaN(n) && n >= 1 && n <= maxPages) out.push(n - 1);
        }
      }
      return out;
    }

    async function executeMergeAndPrint(andPrint) {
      const selected = mergeQueue.items.filter(i => i.checked);
      if (selected.length === 0) { alert('Select at least one file to merge.'); return; }
      const msg = document.getElementById('mergeProgressMsg');
      const b1 = document.getElementById('mergeSaveOnlyBtn');
      const b2 = document.getElementById('mergeDirectPrintBtn');
      msg.style.display = 'block';
      b1.disabled = true; b2.disabled = true;

      try {
        const mergedPdf = await window.PDFLib.PDFDocument.create();
        let totalPages = 0;

        for (let i = 0; i < selected.length; i++) {
          const entry = selected[i];
          const f = entry.file;
          msg.textContent = 'Preparing (' + (i + 1) + '/' + selected.length + '): ' + f.originalName + '...';
          const prepRes = await fetch('/api/file/prepare-pdf', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ fileId: f.id })
          });
          const prepData = await prepRes.json();
          if (!prepRes.ok || !prepData.success) {
            throw new Error(f.originalName + ': ' + (prepData.error || 'Could not prepare PDF'));
          }

          const pdfRes = await fetch(prepData.url + (prepData.url.includes('?') ? '&' : '?') + 't=' + Date.now());
          if (!pdfRes.ok) throw new Error('Could not read prepared PDF for ' + f.originalName);
          const bytes = await pdfRes.arrayBuffer();
          const srcDoc = await window.PDFLib.PDFDocument.load(bytes, { ignoreEncryption: true });
          const allCount = srcDoc.getPageCount();
          const indices = parsePageRangeSpec(entry.pageRange, allCount);
          if (indices.length === 0) throw new Error(f.originalName + ': Page range "' + entry.pageRange + '" matched 0 pages (doc has ' + allCount + ').');
          const copiedPages = await mergedPdf.copyPages(srcDoc, indices);
          copiedPages.forEach(p => mergedPdf.addPage(p));
          totalPages += copiedPages.length;
        }

        msg.textContent = 'Saving merged ' + totalPages + '-page PDF...';
        const mergedBytes = await mergedPdf.save();
        const outName = 'Merged_' + mergeQueue.receiptCode + '_' + selected.length + 'files.pdf';
        const saveRes = await fetch('/api/file/save-edited-pdf', {
          method: 'POST',
          headers: { 'Content-Type': 'application/pdf', 'X-Output-Name': outName },
          body: mergedBytes
        });
        const saveData = await saveRes.json();
        if (!saveRes.ok || !saveData.success) throw new Error(saveData.error || 'Could not save merged PDF');

        await fetchCloudState(true);
        msg.textContent = '✓ Merged ' + selected.length + ' file(s) (' + totalPages + ' pages) into ' + saveData.filename;
        closeStudioModal('mergePrintModal');

        if (andPrint) {
          directPrint(saveData.url, saveData.filename);
        } else {
          previewUrl(saveData.url, saveData.filename, '.pdf', null);
        }
      } catch (err) {
        msg.textContent = 'Merge failed: ' + err.message;
        alert('Merge & Print failed: ' + err.message);
      } finally {
        b1.disabled = false; b2.disabled = false;
      }
    }

    // ------------------------------------------------------------------------
    // 7. VISUAL PAGE MANAGER STUDIO (PDFs & Word Docs — Full Page Control + Undo/Redo)
    // ------------------------------------------------------------------------
    let pmState = {
      mode: 'file', // 'file' | 'tray'
      fileId: null,
      trayId: null,
      fileName: '',
      returnToMerge: false,
      pdfBytes: null,
      pdfJsDoc: null,
      totalSourcePages: 0,
      pages: [], // [{ uid, srcIndex (0-based), selected, rot (0|90|180|270) }]
      undoStack: [],
      redoStack: [],
      thumbCache: new Map(), // srcIndex -> dataURL
      dragUid: null
    };

    function pmSnapshot() {
      return JSON.stringify(pmState.pages);
    }

    function pmPushUndo() {
      pmState.undoStack.push(pmSnapshot());
      if (pmState.undoStack.length > 40) pmState.undoStack.shift();
      pmState.redoStack = [];
      pmUpdateButtons();
    }

    function pmUpdateButtons() {
      const u = document.getElementById('pmUndoBtn');
      const r = document.getElementById('pmRedoBtn');
      if (u) {
        u.disabled = pmState.undoStack.length === 0;
        u.textContent = pmState.undoStack.length > 0 ? ('↩ Undo (' + pmState.undoStack.length + ')') : '↩ Undo';
      }
      if (r) r.disabled = pmState.redoStack.length === 0;
      const selCount = pmState.pages.filter(p => p.selected).length;
      const badge = document.getElementById('pmSelectionBadge');
      if (badge) badge.textContent = selCount + ' / ' + pmState.pages.length + ' selected';
    }

    function pmUndo() {
      if (pmState.undoStack.length === 0) return;
      pmState.redoStack.push(pmSnapshot());
      pmState.pages = JSON.parse(pmState.undoStack.pop());
      pmRenderGrid();
    }

    function pmRedo() {
      if (pmState.redoStack.length === 0) return;
      pmState.undoStack.push(pmSnapshot());
      pmState.pages = JSON.parse(pmState.redoStack.pop());
      pmRenderGrid();
    }

    function pmResetAll() {
      pmPushUndo();
      pmState.pages = Array.from({ length: pmState.totalSourcePages }, (_, i) => ({
        uid: 'pg_' + i + '_' + Date.now(),
        srcIndex: i,
        selected: true,
        rot: 0
      }));
      document.getElementById('pmRangeInput').value = '';
      pmRenderGrid();
    }

    async function openPageManager(fileId, returnToMerge) {
      if (!ensurePdfJsWorker() || !window.PDFLib) {
        alert('PDF libraries are still loading. Check connection or refresh.');
        return;
      }
      let targetFile = null;
      for (const s of (cloudState && cloudState.submissions) || []) {
        const hit = (s.files || []).find(f => f.id === fileId);
        if (hit) { targetFile = hit; break; }
      }
      if (!targetFile) { alert('File not found.'); return; }

      pmState.mode = 'file';
      pmState.fileId = targetFile.id;
      pmState.trayId = null;
      pmState.fileName = targetFile.originalName;
      pmState.returnToMerge = !!returnToMerge;
      pmState.undoStack = [];
      pmState.redoStack = [];
      pmState.thumbCache.clear();

      document.getElementById('pmFileName').textContent = targetFile.originalName;
      document.getElementById('pmRangeInput').value = '';
      document.getElementById('pmGrid').innerHTML = '<div style="grid-column:1/-1;text-align:center;padding:60px;color:var(--muted);font-weight:600;">⏳ Loading &amp; rendering pages for <strong>' + esc(targetFile.originalName) + '</strong>...</div>';
      document.getElementById('pageManagerModal').classList.add('open');

      try {
        const ext = (targetFile.fileExtension || '').toLowerCase();
        let pdfUrl;
        if (ext === '.pdf') {
          // Always open the original full PDF so shopkeeper can see & toggle ALL pages!
          pdfUrl = '/api/file?id=' + encodeURIComponent(targetFile.id);
        } else {
          // Convert original Word doc (.docx/.doc) to PDF so all original pages stay inspectable
          const prepRes = await fetch('/api/file/prepare-pdf', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ fileId: targetFile.id, forceOriginal: true })
          });
          const prep = await prepRes.json();
          if (!prepRes.ok || !prep.success) throw new Error(prep.error || 'Could not convert Word document to pages.');
          pdfUrl = prep.url;
        }
        await pmLoadPdfFromUrl(pdfUrl, targetFile.pagePlanJson || null);
      } catch (err) {
        alert('Page Manager error: ' + err.message);
        closeStudioModal('pageManagerModal');
      }
    }

    async function openTrayPageManager(trayId) {
      if (!ensurePdfJsWorker() || !window.PDFLib) {
        alert('PDF libraries are still loading.');
        return;
      }
      const item = ((cloudState && cloudState.trayItems) || []).find(t => t.id === trayId);
      if (!item) return;

      pmState.mode = 'tray';
      pmState.fileId = null;
      pmState.trayId = item.id;
      pmState.fileName = item.originalName;
      pmState.returnToMerge = false;
      pmState.undoStack = [];
      pmState.redoStack = [];
      pmState.thumbCache.clear();

      document.getElementById('pmFileName').textContent = item.originalName + ' (Tray)';
      document.getElementById('pmRangeInput').value = '';
      document.getElementById('pmGrid').innerHTML = '<div style="grid-column:1/-1;text-align:center;padding:60px;color:var(--muted);font-weight:600;">⏳ Loading pages...</div>';
      document.getElementById('pageManagerModal').classList.add('open');

      try {
        const prepRes = await fetch('/api/tray/prepare-pdf', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ trayId: item.id })
        });
        const prep = await prepRes.json();
        if (!prepRes.ok || !prep.success) throw new Error(prep.error || 'Could not prepare pages.');
        await pmLoadPdfFromUrl(prep.url);
      } catch (err) {
        alert('Page Manager error: ' + err.message);
        closeStudioModal('pageManagerModal');
      }
    }

    async function pmLoadPdfFromUrl(pdfUrl, savedPlanJson) {
      const res = await fetch(pdfUrl + (pdfUrl.includes('?') ? '&' : '?') + 't=' + Date.now());
      if (!res.ok) throw new Error('Could not fetch PDF (' + res.status + ')');
      const buf = new Uint8Array(await res.arrayBuffer());
      pmState.pdfBytes = buf;
      pmState.pdfJsDoc = await window.pdfjsLib.getDocument({ data: buf.slice() }).promise;
      pmState.totalSourcePages = pmState.pdfJsDoc.numPages;

      let restored = null;
      if (savedPlanJson) {
        try {
          const parsed = JSON.parse(savedPlanJson);
          if (Array.isArray(parsed) && parsed.length > 0) {
            const valid = parsed
              .filter(x => x && typeof x.s === 'number' && x.s >= 0 && x.s < pmState.totalSourcePages)
              .map((x, idx) => ({
                uid: 'pg_' + x.s + '_' + idx + '_' + Date.now(),
                srcIndex: x.s,
                selected: x.on !== 0 && x.on !== false,
                rot: Number(x.r || 0) % 360
              }));
            if (valid.length > 0) restored = valid;
          }
        } catch {}
      }

      pmState.pages = restored || Array.from({ length: pmState.totalSourcePages }, (_, i) => ({
        uid: 'pg_' + i + '_' + Date.now(),
        srcIndex: i,
        selected: true,
        rot: 0
      }));
      pmRenderGrid();
    }

    function formatFileEditBadge(f) {
      if (!f) return '';
      if (f.pagePlanJson) {
        try {
          const arr = JSON.parse(f.pagePlanJson);
          if (Array.isArray(arr) && arr.length > 0) {
            const sel = arr.filter(x => x && x.on !== 0 && x.on !== false).length;
            return '<span class="badge badge-green" style="font-size:10px;padding:1px 6px;">✓ ' + sel + '/' + arr.length + ' pages</span>';
          }
        } catch {}
      }
      if (f.conversionStatus === 'edited' && f.convertedUrl) {
        return '<span class="badge badge-green" style="font-size:10px;padding:1px 6px;">✓ Edited</span>';
      }
      if (f.conversionStatus === 'converted') return '<span>• PDF Ready</span>';
      return '';
    }

    function pmRenderGrid() {
      pmUpdateButtons();
      const grid = document.getElementById('pmGrid');
      if (!grid) return;
      if (pmState.pages.length === 0) {
        grid.innerHTML = '<div style="grid-column:1/-1;text-align:center;padding:50px;color:var(--muted);">No pages left. Click <strong>↩ Undo</strong> or <strong>↻ Reset</strong> to restore pages.</div>';
        return;
      }

      grid.innerHTML = pmState.pages.map((p, idx) => {
        const cls = 'pm-card ' + (p.selected ? 'selected' : 'excluded');
        return '<div class="' + cls + '" draggable="true" data-uid="' + p.uid + '" ' +
          'ondragstart="pmDragStart(event,\\'' + p.uid + '\\')" ' +
          'ondragover="pmDragOver(event,\\'' + p.uid + '\\')" ' +
          'ondragleave="this.classList.remove(\\'pm-drag-over\\')" ' +
          'ondrop="pmDrop(event,\\'' + p.uid + '\\')">' +
          '<div class="pm-thumb-box" onclick="pmTogglePage(\\'' + p.uid + '\\')" title="Click to include/exclude Page ' + (p.srcIndex + 1) + '">' +
            '<canvas id="pm_cv_' + p.uid + '" style="transform:rotate(' + p.rot + 'deg);"></canvas>' +
            '<span class="pm-page-pill">#' + (idx + 1) + (p.srcIndex !== idx ? ' (orig p.' + (p.srcIndex + 1) + ')' : '') + '</span>' +
            '<span class="pm-excl-pill">Excluded</span>' +
          '</div>' +
          '<div class="pm-card-bar">' +
            '<label style="display:flex;align-items:center;gap:4px;font-size:11px;font-weight:700;cursor:pointer;">' +
              '<input type="checkbox" ' + (p.selected ? 'checked' : '') + ' onchange="pmTogglePage(\\'' + p.uid + '\\')" style="width:14px;height:14px;cursor:pointer;"> p.' + (p.srcIndex + 1) +
            '</label>' +
            '<div style="display:flex;gap:3px;">' +
              '<button class="pm-icon-btn" onclick="pmZoomPage(' + p.srcIndex + ',' + p.rot + ')" title="Inspect full page">🔍</button>' +
              '<button class="pm-icon-btn" onclick="pmRotatePage(\\'' + p.uid + '\\',90)" title="Rotate 90°">↻</button>' +
              '<button class="pm-icon-btn" onclick="pmDuplicatePage(\\'' + p.uid + '\\')" title="Duplicate page">⧉</button>' +
              '<button class="pm-icon-btn" style="color:var(--danger);" onclick="pmDeletePage(\\'' + p.uid + '\\')" title="Remove page">🗑</button>' +
            '</div>' +
          '</div>' +
        '</div>';
      }).join('');

      pmRenderThumbnailsAsync();
    }

    async function pmRenderThumbnailsAsync() {
      const doc = pmState.pdfJsDoc;
      if (!doc) return;
      const snapshotPages = pmState.pages.slice();
      for (const p of snapshotPages) {
        const cv = document.getElementById('pm_cv_' + p.uid);
        if (!cv) continue;
        if (pmState.thumbCache.has(p.srcIndex)) {
          const cached = pmState.thumbCache.get(p.srcIndex);
          cv.width = cached.width;
          cv.height = cached.height;
          cv.getContext('2d').drawImage(cached, 0, 0);
          continue;
        }
        try {
          const page = await doc.getPage(p.srcIndex + 1);
          const vp1 = page.getViewport({ scale: 1 });
          const scale = Math.min(220 / vp1.width, 300 / vp1.height);
          const vp = page.getViewport({ scale });
          const off = document.createElement('canvas');
          off.width = Math.round(vp.width);
          off.height = Math.round(vp.height);
          await page.render({ canvasContext: off.getContext('2d'), viewport: vp }).promise;
          pmState.thumbCache.set(p.srcIndex, off);
          const liveCv = document.getElementById('pm_cv_' + p.uid);
          if (liveCv) {
            liveCv.width = off.width;
            liveCv.height = off.height;
            liveCv.getContext('2d').drawImage(off, 0, 0);
          }
        } catch {}
      }
    }

    function pmTogglePage(uid) {
      pmPushUndo();
      const item = pmState.pages.find(p => p.uid === uid);
      if (item) item.selected = !item.selected;
      pmRenderGrid();
    }

    function pmRotatePage(uid, delta) {
      pmPushUndo();
      const item = pmState.pages.find(p => p.uid === uid);
      if (item) item.rot = (item.rot + delta + 360) % 360;
      pmRenderGrid();
    }

    function pmDuplicatePage(uid) {
      pmPushUndo();
      const idx = pmState.pages.findIndex(p => p.uid === uid);
      if (idx < 0) return;
      const orig = pmState.pages[idx];
      pmState.pages.splice(idx + 1, 0, {
        uid: 'pg_dup_' + orig.srcIndex + '_' + Date.now() + '_' + Math.random().toString(36).slice(2, 5),
        srcIndex: orig.srcIndex,
        selected: true,
        rot: orig.rot
      });
      pmRenderGrid();
    }

    function pmDeletePage(uid) {
      pmPushUndo();
      pmState.pages = pmState.pages.filter(p => p.uid !== uid);
      pmRenderGrid();
    }

    function pmRemoveUnselected() {
      const kept = pmState.pages.filter(p => p.selected);
      if (kept.length === pmState.pages.length) return;
      pmPushUndo();
      pmState.pages = kept;
      pmRenderGrid();
    }

    function pmApplyRange() {
      const spec = (document.getElementById('pmRangeInput').value || '').trim();
      if (!spec) { pmPreset('all'); return; }
      const matched = new Set(parsePageRangeSpec(spec, pmState.pages.length));
      if (matched.size === 0) {
        alert('No pages matched "' + spec + '". Try e.g. 1-3, 5, 8-10');
        return;
      }
      pmPushUndo();
      pmState.pages.forEach((p, idx) => { p.selected = matched.has(idx); });
      pmRenderGrid();
    }

    function pmPreset(kind) {
      pmPushUndo();
      pmState.pages.forEach((p, idx) => {
        if (kind === 'all') p.selected = true;
        else if (kind === 'none') p.selected = false;
        else if (kind === 'odd') p.selected = ((idx + 1) % 2 === 1);
        else if (kind === 'even') p.selected = ((idx + 1) % 2 === 0);
        else if (kind === 'first') p.selected = (idx === 0);
        else if (kind === 'invert') p.selected = !p.selected;
      });
      pmRenderGrid();
    }

    function pmDragStart(ev, uid) {
      pmState.dragUid = uid;
      ev.dataTransfer.effectAllowed = 'move';
      try { ev.dataTransfer.setData('text/plain', uid); } catch {}
    }

    function pmDragOver(ev, targetUid) {
      if (pmState.dragUid && pmState.dragUid !== targetUid) {
        ev.preventDefault();
        if (ev.currentTarget) ev.currentTarget.classList.add('pm-drag-over');
      }
    }

    function pmDrop(ev, targetUid) {
      if (ev.currentTarget) ev.currentTarget.classList.remove('pm-drag-over');
      if (!pmState.dragUid || pmState.dragUid === targetUid) return;
      ev.preventDefault();
      const fromIdx = pmState.pages.findIndex(p => p.uid === pmState.dragUid);
      const toIdx = pmState.pages.findIndex(p => p.uid === targetUid);
      pmState.dragUid = null;
      if (fromIdx < 0 || toIdx < 0) return;
      pmPushUndo();
      const [moved] = pmState.pages.splice(fromIdx, 1);
      pmState.pages.splice(toIdx, 0, moved);
      pmRenderGrid();
    }

    async function pmZoomPage(srcIndex, rot) {
      if (!pmState.pdfJsDoc) return;
      document.getElementById('pmZoomTitle').textContent = pmState.fileName + ' — Page ' + (srcIndex + 1);
      document.getElementById('pmZoomOverlay').classList.add('open');
      const page = await pmState.pdfJsDoc.getPage(srcIndex + 1);
      const vp = page.getViewport({ scale: 1.5, rotation: (page.rotate + rot) % 360 });
      const cv = document.getElementById('pmZoomCanvas');
      cv.width = vp.width;
      cv.height = vp.height;
      await page.render({ canvasContext: cv.getContext('2d'), viewport: vp }).promise;
    }

    async function pmBuildSelectedPdfBytes() {
      const chosen = pmState.pages.filter(p => p.selected);
      if (chosen.length === 0) throw new Error('Select at least 1 page first.');
      const srcDoc = await window.PDFLib.PDFDocument.load(pmState.pdfBytes, { ignoreEncryption: true });
      const outDoc = await window.PDFLib.PDFDocument.create();
      for (const p of chosen) {
        const [copied] = await outDoc.copyPages(srcDoc, [p.srcIndex]);
        if (p.rot) {
          const curRot = copied.getRotation().angle || 0;
          copied.setRotation(window.PDFLib.degrees((curRot + p.rot) % 360));
        }
        outDoc.addPage(copied);
      }
      return { bytes: await outDoc.save(), count: chosen.length };
    }

    async function pmSaveOutput(andPrint) {
      const btn = document.getElementById(andPrint ? 'pmPrintBtn' : 'pmSaveBtn');
      const origTxt = btn ? btn.textContent : '';
      if (btn) { btn.disabled = true; btn.textContent = '⏳ Building PDF...'; }
      try {
        const { bytes, count } = await pmBuildSelectedPdfBytes();
        const base = (pmState.fileName || 'document').replace(/\\.[^.]+$/, '');
        const outName = base + '_p' + count + '_' + Date.now().toString().slice(-4) + '.pdf';

        if (pmState.mode === 'tray') {
          const res = await fetch('/api/tray/add-upload', {
            method: 'POST',
            headers: { 'Content-Type': 'application/pdf', 'X-File-Name': encodeURIComponent(outName) },
            body: bytes
          });
          const data = await res.json();
          if (!res.ok || !data.success) throw new Error(data.error || 'Could not save to Tray');
          await fetchCloudState(true);
          closeStudioModal('pageManagerModal');
          if (andPrint) {
            directPrint('/api/tray/file?id=' + encodeURIComponent(data.item.id), outName);
          } else {
            showToast('✓ Saved ' + count + ' selected page(s) to Holding Tray (' + outName + ')', null, null, 5000);
          }
          return;
        }

        const compactPlan = JSON.stringify(pmState.pages.map(p => ({ s: p.srcIndex, on: p.selected ? 1 : 0, r: p.rot || 0 })));
        const res = await fetch('/api/file/save-edited-pdf', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/pdf',
            'X-File-Id': pmState.fileId || '',
            'X-Output-Name': outName,
            'X-Page-Plan': encodeURIComponent(compactPlan)
          },
          body: bytes
        });
        const data = await res.json();
        if (!res.ok || !data.success) throw new Error(data.error || 'Save failed');

        const savedFileId = pmState.fileId;
        const savedFileName = pmState.fileName;
        pushGlobalUndo('Page Selection on "' + savedFileName + '"', async () => {
          await fetch('/api/file/reset-edited', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ fileId: savedFileId })
          });
          await fetchCloudState(true);
        });

        await fetchCloudState(true);
        closeStudioModal('pageManagerModal');
        if (andPrint) {
          directPrint(data.url, data.filename);
          showToast('🖨 Printing ' + count + ' selected page(s) of ' + savedFileName, '↩ Revert Original', async () => {
            await fetch('/api/file/reset-edited', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ fileId: savedFileId }) });
            await fetchCloudState(true);
          }, 8000);
        } else {
          showToast('✓ Saved ' + count + ' selected page(s) for ' + savedFileName + ' (used in Print & Merge)', '↩ Undo', async () => {
            await fetch('/api/file/reset-edited', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ fileId: savedFileId }) });
            await fetchCloudState(true);
          }, 8000);
          if (pmState.returnToMerge && mergeQueue.submissionId) openMergeModal(mergeQueue.submissionId);
        }
      } catch (err) {
        alert('Page Manager error: ' + err.message);
      } finally {
        if (btn) { btn.disabled = false; btn.textContent = origTxt; }
      }
    }

    async function pmSendToTray() {
      const btn = document.getElementById('pmToTrayBtn');
      const origTxt = btn ? btn.textContent : '';
      if (btn) { btn.disabled = true; btn.textContent = '⏳ Sending...'; }
      try {
        const { bytes, count } = await pmBuildSelectedPdfBytes();
        const base = (pmState.fileName || 'document').replace(/\\.[^.]+$/, '');
        const outName = base + '_selected_' + count + 'p.pdf';
        const res = await fetch('/api/tray/add-upload', {
          method: 'POST',
          headers: { 'Content-Type': 'application/pdf', 'X-File-Name': encodeURIComponent(outName) },
          body: bytes
        });
        const data = await res.json();
        if (!res.ok || !data.success) throw new Error(data.error || 'Could not send to Holding Tray');
        trayCollapsed = false;
        await fetchCloudState(true);
        closeStudioModal('pageManagerModal');
        showToast('📌 Sent ' + count + ' selected page(s) to Holding Tray (' + outName + ')', null, null, 5000);
      } catch (err) {
        alert('Send to Tray failed: ' + err.message);
      } finally {
        if (btn) { btn.disabled = false; btn.textContent = origTxt; }
      }
    }

    // ------------------------------------------------------------------------
    // 8. BRANDED COUNTER QR POSTER GENERATOR (Engraved Shop Name + Live QR)
    // ------------------------------------------------------------------------
    let qrPosterState = {
      shopName: '',
      customNameTouched: false,
      mode: 'permanent',
      lastUrl: '',
      templateImg: null
    };

    function qrPosterUpdateName(val, permUrl, directUrl) {
      qrPosterState.shopName = val;
      qrPosterState.customNameTouched = true;
      const targetUrl = qrPosterState.mode === 'direct' && directUrl ? directUrl : permUrl;
      renderQrPosterCanvas(val, targetUrl);
    }

    function qrPosterUpdateMode(mode, permUrl, directUrl) {
      qrPosterState.mode = mode;
      const targetUrl = mode === 'direct' && directUrl ? directUrl : permUrl;
      renderQrPosterCanvas(qrPosterState.shopName, targetUrl);
    }

    async function loadQrPosterTemplateImage() {
      if (qrPosterState.templateImg && qrPosterState.templateImg.complete && qrPosterState.templateImg.naturalWidth > 0) {
        return qrPosterState.templateImg;
      }
      const urls = ['/assets/scandrop-qr-poster.png', 'https://scandrop-xi.vercel.app/assets/scandrop-qr-poster.png'];
      for (const u of urls) {
        try {
          const img = new Image();
          img.crossOrigin = 'anonymous';
          await new Promise((res, rej) => { img.onload = res; img.onerror = rej; img.src = u; });
          qrPosterState.templateImg = img;
          return img;
        } catch {}
      }
      throw new Error('Could not load QR poster template image.');
    }

    async function renderQrPosterCanvas(shopName, qrUrl) {
      const canvas = document.getElementById('qrPosterCanvas');
      if (!canvas) return;
      qrPosterState.lastUrl = qrUrl || qrPosterState.lastUrl;
      const W = 1448, H = 2048;
      canvas.width = W;
      canvas.height = H;
      const ctx = canvas.getContext('2d');
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(0, 0, W, H);

      try {
        const tpl = await loadQrPosterTemplateImage();
        ctx.drawImage(tpl, 0, 0, W, H);
      } catch {
        ctx.fillStyle = '#EAF5FF';
        ctx.fillRect(0, 0, W, H);
      }

      // 1. Engrave Shop Name centered cleanly inside the ice-blue pill (413/724, 149/1024, max width 344/724)
      const label = String(shopName || 'YOUR SHOP NAME').trim().toUpperCase() || 'YOUR SHOP NAME';
      const pillCx = (413 / 724) * W;
      const pillCy = (149.5 / 1024) * H;
      const maxTextW = (338 / 724) * W;
      let fontSize = 58;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      while (fontSize > 24) {
        ctx.font = 'italic 900 ' + fontSize + 'px "Segoe UI", "Inter", Arial, sans-serif';
        if (ctx.measureText(label).width <= maxTextW) break;
        fontSize -= 2;
      }
      // Subtle engraved bevel highlight + crisp deep-navy lettering
      ctx.fillStyle = 'rgba(255, 255, 255, 0.78)';
      ctx.fillText(label, pillCx + 1.5, pillCy + 2.5, maxTextW);
      ctx.fillStyle = '#0B2545';
      ctx.fillText(label, pillCx, pillCy, maxTextW);

      // 2. Render vector-crisp QR code centered inside the white SCAN HERE card (361.5/724, 541/1024)
      const qrBoxSize = Math.round((252 / 724) * W);
      const qrLeft = Math.round((361.5 / 724) * W - qrBoxSize / 2);
      const qrTop = Math.round((541 / 1024) * H - qrBoxSize / 2);

      // Pure white quiet zone
      ctx.fillStyle = '#FFFFFF';
      ctx.fillRect(qrLeft - 14, qrTop - 14, qrBoxSize + 28, qrBoxSize + 28);

      const urlToEncode = qrUrl || 'https://scandrop-xi.vercel.app';
      if (typeof window.qrcode === 'function') {
        const qr = window.qrcode(0, 'M');
        qr.addData(urlToEncode);
        qr.make();
        const count = qr.getModuleCount();
        const cell = qrBoxSize / count;
        ctx.fillStyle = '#071426';
        for (let r = 0; r < count; r++) {
          for (let c = 0; c < count; c++) {
            if (qr.isDark(r, c)) {
              const x = Math.floor(qrLeft + c * cell);
              const y = Math.floor(qrTop + r * cell);
              const cw = Math.ceil(qrLeft + (c + 1) * cell) - x;
              const ch = Math.ceil(qrTop + (r + 1) * cell) - y;
              ctx.fillRect(x, y, cw, ch);
            }
          }
        }
      } else {
        // Fallback if offline before CDN loaded
        try {
          const qrImg = new Image();
          qrImg.crossOrigin = 'anonymous';
          await new Promise((res, rej) => {
            qrImg.onload = res;
            qrImg.onerror = rej;
            qrImg.src = 'https://api.qrserver.com/v1/create-qr-code/?size=600x600&margin=0&data=' + encodeURIComponent(urlToEncode);
          });
          ctx.drawImage(qrImg, qrLeft, qrTop, qrBoxSize, qrBoxSize);
        } catch {}
      }
    }

    function getCleanPosterFileBase() {
      const raw = (qrPosterState.shopName || 'ScanDrop_Shop').trim();
      const safe = raw.replace(/[^a-zA-Z0-9_-]+/g, '_').replace(/^_+|_+$/g, '') || 'ScanDrop_Shop';
      return safe + '_ScanDrop_QR_Poster';
    }

    async function downloadQrPoster(format) {
      const canvas = document.getElementById('qrPosterCanvas');
      if (!canvas) return;
      const base = getCleanPosterFileBase();

      if (format === 'png') {
        const dataUrl = canvas.toDataURL('image/png');
        const a = document.createElement('a');
        a.href = dataUrl;
        a.download = base + '.png';
        document.body.appendChild(a);
        a.click();
        a.remove();
        showToast('✓ Downloaded ' + base + '.png (High-Res 1448×2048)!', null, null, 4500);
        return;
      }

      // Print-ready A4 PDF via pdf-lib
      if (!window.PDFLib) { alert('PDF library still loading — try PNG download or refresh.'); return; }
      const jpgUrl = canvas.toDataURL('image/jpeg', 0.96);
      const jpgBytes = Uint8Array.from(atob(jpgUrl.split(',')[1]), c => c.charCodeAt(0));
      const doc = await window.PDFLib.PDFDocument.create();
      const pageW = 595.28, pageH = 841.89; // A4 pt
      const page = doc.addPage([pageW, pageH]);
      const embedded = await doc.embedJpg(jpgBytes);
      const scale = Math.min(pageW / canvas.width, pageH / canvas.height);
      const drawW = canvas.width * scale;
      const drawH = canvas.height * scale;
      page.drawImage(embedded, {
        x: (pageW - drawW) / 2,
        y: (pageH - drawH) / 2,
        width: drawW,
        height: drawH
      });
      const pdfBytes = await doc.save();
      const blob = new Blob([pdfBytes], { type: 'application/pdf' });
      const blobUrl = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = blobUrl;
      a.download = base + '_A4.pdf';
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => { try { URL.revokeObjectURL(blobUrl); } catch {} }, 30000);
      showToast('✓ Downloaded A4 Print PDF (' + base + '_A4.pdf)!', null, null, 4500);
    }

    async function printQrPoster() {
      const canvas = document.getElementById('qrPosterCanvas');
      if (!canvas) return;
      const dataUrl = canvas.toDataURL('image/png');
      const frame = document.getElementById('directPrintFrame');
      if (!frame) return;
      frame.onload = () => {
        setTimeout(() => {
          try { frame.contentWindow.focus(); frame.contentWindow.print(); } catch {}
        }, 120);
      };
      frame.srcdoc = '<!doctype html><html><head><style>@page{size:A4 portrait;margin:0;}html,body{margin:0;padding:0;width:100%;height:100%;display:flex;align-items:center;justify-content:center;background:#fff;}img{max-width:100%;max-height:100vh;width:auto;height:100vh;object-fit:contain;display:block;}</style></head><body><img src="' + dataUrl + '"></body></html>';
      showToast('🖨 Sending branded QR poster to print dialog...', null, null, 3500);
    }

    // ========================================================================
    // 10. LOCAL PC STORAGE BROWSER (C:\Users\<User>\Downloads, Desktop, Docs, Pictures)
    // ========================================================================
    const localPcState = {
      folder: 'downloads',
      sub: '',
      q: '',
      loading: false,
      loadedOnce: false,
      data: null,
      error: null
    };

    let localPcSearchTimer = null;

    function setLocalPcFolder(folderKey) {
      localPcState.folder = folderKey || 'downloads';
      localPcState.sub = '';
      loadLocalPcFiles();
    }

    function openLocalPcSubfolder(relPath) {
      localPcState.sub = relPath || '';
      loadLocalPcFiles();
    }

    function onLocalPcSearchInput(val) {
      localPcState.q = val || '';
      if (localPcSearchTimer) clearTimeout(localPcSearchTimer);
      localPcSearchTimer = setTimeout(() => loadLocalPcFiles(), 180);
    }

    async function loadLocalPcFiles() {
      localPcState.loading = true;
      localPcState.error = null;
      renderLocalPcBrowser();
      try {
        const url = '/api/local-files?folder=' + encodeURIComponent(localPcState.folder) +
          '&sub=' + encodeURIComponent(localPcState.sub || '') +
          '&q=' + encodeURIComponent(localPcState.q || '');
        const res = await fetch(url);
        const data = await res.json();
        if (!res.ok || !data.success) throw new Error(data.error || 'Could not list local folder');
        localPcState.data = data;
        localPcState.loadedOnce = true;
      } catch (err) {
        localPcState.error = err.message || 'Local folder access error';
      } finally {
        localPcState.loading = false;
        renderLocalPcBrowser();
      }
    }

    function renderLocalPcBrowser() {
      const host = document.getElementById('localPcBrowserBody');
      const pathTag = document.getElementById('localPcPathTag');
      const backBtn = document.getElementById('localPcUpBtn');
      if (!host) return;

      document.querySelectorAll('.local-folder-pill').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.folder === localPcState.folder);
      });

      const d = localPcState.data;
      if (pathTag) {
        pathTag.textContent = (d && d.currentPath) ? d.currentPath : ('C:\\\\Users\\\\...\\\\' + localPcState.folder);
      }
      if (backBtn) {
        if (d && d.subPath) {
          backBtn.style.display = 'inline-flex';
          backBtn.onclick = () => openLocalPcSubfolder(d.parentSubPath || '');
        } else {
          backBtn.style.display = 'none';
        }
      }

      if (localPcState.loading && !d) {
        host.innerHTML = '<div style="padding:26px;text-align:center;color:var(--muted);font-size:12.5px;">Loading local Windows folder...</div>';
        return;
      }
      if (localPcState.error) {
        host.innerHTML = '<div style="padding:20px;text-align:center;color:var(--danger);font-size:12.5px;">⚠️ ' + esc(localPcState.error) + '</div>';
        return;
      }

      const items = (d && d.items) || [];
      if (items.length === 0) {
        host.innerHTML = '<div style="padding:28px;text-align:center;color:var(--muted);font-size:12.5px;">No matching files found in this folder.</div>';
        return;
      }

      const activeSubCode = selectedSubId || (((cloudState && cloudState.submissions) || []).find(s => s.status !== 'expired') || {}).receiptCode || '';

      host.innerHTML = items.map(it => {
        const safeRel = esc(it.relPath).replace(/'/g, '&#39;');
        const safeName = esc(it.name).replace(/'/g, '&#39;');
        if (it.isDir) {
          return '<div class="local-file-row is-dir" onclick="openLocalPcSubfolder(\\'' + safeRel + '\\')">' +
            '<div style="display:flex;align-items:center;gap:10px;min-width:0;flex:1;">' +
              '<div class="file-type-box ft-other" style="background:linear-gradient(135deg,#FEF3C7,#FDE68A);color:#92400E;">📁</div>' +
              '<div style="min-width:0;">' +
                '<div style="font-size:13px;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">' + esc(it.name) + '</div>' +
                '<div style="font-size:11px;color:var(--muted);">Folder • ' + timeAgo(it.modifiedUtc) + '</div>' +
              '</div>' +
            '</div>' +
            '<span style="font-size:11.5px;font-weight:700;color:var(--primary);">Open →</span>' +
          '</div>';
        }

        const [cls, lbl] = getFileBadge(it.ext);
        const isPdfOrImg = it.canPreview;
        return '<div class="local-file-row">' +
          '<div style="display:flex;align-items:center;gap:10px;min-width:0;flex:1;">' +
            '<div class="file-type-box ' + cls + '">' + lbl + '</div>' +
            '<div style="min-width:0;">' +
              '<div style="font-size:13px;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" title="' + esc(it.name) + '">' + esc(it.name) + '</div>' +
              '<div style="font-size:11px;color:var(--muted);">' + formatSize(it.sizeBytes) + ' • ' + timeAgo(it.modifiedUtc) + '</div>' +
            '</div>' +
          '</div>' +
          '<div style="display:flex;gap:5px;flex-wrap:wrap;align-items:center;">' +
            (activeSubCode ? '<button class="btn btn-sync" style="padding:5px 9px;font-size:11px;" onclick="runLocalPcAction(\\'' + safeRel + '\\',\\'add-to-submission\\',\\'' + esc(activeSubCode) + '\\')" title="Copy into submission ' + esc(activeSubCode) + '">+ Add to ' + esc(activeSubCode) + '</button>' : '') +
            '<button class="btn btn-outline" style="padding:5px 9px;font-size:11px;" onclick="runLocalPcAction(\\'' + safeRel + '\\',\\'pin-tray\\')" title="Pin to Holding Tray">📌 Tray</button>' +
            (isPdfOrImg ? '<button class="btn btn-outline" style="padding:5px 9px;font-size:11px;" onclick="previewUrl(\\'' + esc(it.streamUrl) + '\\',\\'' + safeName + '\\',\\'' + esc(it.ext) + '\\',null)" title="Preview">👁 Preview</button>' : '') +
            '<button class="btn" style="padding:5px 9px;font-size:11px;" onclick="printLocalPcFile(\\'' + safeRel + '\\',\\'' + esc(it.streamUrl) + '\\',\\'' + safeName + '\\',\\'' + esc(it.ext) + '\\')" title="Print">🖨 Print</button>' +
            '<button class="btn btn-outline" style="padding:5px 9px;font-size:11px;" onclick="runLocalPcAction(\\'' + safeRel + '\\',\\'open\\')" title="Open in default Windows app">↗ Open</button>' +
          '</div>' +
        '</div>';
      }).join('');
    }

    async function runLocalPcAction(relPath, action, subCode) {
      try {
        const res = await fetch('/api/local-file/action', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            folder: localPcState.folder,
            path: relPath || localPcState.sub || '',
            action,
            submissionId: subCode || selectedSubId || ''
          })
        });
        const data = await res.json();
        if (!res.ok || !data.success) {
          alert(data.error || 'Local file action failed.');
          return;
        }
        if (action === 'add-to-submission') {
          await fetchCloudState(true);
          showToast('✓ Added "' + (data.filename || 'file') + '" from PC ' + localPcState.folder + ' into ' + (data.receiptCode || subCode), null, null, 4500);
        } else if (action === 'pin-tray') {
          trayCollapsed = false;
          await fetchCloudState(true);
          showToast('📌 Kept "' + (data.filename || 'file') + '" in Holding Tray', null, null, 3500);
        } else if (action === 'print') {
          showToast('🖨 ' + (data.message || 'Sent file to Windows printer.'), null, null, 4000);
        }
      } catch (err) {
        alert('Error: ' + err.message);
      }
    }

    function printLocalPcFile(relPath, streamUrl, name, ext) {
      const e = (ext || '').toLowerCase();
      if (e === '.pdf' || ['.jpg', '.jpeg', '.png', '.webp', '.bmp', '.gif'].includes(e)) {
        directPrint(streamUrl, name);
      } else {
        runLocalPcAction(relPath, 'print');
      }
    }
`;

module.exports = { studioCss, studioHtml, studioJs };
