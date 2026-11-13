using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace ScanDrop.Native
{
    public sealed class LocalHttpServer : IDisposable
    {
        private readonly string _rootDir;
        private readonly string _inboxDir;
        private readonly string _stateFile;
        private readonly int _port;
        private TunnelManager _tunnel;
        private readonly object _stateLock = new object();
        private HttpListener _listener;
        private Thread _listenThread;
        private bool _disposed;

        public event Action<string> OnLog;
        public event Action OnSubmissionsChanged;

        public string InboxDir { get { return _inboxDir; } }

        public LocalHttpServer(string rootDir, int port, TunnelManager tunnel)
        {
            _rootDir = rootDir;
            _port = port;
            _tunnel = tunnel;
            _inboxDir = Path.Combine(rootDir, "ScanDrop_Local_Inbox");
            _stateFile = Path.Combine(_inboxDir, "state.json");
            if (!Directory.Exists(_inboxDir)) Directory.CreateDirectory(_inboxDir);
        }

        public void SetTunnel(TunnelManager tunnel)
        {
            _tunnel = tunnel;
        }

        public int Port { get { return _actualPort; } }
        private int _actualPort;

        public void Start()
        {
            for (int candidate = _port; candidate < _port + 10; candidate++)
            {
                var l = new HttpListener();
                l.Prefixes.Add(string.Format(CultureInfo.InvariantCulture, "http://127.0.0.1:{0}/", candidate));
                l.Prefixes.Add(string.Format(CultureInfo.InvariantCulture, "http://localhost:{0}/", candidate));
                try
                {
                    l.Start();
                    _listener = l;
                    _actualPort = candidate;
                    break;
                }
                catch
                {
                    try { l.Close(); } catch { }
                    if (candidate == _port + 9) throw;
                }
            }

            _listenThread = new Thread(ListenLoop) { IsBackground = true };
            _listenThread.Start();
            Log("Local HTTP server listening on http://127.0.0.1:" + _actualPort + "/workspace/");
        }

        private void ListenLoop()
        {
            while (!_disposed && _listener != null && _listener.IsListening)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Log("Listener error: " + ex.Message); }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            AddCors(res);

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            string path = req.Url.AbsolutePath;
            try
            {
                if (path == "/" && req.HttpMethod == "GET")
                {
                    res.StatusCode = 302;
                    res.RedirectLocation = "/workspace/";
                    res.Close();
                    return;
                }

                if ((path == "/workspace" || path == "/workspace/") && req.HttpMethod == "GET")
                {
                    ServeWorkspaceHtml(res);
                    return;
                }

                if (path == "/api/health" && req.HttpMethod == "GET")
                {
                    WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "appVersion", AutoUpdater.CurrentVersion },
                        { "storageMode", "local-pc" },
                        { "inboxDir", _inboxDir },
                        { "tunnelUrl", _tunnel != null ? _tunnel.LiveTunnelUrl : null },
                        { "tunnelOnline", _tunnel != null && !string.IsNullOrEmpty(_tunnel.LiveTunnelUrl) },
                        { "tunnelProvider", _tunnel != null ? _tunnel.ActiveProviderName : null }
                    });
                    return;
                }

                if ((path == "/api/version" && req.HttpMethod == "GET") || (path == "/api/update/check" && req.HttpMethod == "POST"))
                {
                    var upd = AutoUpdater.CheckForUpdate();
                    bool uiUpdated = AutoUpdater.SyncWorkspaceUi(_rootDir);
                    WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "currentVersion", upd.CurrentVersion },
                        { "latestVersion", upd.LatestVersion },
                        { "updateAvailable", upd.UpdateAvailable },
                        { "releaseNotes", upd.ReleaseNotes },
                        { "downloadUrl", upd.DownloadUrl },
                        { "uiUpdated", uiUpdated }
                    });
                    return;
                }

                if (path == "/api/update/apply" && req.HttpMethod == "POST")
                {
                    var upd = AutoUpdater.CheckForUpdate();
                    if (!upd.UpdateAvailable)
                    {
                        WriteJson(res, 200, new Dictionary<string, object>
                        {
                            { "success", true },
                            { "updating", false },
                            { "message", "Already running the latest version (" + upd.CurrentVersion + ")." }
                        });
                        return;
                    }
                    WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "updating", true },
                        { "targetVersion", upd.LatestVersion },
                        { "message", "Downloading v" + upd.LatestVersion + " and restarting ScanDrop.exe..." }
                    });
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        string err;
                        AutoUpdater.ApplyUpdate(upd, () =>
                        {
                            try { if (_tunnel != null) _tunnel.Dispose(); } catch { }
                            try { Dispose(); } catch { }
                        }, out err);
                        if (!string.IsNullOrEmpty(err)) Log("Auto-update error: " + err);
                    });
                    return;
                }

                if (path == "/api/state" && req.HttpMethod == "GET")
                {
                    var state = LoadState();
                    var shops = state["shops"] as System.Collections.IList;
                    if (shops != null && shops.Count > 0)
                    {
                        var s0 = shops[0] as Dictionary<string, object>;
                        if (s0 != null)
                        {
                            s0["tunnelUrl"] = _tunnel.LiveTunnelUrl;
                            s0["tunnelOnline"] = !string.IsNullOrEmpty(_tunnel.LiveTunnelUrl);
                        }
                    }
                    WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "appVersion", AutoUpdater.CurrentVersion },
                        { "plans", GetPlans() },
                        { "state", state },
                        { "storageMode", "local-pc" },
                        { "tunnelUrl", _tunnel != null ? _tunnel.LiveTunnelUrl : null },
                        { "tunnelOnline", _tunnel != null && !string.IsNullOrEmpty(_tunnel.LiveTunnelUrl) }
                    });
                    return;
                }

                if (path == "/api/direct-upload" && req.HttpMethod == "POST")
                {
                    HandleDirectUpload(req, res);
                    return;
                }

                if (path == "/api/file" && req.HttpMethod == "GET")
                {
                    HandleFileDownload(req, res);
                    return;
                }

                if (path == "/api/receipt" && req.HttpMethod == "GET")
                {
                    string rid = (req.QueryString["id"] ?? "").Trim().ToUpperInvariant();
                    var state = LoadState();
                    var subs = state["submissions"] as System.Collections.IList;
                    Dictionary<string, object> found = null;
                    if (subs != null)
                    {
                        foreach (Dictionary<string, object> s in subs)
                        {
                            if (GetStr(s, "id") == rid && GetStr(s, "status") != "expired") { found = s; break; }
                        }
                    }
                    if (found == null)
                        WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "Receipt not found or expired." } });
                    else
                        WriteJson(res, 200, new Dictionary<string, object> { { "success", true }, { "submission", found } });
                    return;
                }

                if (path == "/api/submission/status" && req.HttpMethod == "POST")
                {
                    var body = ReadJsonBody(req);
                    string subId = GetStr(body, "submissionId");
                    string status = GetStr(body, "status");
                    lock (_stateLock)
                    {
                        var state = LoadStateUnsafe();
                        var subs = state["submissions"] as System.Collections.IList;
                        Dictionary<string, object> target = null;
                        if (subs != null)
                        {
                            foreach (Dictionary<string, object> s in subs)
                            {
                                if (GetStr(s, "id") == subId) { target = s; break; }
                            }
                        }
                        if (target == null)
                        {
                            WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "Submission not found" } });
                            return;
                        }
                        target["status"] = status;
                        AddAudit(state, GetStr(target, "shopId"), "shopkeeper", "SUBMISSION_STATUS", "Marked " + subId + " as " + status);
                        SaveStateUnsafe(state);
                        WriteJson(res, 200, new Dictionary<string, object> { { "success", true }, { "submission", target } });
                    }
                    FireSubmissionsChanged();
                    return;
                }

                if (path == "/api/submission/delete" && req.HttpMethod == "POST")
                {
                    var body = ReadJsonBody(req);
                    var idsToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string singleId = GetStr(body, "submissionId");
                    if (!string.IsNullOrEmpty(singleId)) idsToDelete.Add(singleId);
                    object rawList;
                    if (body != null && body.TryGetValue("submissionIds", out rawList) && rawList is System.Collections.IEnumerable)
                    {
                        foreach (var item in (System.Collections.IEnumerable)rawList)
                        {
                            string sId = Convert.ToString(item);
                            if (!string.IsNullOrEmpty(sId)) idsToDelete.Add(sId);
                        }
                    }

                    int deletedCount = 0;
                    lock (_stateLock)
                    {
                        var state = LoadStateUnsafe();
                        var subs = state["submissions"] as System.Collections.IList;
                        string shopId = null;
                        if (subs != null)
                        {
                            for (int i = subs.Count - 1; i >= 0; i--)
                            {
                                var s = subs[i] as Dictionary<string, object>;
                                string curId = GetStr(s, "id");
                                if (idsToDelete.Contains(curId))
                                {
                                    shopId = GetStr(s, "shopId");
                                    subs.RemoveAt(i);
                                    deletedCount++;
                                    string subDir = Path.Combine(_inboxDir, curId);
                                    if (Directory.Exists(subDir)) try { Directory.Delete(subDir, true); } catch { }
                                }
                            }
                        }
                        if (deletedCount == 0)
                        {
                            WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "Submission not found" } });
                            return;
                        }
                        AddAudit(state, shopId, "shopkeeper", "SUBMISSION_DELETED", "Deleted " + deletedCount + " submission(s) from local SSD");
                        SaveStateUnsafe(state);
                    }
                    FireSubmissionsChanged();
                    WriteJson(res, 200, new Dictionary<string, object> { { "success", true }, { "deletedCount", deletedCount } });
                    return;
                }

                if (path == "/api/pair" && req.HttpMethod == "POST")
                {
                    var body = ReadJsonBody(req);
                    string code = GetStr(body, "pairingCode");
                    string devName = GetStr(body, "deviceName");
                    lock (_stateLock)
                    {
                        var state = LoadStateUnsafe();
                        var shops = state["shops"] as System.Collections.IList;
                        var devs = state["devices"] as System.Collections.IList;
                        var shop = shops != null && shops.Count > 0 ? shops[0] as Dictionary<string, object> : null;
                        var dev = devs != null && devs.Count > 0 ? devs[0] as Dictionary<string, object> : null;
                        if (shop != null && !string.IsNullOrEmpty(code)) shop["pairingCode"] = code;
                        if (dev != null && !string.IsNullOrEmpty(devName)) dev["deviceName"] = devName;
                        SaveStateUnsafe(state);
                        _tunnel.UpdateIdentity(GetStr(shop, "id"), GetStr(shop, "pairingCode"));
                        WriteJson(res, 200, new Dictionary<string, object> { { "success", true }, { "shop", shop }, { "device", dev } });
                    }
                    return;
                }

                WriteJson(res, 404, new Dictionary<string, object> { { "error", "Not Found" } });
            }
            catch (Exception ex)
            {
                Log("Request error (" + path + "): " + ex.Message);
                try { WriteJson(res, 500, new Dictionary<string, object> { { "success", false }, { "error", ex.Message } }); } catch { }
            }
        }

        private void ServeWorkspaceHtml(HttpListenerResponse res)
        {
            string[] candidates = new[]
            {
                Path.Combine(_rootDir, "workspace", "index.html"),
                Path.Combine(_rootDir, "public", "workspace", "index.html"),
                Path.Combine(_rootDir, "dist_netlify", "workspace", "index.html")
            };
            string html = null;
            foreach (var c in candidates)
            {
                if (File.Exists(c)) { html = File.ReadAllText(c, Encoding.UTF8); break; }
            }
            if (html == null)
            {
                html = "<html><body style='font-family:sans-serif;padding:40px;'><h2>Scan Drop Workspace</h2><p>workspace/index.html not found next to ScanDrop.exe. Run <code>node build_netlify.js</code>.</p></body></html>";
            }
            byte[] buf = Encoding.UTF8.GetBytes(html);
            res.StatusCode = 200;
            res.ContentType = "text/html; charset=utf-8";
            res.ContentLength64 = buf.Length;
            res.OutputStream.Write(buf, 0, buf.Length);
            res.Close();
        }

        private void HandleDirectUpload(HttpListenerRequest req, HttpListenerResponse res)
        {
            byte[] raw;
            using (var ms = new MemoryStream())
            {
                req.InputStream.CopyTo(ms);
                raw = ms.ToArray();
            }

            Dictionary<string, string> fields;
            List<UploadedFilePart> files;
            ParseMultipart(raw, req.ContentType, out fields, out files);

            if (files.Count == 0)
            {
                WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", "No files attached." } });
                return;
            }

            var rng = new Random();
            string receiptId;
            DateTime now = DateTime.UtcNow;
            DateTime expires = now.AddHours(24);
            string nowIso = now.ToString("o");
            string expIso = expires.ToString("o");
            long totalBytes = 0;

            lock (_stateLock)
            {
                var state = LoadStateUnsafe();
                var subs = state["submissions"] as System.Collections.IList;
                if (subs == null) { subs = new List<object>(); state["submissions"] = subs; }
                var shops = state["shops"] as System.Collections.IList;
                var shop = (shops != null && shops.Count > 0) ? shops[0] as Dictionary<string, object> : null;
                string shopId = shop != null ? GetStr(shop, "id") : "shop_quickprint_01";

                receiptId = "SD-" + rng.Next(1000, 9999);
                string subDir = Path.Combine(_inboxDir, receiptId);
                Directory.CreateDirectory(subDir);

                var fileRecords = new List<object>();
                for (int i = 0; i < files.Count; i++)
                {
                    var f = files[i];
                    string fileId = string.Format(CultureInfo.InvariantCulture, "f_{0}_{1}", DateTime.UtcNow.Ticks, i);
                    string safeName = SanitizeFileName(f.FileName);
                    string diskPath = Path.Combine(subDir, fileId + "_" + safeName);
                    File.WriteAllBytes(diskPath, f.Bytes);
                    totalBytes += f.Bytes.Length;

                    string sha;
                    using (var sha256 = SHA256.Create())
                    {
                        sha = BitConverter.ToString(sha256.ComputeHash(f.Bytes)).Replace("-", "").ToLowerInvariant();
                    }

                    fileRecords.Add(new Dictionary<string, object>
                    {
                        { "id", fileId },
                        { "submissionId", receiptId },
                        { "originalName", f.FileName },
                        { "mimeType", f.ContentType },
                        { "fileExtension", Path.GetExtension(f.FileName).ToLowerInvariant() },
                        { "sizeBytes", f.Bytes.Length },
                        { "localPath", diskPath },
                        { "sha256Hash", sha },
                        { "status", "local-ssd" },
                        { "createdAt", nowIso },
                        { "expiresAt", expIso }
                    });
                }

                string custName = fields.ContainsKey("customerName") && !string.IsNullOrWhiteSpace(fields["customerName"])
                    ? fields["customerName"].Trim() : "Anonymous";
                string instr = fields.ContainsKey("instructions") ? fields["instructions"].Trim() : "";

                subs.Insert(0, new Dictionary<string, object>
                {
                    { "id", receiptId },
                    { "shopId", shopId },
                    { "customerName", custName },
                    { "instructions", instr },
                    { "status", "incoming" },
                    { "files", fileRecords },
                    { "totalSizeBytes", totalBytes },
                    { "createdAt", nowIso },
                    { "expiresAt", expIso }
                });

                AddAudit(state, shopId, "customer", "DIRECT_TUNNEL_UPLOAD",
                    string.Format(CultureInfo.InvariantCulture, "Received {0} ({1} file(s), {2:F1} KB) -> Local SSD",
                        receiptId, fileRecords.Count, totalBytes / 1024.0));
                SaveStateUnsafe(state);
            }

            Log(string.Format(CultureInfo.InvariantCulture, "⚡ Received {0} ({1} file(s), {2:F1} KB)", receiptId, files.Count, totalBytes / 1024.0));
            FireSubmissionsChanged();

            WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "receiptId", receiptId },
                { "expiresAt", expIso },
                { "storageMode", "local-pc" },
                { "fileCount", files.Count }
            });
        }

        private void HandleFileDownload(HttpListenerRequest req, HttpListenerResponse res)
        {
            string fileId = req.QueryString["id"];
            bool download = req.QueryString["download"] == "1";
            if (string.IsNullOrEmpty(fileId))
            {
                WriteJson(res, 400, new Dictionary<string, object> { { "error", "Missing file id" } });
                return;
            }

            Dictionary<string, object> found = null;
            lock (_stateLock)
            {
                var state = LoadStateUnsafe();
                var subs = state["submissions"] as System.Collections.IList;
                if (subs != null)
                {
                    foreach (Dictionary<string, object> s in subs)
                    {
                        var files = s["files"] as System.Collections.IList;
                        if (files == null) continue;
                        foreach (Dictionary<string, object> f in files)
                        {
                            if (GetStr(f, "id") == fileId) { found = f; break; }
                        }
                        if (found != null) break;
                    }
                }
            }

            string localPath = found != null ? GetStr(found, "localPath") : null;
            if (found == null || string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
            {
                WriteJson(res, 404, new Dictionary<string, object> { { "error", "File not found on local disk" } });
                return;
            }

            byte[] bytes = File.ReadAllBytes(localPath);
            res.StatusCode = 200;
            res.ContentType = GetStr(found, "mimeType") ?? "application/octet-stream";
            res.AddHeader("Content-Disposition", (download ? "attachment" : "inline") + "; filename=\"" + Uri.EscapeDataString(GetStr(found, "originalName") ?? "file") + "\"");
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }

        // ====================================================================
        // State Persistence & Helpers
        // ====================================================================

        public Dictionary<string, object> LoadState()
        {
            lock (_stateLock) return LoadStateUnsafe();
        }

        private Dictionary<string, object> LoadStateUnsafe()
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
            Dictionary<string, object> state;
            if (!File.Exists(_stateFile))
            {
                state = CreateDefaultState();
                SaveStateUnsafe(state);
                return state;
            }
            try
            {
                state = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(_stateFile, Encoding.UTF8));
                if (state == null) state = CreateDefaultState();
            }
            catch
            {
                state = CreateDefaultState();
            }

            string nowIso = DateTime.UtcNow.ToString("o");
            bool changed = false;
            var subs = state.ContainsKey("submissions") ? state["submissions"] as System.Collections.IList : null;
            if (subs != null)
            {
                foreach (Dictionary<string, object> sub in subs)
                {
                    string st = GetStr(sub, "status");
                    string exp = GetStr(sub, "expiresAt");
                    if (st != "expired" && !string.IsNullOrEmpty(exp) && string.CompareOrdinal(exp, nowIso) <= 0)
                    {
                        sub["status"] = "expired";
                        string subDir = Path.Combine(_inboxDir, GetStr(sub, "id"));
                        if (Directory.Exists(subDir)) try { Directory.Delete(subDir, true); } catch { }
                        AddAudit(state, GetStr(sub, "shopId"), "system", "RETENTION_PURGED", "Purged expired submission " + GetStr(sub, "id"));
                        changed = true;
                    }
                }
            }
            if (changed) SaveStateUnsafe(state);
            return state;
        }

        private void SaveStateUnsafe(Dictionary<string, object> state)
        {
            var events = state.ContainsKey("auditEvents") ? state["auditEvents"] as System.Collections.IList : null;
            if (events != null) { while (events.Count > 200) events.RemoveAt(200); }
            var ser = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
            File.WriteAllText(_stateFile, ser.Serialize(state), Encoding.UTF8);
        }

        private Dictionary<string, object> CreateDefaultState()
        {
            string nowIso = DateTime.UtcNow.ToString("o");
            string exp30 = DateTime.UtcNow.AddDays(30).ToString("o");
            return new Dictionary<string, object>
            {
                { "shops", new List<object> {
                    new Dictionary<string, object> {
                        { "id", "shop_quickprint_01" }, { "slug", "quickprint" },
                        { "name", "QuickPrint Xerox & Stationery" }, { "tagline", "Scan. Drop. Done." },
                        { "ownerName", "Ramesh Kumar" }, { "ownerEmail", "ramesh@quickprint.local" },
                        { "ownerPhone", "+91 98765 43210" }, { "planId", "plan_pro_monthly" },
                        { "subscriptionStatus", "active" }, { "pairingCode", "SD-PAIR-7890" },
                        { "expiryDate", exp30 }, { "createdAt", nowIso }
                    }
                }},
                { "devices", new List<object> {
                    new Dictionary<string, object> {
                        { "id", "dev_local_pc_01" }, { "shopId", "shop_quickprint_01" },
                        { "deviceName", Environment.MachineName },
                        { "windowsVersion", Environment.OSVersion.VersionString },
                        { "appVersion", "3.1.0-exe" }, { "status", "active" },
                        { "lastHeartbeatAt", nowIso }, { "lastIp", "127.0.0.1 (Local SSD)" }
                    }
                }},
                { "submissions", new List<object>() },
                { "derivedFiles", new List<object>() },
                { "printerProfiles", new List<object> {
                    new Dictionary<string, object> { { "id", "prof_1" }, { "name", "A4 Black & White" } },
                    new Dictionary<string, object> { { "id", "prof_2" }, { "name", "A4 Full Color" } },
                    new Dictionary<string, object> { { "id", "prof_3" }, { "name", "Photo 4x6 Glossy" } }
                }},
                { "auditEvents", new List<object> {
                    new Dictionary<string, object> {
                        { "id", "evt_init" }, { "shopId", "shop_quickprint_01" },
                        { "actorType", "system" }, { "eventType", "LOCAL_EXE_STARTED" },
                        { "description", "ScanDrop.exe local PC SSD storage initialized" }, { "createdAt", nowIso }
                    }
                }}
            };
        }

        private static List<object> GetPlans()
        {
            return new List<object>
            {
                new Dictionary<string, object> { { "id", "plan_free_trial" }, { "name", "Free / Trial" }, { "tier", "free" }, { "priceText", "$0 / 30 days" }, { "maxDevices", 1 }, { "maxMonthlySubmissions", 50 }, { "batchConversion", false }, { "pdfAdvanced", false }, { "printerProfiles", false } },
                new Dictionary<string, object> { { "id", "plan_pro_monthly" }, { "name", "Pro Monthly" }, { "tier", "pro" }, { "priceText", "$29 / month" }, { "maxDevices", 1 }, { "maxMonthlySubmissions", 1000 }, { "batchConversion", true }, { "pdfAdvanced", true }, { "printerProfiles", true } },
                new Dictionary<string, object> { { "id", "plan_business_monthly" }, { "name", "Business Multi-PC" }, { "tier", "business" }, { "priceText", "$69 / month" }, { "maxDevices", 5 }, { "maxMonthlySubmissions", 10000 }, { "batchConversion", true }, { "pdfAdvanced", true }, { "printerProfiles", true } }
            };
        }

        private static void AddAudit(Dictionary<string, object> state, string shopId, string actor, string evtType, string desc)
        {
            var events = state["auditEvents"] as System.Collections.IList;
            if (events == null) { events = new List<object>(); state["auditEvents"] = events; }
            events.Insert(0, new Dictionary<string, object>
            {
                { "id", "evt_" + DateTime.UtcNow.Ticks },
                { "shopId", shopId },
                { "actorType", actor },
                { "eventType", evtType },
                { "description", desc },
                { "createdAt", DateTime.UtcNow.ToString("o") }
            });
        }

        private sealed class UploadedFilePart
        {
            public string FileName;
            public string ContentType;
            public byte[] Bytes;
        }

        private static void ParseMultipart(byte[] buffer, string contentType, out Dictionary<string, string> fields, out List<UploadedFilePart> files)
        {
            fields = new Dictionary<string, string>();
            files = new List<UploadedFilePart>();
            if (string.IsNullOrEmpty(contentType)) throw new InvalidOperationException("Missing Content-Type");
            var m = Regex.Match(contentType, @"boundary=(?:""([^""]+)""|([^;\s]+))", RegexOptions.IgnoreCase);
            if (!m.Success) throw new InvalidOperationException("Missing multipart boundary");
            byte[] boundary = Encoding.ASCII.GetBytes("--" + (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value));
            byte[] headerSep = new byte[] { 13, 10, 13, 10 };

            int pos = IndexOfBytes(buffer, boundary, 0);
            while (pos != -1)
            {
                pos += boundary.Length;
                if (pos + 1 < buffer.Length && buffer[pos] == 45 && buffer[pos + 1] == 45) break;
                if (pos + 1 < buffer.Length && buffer[pos] == 13 && buffer[pos + 1] == 10) pos += 2;

                int next = IndexOfBytes(buffer, boundary, pos);
                if (next == -1) break;

                int partEnd = next;
                if (partEnd >= 2 && buffer[partEnd - 2] == 13 && buffer[partEnd - 1] == 10) partEnd -= 2;

                int hdrEnd = IndexOfBytes(buffer, headerSep, pos);
                if (hdrEnd == -1 || hdrEnd > partEnd) { pos = next; continue; }

                string hdr = Encoding.UTF8.GetString(buffer, pos, hdrEnd - pos);
                int bodyStart = hdrEnd + 4;
                int bodyLen = partEnd - bodyStart;

                var nameMatch = Regex.Match(hdr, @"name=""([^""]+)""", RegexOptions.IgnoreCase);
                var fileMatch = Regex.Match(hdr, @"filename=""([^""]*)""", RegexOptions.IgnoreCase);
                var ctMatch = Regex.Match(hdr, @"Content-Type:\s*([^\r\n]+)", RegexOptions.IgnoreCase);

                if (nameMatch.Success)
                {
                    if (fileMatch.Success && !string.IsNullOrEmpty(fileMatch.Groups[1].Value))
                    {
                        var bytes = new byte[bodyLen];
                        Buffer.BlockCopy(buffer, bodyStart, bytes, 0, bodyLen);
                        files.Add(new UploadedFilePart
                        {
                            FileName = Path.GetFileName(fileMatch.Groups[1].Value),
                            ContentType = ctMatch.Success ? ctMatch.Groups[1].Value.Trim() : "application/octet-stream",
                            Bytes = bytes
                        });
                    }
                    else
                    {
                        fields[nameMatch.Groups[1].Value] = Encoding.UTF8.GetString(buffer, bodyStart, bodyLen);
                    }
                }
                pos = next;
            }
        }

        private static int IndexOfBytes(byte[] haystack, byte[] needle, int start)
        {
            int max = haystack.Length - needle.Length;
            for (int i = start; i <= max; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        private static string SanitizeFileName(string name)
        {
            string clean = Path.GetFileName(name ?? "file.bin");
            foreach (char c in Path.GetInvalidFileNameChars()) clean = clean.Replace(c, '_');
            return clean;
        }

        private static Dictionary<string, object> ReadJsonBody(HttpListenerRequest req)
        {
            using (var sr = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                string raw = sr.ReadToEnd();
                if (string.IsNullOrWhiteSpace(raw)) return new Dictionary<string, object>();
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
            }
        }

        private static string GetStr(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : null;
        }

        private static void AddCors(HttpListenerResponse res)
        {
            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.AddHeader("Access-Control-Allow-Headers", "Content-Type, X-Requested-With");
        }

        private static void WriteJson(HttpListenerResponse res, int status, object obj)
        {
            byte[] buf = Encoding.UTF8.GetBytes(new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 }.Serialize(obj));
            res.StatusCode = status;
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = buf.Length;
            res.OutputStream.Write(buf, 0, buf.Length);
            res.Close();
        }

        private void Log(string msg) { var h = OnLog; if (h != null) try { h(msg); } catch { } }
        private void FireSubmissionsChanged() { var h = OnSubmissionsChanged; if (h != null) try { h(); } catch { } }

        public void Dispose()
        {
            _disposed = true;
            try { if (_listener != null && _listener.IsListening) _listener.Stop(); } catch { }
            try { if (_listener != null) _listener.Close(); } catch { }
        }
    }
}

