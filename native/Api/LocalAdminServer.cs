using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using ScanDrop.Native.Conversion;
using ScanDrop.Native.Core;
using ScanDrop.Native.Storage;

namespace ScanDrop.Native.Api
{
    /// <summary>
    /// LOCAL-ONLY SHOPKEEPER WORKSPACE & ADMIN SERVER (PRD Sec 3.1, 5, 11).
    /// Bound strictly to 127.0.0.1:3838 and NEVER tunneled publicly.
    /// Rejects any request carrying reverse-proxy forwarding headers as defense-in-depth.
    /// </summary>
    public sealed class LocalAdminServer : IDisposable
    {
        private readonly string _appRootDir;
        private readonly int _preferredPort;
        private readonly SqliteDatabase _db;
        private readonly StorageManager _storage;
        private readonly ConversionEngine _converter;
        private readonly AppLogger _logger;
        private TunnelManager _tunnel;
        private PublicUploadServer _publicReceiver;

        private HttpListener _listener;
        private Thread _listenThread;
        private int _actualPort;
        private bool _disposed;

        public event Action OnStateChanged;
        public event Action OnShowWindowRequested;

        public int Port { get { return _actualPort; } }
        public string InboxDir { get { return _storage.Paths.ReceivedDir; } }
        public string DataRootDir { get { return _storage.Paths.DataRootDir; } }

        public LocalAdminServer(
            string appRootDir,
            int preferredPort,
            SqliteDatabase db,
            StorageManager storage,
            ConversionEngine converter,
            AppLogger logger)
        {
            _appRootDir = appRootDir;
            _preferredPort = preferredPort;
            _db = db;
            _storage = storage;
            _converter = converter;
            _logger = logger;
        }

        public void AttachServices(TunnelManager tunnel, PublicUploadServer publicReceiver)
        {
            _tunnel = tunnel;
            _publicReceiver = publicReceiver;
        }

        public void Start()
        {
            for (int candidate = _preferredPort; candidate < _preferredPort + 10; candidate++)
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
                    if (candidate == _preferredPort + 9) throw;
                }
            }

            _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "LocalAdminServer" };
            _listenThread.Start();
            _logger.Info("AdminAPI", "Local Shopkeeper Workspace listening on http://127.0.0.1:" + _actualPort + "/workspace/ (loopback only)");
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
                catch (Exception ex) { _logger.Warn("AdminAPI", "Accept error: " + ex.Message); }
            }
        }

        private static bool IsStrictlyLocalRequest(HttpListenerRequest req)
        {
            // Defense-in-depth: block any request forwarded through Cloudflare/SSH/reverse proxy
            if (!string.IsNullOrEmpty(req.Headers["CF-Connecting-IP"]) ||
                !string.IsNullOrEmpty(req.Headers["X-Forwarded-For"]) ||
                !string.IsNullOrEmpty(req.Headers["X-Forwarded-Host"]))
            {
                return false;
            }
            if (req.RemoteEndPoint != null && req.RemoteEndPoint.Address != null)
            {
                return IPAddress.IsLoopback(req.RemoteEndPoint.Address);
            }
            return true;
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            HttpHelpers.AddLocalCors(res);

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            if (!IsStrictlyLocalRequest(req))
            {
                HttpHelpers.WriteJson(res, 403, new Dictionary<string, object>
                {
                    { "success", false },
                    { "error", "Administrative workspace API is strictly restricted to localhost." }
                });
                return;
            }

            string path = (req.Url.AbsolutePath ?? "/").TrimEnd('/');
            if (string.IsNullOrEmpty(path)) path = "/";

            try
            {
                if (path == "/" && req.HttpMethod == "GET")
                {
                    res.StatusCode = 302;
                    res.RedirectLocation = "/workspace/";
                    res.Close();
                    return;
                }

                if (req.HttpMethod == "GET" &&
                    (path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)))
                {
                    if (HttpHelpers.TryServeBrandAsset(res, _appRootDir, path)) return;
                }

                if (path == "/workspace" && req.HttpMethod == "GET")
                {
                    ServeWorkspaceHtml(res);
                    return;
                }

                if (path == "/api/health" && req.HttpMethod == "GET")
                {
                    var settings = _db.LoadSettings(_storage.Paths.DataRootDir);
                    var snap = _tunnel != null ? _tunnel.GetStatusSnapshot() : null;
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "ok", true },
                        { "appVersion", AutoUpdater.CurrentVersion },
                        { "storageMode", "local-pc" },
                        { "dataRoot", _storage.Paths.DataRootDir },
                        { "inboxDir", _storage.Paths.ReceivedDir },
                        { "receiverRunning", _publicReceiver != null && _publicReceiver.IsRunning },
                        { "receiverPort", _publicReceiver != null ? _publicReceiver.Port : 0 },
                        { "acceptingUploads", !settings.PauseReceiving },
                        { "libreOfficeInstalled", ConversionEngine.IsLibreOfficeInstalled() },
                        { "tunnelUrl", snap != null ? snap.PublicUrl : null },
                        { "tunnelOnline", snap != null && snap.IsOnline },
                        { "tunnelState", snap != null ? snap.StateName : "STOPPED" },
                        { "tunnelMode", snap != null ? snap.Mode : "development_quick" },
                        { "tunnelProvider", snap != null ? snap.ModeLabel : "Cloudflare Tunnel" },
                        { "tunnelHealth", snap != null ? snap.DisplayStatus : "Stopped" },
                        { "tunnelPid", snap != null ? (object)snap.ProcessId : null },
                        { "tunnelLastOkUtc", snap != null && snap.LastSuccessfulHealthCheckUtc.HasValue ? snap.LastSuccessfulHealthCheckUtc.Value.ToString("o") : null },
                        { "tunnelLastFailure", snap != null ? snap.LastFailureReason : null },
                        { "tunnelRetryCount", snap != null ? snap.RetryCount : 0 },
                        { "tunnelNextRetryUtc", snap != null && snap.NextRetryUtc.HasValue ? snap.NextRetryUtc.Value.ToString("o") : null }
                    });
                    return;
                }

                if (path == "/api/window/show" && req.HttpMethod == "POST")
                {
                    var h = OnShowWindowRequested;
                    if (h != null) try { h(); } catch { }
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true } });
                    return;
                }

                if (path == "/api/tunnel/status" && req.HttpMethod == "GET")
                {
                    var snap = _tunnel != null ? _tunnel.GetStatusSnapshot() : new TunnelStatusSnapshot { State = TunnelState.STOPPED, StateName = "STOPPED", DisplayStatus = "Stopped", Mode = "development_quick", ModeLabel = "Cloudflare Quick Tunnel (Development)" };
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "provider", "cloudflare" },
                        { "state", snap.StateName },
                        { "displayStatus", snap.DisplayStatus },
                        { "mode", snap.Mode },
                        { "modeLabel", snap.ModeLabel },
                        { "processId", snap.ProcessId },
                        { "publicUrl", snap.PublicUrl },
                        { "isOnline", snap.IsOnline },
                        { "lastSuccessfulHealthCheckUtc", snap.LastSuccessfulHealthCheckUtc.HasValue ? snap.LastSuccessfulHealthCheckUtc.Value.ToString("o") : null },
                        { "lastFailureReason", snap.LastFailureReason },
                        { "retryCount", snap.RetryCount },
                        { "nextRetryUtc", snap.NextRetryUtc.HasValue ? snap.NextRetryUtc.Value.ToString("o") : null },
                        { "generation", snap.Generation },
                        { "activeUploadsCount", snap.ActiveUploadsCount }
                    });
                    return;
                }

                if (path == "/api/state" && req.HttpMethod == "GET")
                {
                    WriteFullWorkspaceState(res);
                    return;
                }

                if (path == "/api/file" && req.HttpMethod == "GET")
                {
                    HandleStreamFile(req, res);
                    return;
                }

                if (path == "/api/converted" && req.HttpMethod == "GET")
                {
                    HandleStreamConverted(req, res);
                    return;
                }

                if (path == "/api/submission/status" && req.HttpMethod == "POST")
                {
                    var body = HttpHelpers.ReadJsonBody(req);
                    string subId = HttpHelpers.GetStr(body, "submissionId");
                    string status = (HttpHelpers.GetStr(body, "status") ?? "received").Trim().ToLowerInvariant();
                    if (status == "incoming" || status == "unarchived") status = "received";
                    if (status == "completed") status = "printed";
                    var sub = _db.GetSubmissionByReceiptOrId(subId);
                    if (sub == null)
                    {
                        HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "Submission not found." } });
                        return;
                    }
                    bool wasArchived = string.Equals(sub.Status, "archived", StringComparison.OrdinalIgnoreCase);
                    bool isUnarchiving = wasArchived && !string.Equals(status, "archived", StringComparison.OrdinalIgnoreCase);
                    _db.UpdateSubmissionStatus(sub.SubmissionId, status);
                    if (isUnarchiving)
                    {
                        var settings = _db.LoadSettings(_storage.Paths.DataRootDir);
                        DateTime parsedExp;
                        DateTime freshExp = DateTime.UtcNow.AddHours(Math.Max(1, settings.RetentionHours));
                        if (!DateTime.TryParse(sub.ExpiresAt, null, DateTimeStyles.AdjustToUniversal, out parsedExp) || parsedExp < freshExp)
                        {
                            _db.ExtendSubmissionExpiry(sub.SubmissionId, freshExp.ToString("o", CultureInfo.InvariantCulture));
                        }
                        _db.AddActivity("SUBMISSION_STATUS", "shopkeeper", sub.SubmissionId, "Unarchived " + sub.ReceiptCode + " back to " + status);
                    }
                    else
                    {
                        _db.AddActivity("SUBMISSION_STATUS", "shopkeeper", sub.SubmissionId, "Marked " + sub.ReceiptCode + " as " + status);
                    }
                    FireStateChanged();
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true }, { "status", status }, { "unarchived", isUnarchiving } });
                    return;
                }

                if (path == "/api/submission/delete" && req.HttpMethod == "POST")
                {
                    var body = HttpHelpers.ReadJsonBody(req);
                    var ids = new List<string>();
                    string single = HttpHelpers.GetStr(body, "submissionId");
                    if (!string.IsNullOrEmpty(single)) ids.Add(single);
                    object rawList;
                    if (body.TryGetValue("submissionIds", out rawList) && rawList is System.Collections.IEnumerable)
                    {
                        foreach (var item in (System.Collections.IEnumerable)rawList)
                        {
                            string sId = Convert.ToString(item);
                            if (!string.IsNullOrEmpty(sId)) ids.Add(sId);
                        }
                    }
                    int deleted = _storage.DeleteSubmissions(ids);
                    FireStateChanged();
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true }, { "deletedCount", deleted } });
                    return;
                }

                // Native Windows File Operations (PRD Sec 5.2)
                if (path == "/api/file/open" && req.HttpMethod == "POST")
                {
                    var file = ResolveFileFromBody(req);
                    if (file == null || !File.Exists(file.StoragePath))
                    {
                        HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "File not found." } });
                        return;
                    }
                    Process.Start(new ProcessStartInfo(file.StoragePath) { UseShellExecute = true });
                    _db.AddActivity("FILE_OPENED", "shopkeeper", file.SubmissionId, "Opened " + file.OriginalFilename + " in default Windows app.");
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true } });
                    return;
                }

                if (path == "/api/file/open-with" && req.HttpMethod == "POST")
                {
                    var file = ResolveFileFromBody(req);
                    if (file == null || !File.Exists(file.StoragePath))
                    {
                        HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "File not found." } });
                        return;
                    }
                    Process.Start(new ProcessStartInfo("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + file.StoragePath) { UseShellExecute = false });
                    _db.AddActivity("FILE_OPEN_WITH", "shopkeeper", file.SubmissionId, "Opened 'Open With' dialog for " + file.OriginalFilename);
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true } });
                    return;
                }

                if (path == "/api/file/reveal" && req.HttpMethod == "POST")
                {
                    var file = ResolveFileFromBody(req);
                    if (file == null || !File.Exists(file.StoragePath))
                    {
                        HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "File not found." } });
                        return;
                    }
                    Process.Start("explorer.exe", "/select,\"" + file.StoragePath + "\"");
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true } });
                    return;
                }

                if (path == "/api/file/rename" && req.HttpMethod == "POST")
                {
                    var body = HttpHelpers.ReadJsonBody(req);
                    string fileId = HttpHelpers.GetStr(body, "fileId");
                    string newName = StorageManager.SanitizeDisplayName(HttpHelpers.GetStr(body, "newName"));
                    var file = _db.GetFileById(fileId);
                    if (file == null)
                    {
                        HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "File not found." } });
                        return;
                    }
                    if (!string.IsNullOrEmpty(file.FileExtension) && !newName.EndsWith(file.FileExtension, StringComparison.OrdinalIgnoreCase))
                        newName += file.FileExtension;
                    _db.RenameFile(file.FileId, newName);
                    _db.AddActivity("FILE_RENAMED", "shopkeeper", file.SubmissionId, "Renamed '" + file.OriginalFilename + "' -> '" + newName + "'");
                    FireStateChanged();
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true }, { "newName", newName } });
                    return;
                }

                if (path == "/api/file/export" && req.HttpMethod == "POST")
                {
                    var body = HttpHelpers.ReadJsonBody(req);
                    string fileId = HttpHelpers.GetStr(body, "fileId");
                    string destDir = HttpHelpers.GetStr(body, "destinationDir");
                    var file = _db.GetFileById(fileId);
                    if (file == null)
                    {
                        HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "File not found." } });
                        return;
                    }
                    string exportedPath = _storage.ExportFilePermanently(file, destDir);
                    FireStateChanged();
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "exportedPath", exportedPath },
                        { "message", "Saved permanent copy to " + exportedPath }
                    });
                    return;
                }

                if (path == "/api/file/convert-pdf" && req.HttpMethod == "POST")
                {
                    HandleConvertToPdf(req, res);
                    return;
                }

                if (path == "/api/printers" && req.HttpMethod == "GET")
                {
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "printers", GetInstalledPrinters() }
                    });
                    return;
                }

                if (path == "/api/file/print-native" && req.HttpMethod == "POST")
                {
                    HandleNativePrint(req, res);
                    return;
                }

                if (path == "/api/settings" && req.HttpMethod == "GET")
                {
                    var settings = _db.LoadSettings(_storage.Paths.DataRootDir);
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "settings", SettingsToDict(settings) },
                        { "libreOfficeInstalled", ConversionEngine.IsLibreOfficeInstalled() }
                    });
                    return;
                }

                if (path == "/api/settings" && req.HttpMethod == "POST")
                {
                    HandleSaveSettings(req, res);
                    return;
                }

                if (path == "/api/receiving/pause" && req.HttpMethod == "POST")
                {
                    var body = HttpHelpers.ReadJsonBody(req);
                    var settings = _db.LoadSettings(_storage.Paths.DataRootDir);
                    settings.PauseReceiving = HttpHelpers.GetBool(body, "paused", !settings.PauseReceiving);
                    _db.SaveSettings(settings);
                    _db.AddActivity("RECEIVING_TOGGLED", "shopkeeper", null, settings.PauseReceiving ? "Paused incoming customer uploads." : "Resumed incoming customer uploads.");
                    FireStateChanged();
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "paused", settings.PauseReceiving }
                    });
                    return;
                }

                if (path == "/api/storage" && req.HttpMethod == "GET")
                {
                    var st = _storage.GetStorageSummary();
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "storageMode", "local-pc" },
                        { "storage", StorageSummaryToDict(st) }
                    });
                    return;
                }

                if (path == "/api/storage/clear" && req.HttpMethod == "POST")
                {
                    var body = HttpHelpers.ReadJsonBody(req);
                    string scope = HttpHelpers.GetStr(body, "scope") ?? "temp";
                    var cleared = _storage.ClearStorage(scope);
                    FireStateChanged();
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "storageMode", "local-pc" },
                        { "scope", cleared.Scope },
                        { "freedBytes", cleared.FreedBytes },
                        { "deletedSubmissions", cleared.DeletedSubmissions },
                        { "removedTempItems", cleared.RemovedTempItems },
                        { "storage", StorageSummaryToDict(cleared.Remaining) }
                    });
                    return;
                }

                if (path == "/api/tunnel/restart" && req.HttpMethod == "POST")
                {
                    if (_tunnel != null) _tunnel.Restart();
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true }, { "message", "Restarting tunnel..." } });
                    return;
                }

                if (path == "/api/tunnel/config" && req.HttpMethod == "GET")
                {
                    string tok = null, cust = null, mode = "auto";
                    if (_tunnel != null) _tunnel.ReadConfig(out tok, out cust, out mode);
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "hasToken", !string.IsNullOrWhiteSpace(tok) },
                        { "customTunnelUrl", cust ?? "" },
                        { "tunnelProvider", mode ?? "auto" }
                    });
                    return;
                }

                if (path == "/api/tunnel/config" && req.HttpMethod == "POST")
                {
                    var body = HttpHelpers.ReadJsonBody(req);
                    string tok = HttpHelpers.GetStr(body, "cloudflareTunnelToken") ?? "";
                    string cust = HttpHelpers.GetStr(body, "customTunnelUrl") ?? "";
                    string mode = HttpHelpers.GetStr(body, "tunnelProvider") ?? "auto";
                    if (_tunnel != null) _tunnel.SaveConfig(tok, cust, mode);
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true }, { "message", "Tunnel config saved & reconnecting..." } });
                    return;
                }

                if ((path == "/api/version" && req.HttpMethod == "GET") || (path == "/api/update/check" && req.HttpMethod == "POST"))
                {
                    var upd = AutoUpdater.CheckForUpdate();
                    bool uiUpdated = AutoUpdater.SyncWorkspaceUi(_appRootDir);
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
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
                        HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                        {
                            { "success", true },
                            { "updating", false },
                            { "message", "Already running the latest version (" + upd.CurrentVersion + ")." }
                        });
                        return;
                    }
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
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
                            try { if (_publicReceiver != null) _publicReceiver.Dispose(); } catch { }
                            try { Dispose(); } catch { }
                        }, out err);
                        if (!string.IsNullOrEmpty(err)) _logger.Error("Updater", "Auto-update error: " + err);
                    });
                    return;
                }

                if (path == "/api/pair" && req.HttpMethod == "POST")
                {
                    var body = HttpHelpers.ReadJsonBody(req);
                    var settings = _db.LoadSettings(_storage.Paths.DataRootDir);
                    string code = (HttpHelpers.GetStr(body, "pairingCode") ?? "").Trim().ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", "Pairing code is required." } });
                        return;
                    }

                    string fp = _db.GetOrCreateMachineFingerprint();
                    ShopPairResult pairRes;
                    string pairErr;
                    if (!ShopPairingClient.PairWithCloud(settings.PublicBaseUrl, code, fp, AutoUpdater.CurrentVersion, out pairRes, out pairErr))
                    {
                        HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", pairErr ?? "Pairing failed." } });
                        return;
                    }

                    _db.ReplacePrimaryShop(pairRes.ShopId, pairRes.ShopName, pairRes.ShopSlug);
                    settings.ShopId = pairRes.ShopId;
                    settings.ShopName = pairRes.ShopName;
                    settings.ShopSlug = pairRes.ShopSlug;
                    settings.PairingCode = pairRes.PairingCode;
                    settings.DeviceId = pairRes.DeviceId;
                    settings.DeviceSecret = pairRes.DeviceSecret;
                    settings.IsPaired = true;
                    settings.FirstRunCompleted = true;
                    _db.SaveSettings(settings);
                    _db.AddActivity("SHOP_PAIRED", "shopkeeper", null, "Paired PC to /shop/" + settings.ShopSlug + " (" + settings.ShopName + ")");

                    if (_tunnel != null)
                    {
                        _tunnel.UpdateIdentity(settings.ShopId, settings.PairingCode, settings.DeviceId, settings.DeviceSecret);
                    }
                    FireStateChanged();

                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "deviceId", settings.DeviceId },
                        { "shop", new Dictionary<string, object>
                            {
                                { "id", settings.ShopId },
                                { "name", settings.ShopName },
                                { "slug", settings.ShopSlug },
                                { "pairingCode", settings.PairingCode },
                                { "publicUrl", settings.PublicBaseUrl.TrimEnd('/') + "/shop/" + settings.ShopSlug }
                            }
                        }
                    });
                    return;
                }

                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "error", "Not Found" } });
            }
            catch (Exception ex)
            {
                _logger.Warn("AdminAPI", "Request error (" + path + "): " + ex.Message);
                try { HttpHelpers.WriteJson(res, 500, new Dictionary<string, object> { { "success", false }, { "error", ex.Message } }); } catch { }
            }
        }

        private void WriteFullWorkspaceState(HttpListenerResponse res)
        {
            var settings = _db.LoadSettings(_storage.Paths.DataRootDir);
            var subs = _db.GetSubmissions(false);
            var acts = _db.GetRecentActivity(150);
            var storage = _storage.GetStorageSummary();
            string nowIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            int todayCount = 0;
            string todayPrefix = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var subDtos = new List<object>();
            foreach (var s in subs)
            {
                if (s.CreatedAt != null && s.CreatedAt.StartsWith(todayPrefix, StringComparison.Ordinal)) todayCount++;
                var fDtos = new List<object>();
                foreach (var f in s.Files)
                {
                    fDtos.Add(new Dictionary<string, object>
                    {
                        { "id", f.FileId },
                        { "submissionId", s.ReceiptCode },
                        { "originalName", f.OriginalFilename },
                        { "internalName", f.InternalFilename },
                        { "mimeType", f.ContentType },
                        { "fileExtension", f.FileExtension },
                        { "sizeBytes", f.FileSize },
                        { "sha256Hash", f.Checksum },
                        { "conversionStatus", f.ConversionStatus },
                        { "convertedUrl", !string.IsNullOrEmpty(f.ConvertedPath) ? ("/api/converted?name=" + Uri.EscapeDataString(Path.GetFileName(f.ConvertedPath))) : null },
                        { "receivedAt", f.ReceivedAt }
                    });
                }

                subDtos.Add(new Dictionary<string, object>
                {
                    { "id", s.ReceiptCode },
                    { "submissionId", s.SubmissionId },
                    { "receiptCode", s.ReceiptCode },
                    { "shopId", s.ShopId },
                    { "customerName", s.CustomerName },
                    { "instructions", s.CustomerNote },
                    { "status", s.Status },
                    { "files", fDtos },
                    { "totalSizeBytes", s.TotalSize },
                    { "createdAt", s.CompletedAt ?? s.CreatedAt },
                    { "expiresAt", s.ExpiresAt }
                });
            }

            var actDtos = new List<object>();
            foreach (var a in acts)
            {
                actDtos.Add(new Dictionary<string, object>
                {
                    { "id", a.ActivityId },
                    { "eventType", a.ActivityType },
                    { "actorType", a.Actor },
                    { "submissionId", a.RelatedSubmissionId },
                    { "description", a.Details },
                    { "createdAt", a.Timestamp }
                });
            }

            var convertedList = new List<object>();
            if (Directory.Exists(_storage.Paths.ConvertedDir))
            {
                foreach (var f in Directory.GetFiles(_storage.Paths.ConvertedDir, "*.pdf"))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        convertedList.Add(new Dictionary<string, object>
                        {
                            { "name", fi.Name },
                            { "sizeBytes", fi.Length },
                            { "createdAt", fi.LastWriteTimeUtc.ToString("o") },
                            { "url", "/api/converted?name=" + Uri.EscapeDataString(fi.Name) }
                        });
                    }
                    catch { }
                }
            }

            var snap = _tunnel != null ? _tunnel.GetStatusSnapshot() : null;
            var shopObj = new Dictionary<string, object>
            {
                { "id", settings.ShopId },
                { "slug", settings.ShopSlug },
                { "name", settings.ShopName },
                { "tagline", "Scan. Send. Done." },
                { "planId", "plan_local_first" },
                { "subscriptionStatus", settings.PauseReceiving ? "paused" : "active" },
                { "pairingCode", settings.PairingCode },
                { "expiryDate", DateTime.UtcNow.AddYears(10).ToString("o") },
                { "tunnelUrl", snap != null ? snap.PublicUrl : null },
                { "tunnelOnline", snap != null && snap.IsOnline }
            };

            var stateObj = new Dictionary<string, object>
            {
                { "shops", new List<object> { shopObj } },
                { "submissions", subDtos },
                { "auditEvents", actDtos },
                { "convertedFiles", convertedList },
                { "printerProfiles", GetInstalledPrinters() }
            };

            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "appVersion", AutoUpdater.CurrentVersion },
                { "storageMode", "local-pc" },
                { "receiverRunning", _publicReceiver != null && _publicReceiver.IsRunning },
                { "receiverPort", _publicReceiver != null ? _publicReceiver.Port : 0 },
                { "acceptingUploads", !settings.PauseReceiving },
                { "dailyCount", todayCount },
                { "libreOfficeInstalled", ConversionEngine.IsLibreOfficeInstalled() },
                { "tunnelUrl", snap != null ? snap.PublicUrl : null },
                { "tunnelOnline", snap != null && snap.IsOnline },
                { "tunnelState", snap != null ? snap.StateName : "STOPPED" },
                { "tunnelMode", snap != null ? snap.Mode : "development_quick" },
                { "tunnelProvider", snap != null ? snap.ModeLabel : "Cloudflare Tunnel" },
                { "tunnelHealth", snap != null ? snap.DisplayStatus : "Stopped" },
                { "tunnelPid", snap != null ? (object)snap.ProcessId : null },
                { "tunnelLastOkUtc", snap != null && snap.LastSuccessfulHealthCheckUtc.HasValue ? snap.LastSuccessfulHealthCheckUtc.Value.ToString("o") : null },
                { "tunnelLastFailure", snap != null ? snap.LastFailureReason : null },
                { "tunnelRetryCount", snap != null ? snap.RetryCount : 0 },
                { "tunnelNextRetryUtc", snap != null && snap.NextRetryUtc.HasValue ? snap.NextRetryUtc.Value.ToString("o") : null },
                { "settings", SettingsToDict(settings) },
                { "storage", StorageSummaryToDict(storage) },
                { "plans", new List<object> { new Dictionary<string, object> { { "id", "plan_local_first" }, { "name", "Local-First PC Edition" }, { "priceText", "Free / Local PC" }, { "maxDevices", 1 }, { "maxMonthlySubmissions", 999999 }, { "batchConversion", true }, { "pdfAdvanced", true } } } },
                { "state", stateObj },
                { "serverTimeUtc", nowIso }
            });
        }

        private void HandleStreamFile(HttpListenerRequest req, HttpListenerResponse res)
        {
            string fileId = req.QueryString["id"];
            bool download = req.QueryString["download"] == "1";
            var file = _db.GetFileById(fileId);
            if (file == null || string.IsNullOrEmpty(file.StoragePath) || !File.Exists(file.StoragePath))
            {
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "error", "File not found on local disk" } });
                return;
            }
            StreamDiskFile(res, file.StoragePath, file.ContentType, file.OriginalFilename, download);
        }

        private void HandleStreamConverted(HttpListenerRequest req, HttpListenerResponse res)
        {
            string name = StorageManager.SanitizeDisplayName(req.QueryString["name"]);
            bool download = req.QueryString["download"] == "1";
            string full = StoragePaths.EnsureSafeSubPath(_storage.Paths.ConvertedDir, Path.Combine(_storage.Paths.ConvertedDir, name));
            if (!File.Exists(full))
            {
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "error", "Converted file not found" } });
                return;
            }
            StreamDiskFile(res, full, "application/pdf", name, download);
        }

        private static void StreamDiskFile(HttpListenerResponse res, string path, string contentType, string displayName, bool download)
        {
            var fi = new FileInfo(path);
            res.StatusCode = 200;
            res.ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
            res.AddHeader("Content-Disposition", (download ? "attachment" : "inline") + "; filename=\"" + Uri.EscapeDataString(displayName ?? fi.Name) + "\"");
            res.ContentLength64 = fi.Length;
            using (var fs = File.OpenRead(path))
            {
                fs.CopyTo(res.OutputStream);
            }
            res.Close();
        }

        private void HandleConvertToPdf(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string singleId = HttpHelpers.GetStr(body, "fileId");
            string paper = HttpHelpers.GetStr(body, "paperSize") ?? "A4";
            string orient = HttpHelpers.GetStr(body, "orientation") ?? "portrait";

            var multiIds = new List<string>();
            object rawList;
            if (body.TryGetValue("fileIds", out rawList) && rawList is System.Collections.IEnumerable)
            {
                foreach (var item in (System.Collections.IEnumerable)rawList)
                {
                    string id = Convert.ToString(item);
                    if (!string.IsNullOrEmpty(id)) multiIds.Add(id);
                }
            }

            if (multiIds.Count > 1)
            {
                var imgPaths = new List<string>();
                foreach (var fid in multiIds)
                {
                    var fr = _db.GetFileById(fid);
                    if (fr != null && File.Exists(fr.StoragePath)) imgPaths.Add(fr.StoragePath);
                }
                string outName = "Combined_Images_" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture) + ".pdf";
                var cRes = _converter.ConvertImagesToPdf(imgPaths, outName, paper, orient);
                if (!cRes.Success)
                {
                    HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", cRes.ErrorMessage } });
                    return;
                }
                _db.AddActivity("CONVERTED_TO_PDF", "shopkeeper", null, "Combined " + imgPaths.Count + " image(s) -> " + cRes.OutputFilename);
                FireStateChanged();
                HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                {
                    { "success", true },
                    { "outputFilename", cRes.OutputFilename },
                    { "sizeBytes", cRes.OutputSizeBytes },
                    { "engine", cRes.EngineUsed },
                    { "url", "/api/converted?name=" + Uri.EscapeDataString(cRes.OutputFilename) }
                });
                return;
            }

            var file = _db.GetFileById(singleId ?? (multiIds.Count == 1 ? multiIds[0] : null));
            if (file == null)
            {
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "File not found." } });
                return;
            }

            _db.UpdateFileConversion(file.FileId, "converting", null);
            var conv = _converter.ConvertFileToPdf(file);
            if (!conv.Success)
            {
                _db.UpdateFileConversion(file.FileId, "failed", null);
                HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", conv.ErrorMessage } });
                return;
            }

            _db.UpdateFileConversion(file.FileId, "converted", conv.OutputPath);
            _db.AddActivity("CONVERTED_TO_PDF", "shopkeeper", file.SubmissionId, "Converted " + file.OriginalFilename + " -> " + conv.OutputFilename + " (" + conv.EngineUsed + ")");
            FireStateChanged();
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "outputFilename", conv.OutputFilename },
                { "sizeBytes", conv.OutputSizeBytes },
                { "engine", conv.EngineUsed },
                { "url", "/api/converted?name=" + Uri.EscapeDataString(conv.OutputFilename) }
            });
        }

        private void HandleNativePrint(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string fileId = HttpHelpers.GetStr(body, "fileId");
            string printerName = HttpHelpers.GetStr(body, "printerName");
            var file = _db.GetFileById(fileId);
            if (file == null || !File.Exists(file.StoragePath))
            {
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "File not found." } });
                return;
            }

            string ext = (file.FileExtension ?? "").ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp")
            {
                using (var img = Image.FromFile(file.StoragePath))
                using (var pd = new PrintDocument())
                {
                    if (!string.IsNullOrWhiteSpace(printerName)) pd.PrinterSettings.PrinterName = printerName;
                    pd.DocumentName = file.OriginalFilename;
                    pd.PrintPage += (s, ev) =>
                    {
                        Rectangle m = ev.MarginBounds;
                        double ratio = Math.Min((double)m.Width / img.Width, (double)m.Height / img.Height);
                        int w = (int)(img.Width * ratio);
                        int h = (int)(img.Height * ratio);
                        int x = m.Left + (m.Width - w) / 2;
                        int y = m.Top + (m.Height - h) / 2;
                        ev.Graphics.DrawImage(img, x, y, w, h);
                        ev.HasMorePages = false;
                    };
                    pd.Print();
                }
            }
            else
            {
                var psi = new ProcessStartInfo(file.StoragePath) { UseShellExecute = true, CreateNoWindow = true };
                if (!string.IsNullOrWhiteSpace(printerName))
                {
                    psi.Verb = "printto";
                    psi.Arguments = "\"" + printerName + "\"";
                }
                else
                {
                    psi.Verb = "print";
                }
                Process.Start(psi);
            }

            // Honest status per PRD Sec 5.3: record spooler submission, let staff mark physical completion
            _db.AddActivity("SENT_TO_PRINTER", "shopkeeper", file.SubmissionId,
                "Sent " + file.OriginalFilename + " to Windows print spooler" + (string.IsNullOrWhiteSpace(printerName) ? " (default printer)." : " (" + printerName + ")."));
            FireStateChanged();
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "message", "Sent '" + file.OriginalFilename + "' to Windows print spooler." }
            });
        }

        private void HandleSaveSettings(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            var settings = _db.LoadSettings(_storage.Paths.DataRootDir);

            string name = HttpHelpers.GetStr(body, "shopName");
            if (!string.IsNullOrWhiteSpace(name)) settings.ShopName = name.Trim();

            string slug = HttpHelpers.GetStr(body, "shopSlug");
            if (!string.IsNullOrWhiteSpace(slug)) settings.ShopSlug = slug.Trim().ToLowerInvariant();

            int retHrs = HttpHelpers.GetInt(body, "retentionHours", settings.RetentionHours);
            if (retHrs >= 1 && retHrs <= 720) settings.RetentionHours = retHrs;

            long maxFileMb = HttpHelpers.GetLong(body, "maxFileSizeMb", settings.MaxFileSizeBytes / (1024 * 1024));
            if (maxFileMb >= 5 && maxFileMb <= 500) settings.MaxFileSizeBytes = maxFileMb * 1024L * 1024L;

            long maxSubMb = HttpHelpers.GetLong(body, "maxSubmissionMb", settings.MaxSubmissionBytes / (1024 * 1024));
            if (maxSubMb >= maxFileMb && maxSubMb <= 1024) settings.MaxSubmissionBytes = maxSubMb * 1024L * 1024L;

            string allowed = HttpHelpers.GetStr(body, "allowedExtensions");
            if (!string.IsNullOrWhiteSpace(allowed)) settings.AllowedExtensions = allowed.Trim().ToLowerInvariant();

            string theme = HttpHelpers.GetStr(body, "theme");
            if (theme == "light" || theme == "dark") settings.Theme = theme;

            if (body.ContainsKey("pauseReceiving")) settings.PauseReceiving = HttpHelpers.GetBool(body, "pauseReceiving", settings.PauseReceiving);
            if (body.ContainsKey("firstRunCompleted")) settings.FirstRunCompleted = HttpHelpers.GetBool(body, "firstRunCompleted", settings.FirstRunCompleted);

            string newStorage = HttpHelpers.GetStr(body, "storageRoot");
            bool storageChanged = false;
            if (!string.IsNullOrWhiteSpace(newStorage) && !string.Equals(newStorage.Trim(), _storage.Paths.DataRootDir, StringComparison.OrdinalIgnoreCase))
            {
                if (!StoragePaths.TryVerifyWritableDir(newStorage.Trim()))
                {
                    HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", "Selected storage folder is not writable: " + newStorage } });
                    return;
                }
                StoragePaths.SaveBootstrapDataRoot(newStorage.Trim());
                storageChanged = true;
            }

            _db.SaveSettings(settings);
            _db.AddActivity("SETTINGS_UPDATED", "shopkeeper", null, "Updated shop configuration (" + settings.ShopName + ", retention " + settings.RetentionHours + "h, max file " + (settings.MaxFileSizeBytes / (1024 * 1024)) + " MB).");
            FireStateChanged();

            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "settings", SettingsToDict(settings) },
                { "restartRequiredForStorageMove", storageChanged }
            });
        }

        private FileRecord ResolveFileFromBody(HttpListenerRequest req)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string fileId = HttpHelpers.GetStr(body, "fileId");
            return _db.GetFileById(fileId);
        }

        private static List<object> GetInstalledPrinters()
        {
            var list = new List<object>();
            string defaultPrinter = "";
            try { defaultPrinter = new PrinterSettings().PrinterName; } catch { }
            try
            {
                foreach (string name in PrinterSettings.InstalledPrinters)
                {
                    list.Add(new Dictionary<string, object>
                    {
                        { "id", name },
                        { "name", name },
                        { "isDefault", string.Equals(name, defaultPrinter, StringComparison.OrdinalIgnoreCase) }
                    });
                }
            }
            catch { }
            return list;
        }

        private static Dictionary<string, object> SettingsToDict(AppSettingsModel s)
        {
            return new Dictionary<string, object>
            {
                { "shopId", s.ShopId },
                { "shopName", s.ShopName },
                { "shopSlug", s.ShopSlug },
                { "pairingCode", s.PairingCode },
                { "deviceId", s.DeviceId ?? "" },
                { "isPaired", s.IsPaired },
                { "storageRoot", s.StorageRoot },
                { "retentionHours", s.RetentionHours },
                { "maxFileSizeMb", s.MaxFileSizeBytes / (1024 * 1024) },
                { "maxSubmissionMb", s.MaxSubmissionBytes / (1024 * 1024) },
                { "maxFileSizeBytes", s.MaxFileSizeBytes },
                { "maxSubmissionBytes", s.MaxSubmissionBytes },
                { "chunkSizeBytes", s.ChunkSizeBytes },
                { "pauseReceiving", s.PauseReceiving },
                { "allowedExtensions", s.AllowedExtensions },
                { "theme", s.Theme },
                { "firstRunCompleted", s.FirstRunCompleted },
                { "publicBaseUrl", s.PublicBaseUrl }
            };
        }

        private static Dictionary<string, object> StorageSummaryToDict(StorageSummary s)
        {
            return new Dictionary<string, object>
            {
                { "storageRoot", s.StorageRoot },
                { "inboxDir", s.InboxDir },
                { "activeBytes", s.ActiveBytes },
                { "activeCount", s.ActiveCount },
                { "completedBytes", s.CompletedBytes },
                { "completedCount", s.CompletedCount },
                { "expiredCount", s.ExpiredCount },
                { "stagingBytes", s.StagingBytes },
                { "stagingCount", s.StagingCount },
                { "convertedBytes", s.ConvertedBytes },
                { "convertedCount", s.ConvertedCount },
                { "orphanBytes", s.OrphanBytes },
                { "orphanCount", s.OrphanCount },
                { "databaseBytes", s.DatabaseBytes },
                { "totalUsedBytes", s.TotalUsedBytes },
                { "freeDriveBytes", s.FreeDriveBytes }
            };
        }

        private void ServeWorkspaceHtml(HttpListenerResponse res)
        {
            string[] candidates = new[]
            {
                Path.Combine(_appRootDir, "workspace", "index.html"),
                Path.Combine(_appRootDir, "public", "workspace", "index.html"),
                Path.Combine(_appRootDir, "dist_netlify", "workspace", "index.html")
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c))
                {
                    HttpHelpers.WriteHtml(res, 200, File.ReadAllText(c, Encoding.UTF8));
                    return;
                }
            }
            HttpHelpers.WriteHtml(res, 200, "<html><body style='font-family:sans-serif;padding:40px;'><h2>Scan Drop Workspace</h2><p>workspace/index.html not found. Run <code>node build_netlify.js</code>.</p></body></html>");
        }

        private void FireStateChanged()
        {
            var h = OnStateChanged;
            if (h != null) try { h(); } catch { }
        }

        public void Dispose()
        {
            _disposed = true;
            try { if (_listener != null && _listener.IsListening) _listener.Stop(); } catch { }
            try { if (_listener != null) _listener.Close(); } catch { }
        }
    }
}
