using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ScanDrop.Native.Core;
using ScanDrop.Native.Storage;

namespace ScanDrop.Native.Ui
{
    /// <summary>
    /// Graphical First-Run Setup Wizard (PRD Sec 13).
    /// Configures shop name, local storage folder, retention hours, and max upload size
    /// without requiring terminal interaction.
    /// </summary>
    public sealed class FirstRunWizardForm : Form
    {
        private readonly SqliteDatabase _db;
        private readonly StoragePaths _paths;

        private TextBox _shopNameBox;
        private TextBox _storageDirBox;
        private NumericUpDown _retentionNum;
        private NumericUpDown _maxFileMbNum;

        public bool StorageRootChanged { get; private set; }

        public FirstRunWizardForm(SqliteDatabase db, StoragePaths paths)
        {
            _db = db;
            _paths = paths;
            var current = _db.LoadSettings(_paths.DataRootDir);

            Text = "Welcome to Scan Drop — Quick Shop Setup";
            Size = new Size(560, 440);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Segoe UI", 9.5f);

            var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.White, Padding = new Padding(22, 14, 22, 10) };
            header.Controls.Add(new Label
            {
                Text = "⚡ Scan Drop — First-Run Shop Setup",
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                AutoSize = true,
                Location = new Point(22, 14)
            });
            header.Controls.Add(new Label
            {
                Text = "Scan. Send. Done. • Local-First Windows File Receiver",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = true,
                Location = new Point(24, 44)
            });

            int y = 96;
            Controls.Add(new Label { Text = "Shop Name:", Location = new Point(24, y), Size = new Size(160, 22), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            _shopNameBox = new TextBox { Location = new Point(24, y + 24), Size = new Size(496, 26), Text = current.ShopName };
            Controls.Add(_shopNameBox);

            y += 66;
            Controls.Add(new Label { Text = "Local File Storage Folder:", Location = new Point(24, y), Size = new Size(220, 22), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            _storageDirBox = new TextBox { Location = new Point(24, y + 24), Size = new Size(404, 26), Text = _paths.DataRootDir };
            var browseBtn = new Button { Text = "Browse...", Location = new Point(436, y + 22), Size = new Size(84, 28), BackColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
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

            y += 68;
            Controls.Add(new Label { Text = "Auto-Delete Retention (Hours):", Location = new Point(24, y), Size = new Size(220, 22), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            _retentionNum = new NumericUpDown { Location = new Point(24, y + 24), Size = new Size(220, 26), Minimum = 1, Maximum = 720, Value = Math.Max(1, Math.Min(720, current.RetentionHours)) };
            Controls.Add(_retentionNum);

            Controls.Add(new Label { Text = "Max File Size Limit (MB):", Location = new Point(276, y), Size = new Size(220, 22), Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            long curMb = Math.Max(5, Math.Min(500, current.MaxFileSizeBytes / (1024 * 1024)));
            _maxFileMbNum = new NumericUpDown { Location = new Point(276, y + 24), Size = new Size(244, 26), Minimum = 5, Maximum = 500, Value = curMb };
            Controls.Add(_maxFileMbNum);

            y += 70;
            Controls.Add(new Label
            {
                Location = new Point(24, y),
                Size = new Size(496, 36),
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 8.5f),
                Text = "All customer uploads stay on your Windows PC. Internet tunnel connects customers only to the isolated public upload receiver — your dashboard stays strictly local."
            });

            var saveBtn = new Button
            {
                Text = "✓ Finish Setup & Launch Scan Drop",
                Location = new Point(24, 344),
                Size = new Size(340, 40),
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
                Text = "Use Defaults",
                Location = new Point(376, 344),
                Size = new Size(144, 40),
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

        private void OnSaveClick(object sender, EventArgs e)
        {
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

            var st = _db.LoadSettings(_paths.DataRootDir);
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
