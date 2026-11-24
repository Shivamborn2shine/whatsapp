using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using ScanDrop.Native.Core;

namespace ScanDrop.Native.Storage
{
    /// <summary>
    /// ACID-compliant local SQLite operational database powered by Windows 10/11 built-in winsqlite3.dll.
    /// Implements all 7 PRD Section 10 tables with WAL mode, foreign keys, indexes, and parameterized queries.
    /// </summary>
    public sealed class SqliteDatabase : IDisposable
    {
        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_DONE = 101;
        private const int SQLITE_INTEGER = 1;
        private const int SQLITE_FLOAT = 2;
        private const int SQLITE_TEXT = 3;
        private const int SQLITE_NULL = 5;
        private const int SQLITE_OPEN_READWRITE = 0x00000002;
        private const int SQLITE_OPEN_CREATE = 0x00000004;
        private const int SQLITE_OPEN_FULLMUTEX = 0x00010000;
        private static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr zVfs);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close_v2(IntPtr db);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_free(IntPtr ptr);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(IntPtr db, byte[] zSql, int nByte, out IntPtr ppStmt, out IntPtr pzTail);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_text(IntPtr stmt, int idx, byte[] val, int nBytes, IntPtr destructor);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_int64(IntPtr stmt, int idx, long val);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_null(IntPtr stmt, int idx);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_count(IntPtr stmt);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_type(IntPtr stmt, int iCol);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_name(IntPtr stmt, int iCol);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text(IntPtr stmt, int iCol);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_bytes(IntPtr stmt, int iCol);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern long sqlite3_column_int64(IntPtr stmt, int iCol);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_errmsg(IntPtr db);

        private readonly string _dbPath;
        private readonly object _lock = new object();
        private IntPtr _db;
        private bool _disposed;

        public string DbPath { get { return _dbPath; } }

        public SqliteDatabase(string dbPath)
        {
            _dbPath = dbPath;
            string dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            byte[] pathBytes = Encoding.UTF8.GetBytes(dbPath + "\0");
            int rc = sqlite3_open_v2(pathBytes, out _db, SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX, IntPtr.Zero);
            if (rc != SQLITE_OK || _db == IntPtr.Zero)
            {
                string err = _db != IntPtr.Zero ? ReadUtf8NullTerminated(sqlite3_errmsg(_db)) : "Unknown error";
                throw new InvalidOperationException("Failed to open SQLite DB (" + rc + "): " + err);
            }

            InitializeSchema();
        }

        private void InitializeSchema()
        {
            ExecRaw("PRAGMA journal_mode=WAL;");
            ExecRaw("PRAGMA synchronous=NORMAL;");
            ExecRaw("PRAGMA foreign_keys=ON;");
            ExecRaw("PRAGMA busy_timeout=5000;");

            ExecRaw(@"
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
                    status TEXT NOT NULL DEFAULT 'uploading',
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
                    conversion_status TEXT NOT NULL DEFAULT 'none',
                    converted_path TEXT,
                    received_at TEXT NOT NULL,
                    FOREIGN KEY (submission_id) REFERENCES submissions(submission_id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS idx_files_submission ON files(submission_id);

                CREATE TABLE IF NOT EXISTS upload_sessions (
                    session_id TEXT PRIMARY KEY,
                    submission_id TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'open',
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
            ");

            EnsureDefaultShop();
        }

        private void EnsureDefaultShop()
        {
            var rows = Query("SELECT shop_id FROM shops LIMIT 1;");
            if (rows.Count == 0)
            {
                string nowIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                Execute(
                    "INSERT INTO shops (shop_id, shop_name, slug, created_at, settings) VALUES (?, ?, ?, ?, ?);",
                    "shop_quickprint_01", "QuickPrint Xerox & Stationery", "quickprint", nowIso, "{}"
                );
                AddActivity("SYSTEM_INIT", "system", null, "Local SQLite database initialized (WAL mode).");
            }
        }

        public void ExecRaw(string sql)
        {
            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException("SqliteDatabase");
                byte[] bytes = Encoding.UTF8.GetBytes(sql + "\0");
                IntPtr errmsg;
                int rc = sqlite3_exec(_db, bytes, IntPtr.Zero, IntPtr.Zero, out errmsg);
                if (rc != SQLITE_OK)
                {
                    string err = errmsg != IntPtr.Zero ? ReadUtf8NullTerminated(errmsg) : ReadUtf8NullTerminated(sqlite3_errmsg(_db));
                    if (errmsg != IntPtr.Zero) sqlite3_free(errmsg);
                    throw new InvalidOperationException("SQLite exec error (" + rc + "): " + err);
                }
            }
        }

        public void Execute(string sql, params object[] parameters)
        {
            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException("SqliteDatabase");
                IntPtr stmt = PrepareAndBindUnsafe(sql, parameters);
                try
                {
                    int rc = sqlite3_step(stmt);
                    if (rc != SQLITE_DONE && rc != SQLITE_ROW)
                    {
                        throw new InvalidOperationException("SQLite step error (" + rc + "): " + ReadUtf8NullTerminated(sqlite3_errmsg(_db)));
                    }
                }
                finally
                {
                    sqlite3_finalize(stmt);
                }
            }
        }

        public List<Dictionary<string, object>> Query(string sql, params object[] parameters)
        {
            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException("SqliteDatabase");
                var results = new List<Dictionary<string, object>>();
                IntPtr stmt = PrepareAndBindUnsafe(sql, parameters);
                try
                {
                    int colCount = sqlite3_column_count(stmt);
                    string[] colNames = new string[colCount];
                    for (int i = 0; i < colCount; i++)
                    {
                        colNames[i] = ReadUtf8NullTerminated(sqlite3_column_name(stmt, i));
                    }

                    while (true)
                    {
                        int rc = sqlite3_step(stmt);
                        if (rc == SQLITE_ROW)
                        {
                            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            for (int i = 0; i < colCount; i++)
                            {
                                int t = sqlite3_column_type(stmt, i);
                                if (t == SQLITE_NULL)
                                {
                                    row[colNames[i]] = null;
                                }
                                else if (t == SQLITE_INTEGER)
                                {
                                    row[colNames[i]] = sqlite3_column_int64(stmt, i);
                                }
                                else
                                {
                                    IntPtr txtPtr = sqlite3_column_text(stmt, i);
                                    int byteLen = sqlite3_column_bytes(stmt, i);
                                    if (txtPtr == IntPtr.Zero || byteLen <= 0)
                                    {
                                        row[colNames[i]] = "";
                                    }
                                    else
                                    {
                                        byte[] buf = new byte[byteLen];
                                        Marshal.Copy(txtPtr, buf, 0, byteLen);
                                        row[colNames[i]] = Encoding.UTF8.GetString(buf);
                                    }
                                }
                            }
                            results.Add(row);
                        }
                        else if (rc == SQLITE_DONE)
                        {
                            break;
                        }
                        else
                        {
                            throw new InvalidOperationException("SQLite query error (" + rc + "): " + ReadUtf8NullTerminated(sqlite3_errmsg(_db)));
                        }
                    }
                }
                finally
                {
                    sqlite3_finalize(stmt);
                }
                return results;
            }
        }

        public void RunInTransaction(Action action)
        {
            lock (_lock)
            {
                ExecRaw("BEGIN IMMEDIATE TRANSACTION;");
                try
                {
                    action();
                    ExecRaw("COMMIT;");
                }
                catch
                {
                    try { ExecRaw("ROLLBACK;"); } catch { }
                    throw;
                }
            }
        }

        private IntPtr PrepareAndBindUnsafe(string sql, object[] parameters)
        {
            byte[] sqlBytes = Encoding.UTF8.GetBytes(sql);
            IntPtr stmt;
            IntPtr tail;
            int rc = sqlite3_prepare_v2(_db, sqlBytes, sqlBytes.Length, out stmt, out tail);
            if (rc != SQLITE_OK || stmt == IntPtr.Zero)
            {
                throw new InvalidOperationException("SQLite prepare error (" + rc + "): " + ReadUtf8NullTerminated(sqlite3_errmsg(_db)));
            }

            if (parameters != null)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    object p = parameters[i];
                    int bindIdx = i + 1;
                    if (p == null)
                    {
                        sqlite3_bind_null(stmt, bindIdx);
                    }
                    else if (p is int)
                    {
                        sqlite3_bind_int64(stmt, bindIdx, (int)p);
                    }
                    else if (p is long)
                    {
                        sqlite3_bind_int64(stmt, bindIdx, (long)p);
                    }
                    else if (p is bool)
                    {
                        sqlite3_bind_int64(stmt, bindIdx, (bool)p ? 1L : 0L);
                    }
                    else
                    {
                        string s = Convert.ToString(p, CultureInfo.InvariantCulture);
                        byte[] valBytes = Encoding.UTF8.GetBytes(s);
                        sqlite3_bind_text(stmt, bindIdx, valBytes, valBytes.Length, SQLITE_TRANSIENT);
                    }
                }
            }

            return stmt;
        }

        private static string ReadUtf8NullTerminated(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return "";
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            if (len == 0) return "";
            byte[] buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }

        // ====================================================================
        // High-Level Domain Operations
        // ====================================================================

        public ShopRecord GetPrimaryShop()
        {
            var rows = Query("SELECT shop_id, shop_name, slug, created_at, settings FROM shops LIMIT 1;");
            if (rows.Count == 0)
            {
                EnsureDefaultShop();
                rows = Query("SELECT shop_id, shop_name, slug, created_at, settings FROM shops LIMIT 1;");
            }
            var r = rows[0];
            return new ShopRecord
            {
                ShopId = Str(r, "shop_id"),
                ShopName = Str(r, "shop_name"),
                Slug = Str(r, "slug"),
                CreatedAt = Str(r, "created_at"),
                SettingsJson = Str(r, "settings")
            };
        }

        public void UpdateShop(string shopId, string shopName, string slug)
        {
            Execute("UPDATE shops SET shop_name = ?, slug = ? WHERE shop_id = ?;", shopName, slug, shopId);
        }

        public void ReplacePrimaryShop(string newShopId, string shopName, string slug)
        {
            var cur = GetPrimaryShop();
            if (cur == null) return;
            string cleanId = string.IsNullOrWhiteSpace(newShopId) ? cur.ShopId : newShopId.Trim();
            string cleanName = string.IsNullOrWhiteSpace(shopName) ? cur.ShopName : shopName.Trim();
            string cleanSlug = string.IsNullOrWhiteSpace(slug) ? cur.Slug : slug.Trim().ToLowerInvariant();
            if (string.Equals(cur.ShopId, cleanId, StringComparison.Ordinal))
            {
                Execute("UPDATE shops SET shop_name = ?, slug = ? WHERE shop_id = ?;", cleanName, cleanSlug, cur.ShopId);
                return;
            }

            string createdAt = string.IsNullOrWhiteSpace(cur.CreatedAt)
                ? DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                : cur.CreatedAt;
            string settingsJson = string.IsNullOrWhiteSpace(cur.SettingsJson) ? "{}" : cur.SettingsJson;

            RunInTransaction(() =>
            {
                // 1. Free the UNIQUE slug on the old shop row before inserting the new row
                string tempOldSlug = cur.Slug + "_migrating_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                Execute("UPDATE shops SET slug = ? WHERE shop_id = ?;", tempOldSlug, cur.ShopId);

                // 2. Insert/update the new shop row first so FK target exists
                Execute(
                    "INSERT INTO shops (shop_id, shop_name, slug, created_at, settings) VALUES (?, ?, ?, ?, ?) " +
                    "ON CONFLICT(shop_id) DO UPDATE SET shop_name = excluded.shop_name, slug = excluded.slug;",
                    cleanId, cleanName, cleanSlug, createdAt, settingsJson
                );

                // 3. Repoint child submissions to the new shop_id while both parent rows exist
                Execute("UPDATE submissions SET shop_id = ? WHERE shop_id = ?;", cleanId, cur.ShopId);

                // 4. Delete the old shop row now that zero child rows reference it
                Execute("DELETE FROM shops WHERE shop_id = ?;", cur.ShopId);
            });
        }

        public string GetOrCreateMachineFingerprint()
        {
            string existing = GetSetting("machine_fingerprint");
            if (!string.IsNullOrWhiteSpace(existing)) return existing;
            string raw = Environment.MachineName + "|" + Environment.UserDomainName + "|" + Guid.NewGuid().ToString("N");
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                string hex = BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
                SetSetting("machine_fingerprint", hex);
                return hex;
            }
        }

        public string GetSetting(string key, string defaultValue = null)
        {
            var rows = Query("SELECT value, is_secret FROM app_settings WHERE key = ?;", key);
            if (rows.Count == 0) return defaultValue;
            string val = Str(rows[0], "value");
            bool isSecret = Long(rows[0], "is_secret") != 0;
            if (isSecret) return SecretProtector.Unprotect(val);
            return val;
        }

        public void SetSetting(string key, string value, bool isSecret = false)
        {
            string stored = isSecret ? SecretProtector.Protect(value ?? "") : (value ?? "");
            string nowIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            Execute(
                "INSERT INTO app_settings (key, value, is_secret, updated_at) VALUES (?, ?, ?, ?) " +
                "ON CONFLICT(key) DO UPDATE SET value = excluded.value, is_secret = excluded.is_secret, updated_at = excluded.updated_at;",
                key, stored, isSecret ? 1 : 0, nowIso
            );
        }

        public AppSettingsModel LoadSettings(string currentDataRoot)
        {
            var shop = GetPrimaryShop();
            var m = new AppSettingsModel
            {
                ShopId = shop.ShopId,
                ShopName = shop.ShopName,
                ShopSlug = shop.Slug,
                StorageRoot = currentDataRoot
            };

            string pair = GetSetting("pairing_code");
            if (!string.IsNullOrWhiteSpace(pair)) m.PairingCode = pair;

            string devId = GetSetting("device_id");
            if (!string.IsNullOrWhiteSpace(devId)) m.DeviceId = devId;

            string devSec = GetSetting("device_secret");
            if (!string.IsNullOrWhiteSpace(devSec)) m.DeviceSecret = devSec;

            string paired = GetSetting("is_paired");
            if (paired != null)
            {
                m.IsPaired = (paired == "1" || paired.Equals("true", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // Preserve seamless paired state for existing upgraded installations
                m.IsPaired = !string.IsNullOrWhiteSpace(m.DeviceSecret) ||
                             string.Equals(m.ShopSlug, "quickprint", StringComparison.OrdinalIgnoreCase);
            }

            string ret = GetSetting("retention_hours");
            int retVal;
            if (int.TryParse(ret, out retVal) && retVal >= 1 && retVal <= 720) m.RetentionHours = retVal;

            string maxFile = GetSetting("max_file_size_bytes");
            long mfVal;
            if (long.TryParse(maxFile, out mfVal) && mfVal >= 1024 * 1024) m.MaxFileSizeBytes = mfVal;

            string maxSub = GetSetting("max_submission_bytes");
            long msVal;
            if (long.TryParse(maxSub, out msVal) && msVal >= m.MaxFileSizeBytes) m.MaxSubmissionBytes = msVal;

            string pause = GetSetting("pause_receiving");
            if (pause != null) m.PauseReceiving = (pause == "1" || pause.Equals("true", StringComparison.OrdinalIgnoreCase));

            string allow = GetSetting("allowed_extensions");
            if (!string.IsNullOrWhiteSpace(allow)) m.AllowedExtensions = allow;

            string theme = GetSetting("theme");
            if (theme == "dark" || theme == "light") m.Theme = theme;

            string wiz = GetSetting("first_run_completed");
            if (wiz != null) m.FirstRunCompleted = (wiz == "1" || wiz.Equals("true", StringComparison.OrdinalIgnoreCase));

            string pubBase = GetSetting("public_base_url");
            if (!string.IsNullOrWhiteSpace(pubBase))
            {
                string trimmedBase = pubBase.TrimEnd('/');
                if (trimmedBase.Equals("https://scandropdone.netlify.app", StringComparison.OrdinalIgnoreCase) ||
                    trimmedBase.Equals("http://scandropdone.netlify.app", StringComparison.OrdinalIgnoreCase))
                {
                    trimmedBase = "https://scandrop-xi.vercel.app";
                    SetSetting("public_base_url", trimmedBase);
                }
                m.PublicBaseUrl = trimmedBase;
            }

            return m;
        }

        public void SaveSettings(AppSettingsModel s)
        {
            if (s == null) return;
            var shop = GetPrimaryShop();
            string cleanName = string.IsNullOrWhiteSpace(s.ShopName) ? shop.ShopName : s.ShopName.Trim();
            string cleanSlug = string.IsNullOrWhiteSpace(s.ShopSlug) ? shop.Slug : s.ShopSlug.Trim().ToLowerInvariant();
            UpdateShop(shop.ShopId, cleanName, cleanSlug);

            SetSetting("pairing_code", string.IsNullOrWhiteSpace(s.PairingCode) ? "SD-PAIR-7890" : s.PairingCode.Trim());
            if (!string.IsNullOrWhiteSpace(s.DeviceId)) SetSetting("device_id", s.DeviceId.Trim());
            if (!string.IsNullOrWhiteSpace(s.DeviceSecret)) SetSetting("device_secret", s.DeviceSecret.Trim(), true);
            SetSetting("is_paired", s.IsPaired ? "1" : "0");
            SetSetting("retention_hours", Math.Max(1, Math.Min(720, s.RetentionHours)).ToString(CultureInfo.InvariantCulture));
            SetSetting("max_file_size_bytes", Math.Max(5L * 1024L * 1024L, s.MaxFileSizeBytes).ToString(CultureInfo.InvariantCulture));
            SetSetting("max_submission_bytes", Math.Max(s.MaxFileSizeBytes, s.MaxSubmissionBytes).ToString(CultureInfo.InvariantCulture));
            SetSetting("pause_receiving", s.PauseReceiving ? "1" : "0");
            if (!string.IsNullOrWhiteSpace(s.AllowedExtensions)) SetSetting("allowed_extensions", s.AllowedExtensions.Trim().ToLowerInvariant());
            SetSetting("theme", s.Theme == "dark" ? "dark" : "light");
            SetSetting("first_run_completed", s.FirstRunCompleted ? "1" : "0");
            if (!string.IsNullOrWhiteSpace(s.PublicBaseUrl)) SetSetting("public_base_url", s.PublicBaseUrl.Trim().TrimEnd('/'));
        }

        public void AddActivity(string activityType, string actor, string submissionId, string details)
        {
            string id = "act_" + DateTime.UtcNow.Ticks + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            string ts = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            Execute(
                "INSERT INTO activity_logs (activity_id, activity_type, actor, related_submission_id, timestamp, details) VALUES (?, ?, ?, ?, ?, ?);",
                id, activityType ?? "INFO", actor ?? "system", submissionId, ts, details ?? ""
            );
            // Keep activity logs bounded to last 500 records
            Execute("DELETE FROM activity_logs WHERE activity_id NOT IN (SELECT activity_id FROM activity_logs ORDER BY timestamp DESC LIMIT 500);");
        }

        public List<ActivityLogRecord> GetRecentActivity(int limit = 150)
        {
            var rows = Query("SELECT activity_id, activity_type, actor, related_submission_id, timestamp, details FROM activity_logs ORDER BY timestamp DESC LIMIT ?;", limit);
            var list = new List<ActivityLogRecord>(rows.Count);
            foreach (var r in rows)
            {
                list.Add(new ActivityLogRecord
                {
                    ActivityId = Str(r, "activity_id"),
                    ActivityType = Str(r, "activity_type"),
                    Actor = Str(r, "actor"),
                    RelatedSubmissionId = Str(r, "related_submission_id"),
                    Timestamp = Str(r, "timestamp"),
                    Details = Str(r, "details")
                });
            }
            return list;
        }

        public SubmissionRecord GetSubmissionByIdempotencyKey(string idempotencyKey)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey)) return null;
            var rows = Query("SELECT * FROM submissions WHERE idempotency_key = ? LIMIT 1;", idempotencyKey.Trim());
            if (rows.Count == 0) return null;
            var sub = MapSubmission(rows[0]);
            sub.Files = GetFilesForSubmission(sub.SubmissionId);
            return sub;
        }

        public SubmissionRecord GetSubmissionByReceiptOrId(string receiptOrId)
        {
            if (string.IsNullOrWhiteSpace(receiptOrId)) return null;
            string q = receiptOrId.Trim().ToUpperInvariant();
            var rows = Query("SELECT * FROM submissions WHERE UPPER(receipt_code) = ? OR UPPER(submission_id) = ? LIMIT 1;", q, q);
            if (rows.Count == 0) return null;
            var sub = MapSubmission(rows[0]);
            sub.Files = GetFilesForSubmission(sub.SubmissionId);
            return sub;
        }

        public List<SubmissionRecord> GetSubmissions(bool includeUploading = false)
        {
            string sql = includeUploading
                ? "SELECT * FROM submissions ORDER BY created_at DESC;"
                : "SELECT * FROM submissions WHERE status != 'uploading' ORDER BY created_at DESC;";
            var rows = Query(sql);
            var list = new List<SubmissionRecord>(rows.Count);
            var allFiles = GetAllFilesGrouped();
            foreach (var r in rows)
            {
                var sub = MapSubmission(r);
                List<FileRecord> fl;
                if (allFiles.TryGetValue(sub.SubmissionId, out fl)) sub.Files = fl;
                list.Add(sub);
            }
            return list;
        }

        public List<FileRecord> GetFilesForSubmission(string submissionId)
        {
            var rows = Query("SELECT * FROM files WHERE submission_id = ? ORDER BY received_at ASC;", submissionId);
            var list = new List<FileRecord>(rows.Count);
            foreach (var r in rows) list.Add(MapFile(r));
            return list;
        }

        private Dictionary<string, List<FileRecord>> GetAllFilesGrouped()
        {
            var rows = Query("SELECT * FROM files ORDER BY received_at ASC;");
            var map = new Dictionary<string, List<FileRecord>>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                var f = MapFile(r);
                List<FileRecord> bucket;
                if (!map.TryGetValue(f.SubmissionId, out bucket))
                {
                    bucket = new List<FileRecord>();
                    map[f.SubmissionId] = bucket;
                }
                bucket.Add(f);
            }
            return map;
        }

        public FileRecord GetFileById(string fileId)
        {
            if (string.IsNullOrWhiteSpace(fileId)) return null;
            var rows = Query("SELECT * FROM files WHERE file_id = ? LIMIT 1;", fileId.Trim());
            return rows.Count > 0 ? MapFile(rows[0]) : null;
        }

        public bool ReceiptCodeExists(string code)
        {
            var rows = Query("SELECT 1 FROM submissions WHERE UPPER(receipt_code) = ? LIMIT 1;", code.ToUpperInvariant());
            return rows.Count > 0;
        }

        public void InsertSubmission(SubmissionRecord s)
        {
            Execute(
                "INSERT INTO submissions (submission_id, shop_id, receipt_code, customer_name, customer_note, status, idempotency_key, created_at, completed_at, expires_at, total_size) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);",
                s.SubmissionId, s.ShopId, s.ReceiptCode, s.CustomerName ?? "Anonymous", s.CustomerNote ?? "",
                s.Status ?? "uploading", string.IsNullOrWhiteSpace(s.IdempotencyKey) ? null : s.IdempotencyKey,
                s.CreatedAt, s.CompletedAt, s.ExpiresAt, s.TotalSize
            );
        }

        public void InsertFile(FileRecord f)
        {
            Execute(
                "INSERT INTO files (file_id, submission_id, original_filename, internal_filename, content_type, file_extension, file_size, checksum, storage_path, conversion_status, converted_path, received_at) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);",
                f.FileId, f.SubmissionId, f.OriginalFilename, f.InternalFilename, f.ContentType ?? "application/octet-stream",
                f.FileExtension ?? "", f.FileSize, f.Checksum ?? "", f.StoragePath,
                f.ConversionStatus ?? "none", f.ConvertedPath, f.ReceivedAt
            );
        }

        public void UpdateSubmissionStatus(string submissionOrReceipt, string newStatus, long? totalSize = null, string completedAt = null)
        {
            var sub = GetSubmissionByReceiptOrId(submissionOrReceipt);
            if (sub == null) return;
            if (totalSize.HasValue && completedAt != null)
            {
                Execute("UPDATE submissions SET status = ?, total_size = ?, completed_at = ? WHERE submission_id = ?;",
                    newStatus, totalSize.Value, completedAt, sub.SubmissionId);
            }
            else if (totalSize.HasValue)
            {
                Execute("UPDATE submissions SET status = ?, total_size = ? WHERE submission_id = ?;",
                    newStatus, totalSize.Value, sub.SubmissionId);
            }
            else
            {
                Execute("UPDATE submissions SET status = ? WHERE submission_id = ?;", newStatus, sub.SubmissionId);
            }
        }

        public void ExtendSubmissionExpiry(string submissionOrReceipt, string newExpiresAtIso)
        {
            var sub = GetSubmissionByReceiptOrId(submissionOrReceipt);
            if (sub == null) return;
            Execute("UPDATE submissions SET expires_at = ? WHERE submission_id = ?;", newExpiresAtIso, sub.SubmissionId);
        }

        public void RenameFile(string fileId, string newDisplayName)
        {
            Execute("UPDATE files SET original_filename = ? WHERE file_id = ?;", newDisplayName, fileId);
        }

        public void UpdateFileConversion(string fileId, string status, string convertedPath)
        {
            Execute("UPDATE files SET conversion_status = ?, converted_path = ? WHERE file_id = ?;", status, convertedPath, fileId);
        }

        public void DeleteSubmission(string submissionId)
        {
            Execute("DELETE FROM submissions WHERE submission_id = ?;", submissionId);
        }

        public void InsertUploadSession(UploadSessionRecord u)
        {
            Execute(
                "INSERT INTO upload_sessions (session_id, submission_id, status, authorized_upload_token_hash, client_ip, bytes_received, expected_total_bytes, created_at, expires_at) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?);",
                u.SessionId, u.SubmissionId, u.Status ?? "open", u.AuthorizedUploadTokenHash,
                u.ClientIp ?? "", u.BytesReceived, u.ExpectedTotalBytes, u.CreatedAt, u.ExpiresAt
            );
        }

        public UploadSessionRecord GetUploadSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return null;
            var rows = Query("SELECT * FROM upload_sessions WHERE session_id = ? LIMIT 1;", sessionId.Trim());
            if (rows.Count == 0) return null;
            var r = rows[0];
            return new UploadSessionRecord
            {
                SessionId = Str(r, "session_id"),
                SubmissionId = Str(r, "submission_id"),
                Status = Str(r, "status"),
                AuthorizedUploadTokenHash = Str(r, "authorized_upload_token_hash"),
                ClientIp = Str(r, "client_ip"),
                BytesReceived = Long(r, "bytes_received"),
                ExpectedTotalBytes = Long(r, "expected_total_bytes"),
                CreatedAt = Str(r, "created_at"),
                ExpiresAt = Str(r, "expires_at")
            };
        }

        public void UpdateUploadSession(string sessionId, string status, long bytesReceived)
        {
            Execute("UPDATE upload_sessions SET status = ?, bytes_received = ? WHERE session_id = ?;", status, bytesReceived, sessionId);
        }

        public void UpsertUploadPart(UploadPartRecord p)
        {
            Execute(
                "INSERT INTO upload_parts (session_id, file_id, chunk_index, byte_offset, chunk_size, checksum, completed_at) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?) " +
                "ON CONFLICT(session_id, file_id, chunk_index) DO UPDATE SET byte_offset = excluded.byte_offset, chunk_size = excluded.chunk_size, checksum = excluded.checksum, completed_at = excluded.completed_at;",
                p.SessionId, p.FileId, p.ChunkIndex, p.ByteOffset, p.ChunkSize, p.Checksum ?? "", p.CompletedAt
            );
        }

        public List<UploadPartRecord> GetUploadParts(string sessionId, string fileId)
        {
            var rows = Query("SELECT * FROM upload_parts WHERE session_id = ? AND file_id = ? ORDER BY chunk_index ASC;", sessionId, fileId);
            var list = new List<UploadPartRecord>(rows.Count);
            foreach (var r in rows)
            {
                list.Add(new UploadPartRecord
                {
                    SessionId = Str(r, "session_id"),
                    FileId = Str(r, "file_id"),
                    ChunkIndex = (int)Long(r, "chunk_index"),
                    ByteOffset = Long(r, "byte_offset"),
                    ChunkSize = Long(r, "chunk_size"),
                    Checksum = Str(r, "checksum"),
                    CompletedAt = Str(r, "completed_at")
                });
            }
            return list;
        }

        public long GetSessionTotalPartBytes(string sessionId)
        {
            var rows = Query("SELECT COALESCE(SUM(chunk_size), 0) AS total FROM upload_parts WHERE session_id = ?;", sessionId);
            return rows.Count > 0 ? Long(rows[0], "total") : 0L;
        }

        /// <summary>
        /// Crash recovery on startup (PRD Sec 15): marks interrupted 'uploading' sessions/submissions
        /// as failed/expired so partial files never appear as successfully received.
        /// </summary>
        public int RecoverInterruptedSessions(string tempDir)
        {
            int recovered = 0;
            var openSessions = Query("SELECT session_id, submission_id FROM upload_sessions WHERE status IN ('open', 'committing');");
            foreach (var r in openSessions)
            {
                string sid = Str(r, "session_id");
                string subId = Str(r, "submission_id");
                Execute("UPDATE upload_sessions SET status = 'failed' WHERE session_id = ?;", sid);
                Execute("UPDATE submissions SET status = 'failed' WHERE submission_id = ? AND status = 'uploading';", subId);
                string sDir = Path.Combine(tempDir, sid);
                if (Directory.Exists(sDir)) try { Directory.Delete(sDir, true); } catch { }
                recovered++;
            }
            if (recovered > 0)
            {
                AddActivity("CRASH_RECOVERY", "system", null, "Cleaned up " + recovered + " interrupted upload session(s) on startup.");
            }
            return recovered;
        }

        /// <summary>
        /// One-time migration of active non-expired submissions from legacy ScanDrop_Local_Inbox/state.json.
        /// </summary>
        public void MigrateLegacyJsonIfNeeded(string appRootDir)
        {
            if (GetSetting("legacy_json_migrated") == "1") return;
            try
            {
                string legacyState = Path.Combine(appRootDir, "ScanDrop_Local_Inbox", "state.json");
                if (File.Exists(legacyState))
                {
                    var ser = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
                    var state = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(legacyState, Encoding.UTF8));
                    var shop = GetPrimaryShop();
                    if (state != null && state.ContainsKey("submissions") && state["submissions"] is System.Collections.IList)
                    {
                        var subs = (System.Collections.IList)state["submissions"];
                        int imported = 0;
                        foreach (Dictionary<string, object> s in subs)
                        {
                            if (s == null) continue;
                            string oldId = s.ContainsKey("id") ? Convert.ToString(s["id"]) : null;
                            string status = s.ContainsKey("status") ? Convert.ToString(s["status"]) : "received";
                            if (string.IsNullOrEmpty(oldId) || status == "expired") continue;
                            if (GetSubmissionByReceiptOrId(oldId) != null) continue;

                            string mappedStatus = status == "incoming" ? "received" : (status == "completed" ? "printed" : status);
                            string createdAt = s.ContainsKey("createdAt") && s["createdAt"] != null ? Convert.ToString(s["createdAt"]) : DateTime.UtcNow.ToString("o");
                            string expiresAt = s.ContainsKey("expiresAt") && s["expiresAt"] != null ? Convert.ToString(s["expiresAt"]) : DateTime.UtcNow.AddHours(24).ToString("o");
                            long totalBytes = s.ContainsKey("totalSizeBytes") && s["totalSizeBytes"] != null ? Convert.ToInt64(s["totalSizeBytes"]) : 0;

                            var subRec = new SubmissionRecord
                            {
                                SubmissionId = oldId,
                                ShopId = shop.ShopId,
                                ReceiptCode = oldId,
                                CustomerName = s.ContainsKey("customerName") && s["customerName"] != null ? Convert.ToString(s["customerName"]) : "Anonymous",
                                CustomerNote = s.ContainsKey("instructions") && s["instructions"] != null ? Convert.ToString(s["instructions"]) : "",
                                Status = mappedStatus,
                                CreatedAt = createdAt,
                                CompletedAt = createdAt,
                                ExpiresAt = expiresAt,
                                TotalSize = totalBytes
                            };

                            var filesList = s.ContainsKey("files") ? s["files"] as System.Collections.IList : null;
                            var validFiles = new List<FileRecord>();
                            if (filesList != null)
                            {
                                foreach (Dictionary<string, object> f in filesList)
                                {
                                    if (f == null) continue;
                                    string lp = f.ContainsKey("localPath") && f["localPath"] != null ? Convert.ToString(f["localPath"]) : "";
                                    if (string.IsNullOrEmpty(lp) || !File.Exists(lp)) continue;
                                    validFiles.Add(new FileRecord
                                    {
                                        FileId = f.ContainsKey("id") ? Convert.ToString(f["id"]) : "f_" + Guid.NewGuid().ToString("N"),
                                        SubmissionId = oldId,
                                        OriginalFilename = f.ContainsKey("originalName") ? Convert.ToString(f["originalName"]) : Path.GetFileName(lp),
                                        InternalFilename = Path.GetFileName(lp),
                                        ContentType = f.ContainsKey("mimeType") ? Convert.ToString(f["mimeType"]) : "application/octet-stream",
                                        FileExtension = Path.GetExtension(lp).ToLowerInvariant(),
                                        FileSize = new FileInfo(lp).Length,
                                        Checksum = f.ContainsKey("sha256Hash") && f["sha256Hash"] != null ? Convert.ToString(f["sha256Hash"]) : "",
                                        StoragePath = lp,
                                        ConversionStatus = "none",
                                        ReceivedAt = createdAt
                                    });
                                }
                            }

                            if (validFiles.Count > 0)
                            {
                                RunInTransaction(() =>
                                {
                                    InsertSubmission(subRec);
                                    foreach (var fr in validFiles) InsertFile(fr);
                                });
                                imported++;
                            }
                        }
                        if (imported > 0)
                        {
                            AddActivity("LEGACY_MIGRATED", "system", null, "Imported " + imported + " existing submission(s) into SQLite database.");
                        }
                    }
                }
            }
            catch { }
            SetSetting("legacy_json_migrated", "1");
        }

        private static SubmissionRecord MapSubmission(Dictionary<string, object> r)
        {
            return new SubmissionRecord
            {
                SubmissionId = Str(r, "submission_id"),
                ShopId = Str(r, "shop_id"),
                ReceiptCode = Str(r, "receipt_code"),
                CustomerName = Str(r, "customer_name"),
                CustomerNote = Str(r, "customer_note"),
                Status = Str(r, "status"),
                IdempotencyKey = Str(r, "idempotency_key"),
                CreatedAt = Str(r, "created_at"),
                CompletedAt = Str(r, "completed_at"),
                ExpiresAt = Str(r, "expires_at"),
                TotalSize = Long(r, "total_size")
            };
        }

        private static FileRecord MapFile(Dictionary<string, object> r)
        {
            return new FileRecord
            {
                FileId = Str(r, "file_id"),
                SubmissionId = Str(r, "submission_id"),
                OriginalFilename = Str(r, "original_filename"),
                InternalFilename = Str(r, "internal_filename"),
                ContentType = Str(r, "content_type"),
                FileExtension = Str(r, "file_extension"),
                FileSize = Long(r, "file_size"),
                Checksum = Str(r, "checksum"),
                StoragePath = Str(r, "storage_path"),
                ConversionStatus = Str(r, "conversion_status"),
                ConvertedPath = Str(r, "converted_path"),
                ReceivedAt = Str(r, "received_at")
            };
        }

        private static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        private static long Long(Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return 0;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                if (_db != IntPtr.Zero)
                {
                    try { sqlite3_close_v2(_db); } catch { }
                    _db = IntPtr.Zero;
                }
            }
        }
    }
}
