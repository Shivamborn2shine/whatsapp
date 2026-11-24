# Scan Drop — Deployment, Configuration & Production Networking Guide

## 1. Development & Instant Setup (Zero Cost, No Account Needed)
1. Build the executable & web portals:
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\build-exe.ps1"
   node build_netlify.js
   ```
2. Run `ScanDrop.exe` (or double-click `start-scandrop.bat`).
3. On first launch, complete the **First-Run Setup Wizard**:
   - Enter your **Shop Name**.
   - Confirm your **Local Storage Folder** (default `C:\ProgramData\ScanDrop`).
   - Choose **Retention Hours** (default `24`) and **Max File Size (MB)** (default `100`, up to `500`).
4. Scan Drop automatically starts:
   - **Public Upload Receiver** on `http://127.0.0.1:8787` (strictly public upload endpoints only).
   - **Local Shopkeeper Workspace** on `http://127.0.0.1:3838/workspace/` (loopback-only — never exposed publicly).
   - **Cloudflare Tunnel Only** (`*.trycloudflare.com` in Development Quick Tunnel mode, or permanent Production Named Tunnel when configured — OpenSSH/third-party fallbacks are permanently disabled).

---

## 2. Production Mode — Permanent Shop Domain & Cloudflare Named Tunnel

Cloudflare Quick Tunnel URLs (`*.trycloudflare.com`) are temporary and rotate on restart. For a permanent printed QR code with zero rate limits:

1. Create a free Cloudflare Zero Trust account and add your domain (e.g. `example.com`).
2. In **Cloudflare Zero Trust → Networks → Tunnels**, click **Create a tunnel** → **Cloudflared**.
3. Name the tunnel (e.g. `shop-quickprint`) and copy the **Tunnel Token**.
4. Under **Public Hostnames**, add a hostname for this shop:
   - Subdomain: `quickprint` (e.g., `quickprint.example.com`) — one hostname per shop is recommended for simplest direct routing.
   - Service Type: `HTTP`
   - URL: `127.0.0.1:8787` *(CRITICAL: point ONLY to port `8787`, NEVER `3838`!)*
5. **Option A — Run as a Windows Service (Recommended for Production):**
   Run in an elevated PowerShell prompt:
   ```powershell
   .\bin\cloudflared.exe service install <YOUR_CLOUDFLARE_TUNNEL_TOKEN>
   ```
   Then open **Scan Drop Station → ⚙️ Tunnel Settings**, enter **Permanent Hostname** (`https://quickprint.example.com`), leave mode on `auto` (or `named`), and click **Save & Reconnect Cloudflare**. Scan Drop automatically detects and monitors the running `cloudflared` Windows service without launching a duplicate process.
6. **Option B — Managed Named Tunnel inside Scan Drop:**
   Open **Scan Drop Station → ⚙️ Tunnel Settings**, set mode to `auto` or `named`, paste your **Named Tunnel Token** (stored encrypted via Windows DPAPI in SQLite — never plaintext), enter **Permanent Hostname** (`https://quickprint.example.com`), and click **Save & Reconnect Cloudflare**.
7. Print the shop QR from **Workspace → QR & Sharing**. That printed QR remains valid permanently across PC restarts and IP changes.

---

## 3. Optional Local Office Document Conversion (DOCX/XLSX/PPTX → PDF)

- **Image-to-PDF** (`JPG`, `PNG`, `BMP` — single and multi-image combined PDFs) works out of the box with zero external dependencies using built-in Windows GDI+.
- For **Office-to-PDF** (`DOCX`, `DOC`, `XLSX`, `XLS`, `PPTX`, `PPT`, `TXT`), install free open-source **LibreOffice** (`https://www.libreoffice.org`). Scan Drop automatically detects `C:\Program Files\LibreOffice\program\soffice.exe` and runs conversions in an isolated headless worker profile with a 45-second timeout and macros disabled.

---

## 4. Packaging the Windows Installer (`ScanDropSetup.exe`)

1. Install **Inno Setup 6** (`https://jrsoftware.org/isinfo.php`).
2. Build latest binaries and portals:
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\build-exe.ps1"
   node build_netlify.js
   ```
3. Compile the installer:
   ```powershell
   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" ".\installer\ScanDropSetup.iss"
   ```
4. Output installer is written to `dist\installer\ScanDropSetup.exe`.

---

## 5. Known Limitations & Operational Safeguards
- **Cloudflare-only transit:** While persistent file storage and SQLite stay strictly on the shop PC, customer uploads transit HTTPS exclusively through Cloudflare Tunnel during transfer. Scan Drop never launches `ssh.exe` or falls back to third-party tunnel providers.
- **Chunked transfer ceiling:** Chunks default to 4 MiB (< 8 MiB) for mobile reliability and proxy body-size compliance; actual upload speed depends on customer mobile uplink and shop download speed.
- **Manual printing policy:** Sending a job to the Windows spooler records `SENT_TO_PRINTER`; staff explicitly confirm physical completion in Inbox.
