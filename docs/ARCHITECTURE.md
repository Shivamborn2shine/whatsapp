# Scan Drop — Architecture, Security & Implementation Plan (PRD v2.0)

**Version:** 4.0.0 (PRD v2.0 Compliant — Internet-Based, Local-First Architecture)  
**Tagline:** *Scan. Send. Done.*  
**Platform:** Windows 10/11 (x64), Android Chrome & iOS Safari  

---

## 1. Repository Assessment

Inspection of `d:\Business 2027\Scan Drop` revealed two existing tracks:

1. **Legacy Cloud-Sync Prototype (`desktop/`, `server/`, `shared/`, `PRD.md` v3.0):**
   - Electron + Express + Node SQLite prototype built for a cloud-first synchronization model.
2. **Active Production Native Station (`native/*.cs`, `ScanDrop.exe` v3.3.1, `build_netlify.js`, `workspace_template.js`, `netlify/functions/api.mjs`):**
   - Zero-dependency `.NET Framework 4.8` (`csc.exe` x64) standalone Windows executable with `LocalHttpServer`, `TunnelManager` (Cloudflare Named/Quick + OpenSSH + Pinggy 4-tier cascade with 25s health watchdog), `StationForm` WinForms dashboard, `AutoUpdater`, and mobile-first customer web portal.

### Critical Gaps Identified Against PRD v2.0
| Area | Finding in v3.3.1 | Resolution in v4.0.0 |
|---|---|---|
| **Public vs Admin Isolation (Sec 3 & 11)** | Single listener on port `3838` was tunneled publicly, exposing `/workspace/`, `/api/state`, `/api/file`, `/api/storage/clear`, and `/api/tunnel/config` over the internet. | **Strict Dual-Port Isolation:** Public Receiver on `127.0.0.1:8787` (tunneled) mounts *only* customer upload & receipt-status routes. Local Admin/Workspace on `127.0.0.1:3838` is **never tunneled**. |
| **Operational Database (Sec 10)** | Flat JSON file (`ScanDrop_Local_Inbox/state.json`). | Real local **SQLite (WAL mode)** via Windows built-in `C:\Windows\System32\winsqlite3.dll` implementing all 7 PRD tables (`shops`, `submissions`, `files`, `upload_sessions`, `upload_parts`, `app_settings`, `activity_logs`) with foreign keys & indexes. |
| **Storage Layout & Streaming (Sec 6 & 8.5)** | `ScanDrop_Local_Inbox/SD-XXXX/` with 10 MB chunks buffered in memory. | Standardized directory layout (`database/`, `uploads/temp/`, `uploads/received/`, `uploads/quarantine/`, `converted/`, `previews/`, `logs/`, `config/`), configurable destination, **4 MiB (< 8 MiB) chunked streaming**, disk-space guard, safe internal GUID filenames, atomic move. |
| **Upload Security (Sec 11)** | Client-side extension block only; no upload tokens, rate limiting, magic-byte validation, or duplicate-submit protection. | Cryptographic expiring upload session tokens (SHA-256 hashed in DB), per-IP sliding-window rate limiter, server-side **magic-byte + allowlist validator**, idempotency key deduplication, **Pause Receiving** switch, DPAPI-encrypted secrets. |
| **Desktop & File Operations (Sec 5 & 7)** | Missing Open, Open With, Rename, Save As / Permanent Export, Reveal in Folder, Office-to-PDF worker, Windows Printers list, Tray notifications, Light/Dark theme, First-Run Wizard. | Implemented native Windows actions (`Process.Start`, `OpenAs_RunDLL`, `explorer /select`, Save/Export, `PrinterSettings.InstalledPrinters`), isolated conversion worker (Native C# Image→PDF + Headless LibreOffice Office→PDF with timeout), Windows tray notifications, Light/Dark theme, and First-Run Wizard. |
| **Customer UX (Sec 4 & 12)** | 4-digit receipt, no total-size indicator, `alert()` errors, no chunk retry. | Tagline **Scan. Send. Done.**, "Drop your files here", individual + total size display, inline warnings, 4 MiB resumable chunks with retry, **6-char receipt code** + timestamp shown only after receiver commit. |

---

## 2. Final Implementation Architecture

```text
[Customer Smartphone (Android Chrome / iOS Safari)]
        │ HTTPS (No login, no app install)
        ▼
[Cloudflare Tunnel (Named in Prod / Quick in Dev) | Modular Fallback: OpenSSH / Pinggy]
        │ Forwards ONLY to Public Receiver Port (127.0.0.1:8787)
        ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                    ScanDrop.exe (Windows Modular Monolith)              │
│                                                                         │
│  ┌────────────────────────────────┐  ┌───────────────────────────────┐  │
│  │ PublicUploadServer (:8787)     │  │ LocalAdminServer (:3838)      │  │
│  │ [TUNNELED — PUBLIC ONLY]       │  │ [LOCALHOST ONLY — NEVER       │  │
│  │ • Customer Upload Web Page     │  │  TUNNELED]                    │  │
│  │ • Upload Sessions & 4MiB Chunks│  │ • Shopkeeper Dashboard UI     │  │
│  │ • Rate Limit + Magic Bytes     │  │ • Preview/Open/OpenWith/Print │  │
│  │ • Receipt Status (no files)    │  │ • Rename/Export/Reveal/Delete │  │
│  └───────────────┬────────────────┘  │ • Convert to PDF / Printers   │  │
│                  │                   │ • Settings / Storage / Tunnel │  │
│                  ▼                   └──────────────┬────────────────┘  │
│  ┌──────────────────────────────────────────────────┴────────────────┐  │
│  │ TransferService • FileValidator • UploadSecurityGuard             │  │
│  │ ConversionEngine (Native Image→PDF + Isolated LibreOffice Worker) │  │
│  │ StorageManager (24h Retention, Crash Recovery, Disk Space Guard)  │  │
│  │ SqliteDatabase (Windows winsqlite3.dll — WAL Mode, 7 Tables)      │  │
│  │ SecretProtector (Windows DPAPI) • TunnelManager (Swappable)       │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│                                                                         │
│  WinForms Desktop Shell (StationForm + FirstRunWizardForm + System Tray)│
└─────────────────────────────────────────────────────────────────────────┘
        │
        ▼
[Local Windows Storage: C:\ProgramData\ScanDrop\ (or configurable path)]
  ├── database/scandrop.db
  ├── uploads/{temp, received, quarantine}/
  ├── converted/
  ├── previews/
  ├── logs/
  └── config/
```

**Why this stack:** Reuses the proven standalone C# `.NET 4.8` (`csc.exe`) zero-dependency deployment (`ScanDrop.exe`), adds real ACID SQLite via Windows 10/11 built-in `winsqlite3.dll`, Windows DPAPI secret encryption (`System.Security`), GDI+ image-to-PDF + headless LibreOffice conversion, and keeps modular provider boundaries so Cloudflare can be replaced without touching core transfer/storage code.

---

## 3. Module Structure

```text
native/
  Core/
    Models.cs                 # Domain DTOs & statuses (uploading, processing, received, failed, printed, archived, expired)
    SecretProtector.cs        # Windows DPAPI encryption for tunnel tokens & secrets
    AppLogger.cs              # Structured daily rotating operational logs (<DataDir>/logs/)
  Storage/
    StoragePaths.cs           # Standardized directory hierarchy + path traversal guard
    SqliteDatabase.cs         # winsqlite3.dll P/Invoke WAL SQLite engine + schema migrations + crash recovery
    StorageManager.cs         # Disk-space checks, safe internal filenames, retention purge, storage metrics & cleanup
  Transfers/
    FileValidator.cs          # Allowlist + magic-byte signature inspection (PDF, Office, Images, TXT; blocks PE/scripts)
    UploadSecurityGuard.cs    # Sliding-window IP rate limiter, session token issuance/verification, idempotency, pause flag
    TransferService.cs        # Session init, 4 MiB chunk streaming, SHA-256 assembly, atomic commit, 6-char receipt
  Conversion/
    ConversionEngine.cs       # Native Image→PDF (single & multi-image) + isolated LibreOffice headless worker (45s timeout)
  Networking/
    TunnelManager.cs          # Swappable transport bridge targeting ONLY PublicUploadServer (:8787)
  Api/
    PublicUploadServer.cs     # Public-only HTTP receiver (:8787) + embedded customer upload portal
    LocalAdminServer.cs       # Localhost-only Admin & Workspace API (:3838) + native Windows OS actions
  Ui/
    StationForm.cs            # Native Station control UI, connection monitor, Pause/Resume, tray alerts
    FirstRunWizardForm.cs     # First-run setup wizard (shop name, storage path, retention, upload cap, test)
  AutoUpdater.cs              # SHA-256 verified OTA updater
  Program.cs                  # Entrypoint, single-instance mutex, crash recovery, --self-test
```

---

## 4. Database Schema (SQLite — `database/scandrop.db`)

Engine configuration: `PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;`

```sql
CREATE TABLE IF NOT EXISTS shops (
  shop_id TEXT PRIMARY KEY,
  shop_name TEXT NOT NULL,
  slug TEXT NOT NULL UNIQUE,
  created_at TEXT NOT NULL,
  settings TEXT NOT NULL DEFAULT '{}'
);

CREATE TABLE IF NOT EXISTS submissions (
  submission_id TEXT PRIMARY KEY,
  shop_id TEXT NOT NULL,
  receipt_code TEXT NOT NULL UNIQUE,
  customer_name TEXT NOT NULL DEFAULT 'Anonymous',
  customer_note TEXT NOT NULL DEFAULT '',
  status TEXT NOT NULL DEFAULT 'uploading', -- uploading, processing, received, failed, printed, archived, expired
  idempotency_key TEXT,
  created_at TEXT NOT NULL,
  completed_at TEXT,
  expires_at TEXT NOT NULL,
  total_size INTEGER NOT NULL DEFAULT 0,
  FOREIGN KEY (shop_id) REFERENCES shops(shop_id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_submissions_idempotency ON submissions(idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_submissions_status_created ON submissions(status, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_submissions_expires ON submissions(expires_at);

CREATE TABLE IF NOT EXISTS files (
  file_id TEXT PRIMARY KEY,
  submission_id TEXT NOT NULL,
  original_filename TEXT NOT NULL,
  internal_filename TEXT NOT NULL,
  content_type TEXT NOT NULL,
  file_extension TEXT NOT NULL,
  file_size INTEGER NOT NULL DEFAULT 0,
  checksum TEXT NOT NULL DEFAULT '',
  storage_path TEXT NOT NULL,
  conversion_status TEXT NOT NULL DEFAULT 'none', -- none, converting, converted, failed
  converted_path TEXT,
  received_at TEXT NOT NULL,
  FOREIGN KEY (submission_id) REFERENCES submissions(submission_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_files_submission ON files(submission_id);

CREATE TABLE IF NOT EXISTS upload_sessions (
  session_id TEXT PRIMARY KEY,
  submission_id TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'open', -- open, committing, completed, expired, failed
  authorized_upload_token_hash TEXT NOT NULL,
  client_ip TEXT NOT NULL DEFAULT '',
  bytes_received INTEGER NOT NULL DEFAULT 0,
  expected_total_bytes INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  FOREIGN KEY (submission_id) REFERENCES submissions(submission_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_upload_sessions_expires ON upload_sessions(expires_at);

CREATE TABLE IF NOT EXISTS upload_parts (
  session_id TEXT NOT NULL,
  file_id TEXT NOT NULL,
  chunk_index INTEGER NOT NULL,
  byte_offset INTEGER NOT NULL,
  chunk_size INTEGER NOT NULL,
  checksum TEXT NOT NULL DEFAULT '',
  completed_at TEXT NOT NULL,
  PRIMARY KEY (session_id, file_id, chunk_index),
  FOREIGN KEY (session_id) REFERENCES upload_sessions(session_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS app_settings (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL,
  is_secret INTEGER NOT NULL DEFAULT 0,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS activity_logs (
  activity_id TEXT PRIMARY KEY,
  activity_type TEXT NOT NULL,
  actor TEXT NOT NULL DEFAULT 'system',
  related_submission_id TEXT,
  timestamp TEXT NOT NULL,
  details TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_activity_logs_ts ON activity_logs(timestamp DESC);
```

---

## 5. API Contract

### A. Public Customer Upload Receiver (`127.0.0.1:8787` — Tunneled via Cloudflare/SSH)
*Strictly isolated. Zero admin, file-download, or cross-submission endpoints exist on this listener.*

| Method | Endpoint | Purpose | Auth / Guard |
|---|---|---|---|
| `GET` | `/`, `/shop/{slug}` | Serve lightweight mobile-first Customer Upload Portal | Public |
| `GET` | `/api/health` | Public readiness (`ok`, `shopName`, `acceptingUploads`, `maxFileSizeBytes`, `maxSubmissionBytes`, `chunkSizeBytes`, `allowedExtensions`) | Rate-limited |
| `POST` | `/api/upload/sessions` | Create upload session (`customerName`, `customerNote`, `idempotencyKey`, `files: [{clientFileId, name, size, contentType}]`) → returns `{ sessionId, uploadToken, chunkSizeBytes, expiresAt, files }` | Per-IP rate limit, pause check, disk-space check, allowlist & size validation |
| `POST` | `/api/upload/chunk` | Upload a single chunk (`X-Upload-Token`, multipart or raw stream with `sessionId`, `fileId`, `chunkIndex`, `byteOffset`, `chunkSha256`) → idempotent retryable write to `uploads/temp/` | Valid expiring session token, chunk <= 8 MiB, offset & total size cap |
| `POST` | `/api/upload/complete` | Assemble chunks, verify magic-bytes & SHA-256, atomically move to `uploads/received/`, commit SQLite, notify Windows tray → returns `{ receiptCode (6-char), shopName, fileCount, totalSizeBytes, submittedAt, status: "received" }` | Valid session token, atomic commit |
| `GET` | `/api/receipt?id={code}` | Non-sensitive status lookup for a specific 6-char receipt (`receiptCode`, `status`, `fileCount`, `submittedAt`, `expiresAt` — no file bytes/paths) | Rate-limited |
| `POST` | `/api/direct-upload`, `/api/direct-upload-chunk`, `/api/direct-upload-complete` | Backwards-compatible routes routed through the same `FileValidator`, `UploadSecurityGuard`, and `TransferService` | Same guards |

### B. Local-Only Shopkeeper Admin & Workspace API (`127.0.0.1:3838` — NEVER Tunneled)
*Bound strictly to loopback `127.0.0.1` + rejects requests carrying forwarding headers (`CF-Connecting-IP`, `X-Forwarded-For`, `X-Forwarded-Host`) as defense-in-depth.*

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `/`, `/workspace/` | Shopkeeper Desktop Web Workspace |
| `GET` | `/api/health` | Full station diagnostics (receiver, tunnel health, provider, last probe UTC, pause state) |
| `GET` | `/api/state` | Shop info, submissions, files, printers, activity logs, storage summary, settings |
| `GET` | `/api/file?id={fileId}&download=0\|1` | Stream file for local preview or download |
| `POST` | `/api/submission/status` | Update status (`received`, `processing`, `printed`, `archived`, `failed`) |
| `POST` | `/api/submission/delete` | Permanently delete one or multiple submissions (files + DB rows) |
| `POST` | `/api/file/open` | Open file in default Windows application |
| `POST` | `/api/file/open-with` | Open Windows "Open With" dialog (`rundll32 shell32.dll,OpenAs_RunDLL`) |
| `POST` | `/api/file/reveal` | Highlight file in Windows Explorer (`explorer.exe /select`) |
| `POST` | `/api/file/rename` | Rename display filename safely in SQLite |
| `POST` | `/api/file/export` | Permanently save/export copy outside 24h retention (e.g. Desktop/Downloads/Exports) |
| `POST` | `/api/file/convert-pdf` | Convert Office doc (LibreOffice) or single/multiple images (Native C#) into PDF in `converted/` |
| `GET` | `/api/printers` | List installed Windows printers + default printer |
| `POST` | `/api/file/print-native` | Print via selected Windows printer (manual staff action; records `sent_to_spooler` / `printed` honestly) |
| `GET/POST` | `/api/settings` | Configure shop name, storage path, retention hours, max file/submission size, allowed formats, pause toggle, theme |
| `GET/POST` | `/api/storage`, `/api/storage/clear` | Inspect disk usage breakdown and clear `temp` / `completed` / `all` |
| `GET/POST` | `/api/tunnel/config`, `/api/tunnel/restart` | Configure Auto Cascade or DPAPI-protected permanent Cloudflare Named Tunnel token |
| `GET/POST` | `/api/version`, `/api/update/check`, `/api/update/apply` | SHA-256 verified OTA updates |

---

## 6. Security & Threat Model

| Threat | Mitigation |
|---|---|
| **Public access to admin dashboard or other customers' files** | Architectural dual-port isolation: Cloudflare/SSH tunnels forward *only* to `:8787`, which mounts zero admin or file-download routes. `:3838` rejects forwarded headers and stays loopback-only. |
| **Executable / malware / spoofed extension uploads** | Server-side allowlist + **magic-byte header inspection** (`%PDF-`, JPEG `FF D8 FF`, PNG `89 50 4E 47`, WEBP `RIFF..WEBP`, BMP `BM`, OOXML ZIP `50 4B 03 04`, OLE2 `D0 CF 11 E0`, printable UTF-8 TXT). Rejects PE/MZ binaries (`4D 5A`), scripts, and mismatched types; quarantines or deletes immediately. |
| **Path traversal / filename injection** | Internal disk filenames are strictly generated GUIDs (`{guid}{safeExt}`). Customer original filenames are stored only as SQLite metadata. All paths are canonicalized (`Path.GetFullPath`) and verified against the storage root. |
| **Disk exhaustion / DoS / flood uploads** | Per-IP sliding-window rate limits + pre-upload free disk check (requires configurable reserve >= 500 MB free after upload, else HTTP `507`) + max chunk (< 8 MiB), max file (default 100 MB, configurable), max submission limits + one-click **Pause Receiving** switch. |
| **Duplicate submissions from double-taps** | Client sends `idempotencyKey` per submission attempt; SQLite unique index returns the already-committed receipt instead of creating duplicate files. |
| **Power loss / crash mid-upload** | Chunks stream to `uploads/temp/{sessionId}/`. Only after full validation and SHA-256 check are files moved atomically into `uploads/received/` and marked `received` in a SQLite transaction. On startup, unfinished `uploading` sessions are recovered/expired cleanly. |
| **Plaintext tunnel tokens** | Stored encrypted via Windows DPAPI (`ProtectedData.Protect` with `DataProtectionScope.CurrentUser`) in `app_settings`. |
| **Macro execution / hung conversion** | LibreOffice runs headless with `--norestore --nofirststartwizard --nodefault`, an isolated temporary user profile directory, and a strict 45-second watchdog timeout that kills the worker process tree. |

---

## 7. Ordered Milestone Checklist

- [x] **Milestone 0 — Assessment & Architecture Docs:** Repo audit, PRD v2.0 architecture, schema, API contract, threat model (`docs/ARCHITECTURE.md`).
- [ ] **Milestone 1 — Core, SQLite & Standardized Storage (`native/Core/`, `native/Storage/`):** `winsqlite3.dll` WAL database (7 tables + migrations + crash recovery), DPAPI secret protection, standardized folder layout, retention & temp cleanup.
- [ ] **Milestone 2 — Hardened Transfer Pipeline & Dual-Port Servers (`native/Transfers/`, `native/Api/`, `native/Networking/`):** Upload sessions, 4 MiB chunked streaming, magic-byte validator, rate limiting, pause toggle, 6-char receipts, isolated `PublicUploadServer` (:8787) vs `LocalAdminServer` (:3838), `TunnelManager` targeting strictly `:8787`.
- [ ] **Milestone 3 — Conversion Engine & Native Windows File Actions (`native/Conversion/`, `native/Api/`):** Native Image→PDF, isolated LibreOffice Office→PDF, Open / Open With / Reveal / Rename / Export / Windows Printers list.
- [ ] **Milestone 4 — Desktop Shell, First-Run Wizard & Customer/Workspace UI (`native/Ui/`, `build_netlify.js`, `workspace_template.js`):** First-run setup wizard, tray notifications, Pause Receiving toggle, Light/Dark mode, full 6-section navigation, upgraded mobile upload UI (*Scan. Send. Done.*).
- [ ] **Milestone 5 — Installer, Automated Test Suite & Production Deployment Docs (`installer/`, `scripts/test-prd-v2.ps1`, `docs/DEPLOYMENT.md`):** Updated Inno Setup script, automated end-to-end verification tests covering all 14 Section 19 acceptance criteria, and live tunnel verification.

---

## 8. Development Environment Requirements

| Component | Detected on Host PC | Role |
|---|---|---|
| **Windows 10/11 x64** | Present | Target OS + built-in `winsqlite3.dll` + Windows DPAPI + GDI+ + WinForms |
| **.NET Framework 4.8 C# Compiler (`csc.exe`)** | Present (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) | Compiles standalone `ScanDrop.exe` with zero external runtime installer needed |
| **Node.js v24.13.0** | Present | Builds web portal bundles (`build_netlify.js`) and syntax/JS verification |
| **Cloudflared (`bin/cloudflared.exe`)** | Present (`2026.11.0`) | Cloudflare Quick & Named Tunnels |
| **Windows Built-in OpenSSH (`ssh.exe`)** | Present | Zero-config fallback tunnel provider |
| **LibreOffice (`soffice.exe`)** | Optional (Not currently installed on dev PC) | Optional local DOCX/XLSX/PPTX→PDF conversion; detected gracefully at runtime with clear status indicator |
| **Inno Setup 6 (`ISCC.exe`)** | Optional (Not currently installed on dev PC) | Compiles `installer/ScanDropSetup.iss` into `ScanDropSetup.exe`; portable packaging script also provided |
