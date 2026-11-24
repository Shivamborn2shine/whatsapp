using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ScanDrop.Native.Core;
using ScanDrop.Native.Storage;

namespace ScanDrop.Native.Ui
{
    /// <summary>
    /// Graphical First-Run Setup Wizard (PRD Sec 13 + Multi-Shop Admin Pairing).
    /// Pairs this Windows PC with an admin-issued Pairing Code (SD-XXXX-XXXX) and
    /// configures local storage folder, retention hours, and max upload size.
    /// </summary>
    public sealed class FirstRunWizardForm : Form
    {
        private readonly SqliteDatabase _db;
        private readonly StoragePaths _paths;

        private TextBox _pairingCodeBox;
        private Label _pairStatusLabel;
        private TextBox _shopNameBox;
        private TextBox _storageDirBox;
        private NumericUpDown _retentionNum;
        private NumericUpDown _maxFileMbNum;
        private ShopPairResult _pairedResult;

        public bool StorageRootChanged { get; private set; }

        public FirstRunWizardForm(SqliteDatabase db, StoragePaths paths)
        {
            _db = db;
            _paths = paths;
            var current = _db.LoadSettings(_paths.DataRootDir);

            Text = "Welcome to Scan Drop — Shop Pairing & Setup";
            Size = new Size(580, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Segoe UI", 9.5f);

            var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Color.White, Padding = new Padding(22, 14, 22, 10) };
            header.Controls.Add(new Label
            {
                Text = "⚡ Scan Drop — First-Run Shop Setup",
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                AutoSize = true,
                Location = new Point(22, 12)
            });
            header.Controls.Add(new Label
            {
                Text = "Enter the Pairing Code from your Scan Drop admin to link this PC to your unique shop QR.",
                Font = new Font("Segoe UI", 8.8f),
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = true,
                Location = new Point(24, 42)
            });

            int y = 90;
            Controls.Add(new Label { Text = "Shop Pairing Code (from Scan Drop Admin):", Location = new Point(24, y), Size = new Size(340, 22), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            _pairingCodeBox = new TextBox
            {
                Location = new Point(24, y + 24),
                Size = new Size(370, 26),
                Text = current.IsPaired ? current.PairingCode : "",
                CharacterCasing = CharacterCasing.Upper
            };
            var pairBtn = new Button
            {
                Text = "🔗 Verify & Pair",
                Location = new Point(402, y + 22),
                Size = new Size(138, 28),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            pairBtn.FlatAppearance.BorderSize = 0;
            pairBtn.Click += (s, e) => TryPairNow(true);
            Controls.Add(_pairingCodeBox);
            Controls.Add(pairBtn);

            _pairStatusLabel = new Label
            {
                Location = new Point(24, y + 54),
                Size = new Size(516, 20),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = current.IsPaired ? Color.FromArgb(22, 163, 74) : Color.FromArgb(100, 116, 139),
                Text = current.IsPaired
                    ? ("✓ Linked to: " + (current.PublicBaseUrl ?? "https://scandrop-xi.vercel.app") + "/shop/" + current.ShopSlug)
                    : "Example: SD-7K9P-3M2X — paste the code issued by your Scan Drop admin."
            };
            Controls.Add(_pairStatusLabel);

            y += 84;
            Controls.Add(new Label { Text = "Shop Display Name:", Location = new Point(24, y), Size = new Size(200, 22), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            _shopNameBox = new TextBox { Location = new Point(24, y + 24), Size = new Size(516, 26), Text = current.ShopName };
            Controls.Add(_shopNameBox);

            y += 64;
            Controls.Add(new Label { Text = "Local File Storage Folder:", Location = new Point(24, y), Size = new Size(220, 22), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            _storageDirBox = new TextBox { Location = new Point(24, y + 24), Size = new Size(424, 26), Text = _paths.DataRootDir };
            var browseBtn = new Button { Text = "Browse...", Location = new Point(456, y + 22), Size = new Size(84, 28), BackColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            browseBtn.Click += (s, e) =>
            {
                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = Directory.Exists(_storageDirBox.Text) ? _storageDirBox.Text : _paths.DataRootDir;
                    fbd.Description = "Choose where Scan Drop stores received customer files & database";
                    if (fbd.ShowDialog(this) == DialogResult.OK)
                    {
                        _storageDirBox.Text = fbd.SelectedPath;
                    }
                }
            };
            Controls.Add(_storageDirBox);
            Controls.Add(browseBtn);

            y += 66;
            Controls.Add(new Label { Text = "Auto-Delete Retention (Hours):", Location = new Point(24, y), Size = new Size(220, 22), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            _retentionNum = new NumericUpDown { Location = new Point(24, y + 24), Size = new Size(236, 26), Minimum = 1, Maximum = 720, Value = Math.Max(1, Math.Min(720, current.RetentionHours)) };
            Controls.Add(_retentionNum);

            Controls.Add(new Label { Text = "Max File Size Limit (MB):", Location = new Point(280, y), Size = new Size(220, 22), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            long curMb = Math.Max(5, Math.Min(500, current.MaxFileSizeBytes / (1024 * 1024)));
            _maxFileMbNum = new NumericUpDown { Location = new Point(280, y + 24), Size = new Size(260, 26), Minimum = 5, Maximum = 500, Value = curMb };
            Controls.Add(_maxFileMbNum);

            y += 68;
            Controls.Add(new Label
            {
                Location = new Point(24, y),
                Size = new Size(516, 36),
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 8.5f),
                Text = "All customer uploads stay 100% on your Windows PC. The cloud registry only routes your permanent QR code to this PC's secure tunnel."
            });

            var saveBtn = new Button
            {
                Text = "✓ Finish Setup & Launch Scan Drop",
                Location = new Point(24, 422),
                Size = new Size(358, 40),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            saveBtn.FlatAppearance.BorderSize = 0;
            saveBtn.Click += OnSaveClick;

            var skipBtn = new Button
            {
                Text = "Skip for Now",
                Location = new Point(394, 422),
                Size = new Size(146, 40),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(15, 23, 42),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            skipBtn.Click += (s, e) =>
            {
                var st = _db.LoadSettings(_paths.DataRootDir);
                st.FirstRunCompleted = true;
                _db.SaveSettings(st);
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(header);
            Controls.Add(saveBtn);
            Controls.Add(skipBtn);
        }

        private bool TryPairNow(bool showMessageBox)
        {
            string code = (_pairingCodeBox.Text ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code))
            {
                if (showMessageBox)
                    MessageBox.Show("Enter the Pairing Code given by your Scan Drop admin.", "Scan Drop Pairing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var st = _db.LoadSettings(_paths.DataRootDir);
            string fp = _db.GetOrCreateMachineFingerprint();
            Cursor = Cursors.WaitCursor;
            ShopPairResult pairRes;
            string err;
            bool ok = ShopPairingClient.PairWithCloud(st.PublicBaseUrl, code, fp, AutoUpdater.CurrentVersion, out pairRes, out err);
            Cursor = Cursors.Default;

            if (!ok)
            {
                _pairStatusLabel.ForeColor = Color.FromArgb(220, 38, 38);
                _pairStatusLabel.Text = "✕ " + (err ?? "Pairing failed.");
                if (showMessageBox)
                    MessageBox.Show(err ?? "Pairing failed.", "Scan Drop Pairing Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            _pairedResult = pairRes;
            _shopNameBox.Text = pairRes.ShopName;
            _pairStatusLabel.ForeColor = Color.FromArgb(22, 163, 74);
            _pairStatusLabel.Text = "✓ Paired! Permanent QR: " + (st.PublicBaseUrl ?? "https://scandrop-xi.vercel.app").TrimEnd('/') + "/shop/" + pairRes.ShopSlug;
            if (showMessageBox)
                MessageBox.Show("Successfully paired with " + pairRes.ShopName + " (/shop/" + pairRes.ShopSlug + ")!", "Shop Paired", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            string code = (_pairingCodeBox.Text ?? "").Trim().ToUpperInvariant();
            var st = _db.LoadSettings(_paths.DataRootDir);

            // If a new pairing code was entered and not yet verified, pair automatically now
            if (!string.IsNullOrWhiteSpace(code) &&
                (_pairedResult == null || !string.Equals(_pairedResult.PairingCode, code, StringComparison.OrdinalIgnoreCase)) &&
                (!st.IsPaired || !string.Equals(st.PairingCode, code, StringComparison.OrdinalIgnoreCase)))
            {
                if (!TryPairNow(true)) return;
            }

            string shopName = (_shopNameBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(shopName))
            {
                MessageBox.Show("Please enter your shop name.", "Scan Drop Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string targetDir = (_storageDirBox.Text ?? "").Trim();
            if (!StoragePaths.TryVerifyWritableDir(targetDir))
            {
                MessageBox.Show("Cannot write to folder:\n" + targetDir + "\n\nPlease pick another folder.", "Folder Not Writable", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (_pairedResult != null)
            {
                _db.ReplacePrimaryShop(_pairedResult.ShopId, shopName, _pairedResult.ShopSlug);
                st.ShopId = _pairedResult.ShopId;
                st.ShopSlug = _pairedResult.ShopSlug;
                st.PairingCode = _pairedResult.PairingCode;
                st.DeviceId = _pairedResult.DeviceId;
                st.DeviceSecret = _pairedResult.DeviceSecret;
                st.IsPaired = true;
            }

            st.ShopName = shopName;
            st.RetentionHours = (int)_retentionNum.Value;
            st.MaxFileSizeBytes = (long)_maxFileMbNum.Value * 1024L * 1024L;
            if (st.MaxSubmissionBytes < st.MaxFileSizeBytes) st.MaxSubmissionBytes = st.MaxFileSizeBytes;
            st.FirstRunCompleted = true;
            _db.SaveSettings(st);

            if (!string.Equals(Path.GetFullPath(targetDir), _paths.DataRootDir, StringComparison.OrdinalIgnoreCase))
            {
                StoragePaths.SaveBootstrapDataRoot(targetDir);
                StorageRootChanged = true;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
