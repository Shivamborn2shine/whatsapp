using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using ScanDrop.Native.Core;

namespace ScanDrop.Native.Storage
{
    public sealed class StorageManager : IDisposable
    {
        private readonly StoragePaths _paths;
        private readonly SqliteDatabase _db;
        private readonly AppLogger _logger;
        private Timer _cleanupTimer;
        private bool _disposed;

        public event Action OnStorageChanged;

        public StoragePaths Paths { get { return _paths; } }

        public StorageManager(StoragePaths paths, SqliteDatabase db, AppLogger logger)
        {
            _paths = paths;
            _db = db;
            _logger = logger;
        }

        public void StartBackgroundCleanup()
        {
            if (_cleanupTimer != null) return;
            _cleanupTimer = new Timer(_ => RunRetentionSweep(), null, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(10));
        }

        public long GetAvailableDriveBytes()
        {
            try
            {
                string root = Path.GetPathRoot(_paths.DataRootDir);
                if (!string.IsNullOrEmpty(root))
                {
                    return new DriveInfo(root).AvailableFreeSpace;
                }
            }
            catch { }
            return 0;
        }

        public bool HasEnoughDiskSpace(long incomingBytes, long reserveBytes, out long availableBytes)
        {
            availableBytes = GetAvailableDriveBytes();
            if (availableBytes <= 0) return true; // Unable to inspect drive
            return (availableBytes - incomingBytes) >= reserveBytes;
        }

        public string GenerateInternalFilename(string cleanExtension)
        {
            string ext = (cleanExtension ?? ".bin").ToLowerInvariant();
            if (!ext.StartsWith(".")) ext = "." + ext;
            foreach (char c in Path.GetInvalidFileNameChars()) ext = ext.Replace(c, '_');
            return Guid.NewGuid().ToString("N") + ext;
        }

        public static string SanitizeDisplayName(string rawName)
        {
            string name = Path.GetFileName(rawName ?? "file");
            if (string.IsNullOrWhiteSpace(name)) name = "file";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (name.Length > 180) name = name.Substring(0, 180);
            return name;
        }

        /// <summary>
        /// Purges expired submissions (past retention window) and abandoned temporary upload chunks.
        /// </summary>
        public int RunRetentionSweep()
        {
            if (_disposed) return 0;
            int purged = 0;
            try
            {
                string nowIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

                // 1. Expire submissions past expires_at
                var expiredRows = _db.Query("SELECT submission_id, receipt_code FROM submissions WHERE status NOT IN ('expired', 'archived') AND expires_at <= ?;", nowIso);
                foreach (var row in expiredRows)
                {
                    string subId = Convert.ToString(row["submission_id"]);
                    string code = Convert.ToString(row["receipt_code"]);
                    DeleteSubmissionFilesFromDisk(subId);
                    _db.UpdateSubmissionStatus(subId, "expired");
                    _db.AddActivity("RETENTION_PURGED", "system", subId, "Expired submission " + code + " reached retention limit and files were deleted.");
                    purged++;
                }

                // 2. Clean expired upload sessions & temp folders
                var expiredSessions = _db.Query("SELECT session_id, submission_id FROM upload_sessions WHERE status IN ('open', 'committing') AND expires_at <= ?;", nowIso);
                foreach (var row in expiredSessions)
                {
                    string sid = Convert.ToString(row["session_id"]);
                    string subId = Convert.ToString(row["submission_id"]);
                    _db.UpdateUploadSession(sid, "expired", 0);
                    _db.UpdateSubmissionStatus(subId, "expired");
                    string sDir = Path.Combine(_paths.TempDir, sid);
                    if (Directory.Exists(sDir)) try { Directory.Delete(sDir, true); } catch { }
                    purged++;
                }

                // 3. Sweep orphaned temp directories older than 2 hours
                if (Directory.Exists(_paths.TempDir))
                {
                    DateTime cutoff = DateTime.UtcNow.AddHours(-2);
                    foreach (var d in Directory.GetDirectories(_paths.TempDir))
                    {
                        try
                        {
                            if (Directory.GetLastWriteTimeUtc(d) < cutoff)
                            {
                                Directory.Delete(d, true);
                                purged++;
                            }
                        }
                        catch { }
                    }
                }

                // 4. Sweep quarantine older than 24h
                if (Directory.Exists(_paths.QuarantineDir))
                {
                    DateTime qCutoff = DateTime.UtcNow.AddHours(-24);
                    foreach (var f in Directory.GetFiles(_paths.QuarantineDir))
                    {
                        try
                        {
                            if (File.GetLastWriteTimeUtc(f) < qCutoff) File.Delete(f);
                        }
                        catch { }
                    }
                }

                if (purged > 0)
                {
                    _logger.Info("Storage", "Retention sweep cleaned " + purged + " expired/abandoned item(s).");
                    FireChanged();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("Storage", "Retention sweep error: " + ex.Message);
            }
            return purged;
        }

        public void DeleteSubmissionFilesFromDisk(string submissionId)
        {
            var files = _db.GetFilesForSubmission(submissionId);
            foreach (var f in files)
            {
                TryDeleteFile(f.StoragePath);
                if (!string.IsNullOrEmpty(f.ConvertedPath)) TryDeleteFile(f.ConvertedPath);
            }
            string subDir = Path.Combine(_paths.ReceivedDir, submissionId);
            if (Directory.Exists(subDir)) try { Directory.Delete(subDir, true); } catch { }
        }

        public int DeleteSubmissions(IEnumerable<string> ids)
        {
            int count = 0;
            foreach (var rawId in ids)
            {
                var sub = _db.GetSubmissionByReceiptOrId(rawId);
                if (sub == null) continue;
                DeleteSubmissionFilesFromDisk(sub.SubmissionId);
                _db.DeleteSubmission(sub.SubmissionId);
                count++;
            }
            if (count > 0)
            {
                _db.AddActivity("SUBMISSION_DELETED", "shopkeeper", null, "Deleted " + count + " submission(s) from local storage.");
                FireChanged();
            }
            return count;
        }

        public string ExportFilePermanently(FileRecord file, string destinationDir = null)
        {
            if (file == null || string.IsNullOrEmpty(file.StoragePath) || !File.Exists(file.StoragePath))
                throw new FileNotFoundException("Source file not found on disk.");

            string targetDir = destinationDir;
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                targetDir = Path.Combine(docs, "ScanDrop_Saved");
            }
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            string safeBase = SanitizeDisplayName(file.OriginalFilename);
            string destPath = Path.Combine(targetDir, safeBase);
            int suffix = 1;
            string nameNoExt = Path.GetFileNameWithoutExtension(safeBase);
            string ext = Path.GetExtension(safeBase);
            while (File.Exists(destPath))
            {
                destPath = Path.Combine(targetDir, nameNoExt + "_" + suffix + ext);
                suffix++;
            }

            File.Copy(file.StoragePath, destPath, false);
            _db.AddActivity("FILE_EXPORTED", "shopkeeper", file.SubmissionId, "Permanently exported " + file.OriginalFilename + " -> " + destPath);
            return destPath;
        }

        public StorageSummary GetStorageSummary()
        {
            var s = new StorageSummary
            {
                StorageRoot = _paths.DataRootDir,
                InboxDir = _paths.ReceivedDir,
                FreeDriveBytes = GetAvailableDriveBytes()
            };

            var subs = _db.GetSubmissions(true);
            var trackedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sub in subs)
            {
                string st = (sub.Status ?? "").ToLowerInvariant();
                if (st == "expired" || st == "failed")
                {
                    s.ExpiredCount++;
                    continue;
                }
                if (st == "uploading") continue;

                trackedDirs.Add(sub.SubmissionId);
                long subBytes = 0;
                foreach (var f in sub.Files)
                {
                    if (!string.IsNullOrEmpty(f.StoragePath) && File.Exists(f.StoragePath))
                    {
                        try { subBytes += new FileInfo(f.StoragePath).Length; } catch { }
                    }
                }

                if (st == "printed" || st == "completed" || st == "archived")
                {
                    s.CompletedCount++;
                    s.CompletedBytes += subBytes;
                }
                else
                {
                    s.ActiveCount++;
                    s.ActiveBytes += subBytes;
                }
            }

            s.StagingBytes = GetDirSizeSafe(_paths.TempDir);
            s.StagingCount = CountDirectChildrenSafe(_paths.TempDir);

            s.ConvertedBytes = GetDirSizeSafe(_paths.ConvertedDir);
            s.ConvertedCount = CountDirectChildrenSafe(_paths.ConvertedDir);

            // Quarantine + untracked folders inside received/
            s.OrphanBytes = GetDirSizeSafe(_paths.QuarantineDir);
            s.OrphanCount = CountDirectChildrenSafe(_paths.QuarantineDir);
            if (Directory.Exists(_paths.ReceivedDir))
            {
                foreach (var d in Directory.GetDirectories(_paths.ReceivedDir))
                {
                    string name = Path.GetFileName(d);
                    if (!trackedDirs.Contains(name))
                    {
                        s.OrphanCount++;
                        s.OrphanBytes += GetDirSizeSafe(d);
                    }
                }
            }

            if (File.Exists(_paths.DatabaseFile))
            {
                try { s.DatabaseBytes = new FileInfo(_paths.DatabaseFile).Length; } catch { }
            }

            s.TotalUsedBytes = s.ActiveBytes + s.CompletedBytes + s.StagingBytes + s.ConvertedBytes + s.OrphanBytes;
            return s;
        }

        public StorageClearResult ClearStorage(string scope)
        {
            scope = (scope ?? "temp").Trim().ToLowerInvariant();
            if (scope != "temp" && scope != "completed" && scope != "all") scope = "temp";

            var before = GetStorageSummary();
            int deletedSubs = 0;
            int removedTemp = 0;

            var subs = _db.GetSubmissions(true);
            var liveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sub in subs)
            {
                string st = (sub.Status ?? "").ToLowerInvariant();
                bool shouldRemove =
                    (scope == "all") ||
                    (scope == "completed" && (st == "printed" || st == "completed" || st == "archived")) ||
                    (st == "expired" || st == "failed");

                if (shouldRemove)
                {
                    DeleteSubmissionFilesFromDisk(sub.SubmissionId);
                    _db.DeleteSubmission(sub.SubmissionId);
                    if (st == "expired" || st == "failed") removedTemp++;
                    else deletedSubs++;
                }
                else
                {
                    liveIds.Add(sub.SubmissionId);
                }
            }

            // Clear temp & quarantine
            removedTemp += ClearDirectoryContents(_paths.TempDir);
            removedTemp += ClearDirectoryContents(_paths.QuarantineDir);

            if (scope == "completed" || scope == "all")
            {
                removedTemp += ClearDirectoryContents(_paths.ConvertedDir);
                removedTemp += ClearDirectoryContents(_paths.PreviewsDir);
            }

            // Sweep orphan directories in received/
            if (Directory.Exists(_paths.ReceivedDir))
            {
                foreach (var d in Directory.GetDirectories(_paths.ReceivedDir))
                {
                    string name = Path.GetFileName(d);
                    if (scope == "all" || !liveIds.Contains(name))
                    {
                        try { Directory.Delete(d, true); removedTemp++; } catch { }
                    }
                }
            }

            // Sweep leftover updater files
            foreach (var tmp in new[] { "ScanDrop.exe.update", "apply-scandrop-update.bat" })
            {
                string p = Path.Combine(_paths.AppRootDir, tmp);
                if (File.Exists(p)) { TryDeleteFile(p); removedTemp++; }
            }

            var after = GetStorageSummary();
            long freed = Math.Max(0, before.TotalUsedBytes - after.TotalUsedBytes);

            _db.AddActivity("STORAGE_CLEARED", "shopkeeper", null,
                string.Format(CultureInfo.InvariantCulture, "Cleared {0} storage ({1} job(s), {2} temp item(s), {3:F2} MB freed).",
                    scope, deletedSubs, removedTemp, freed / (1024.0 * 1024.0)));
            _logger.Info("Storage", string.Format(CultureInfo.InvariantCulture,
                "Cleared {0} storage: freed {1:F2} MB ({2} job(s), {3} temp/orphan item(s)).",
                scope, freed / (1024.0 * 1024.0), deletedSubs, removedTemp));

            FireChanged();
            return new StorageClearResult
            {
                Scope = scope,
                FreedBytes = freed,
                DeletedSubmissions = deletedSubs,
                RemovedTempItems = removedTemp,
                Remaining = after
            };
        }

        private static int ClearDirectoryContents(string dir)
        {
            int removed = 0;
            if (!Directory.Exists(dir)) return 0;
            foreach (var d in Directory.GetDirectories(dir))
            {
                try { Directory.Delete(d, true); removed++; } catch { }
            }
            foreach (var f in Directory.GetFiles(dir))
            {
                try { File.Delete(f); removed++; } catch { }
            }
            return removed;
        }

        private static void TryDeleteFile(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }

        private static long GetDirSizeSafe(string dir)
        {
            long sum = 0;
            if (!Directory.Exists(dir)) return 0;
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { sum += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return sum;
        }

        private static int CountDirectChildrenSafe(string dir)
        {
            if (!Directory.Exists(dir)) return 0;
            try { return Directory.GetDirectories(dir).Length + Directory.GetFiles(dir).Length; } catch { return 0; }
        }

        private void FireChanged()
        {
            var h = OnStorageChanged;
            if (h != null) try { h(); } catch { }
        }

        public void Dispose()
        {
            _disposed = true;
            if (_cleanupTimer != null) { _cleanupTimer.Dispose(); _cleanupTimer = null; }
        }
    }
}
