using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using ScanDrop.Native.Api;
using ScanDrop.Native.Core;
using ScanDrop.Native.Storage;
using ScanDrop.Native.Transfers;
using ScanDrop.Native.Ui;

namespace ScanDrop.Native
{
    public sealed class StationForm : Form
    {
        private readonly LocalAdminServer _adminServer;
        private readonly PublicUploadServer _publicReceiver;
        private readonly StorageManager _storage;
        private readonly SqliteDatabase _db;
        private readonly TransferService _transfers;
        private readonly TunnelManager _tunnel;
        private readonly AppLogger _logger;
        private readonly string _cloudQrUrl;

        private Label _statusBadge;
        private TextBox _tunnelUrlBox;
        private TextBox _storageBox;
        private TextBox _healthBox;
        private TextBox _logBox;
        private Button _autoStartBtn;
        private Button _pauseBtn;
        private NotifyIcon _trayIcon;
        private System.Windows.Forms.Timer _statusTimer;
        private bool _allowExit;

        public StationForm(
            LocalAdminServer adminServer,
            PublicUploadServer publicReceiver,
            StorageManager storage,
            SqliteDatabase db,
            TransferService transfers,
            TunnelManager tunnel,
            AppLogger logger,
            string cloudQrUrl)
        {
            _adminServer = adminServer;
            _publicReceiver = publicReceiver;
            _storage = storage;
            _db = db;
            _transfers = transfers;
            _tunnel = tunnel;
            _logger = logger;
            _cloudQrUrl = cloudQrUrl;

            Text = "Scan Drop v" + AutoUpdater.CurrentVersion + " — Local-First File Receiver & Tunnel Station";
            Size = new Size(880, 640);
            MinimumSize = new Size(800, 560);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(244, 247, 251);
            Font = new Font("Segoe UI", 9.5f);
            Icon = LoadAppIcon();

            BuildUi();
            SetupTray();

            Shown += (s, e) =>
            {
                RefreshStationMetrics();
                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(3000); RunUpdateCheck(false); });
            };

            _statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _statusTimer.Tick += (s, e) => RefreshStationMetrics();
            _statusTimer.Start();

            _tunnel.OnStatusMessage += AppendLog;
            _logger.OnLogLine += AppendLog;
            _adminServer.OnStateChanged += () => SafeUi(RefreshStationMetrics);
            _storage.OnStorageChanged += () => SafeUi(RefreshStationMetrics);

            _transfers.OnSubmissionReceived += sub => SafeUi(() =>
            {
                RefreshStationMetrics();
                if (_trayIcon != null)
                {
                    _trayIcon.ShowBalloonTip(
                        4500,
                        "New Files Received — " + sub.ReceiptCode,
                        string.Format(CultureInfo.InvariantCulture, "{0} sent {1} file(s) ({2}). Click to open Workspace.",
                            sub.CustomerName, sub.Files.Count, FormatSize(sub.TotalSize)),
                        ToolTipIcon.Info
                    );
                }
            });

            _tunnel.OnStateChanged += snap => SafeUi(() => RefreshStationMetrics());
            _tunnel.OnTunnelUrlCaptured += url => SafeUi(() => RefreshStationMetrics());
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                Icon extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (extracted != null) return extracted;
            }
            catch { }
            return SystemIcons.Application;
        }

        private void BuildUi()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Color.White, Padding = new Padding(20, 14, 20, 10) };
            var logoBox = new PictureBox
            {
                Location = new Point(18, 15),
                Size = new Size(42, 42),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = LoadAppIcon().ToBitmap()
            };
            var title = new Label
            {
                Text = "ScanDrop — Local-First PC Station",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                AutoSize = true,
                Location = new Point(68, 12)
            };
            _statusBadge = new Label
            {
                Text = "● Starting Isolated Receiver & Cloudflare Tunnel...",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(217, 119, 6),
                AutoSize = true,
                Location = new Point(70, 42)
            };
            var headerUpdateBtn = new Button
            {
                Text = "🔄 Check Updates",
                AutoSize = true,
                Height = 36,
                BackColor = Color.FromArgb(239, 246, 255),
                ForeColor = Color.FromArgb(29, 78, 216),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Padding = new Padding(10, 4, 10, 4),
                Cursor = Cursors.Hand
            };
            headerUpdateBtn.FlatAppearance.BorderColor = Color.FromArgb(191, 219, 254);
            headerUpdateBtn.Click += (s, e) => ThreadPool.QueueUserWorkItem(_ => RunUpdateCheck(true));

            header.Controls.Add(logoBox);
            header.Controls.Add(title);
            header.Controls.Add(_statusBadge);
            header.Controls.Add(headerUpdateBtn);
            header.Resize += (s, e) => { headerUpdateBtn.Location = new Point(Math.Max(260, header.ClientSize.Width - headerUpdateBtn.Width - 20), 18); };

            var infoCard = new Panel { Location = new Point(20, 86), Size = new Size(824, 216), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.White, Padding = new Padding(16) };

            infoCard.Controls.Add(new Label { Text = "Local Workspace (Private):", Location = new Point(16, 16), Size = new Size(166, 22), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            var localBox = new TextBox { Text = "http://127.0.0.1:" + _adminServer.Port + "/workspace/  (Loopback-only — never exposed publicly)", ReadOnly = true, Location = new Point(186, 14), Size = new Size(618, 24), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(248, 250, 252) };
            infoCard.Controls.Add(localBox);

            infoCard.Controls.Add(new Label { Text = "Public Tunnel URL:", Location = new Point(16, 49), Size = new Size(166, 22), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            _tunnelUrlBox = new TextBox { Text = "Connecting Cloudflare Tunnel...", ReadOnly = true, Location = new Point(186, 47), Size = new Size(618, 24), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(248, 250, 252) };
            infoCard.Controls.Add(_tunnelUrlBox);

            infoCard.Controls.Add(new Label { Text = "Customer QR Link:", Location = new Point(16, 82), Size = new Size(166, 22), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            var qrBox = new TextBox { Text = _cloudQrUrl, ReadOnly = true, Location = new Point(186, 80), Size = new Size(618, 24), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(248, 250, 252) };
            infoCard.Controls.Add(qrBox);

            infoCard.Controls.Add(new Label { Text = "Local Storage Root:", Location = new Point(16, 115), Size = new Size(166, 22), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            var dirBox = new TextBox { Text = _storage.Paths.DataRootDir, ReadOnly = true, Location = new Point(186, 113), Size = new Size(618, 24), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(248, 250, 252) };
            infoCard.Controls.Add(dirBox);

            infoCard.Controls.Add(new Label { Text = "Local SSD Used:", Location = new Point(16, 148), Size = new Size(166, 22), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            _storageBox = new TextBox { Text = "Calculating disk usage...", ReadOnly = true, Location = new Point(186, 146), Size = new Size(618, 24), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(248, 250, 252) };
            infoCard.Controls.Add(_storageBox);

            infoCard.Controls.Add(new Label { Text = "Connection Monitor:", Location = new Point(16, 181), Size = new Size(166, 22), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            _healthBox = new TextBox { Text = "Initializing...", ReadOnly = true, Location = new Point(186, 179), Size = new Size(618, 24), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(248, 250, 252) };
            infoCard.Controls.Add(_healthBox);

            var btnRow = new FlowLayoutPanel { Location = new Point(20, 310), Size = new Size(824, 86), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, WrapContents = true };

            var openWsBtn = new Button { Text = "🖥️ Open Workspace", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Padding = new Padding(10, 4, 10, 4), Cursor = Cursors.Hand };
            openWsBtn.FlatAppearance.BorderSize = 0;
            openWsBtn.Click += (s, e) => Process.Start("http://127.0.0.1:" + _adminServer.Port + "/workspace/");

            var openDirBtn = new Button { Text = "📂 Open Storage Folder", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.White, ForeColor = Color.FromArgb(15, 23, 42), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Cursor = Cursors.Hand };
            openDirBtn.Click += (s, e) => Process.Start("explorer.exe", _storage.Paths.ReceivedDir);

            var copyQrBtn = new Button { Text = "📋 Copy Upload Link", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.White, ForeColor = Color.FromArgb(15, 23, 42), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Cursor = Cursors.Hand };
            copyQrBtn.Click += (s, e) =>
            {
                string target = !string.IsNullOrEmpty(_tunnel.LiveTunnelUrl) ? _tunnel.LiveTunnelUrl : _cloudQrUrl;
                Clipboard.SetText(target);
                AppendLog("Copied direct Customer Upload URL: " + target);
            };

            _pauseBtn = new Button { Text = "⏸ Pause Receiving", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.White, ForeColor = Color.FromArgb(180, 83, 9), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Cursor = Cursors.Hand };
            _pauseBtn.Click += (s, e) => TogglePauseReceiving();

            var restartBtn = new Button { Text = "↻ Restart Tunnel", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.White, ForeColor = Color.FromArgb(15, 23, 42), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Cursor = Cursors.Hand };
            restartBtn.Click += (s, e) => { _tunnelUrlBox.Text = "Reconnecting..."; _tunnel.Restart(); };

            var tunnelCfgBtn = new Button { Text = "⚙️ Tunnel Settings", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.White, ForeColor = Color.FromArgb(15, 23, 42), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Cursor = Cursors.Hand };
            tunnelCfgBtn.Click += (s, e) => ShowTunnelSettingsDialog();

            var wizardBtn = new Button { Text = "🛠 Shop Setup", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.White, ForeColor = Color.FromArgb(15, 23, 42), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Cursor = Cursors.Hand };
            wizardBtn.Click += (s, e) =>
            {
                using (var w = new FirstRunWizardForm(_db, _storage.Paths))
                {
                    if (w.ShowDialog(this) == DialogResult.OK)
                    {
                        RefreshStationMetrics();
                        if (w.StorageRootChanged)
                        {
                            MessageBox.Show("New storage folder saved. Please restart Scan Drop to switch active storage directory.", "Scan Drop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                }
            };

            _autoStartBtn = new Button { Text = IsAutoStartEnabled() ? "✅ Auto-Start ON" : "Auto-Start OFF", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.White, ForeColor = Color.FromArgb(15, 23, 42), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Cursor = Cursors.Hand };
            _autoStartBtn.Click += (s, e) => ToggleAutoStart();

            var clearStorageBtn = new Button { Text = "🧹 Free Up Storage", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.FromArgb(254, 242, 242), ForeColor = Color.FromArgb(185, 28, 28), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Cursor = Cursors.Hand };
            clearStorageBtn.FlatAppearance.BorderColor = Color.FromArgb(254, 202, 202);
            clearStorageBtn.Click += (s, e) => ShowClearStorageDialog();

            btnRow.Controls.AddRange(new Control[] { openWsBtn, openDirBtn, copyQrBtn, _pauseBtn, restartBtn, tunnelCfgBtn, wizardBtn, _autoStartBtn, clearStorageBtn });

            _logBox = new TextBox
            {
                Location = new Point(20, 404),
                Size = new Size(824, 178),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Consolas", 9f)
            };

            Controls.Add(header);
            Controls.Add(infoCard);
            Controls.Add(btnRow);
            Controls.Add(_logBox);
        }

        private void SetupTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Workspace", null, (s, e) => Process.Start("http://127.0.0.1:" + _adminServer.Port + "/workspace/"));
            menu.Items.Add("Pause / Resume Receiving", null, (s, e) => TogglePauseReceiving());
            menu.Items.Add("Free Up Storage...", null, (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); ShowClearStorageDialog(); });
            menu.Items.Add("Check for Updates", null, (s, e) => ThreadPool.QueueUserWorkItem(_ => RunUpdateCheck(true)));
            menu.Items.Add("Show Station Window", null, (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
            menu.Items.Add("Exit Scan Drop", null, (s, e) => { _allowExit = true; Close(); });

            _trayIcon = new NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = "ScanDrop Station",
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
            _trayIcon.BalloonTipClicked += (s, e) => { try { Process.Start("http://127.0.0.1:" + _adminServer.Port + "/workspace/"); } catch { } };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                _trayIcon.ShowBalloonTip(2500, "Scan Drop Running in Background", "File receiver & tunnel stay active in the system tray.", ToolTipIcon.Info);
                return;
            }
            if (_statusTimer != null) { _statusTimer.Stop(); _statusTimer.Dispose(); }
            if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
            base.OnFormClosing(e);
        }

        private void TogglePauseReceiving()
        {
            var st = _db.LoadSettings(_storage.Paths.DataRootDir);
            st.PauseReceiving = !st.PauseReceiving;
            _db.SaveSettings(st);
            _db.AddActivity("RECEIVING_TOGGLED", "shopkeeper", null, st.PauseReceiving ? "Paused customer uploads from PC Station." : "Resumed customer uploads from PC Station.");
            AppendLog(st.PauseReceiving ? "⏸ Incoming customer uploads PAUSED." : "▶ Incoming customer uploads RESUMED.");
            RefreshStationMetrics();
        }

        private void RefreshStationMetrics()
        {
            if (IsDisposed) return;
            try
            {
                var st = _storage.GetStorageSummary();
                int totalJobs = st.ActiveCount + st.CompletedCount;
                _storageBox.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} used ({1} job(s), {2} temp/staging, {3} converted) • Drive Free: {4}",
                    FormatSize(st.TotalUsedBytes), totalJobs,
                    FormatSize(st.StagingBytes + st.OrphanBytes),
                    FormatSize(st.ConvertedBytes),
                    FormatSize(st.FreeDriveBytes)
                );

                var cfg = _db.LoadSettings(_storage.Paths.DataRootDir);
                _pauseBtn.Text = cfg.PauseReceiving ? "▶ Resume Receiving" : "⏸ Pause Receiving";
                _pauseBtn.BackColor = cfg.PauseReceiving ? Color.FromArgb(254, 243, 199) : Color.White;

                var snap = _tunnel.GetStatusSnapshot();
                if (snap.IsOnline && !string.IsNullOrEmpty(snap.PublicUrl))
                {
                    _tunnelUrlBox.Text = snap.PublicUrl;
                    _statusBadge.Text = "● " + snap.DisplayStatus + " (" + snap.ModeLabel + ") • Receiver :" + _publicReceiver.Port;
                    _statusBadge.ForeColor = snap.State == TunnelState.CONNECTED
                        ? Color.FromArgb(22, 163, 74)
                        : Color.FromArgb(217, 119, 6);
                }
                else
                {
                    _tunnelUrlBox.Text = snap.DisplayStatus + (snap.NextRetryUtc.HasValue ? (" (next retry " + snap.NextRetryUtc.Value.ToLocalTime().ToString("HH:mm:ss") + ")") : "");
                    _statusBadge.Text = "● " + snap.DisplayStatus + " [" + snap.StateName + "]";
                    _statusBadge.ForeColor = (snap.State == TunnelState.OFFLINE || snap.State == TunnelState.COOLDOWN)
                        ? Color.FromArgb(220, 38, 38)
                        : Color.FromArgb(217, 119, 6);
                }

                string lastOk = snap.LastSuccessfulHealthCheckUtc.HasValue
                    ? snap.LastSuccessfulHealthCheckUtc.Value.ToLocalTime().ToString("HH:mm:ss")
                    : "Pending";
                string pidStr = snap.ProcessId.HasValue ? snap.ProcessId.Value.ToString(CultureInfo.InvariantCulture) : "n/a";
                string retryStr = snap.NextRetryUtc.HasValue
                    ? (" • Retry #" + snap.RetryCount + " @ " + snap.NextRetryUtc.Value.ToLocalTime().ToString("HH:mm:ss"))
                    : "";
                _healthBox.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "Receiver: {0} (:{1}) • State: {2} • Mode: {3} • PID: {4} • Last OK: {5}{6}",
                    _publicReceiver.IsRunning ? "Running" : "Stopped",
                    _publicReceiver.Port,
                    snap.StateName,
                    snap.ModeLabel,
                    pidStr,
                    lastOk,
                    retryStr
                );
            }
            catch { }
        }

        private void SafeUi(Action act)
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(act);
                else act();
            }
            catch { }
        }

        private void AppendLog(string msg)
        {
            SafeUi(() =>
            {
                if (_logBox != null && !_logBox.IsDisposed)
                    _logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine);
            });
        }

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                    return key != null && key.GetValue("ScanDropStation") != null;
            }
            catch { return false; }
        }

        private void ToggleAutoStart()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (IsAutoStartEnabled())
                    {
                        key.DeleteValue("ScanDropStation", false);
                        _autoStartBtn.Text = "Auto-Start OFF";
                        AppendLog("Disabled Windows startup auto-launch.");
                    }
                    else
                    {
                        key.SetValue("ScanDropStation", "\"" + Application.ExecutablePath + "\" --minimized");
                        _autoStartBtn.Text = "✅ Auto-Start ON";
                        AppendLog("Enabled Windows startup auto-launch (background tray mode).");
                    }
                }
            }
            catch (Exception ex) { AppendLog("Auto-Start error: " + ex.Message); }
        }

        private void RunUpdateCheck(bool interactive)
        {
            try
            {
                if (AutoUpdater.SyncWorkspaceUi(_storage.Paths.AppRootDir))
                    AppendLog("✨ Synced latest Workspace UI from cloud CDN.");

                var info = AutoUpdater.CheckForUpdate();
                if (!info.UpdateAvailable)
                {
                    if (interactive)
                    {
                        AppendLog("Scan Drop v" + info.CurrentVersion + " is up to date.");
                        SafeUi(() => MessageBox.Show("You are running the latest version (v" + info.CurrentVersion + ").", "Scan Drop Updater", MessageBoxButtons.OK, MessageBoxIcon.Information));
                    }
                    return;
                }

                AppendLog("🔔 Update available: v" + info.LatestVersion + " (current v" + info.CurrentVersion + ")");
                SafeUi(() =>
                {
                    var res = MessageBox.Show(
                        "Scan Drop v" + info.LatestVersion + " is available!\n\n" +
                        (string.IsNullOrEmpty(info.ReleaseNotes) ? "" : info.ReleaseNotes + "\n\n") +
                        "Install and restart ScanDrop.exe now?",
                        "Scan Drop Update Available",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (res == DialogResult.Yes)
                    {
                        AppendLog("⬇️ Downloading & verifying ScanDrop v" + info.LatestVersion + "...");
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            string err;
                            if (!AutoUpdater.ApplyUpdate(info, () =>
                            {
                                try { _tunnel.Dispose(); } catch { }
                                try { _publicReceiver.Dispose(); } catch { }
                                try { _adminServer.Dispose(); } catch { }
                            }, out err))
                            {
                                AppendLog("❌ Update failed: " + err);
                                SafeUi(() => MessageBox.Show("Update failed: " + err, "Scan Drop Updater", MessageBoxButtons.OK, MessageBoxIcon.Error));
                            }
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                if (interactive) AppendLog("Update check failed: " + ex.Message);
            }
        }

        private void ShowClearStorageDialog()
        {
            var st = _storage.GetStorageSummary();
            using (var dlg = new Form())
            {
                dlg.Text = "Scan Drop — Free Up Local PC Storage";
                dlg.Size = new Size(490, 355);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.BackColor = Color.FromArgb(248, 250, 252);
                dlg.Font = new Font("Segoe UI", 9.5f);

                var infoText = new Label
                {
                    Location = new Point(20, 18),
                    Size = new Size(434, 118),
                    BackColor = Color.White,
                    Padding = new Padding(12),
                    BorderStyle = BorderStyle.FixedSingle,
                    Text = string.Format(
                        CultureInfo.InvariantCulture,
                        "Storage Root: {0}\n" +
                        "• Total Used: {1}  (Free on Drive: {2})\n" +
                        "• Active Jobs ({3}): {4}\n" +
                        "• Printed / Completed Jobs ({5}): {6}\n" +
                        "• Temp, Converted & Orphaned Files: {7}",
                        st.StorageRoot,
                        FormatSize(st.TotalUsedBytes), FormatSize(st.FreeDriveBytes),
                        st.ActiveCount, FormatSize(st.ActiveBytes),
                        st.CompletedCount, FormatSize(st.CompletedBytes),
                        FormatSize(st.StagingBytes + st.ConvertedBytes + st.OrphanBytes)
                    )
                };

                Action<string, bool> runClear = (scope, askConfirm) =>
                {
                    if (askConfirm)
                    {
                        var confirm = MessageBox.Show(
                            "Permanently delete ALL uploaded customer files and temp chunks (" + FormatSize(st.TotalUsedBytes) + ")?\nShop settings and permanently exported copies will stay intact.",
                            "Confirm Clear All Storage",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);
                        if (confirm != DialogResult.Yes) return;
                    }
                    var res = _storage.ClearStorage(scope);
                    RefreshStationMetrics();
                    dlg.Close();
                    MessageBox.Show(
                        string.Format(CultureInfo.InvariantCulture,
                            "Storage cleanup complete!\n\nFreed: {0}\nRemoved Jobs: {1}\nRemoved Temp/Orphan Items: {2}\nRemaining Used: {3}",
                            FormatSize(res.FreedBytes), res.DeletedSubmissions, res.RemovedTempItems, FormatSize(res.Remaining.TotalUsedBytes)),
                        "Storage Freed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                };

                var btnTemp = new Button { Text = "🧹 Clear Temp, Expired & Orphans (" + FormatSize(st.StagingBytes + st.OrphanBytes) + ")", Location = new Point(20, 152), Size = new Size(434, 40), BackColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
                btnTemp.Click += (s, e) => runClear("temp", false);

                var btnCompleted = new Button { Text = "✅ Clear Completed Jobs + Converted (" + FormatSize(st.CompletedBytes + st.ConvertedBytes + st.StagingBytes + st.OrphanBytes) + ")", Location = new Point(20, 200), Size = new Size(434, 40), BackColor = Color.FromArgb(239, 246, 255), ForeColor = Color.FromArgb(29, 78, 216), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
                btnCompleted.Click += (s, e) => runClear("completed", false);

                var btnAll = new Button { Text = "🗑️ Clear ALL Uploaded Files & Staging (" + FormatSize(st.TotalUsedBytes) + ")", Location = new Point(20, 248), Size = new Size(434, 42), BackColor = Color.FromArgb(220, 38, 38), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand };
                btnAll.FlatAppearance.BorderSize = 0;
                btnAll.Click += (s, e) => runClear("all", true);

                dlg.Controls.AddRange(new Control[] { infoText, btnTemp, btnCompleted, btnAll });
                dlg.ShowDialog(this);
            }
        }

        private void ShowTunnelSettingsDialog()
        {
            string curToken, curUrl, curMode;
            _tunnel.ReadConfig(out curToken, out curUrl, out curMode);

            using (var dlg = new Form())
            {
                dlg.Text = "Scan Drop — Cloudflare Tunnel Settings";
                dlg.Size = new Size(540, 360);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.BackColor = Color.FromArgb(248, 250, 252);
                dlg.Font = new Font("Segoe UI", 9.5f);

                var note = new Label
                {
                    Location = new Point(20, 14),
                    Size = new Size(484, 44),
                    ForeColor = Color.FromArgb(71, 85, 105),
                    Text = "Cloudflare-only connectivity. Auto mode prefers Production Named Tunnel (permanent hostname / Windows Service) when configured, otherwise uses Development Quick Tunnel."
                };

                dlg.Controls.Add(new Label { Text = "Cloudflare Mode:", Location = new Point(20, 66), Size = new Size(146, 22), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
                var modeBox = new ComboBox { Location = new Point(174, 64), Size = new Size(330, 26), DropDownStyle = ComboBoxStyle.DropDownList };
                modeBox.Items.AddRange(new object[] { "auto", "named", "quick" });
                int idx = modeBox.Items.IndexOf(TunnelManager.NormalizeCloudflareMode(curMode));
                modeBox.SelectedIndex = idx >= 0 ? idx : 0;

                dlg.Controls.Add(new Label { Text = "Named Tunnel Token:", Location = new Point(20, 106), Size = new Size(146, 22), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
                var tokBox = new TextBox { Location = new Point(174, 104), Size = new Size(330, 24), Text = curToken ?? "", UseSystemPasswordChar = !string.IsNullOrEmpty(curToken) };

                dlg.Controls.Add(new Label { Text = "Permanent Hostname:", Location = new Point(20, 144), Size = new Size(146, 22), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
                var urlBox = new TextBox { Location = new Point(174, 142), Size = new Size(330, 24), Text = curUrl ?? "" };

                var hint = new Label
                {
                    Location = new Point(20, 180),
                    Size = new Size(484, 42),
                    ForeColor = Color.FromArgb(100, 116, 139),
                    Font = new Font("Segoe UI", 8.5f),
                    Text = "Token is encrypted via Windows DPAPI. Tunnel forwards strictly to 127.0.0.1:" + _publicReceiver.Port + " (never exposes Admin :" + _adminServer.Port + "). Third-party SSH fallbacks are permanently disabled."
                };

                var saveBtn = new Button { Text = "Save & Reconnect Cloudflare", Location = new Point(20, 244), Size = new Size(240, 38), BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand };
                saveBtn.FlatAppearance.BorderSize = 0;
                saveBtn.Click += (s, e) => { _tunnel.SaveConfig(tokBox.Text, urlBox.Text, Convert.ToString(modeBox.SelectedItem)); dlg.Close(); };

                var resetBtn = new Button { Text = "Reset to Dev Quick Tunnel", Location = new Point(274, 244), Size = new Size(230, 38), BackColor = Color.White, ForeColor = Color.FromArgb(15, 23, 42), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
                resetBtn.Click += (s, e) => { _tunnel.SaveConfig("", "", "auto"); dlg.Close(); };

                dlg.Controls.AddRange(new Control[] { note, modeBox, tokBox, urlBox, hint, saveBtn, resetBtn });
                dlg.ShowDialog(this);
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024L * 1024L) return (bytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture) + " KB";
            if (bytes < 1024L * 1024L * 1024L) return (bytes / (1024.0 * 1024.0)).ToString("F2", CultureInfo.InvariantCulture) + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F2", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
