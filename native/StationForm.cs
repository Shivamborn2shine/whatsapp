using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScanDrop.Native
{
    public sealed class StationForm : Form
    {
        private readonly LocalHttpServer _server;
        private readonly TunnelManager _tunnel;
        private readonly string _cloudQrUrl;
        private readonly int _port;

        private Label _statusBadge;
        private TextBox _tunnelUrlBox;
        private TextBox _storageBox;
        private TextBox _logBox;
        private Button _autoStartBtn;
        private NotifyIcon _trayIcon;
        private System.Windows.Forms.Timer _storageTimer;
        private bool _allowExit;

        public StationForm(LocalHttpServer server, TunnelManager tunnel, string cloudQrUrl, int port)
        {
            _server = server;
            _tunnel = tunnel;
            _cloudQrUrl = cloudQrUrl;
            _port = port;

            Text = "Scan Drop v" + AutoUpdater.CurrentVersion + " — Local PC Storage & Tunnel Station";
            Size = new Size(780, 585);
            MinimumSize = new Size(700, 520);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(244, 247, 251);
            Font = new Font("Segoe UI", 9.5f);

            BuildUi();
            SetupTray();

            Shown += (s, e) =>
            {
                RefreshStorageStats();
                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(3000); RunUpdateCheck(false); });
            };

            _storageTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _storageTimer.Tick += (s, e) => RefreshStorageStats();
            _storageTimer.Start();

            _tunnel.OnStatusMessage += AppendLog;
            _server.OnLog += AppendLog;
            _server.OnSubmissionsChanged += () =>
            {
                if (IsDisposed) return;
                try { BeginInvoke((Action)RefreshStorageStats); } catch { }
            };
            _tunnel.OnTunnelUrlCaptured += url =>
            {
                if (IsDisposed) return;
                BeginInvoke((Action)(() =>
                {
                    _tunnelUrlBox.Text = url;
                    _statusBadge.Text = "● Tunnel Live (" + _tunnel.ActiveProviderName + ")";
                    _statusBadge.ForeColor = Color.FromArgb(22, 163, 74);
                }));
            };
        }

        private void BuildUi()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Color.White, Padding = new Padding(20, 14, 20, 10) };
            var title = new Label
            {
                Text = "⚡ Scan Drop — Shopkeeper PC Station",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                AutoSize = true,
                Location = new Point(20, 12)
            };
            _statusBadge = new Label
            {
                Text = "● Starting Local Server & Tunnel...",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(217, 119, 6),
                AutoSize = true,
                Location = new Point(22, 42)
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

            header.Controls.Add(title);
            header.Controls.Add(_statusBadge);
            header.Controls.Add(headerUpdateBtn);
            header.Resize += (s, e) => { headerUpdateBtn.Location = new Point(Math.Max(260, header.ClientSize.Width - headerUpdateBtn.Width - 20), 18); };

            var infoCard = new Panel { Location = new Point(20, 88), Size = new Size(724, 184), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.White, Padding = new Padding(16) };

            infoCard.Controls.Add(new Label { Text = "Local Workspace URL:", Location = new Point(16, 16), Size = new Size(150, 22), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            var localBox = new TextBox { Text = "http://127.0.0.1:" + _port + "/workspace/", ReadOnly = true, Location = new Point(170, 14), Size = new Size(530, 24), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(248, 250, 252) };
            infoCard.Controls.Add(localBox);

            infoCard.Controls.Add(new Label { Text = "Live Tunnel Endpoint:", Location = new Point(16, 50), Size = new Size(150, 22), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            _tunnelUrlBox = new TextBox { Text = "Connecting Cloudflare / OpenSSH...", ReadOnly = true, Location = new Point(170, 48), Size = new Size(530, 24), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(248, 250, 252) };
            infoCard.Controls.Add(_tunnelUrlBox);

            infoCard.Controls.Add(new Label { Text = "Customer QR Link:", Location = new Point(16, 84), Size = new Size(150, 22), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            var qrBox = new TextBox { Text = _cloudQrUrl, ReadOnly = true, Location = new Point(170, 82), Size = new Size(530, 24), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(248, 250, 252) };
            infoCard.Controls.Add(qrBox);

            infoCard.Controls.Add(new Label { Text = "Local SSD Folder:", Location = new Point(16, 118), Size = new Size(150, 22), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            var dirBox = new TextBox { Text = _server.InboxDir, ReadOnly = true, Location = new Point(170, 116), Size = new Size(530, 24), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(248, 250, 252) };
            infoCard.Controls.Add(dirBox);

            infoCard.Controls.Add(new Label { Text = "Local SSD Used:", Location = new Point(16, 152), Size = new Size(150, 22), ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            _storageBox = new TextBox { Text = "Calculating disk usage...", ReadOnly = true, Location = new Point(170, 150), Size = new Size(530, 24), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(248, 250, 252), ForeColor = Color.FromArgb(15, 23, 42) };
            infoCard.Controls.Add(_storageBox);

            var btnRow = new FlowLayoutPanel { Location = new Point(20, 280), Size = new Size(724, 86), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, WrapContents = true };

            var openWsBtn = new Button { Text = "🖥️ Open Workspace", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Padding = new Padding(10, 4, 10, 4), Cursor = Cursors.Hand };
            openWsBtn.FlatAppearance.BorderSize = 0;
            openWsBtn.Click += (s, e) => Process.Start("http://127.0.0.1:" + _port + "/workspace/");

            var openDirBtn = new Button { Text = "📂 Open Inbox Folder", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.White, ForeColor = Color.FromArgb(15, 23, 42), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Cursor = Cursors.Hand };
            openDirBtn.Click += (s, e) => Process.Start("explorer.exe", _server.InboxDir);

            var copyQrBtn = new Button { Text = "📋 Copy QR Link", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.White, ForeColor = Color.FromArgb(15, 23, 42), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Cursor = Cursors.Hand };
            copyQrBtn.Click += (s, e) => { Clipboard.SetText(_cloudQrUrl); AppendLog("Copied Customer QR Link: " + _cloudQrUrl); };

            var restartBtn = new Button { Text = "↻ Restart Tunnel", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.White, ForeColor = Color.FromArgb(15, 23, 42), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Cursor = Cursors.Hand };
            restartBtn.Click += (s, e) => { _tunnelUrlBox.Text = "Reconnecting..."; _tunnel.Restart(); };

            _autoStartBtn = new Button { Text = IsAutoStartEnabled() ? "✅ Auto-Start ON" : "Auto-Start OFF", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.White, ForeColor = Color.FromArgb(15, 23, 42), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Cursor = Cursors.Hand };
            _autoStartBtn.Click += (s, e) => ToggleAutoStart();

            var clearStorageBtn = new Button { Text = "🧹 Free Up Storage", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.FromArgb(254, 242, 242), ForeColor = Color.FromArgb(185, 28, 28), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Cursor = Cursors.Hand };
            clearStorageBtn.FlatAppearance.BorderColor = Color.FromArgb(254, 202, 202);
            clearStorageBtn.Click += (s, e) => ShowClearStorageDialog();

            var updateBtn = new Button { Text = "🔄 Check Updates", AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 5), BackColor = Color.White, ForeColor = Color.FromArgb(29, 78, 216), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 4, 8, 4), Cursor = Cursors.Hand };
            updateBtn.Click += (s, e) => ThreadPool.QueueUserWorkItem(_ => RunUpdateCheck(true));

            btnRow.Controls.AddRange(new Control[] { openWsBtn, openDirBtn, copyQrBtn, restartBtn, _autoStartBtn, clearStorageBtn, updateBtn });

            _logBox = new TextBox
            {
                Location = new Point(20, 372),
                Size = new Size(724, 152),
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
            menu.Items.Add("Open Workspace", null, (s, e) => Process.Start("http://127.0.0.1:" + _port + "/workspace/"));
            menu.Items.Add("Free Up Storage...", null, (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); ShowClearStorageDialog(); });
            menu.Items.Add("Check for Updates", null, (s, e) => ThreadPool.QueueUserWorkItem(_ => RunUpdateCheck(true)));
            menu.Items.Add("Show Station Window", null, (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
            menu.Items.Add("Exit Scan Drop", null, (s, e) => { _allowExit = true; Close(); });

            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Scan Drop Station",
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                _trayIcon.ShowBalloonTip(2000, "Scan Drop Running", "Local storage & tunnel stay active in the system tray.", ToolTipIcon.Info);
                return;
            }
            if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
            base.OnFormClosing(e);
        }

        private void AppendLog(string msg)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    _logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine);
                }));
            }
            catch { }
        }

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    return key != null && key.GetValue("ScanDropStation") != null;
                }
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
                        key.SetValue("ScanDropStation", "\"" + Application.ExecutablePath + "\"");
                        _autoStartBtn.Text = "✅ Auto-Start ON";
                        AppendLog("Enabled Windows startup auto-launch.");
                    }
                }
            }
            catch (Exception ex) { AppendLog("Auto-Start error: " + ex.Message); }
        }

        private void RunUpdateCheck(bool interactive)
        {
            try
            {
                string rootDir = Path.GetDirectoryName(_server.InboxDir);
                if (AutoUpdater.SyncWorkspaceUi(rootDir))
                {
                    AppendLog("✨ Synced latest Workspace UI from cloud CDN.");
                }

                var info = AutoUpdater.CheckForUpdate();
                if (!info.UpdateAvailable)
                {
                    if (interactive && !IsDisposed)
                    {
                        AppendLog("Scan Drop v" + info.CurrentVersion + " is up to date.");
                        BeginInvoke((Action)(() =>
                            MessageBox.Show("You are running the latest version (v" + info.CurrentVersion + ").", "Scan Drop Updater", MessageBoxButtons.OK, MessageBoxIcon.Information)));
                    }
                    return;
                }

                AppendLog("🔔 Update available: v" + info.LatestVersion + " (current v" + info.CurrentVersion + ")");
                if (IsDisposed) return;
                BeginInvoke((Action)(() =>
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
                                try { _server.Dispose(); } catch { }
                            }, out err) && !IsDisposed)
                            {
                                AppendLog("❌ Update failed: " + err);
                                BeginInvoke((Action)(() =>
                                    MessageBox.Show("Update failed: " + err, "Scan Drop Updater", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                            }
                        });
                    }
                }));
            }
            catch (Exception ex)
            {
                if (interactive) AppendLog("Update check failed: " + ex.Message);
            }
        }

        private void RefreshStorageStats()
        {
            if (IsDisposed || _storageBox == null) return;
            try
            {
                var st = _server.GetStorageSummary();
                int totalJobs = st.ActiveCount + st.CompletedCount;
                string summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} used ({1} job(s), {2} staging/temp) • Drive Free: {3}",
                    FormatSize(st.TotalUsedBytes),
                    totalJobs,
                    FormatSize(st.StagingBytes + st.OrphanBytes),
                    FormatSize(st.FreeDriveBytes)
                );
                _storageBox.Text = summary;
                _storageBox.ForeColor = st.TotalUsedBytes > 250L * 1024L * 1024L
                    ? Color.FromArgb(180, 83, 9)
                    : Color.FromArgb(15, 23, 42);
            }
            catch { }
        }

        private void ShowClearStorageDialog()
        {
            var st = _server.GetStorageSummary();
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
                        "Storage Folder: {0}\n" +
                        "• Total Used: {1}  (Free on Drive: {2})\n" +
                        "• Pending / Incoming Jobs ({3}): {4}\n" +
                        "• Completed Jobs ({5}): {6}\n" +
                        "• Temp Chunks & Orphaned Files: {7}",
                        st.InboxDir,
                        FormatSize(st.TotalUsedBytes),
                        FormatSize(st.FreeDriveBytes),
                        st.ActiveCount, FormatSize(st.ActiveBytes),
                        st.CompletedCount, FormatSize(st.CompletedBytes),
                        FormatSize(st.StagingBytes + st.OrphanBytes)
                    )
                };

                Action<string, bool> runClear = (scope, askConfirm) =>
                {
                    if (askConfirm)
                    {
                        var confirm = MessageBox.Show(
                            "This will permanently delete ALL uploaded customer files and temporary chunks from your PC (" + FormatSize(st.TotalUsedBytes) + ").\nShop pairing & settings will stay intact.\n\nContinue?",
                            "Confirm Clear All Storage",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning
                        );
                        if (confirm != DialogResult.Yes) return;
                    }
                    var res = _server.ClearStorage(scope);
                    RefreshStorageStats();
                    dlg.Close();
                    MessageBox.Show(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Storage cleanup complete!\n\nFreed: {0}\nRemoved Jobs: {1}\nRemoved Temp/Orphan Items: {2}\nRemaining Used: {3}",
                            FormatSize(res.FreedBytes),
                            res.DeletedSubmissions,
                            res.RemovedTempItems,
                            FormatSize(res.Remaining.TotalUsedBytes)
                        ),
                        "Storage Freed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                };

                var btnTemp = new Button
                {
                    Text = "🧹 Clear Temp, Expired & Orphans (" + FormatSize(st.StagingBytes + st.OrphanBytes) + ")",
                    Location = new Point(20, 152),
                    Size = new Size(434, 40),
                    BackColor = Color.White,
                    ForeColor = Color.FromArgb(15, 23, 42),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnTemp.Click += (s, e) => runClear("temp", false);

                var btnCompleted = new Button
                {
                    Text = "✅ Clear Completed Jobs + Temp (" + FormatSize(st.CompletedBytes + st.StagingBytes + st.OrphanBytes) + ")",
                    Location = new Point(20, 200),
                    Size = new Size(434, 40),
                    BackColor = Color.FromArgb(239, 246, 255),
                    ForeColor = Color.FromArgb(29, 78, 216),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnCompleted.Click += (s, e) => runClear("completed", false);

                var btnAll = new Button
                {
                    Text = "🗑️ Clear ALL Uploaded Files & Staging (" + FormatSize(st.TotalUsedBytes) + ")",
                    Location = new Point(20, 248),
                    Size = new Size(434, 42),
                    BackColor = Color.FromArgb(220, 38, 38),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                btnAll.FlatAppearance.BorderSize = 0;
                btnAll.Click += (s, e) => runClear("all", true);

                dlg.Controls.Add(infoText);
                dlg.Controls.Add(btnTemp);
                dlg.Controls.Add(btnCompleted);
                dlg.Controls.Add(btnAll);
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

