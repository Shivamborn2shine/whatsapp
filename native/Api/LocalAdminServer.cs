using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
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
                        { "officeConversionReady", ConversionEngine.IsOfficeConversionAvailable() },
                        { "officeEngine", ConversionEngine.GetOfficeEngineLabel() },
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

                if (path == "/api/docx/inspect" && req.HttpMethod == "POST")
                {
                    HandleDocxInspect(req, res);
                    return;
                }

                if (path == "/api/docx/edit" && req.HttpMethod == "POST")
                {
                    HandleDocxEdit(req, res);
                    return;
                }

                if (path == "/api/file/save-edited-pdf" && req.HttpMethod == "POST")
                {
                    HandleSaveEditedPdf(req, res);
                    return;
                }

                if (path == "/api/file/reset-edited" && req.HttpMethod == "POST")
                {
                    var body = HttpHelpers.ReadJsonBody(req);
                    string fid = HttpHelpers.GetStr(body, "fileId");
                    var f = _db.GetFileById(fid);
                    if (f == null)
                    {
                        HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "File not found." } });
                        return;
                    }
                    _db.UpdateFileConversion(f.FileId, "none", null);
                    _db.AddActivity("FILE_RESET_ORIGINAL", "shopkeeper", f.SubmissionId, "Reverted " + f.OriginalFilename + " back to original version.");
                    FireStateChanged();
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true } });
                    return;
                }

                if (path == "/api/file/prepare-pdf" && req.HttpMethod == "POST")
                {
                    HandlePreparePdf(req, res);
                    return;
                }

                if (path == "/api/file/convert-docx" && req.HttpMethod == "POST")
                {
                    HandleConvertPdfToDocx(req, res);
                    return;
                }

                if (path == "/api/converted/open" && req.HttpMethod == "POST")
                {
                    HandleOpenConverted(req, res);
                    return;
                }

                if (path == "/api/file/trash" && req.HttpMethod == "POST")
                {
                    HandleTrashOrRestoreFiles(req, res, true);
                    return;
                }

                if (path == "/api/file/restore" && req.HttpMethod == "POST")
                {
                    HandleTrashOrRestoreFiles(req, res, false);
                    return;
                }

                if (path == "/api/file/delete-permanent" && req.HttpMethod == "POST")
                {
                    HandleDeleteFilePermanent(req, res);
                    return;
                }

                if (path == "/api/trash/empty" && req.HttpMethod == "POST")
                {
                    HandleEmptyTrash(req, res);
                    return;
                }

                if (path == "/api/submission/add-file" && req.HttpMethod == "POST")
                {
                    HandleAddFileToSubmission(req, res);
                    return;
                }

                if (path == "/api/submission/reorder-files" && req.HttpMethod == "POST")
                {
                    HandleReorderSubmissionFiles(req, res);
                    return;
                }

                if (path == "/api/tray/file" && req.HttpMethod == "GET")
                {
                    HandleStreamTrayFile(req, res);
                    return;
                }

                if (path == "/api/tray/add-from-file" && req.HttpMethod == "POST")
                {
                    HandleTrayAddFromFile(req, res);
                    return;
                }

                if (path == "/api/tray/add-upload" && req.HttpMethod == "POST")
                {
                    HandleTrayAddUpload(req, res);
                    return;
                }

                if (path == "/api/tray/remove" && req.HttpMethod == "POST")
                {
                    HandleTrayRemove(req, res);
                    return;
                }

                if (path == "/api/tray/clear" && req.HttpMethod == "POST")
                {
                    HandleTrayClear(req, res);
                    return;
                }

                if (path == "/api/tray/reorder" && req.HttpMethod == "POST")
                {
                    HandleTrayReorder(req, res);
                    return;
                }

                if (path == "/api/tray/copy-to-submission" && req.HttpMethod == "POST")
                {
                    HandleTrayCopyToSubmission(req, res);
                    return;
                }

                if (path == "/api/tray/prepare-pdf" && req.HttpMethod == "POST")
                {
                    HandleTrayPreparePdf(req, res);
                    return;
                }

                if (path == "/api/tray/open" && req.HttpMethod == "POST")
                {
                    HandleTrayOpen(req, res);
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
                        }, out err, "--no-browser --skip-auto-update", _appRootDir);
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
                        { "receivedAt", f.ReceivedAt },
                        { "sortOrder", f.SortOrder },
                        { "pagePlanJson", f.PagePlanJson }
                    });
                }

                var tfDtos = new List<object>();
                foreach (var tf in s.TrashedFiles)
                {
                    tfDtos.Add(new Dictionary<string, object>
                    {
                        { "id", tf.FileId },
                        { "submissionId", s.ReceiptCode },
                        { "receiptCode", s.ReceiptCode },
                        { "customerName", s.CustomerName },
                        { "originalName", tf.OriginalFilename },
                        { "fileExtension", tf.FileExtension },
                        { "sizeBytes", tf.FileSize },
                        { "trashedAt", tf.TrashedAt ?? tf.ReceivedAt }
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
                    { "trashedFiles", tfDtos },
                    { "totalSizeBytes", s.TotalSize },
                    { "createdAt", s.CompletedAt ?? s.CreatedAt },
                    { "expiresAt", s.ExpiresAt }
                });
            }

            var globalTrashDtos = new List<object>();
            var trashRows = _db.GetAllTrashedFilesWithParent();
            foreach (var tr in trashRows)
            {
                object v;
                string fid = tr.TryGetValue("file_id", out v) && v != null ? Convert.ToString(v) : "";
                string sid = tr.TryGetValue("submission_id", out v) && v != null ? Convert.ToString(v) : "";
                string rcode = tr.TryGetValue("receipt_code", out v) && v != null ? Convert.ToString(v) : sid;
                string cname = tr.TryGetValue("customer_name", out v) && v != null ? Convert.ToString(v) : "Anonymous";
                string oname = tr.TryGetValue("original_filename", out v) && v != null ? Convert.ToString(v) : "";
                string fext = tr.TryGetValue("file_extension", out v) && v != null ? Convert.ToString(v) : "";
                long fsize = tr.TryGetValue("file_size", out v) && v != null ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : 0;
                string tat = tr.TryGetValue("trashed_at", out v) && v != null ? Convert.ToString(v) : "";
                globalTrashDtos.Add(new Dictionary<string, object>
                {
                    { "id", fid },
                    { "submissionId", rcode },
                    { "receiptCode", rcode },
                    { "customerName", cname },
                    { "originalName", oname },
                    { "fileExtension", fext },
                    { "sizeBytes", fsize },
                    { "trashedAt", tat }
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
                var convFiles = new List<string>();
                convFiles.AddRange(Directory.GetFiles(_storage.Paths.ConvertedDir, "*.pdf"));
                convFiles.AddRange(Directory.GetFiles(_storage.Paths.ConvertedDir, "*.docx"));
                convFiles.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                foreach (var f in convFiles)
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        convertedList.Add(new Dictionary<string, object>
                        {
                            { "name", fi.Name },
                            { "ext", fi.Extension.ToLowerInvariant() },
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

            var trayDtos = new List<object>();
            var trayItems = _db.GetTrayItems();
            foreach (var ti in trayItems)
            {
                if (string.IsNullOrEmpty(ti.StoragePath) || !File.Exists(ti.StoragePath)) continue;
                trayDtos.Add(new Dictionary<string, object>
                {
                    { "id", ti.TrayId },
                    { "sourceFileId", ti.SourceFileId },
                    { "sourceLabel", ti.SourceLabel },
                    { "originalName", ti.OriginalFilename },
                    { "fileExtension", ti.FileExtension },
                    { "mimeType", ti.ContentType },
                    { "sizeBytes", ti.FileSize },
                    { "sortOrder", ti.SortOrder },
                    { "addedAt", ti.AddedAt },
                    { "url", "/api/tray/file?id=" + Uri.EscapeDataString(ti.TrayId) }
                });
            }

            var stateObj = new Dictionary<string, object>
            {
                { "shops", new List<object> { shopObj } },
                { "submissions", subDtos },
                { "trashedFiles", globalTrashDtos },
                { "trayItems", trayDtos },
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
                { "officeConversionReady", ConversionEngine.IsOfficeConversionAvailable() },
                { "officeEngine", ConversionEngine.GetOfficeEngineLabel() },
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
            string ct = full.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                : "application/pdf";
            StreamDiskFile(res, full, ct, name, download);
        }

        private string ResolveEditableDocxPath(FileRecord file, bool preferOriginal, out string error)
        {
            error = null;
            if (file == null || string.IsNullOrEmpty(file.StoragePath) || !File.Exists(file.StoragePath))
            {
                error = "File not found on disk.";
                return null;
            }
            string editedDocx = Path.Combine(_storage.Paths.ConvertedDir, "edited_" + StorageManager.SanitizeDisplayName(file.FileId) + ".docx");
            if (!preferOriginal && File.Exists(editedDocx)) return editedDocx;

            string ext = (file.FileExtension ?? Path.GetExtension(file.StoragePath)).ToLowerInvariant();
            if (ext == ".docx") return file.StoragePath;
            if (ext == ".doc")
            {
                string convertedDocx = Path.Combine(_storage.Paths.ConvertedDir, "src_" + StorageManager.SanitizeDisplayName(file.FileId) + ".docx");
                if (!File.Exists(convertedDocx))
                {
                    if (!_converter.ConvertDocToDocx(file.StoragePath, convertedDocx, out error))
                        return null;
                }
                return convertedDocx;
            }
            error = "Only .docx and .doc files are supported by DOCX Studio.";
            return null;
        }

        private void HandleDocxInspect(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string fileId = HttpHelpers.GetStr(body, "fileId");
            bool useOriginal = HttpHelpers.GetBool(body, "useOriginal", false);
            var file = _db.GetFileById(fileId);
            string err;
            string docxPath = ResolveEditableDocxPath(file, useOriginal, out err);
            if (string.IsNullOrEmpty(docxPath))
            {
                HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", err ?? "Cannot inspect file." } });
                return;
            }

            var insp = DocxEditor.Inspect(docxPath);
            if (!insp.Success)
            {
                HttpHelpers.WriteJson(res, 500, new Dictionary<string, object> { { "success", false }, { "error", insp.Error } });
                return;
            }

            string editedDocx = Path.Combine(_storage.Paths.ConvertedDir, "edited_" + StorageManager.SanitizeDisplayName(file.FileId) + ".docx");
            bool hasEdited = File.Exists(editedDocx);

            var headersList = new List<object>();
            foreach (var b in insp.Headers)
                headersList.Add(new Dictionary<string, object> { { "partPath", b.PartPath }, { "partType", b.PartType }, { "paragraphIndex", b.ParagraphIndex }, { "text", b.Text }, { "inTable", b.InTable } });
            var footersList = new List<object>();
            foreach (var b in insp.Footers)
                footersList.Add(new Dictionary<string, object> { { "partPath", b.PartPath }, { "partType", b.PartType }, { "paragraphIndex", b.ParagraphIndex }, { "text", b.Text }, { "inTable", b.InTable } });
            var bodyList = new List<object>();
            foreach (var b in insp.BodyBlocks)
                bodyList.Add(new Dictionary<string, object> { { "partPath", b.PartPath }, { "partType", b.PartType }, { "paragraphIndex", b.ParagraphIndex }, { "text", b.Text }, { "inTable", b.InTable } });
            var imgList = new List<object>();
            foreach (var im in insp.Images)
                imgList.Add(new Dictionary<string, object> { { "partPath", im.PartPath }, { "drawingIndex", im.DrawingIndex }, { "name", im.Name }, { "widthCm", im.WidthCm }, { "heightCm", im.HeightCm } });

            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "fileId", file.FileId },
                { "originalName", file.OriginalFilename },
                { "hasEditedCopy", hasEdited },
                { "convertedUrl", !string.IsNullOrEmpty(file.ConvertedPath) && File.Exists(file.ConvertedPath) ? ("/api/converted?name=" + Uri.EscapeDataString(Path.GetFileName(file.ConvertedPath))) : null },
                { "officeConversionReady", ConversionEngine.IsOfficeConversionAvailable() },
                { "officeEngine", ConversionEngine.GetOfficeEngineLabel() },
                { "headers", headersList },
                { "footers", footersList },
                { "bodyBlocks", bodyList },
                { "images", imgList }
            });
        }

        private void HandleDocxEdit(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string fileId = HttpHelpers.GetStr(body, "fileId");
            bool useOriginal = HttpHelpers.GetBool(body, "useOriginal", false);
            bool convertToPdf = HttpHelpers.GetBool(body, "convertToPdf", true);
            var file = _db.GetFileById(fileId);
            string err;
            string sourcePath = ResolveEditableDocxPath(file, useOriginal, out err);
            if (string.IsNullOrEmpty(sourcePath))
            {
                HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", err ?? "Cannot edit file." } });
                return;
            }

            var findReplaces = new List<DocxFindReplaceRule>();
            object rawFr;
            if (body.TryGetValue("findReplaces", out rawFr) && rawFr is System.Collections.IEnumerable)
            {
                foreach (var item in (System.Collections.IEnumerable)rawFr)
                {
                    var d = item as Dictionary<string, object>;
                    if (d == null) continue;
                    string f = HttpHelpers.GetStr(d, "find");
                    if (string.IsNullOrEmpty(f)) continue;
                    findReplaces.Add(new DocxFindReplaceRule
                    {
                        Find = f,
                        Replace = HttpHelpers.GetStr(d, "replace") ?? "",
                        CaseSensitive = HttpHelpers.GetBool(d, "caseSensitive", false),
                        Scope = HttpHelpers.GetStr(d, "scope") ?? "all"
                    });
                }
            }

            var paraEdits = new List<DocxParagraphEdit>();
            object rawPe;
            if (body.TryGetValue("paragraphEdits", out rawPe) && rawPe is System.Collections.IEnumerable)
            {
                foreach (var item in (System.Collections.IEnumerable)rawPe)
                {
                    var d = item as Dictionary<string, object>;
                    if (d == null) continue;
                    paraEdits.Add(new DocxParagraphEdit
                    {
                        PartPath = HttpHelpers.GetStr(d, "partPath"),
                        ParagraphIndex = (int)HttpHelpers.GetLong(d, "paragraphIndex", -1),
                        NewText = HttpHelpers.GetStr(d, "newText") ?? ""
                    });
                }
            }

            var imgResizes = new List<DocxImageResize>();
            object rawIr;
            if (body.TryGetValue("imageResizes", out rawIr) && rawIr is System.Collections.IEnumerable)
            {
                foreach (var item in (System.Collections.IEnumerable)rawIr)
                {
                    var d = item as Dictionary<string, object>;
                    if (d == null) continue;
                    double scale = 100, wcm = 0, hcm = 0;
                    if (d.ContainsKey("scalePercent") && d["scalePercent"] != null) double.TryParse(Convert.ToString(d["scalePercent"], CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out scale);
                    if (d.ContainsKey("widthCm") && d["widthCm"] != null) double.TryParse(Convert.ToString(d["widthCm"], CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out wcm);
                    if (d.ContainsKey("heightCm") && d["heightCm"] != null) double.TryParse(Convert.ToString(d["heightCm"], CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out hcm);
                    imgResizes.Add(new DocxImageResize
                    {
                        PartPath = HttpHelpers.GetStr(d, "partPath"),
                        DrawingIndex = (int)HttpHelpers.GetLong(d, "drawingIndex", -1),
                        ScalePercent = scale,
                        WidthCm = wcm,
                        HeightCm = hcm
                    });
                }
            }

            string outDocx = Path.Combine(_storage.Paths.ConvertedDir, "edited_" + StorageManager.SanitizeDisplayName(file.FileId) + ".docx");
            string tmpDocx = Path.Combine(_storage.Paths.TempDir, "edit_" + Guid.NewGuid().ToString("N") + ".docx");
            var editRes = DocxEditor.ApplyEdits(sourcePath, tmpDocx, findReplaces, paraEdits, imgResizes);
            if (!editRes.Success)
            {
                try { if (File.Exists(tmpDocx)) File.Delete(tmpDocx); } catch { }
                HttpHelpers.WriteJson(res, 500, new Dictionary<string, object> { { "success", false }, { "error", editRes.Error } });
                return;
            }
            File.Copy(tmpDocx, outDocx, true);
            try { File.Delete(tmpDocx); } catch { }

            string pdfUrl = null;
            string pdfFilename = null;
            string pdfEngine = null;
            string pdfError = null;

            if (convertToPdf)
            {
                string baseName = Path.GetFileNameWithoutExtension(StorageManager.SanitizeDisplayName(file.OriginalFilename)) + "_edited";
                var conv = _converter.ConvertPathToPdf(outDocx, baseName, ".docx");
                if (conv.Success)
                {
                    _db.UpdateFileConversion(file.FileId, "edited", conv.OutputPath);
                    pdfUrl = "/api/converted?name=" + Uri.EscapeDataString(conv.OutputFilename);
                    pdfFilename = conv.OutputFilename;
                    pdfEngine = conv.EngineUsed;
                }
                else
                {
                    _db.UpdateFileConversion(file.FileId, "edited", outDocx);
                    pdfError = conv.ErrorMessage;
                }
            }
            else
            {
                _db.UpdateFileConversion(file.FileId, "edited", outDocx);
            }

            _db.AddActivity("DOCX_EDITED", "shopkeeper", file.SubmissionId,
                string.Format(CultureInfo.InvariantCulture, "Edited {0} ({1} replace, {2} paragraph, {3} image resize)",
                    file.OriginalFilename, editRes.ReplacementsCount, editRes.ParagraphEditsCount, editRes.ImagesResizedCount));
            FireStateChanged();

            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "replacementsCount", editRes.ReplacementsCount },
                { "paragraphEditsCount", editRes.ParagraphEditsCount },
                { "imagesResizedCount", editRes.ImagesResizedCount },
                { "editedDocxUrl", "/api/converted?name=" + Uri.EscapeDataString(Path.GetFileName(outDocx)) + "&download=1" },
                { "pdfUrl", pdfUrl },
                { "pdfFilename", pdfFilename },
                { "pdfEngine", pdfEngine },
                { "pdfError", pdfError }
            });
        }

        private void HandleSaveEditedPdf(HttpListenerRequest req, HttpListenerResponse res)
        {
            string fileId = req.Headers["X-File-Id"];
            string outNameRaw = req.Headers["X-Output-Name"];
            string pagePlanRaw = req.Headers["X-Page-Plan"];
            if (!string.IsNullOrEmpty(pagePlanRaw))
            {
                try { pagePlanRaw = Uri.UnescapeDataString(pagePlanRaw); } catch { }
            }
            if (!Directory.Exists(_storage.Paths.ConvertedDir)) Directory.CreateDirectory(_storage.Paths.ConvertedDir);

            string safeName = StorageManager.SanitizeDisplayName(string.IsNullOrWhiteSpace(outNameRaw)
                ? ("Edited_" + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture) + ".pdf")
                : outNameRaw);
            if (!safeName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) safeName += ".pdf";

            string fullPath = StoragePaths.EnsureSafeSubPath(_storage.Paths.ConvertedDir, Path.Combine(_storage.Paths.ConvertedDir, safeName));
            long written = 0;
            using (var fs = File.Create(fullPath))
            {
                byte[] buf = new byte[65536];
                int n;
                while ((n = req.InputStream.Read(buf, 0, buf.Length)) > 0)
                {
                    fs.Write(buf, 0, n);
                    written += n;
                }
            }

            if (written < 32)
            {
                try { File.Delete(fullPath); } catch { }
                HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", "Empty or invalid PDF payload." } });
                return;
            }

            if (!string.IsNullOrWhiteSpace(fileId))
            {
                var f = _db.GetFileById(fileId);
                if (f != null)
                {
                    _db.UpdateFileConversion(f.FileId, "edited", fullPath);
                    if (!string.IsNullOrWhiteSpace(pagePlanRaw))
                        _db.UpdateFilePagePlan(f.FileId, pagePlanRaw);
                    _db.AddActivity("FILE_EDITED", "shopkeeper", f.SubmissionId, "Saved edited PDF for " + f.OriginalFilename + " -> " + safeName);
                }
            }
            else
            {
                _db.AddActivity("PDF_MERGED", "shopkeeper", null, "Saved merged/canvas PDF -> " + safeName);
            }
            FireStateChanged();

            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "filename", safeName },
                { "sizeBytes", written },
                { "url", "/api/converted?name=" + Uri.EscapeDataString(safeName) }
            });
        }

        private void HandlePreparePdf(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string fileId = HttpHelpers.GetStr(body, "fileId");
            bool forceOriginal = HttpHelpers.GetBool(body, "forceOriginal", false);
            var file = _db.GetFileById(fileId);
            if (file == null)
            {
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "File not found." } });
                return;
            }

            string ext = (file.FileExtension ?? Path.GetExtension(file.StoragePath)).ToLowerInvariant();

            // When opening Page Manager on a Word/Office file, always render ALL original pages (cached locally)
            if (forceOriginal)
            {
                if (ext == ".pdf")
                {
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "source", "original" },
                        { "filename", file.OriginalFilename },
                        { "url", "/api/file?id=" + Uri.EscapeDataString(file.FileId) }
                    });
                    return;
                }
                string origCacheName = "orig_" + StorageManager.SanitizeDisplayName(file.FileId) + ".pdf";
                string origCachePath = Path.Combine(_storage.Paths.ConvertedDir, origCacheName);
                if (!File.Exists(origCachePath))
                {
                    var origConv = _converter.ConvertPathToPdf(file.StoragePath, origCacheName, ext);
                    if (!origConv.Success)
                    {
                        HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", origConv.ErrorMessage } });
                        return;
                    }
                    origCacheName = origConv.OutputFilename;
                }
                HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                {
                    { "success", true },
                    { "source", "original_cached" },
                    { "filename", origCacheName },
                    { "url", "/api/converted?name=" + Uri.EscapeDataString(origCacheName) }
                });
                return;
            }

            // 1. Prefer already edited/converted PDF on disk so edits flow automatically into Merge & Print!
            if (!string.IsNullOrEmpty(file.ConvertedPath) && File.Exists(file.ConvertedPath) &&
                file.ConvertedPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                string fn = Path.GetFileName(file.ConvertedPath);
                HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                {
                    { "success", true },
                    { "source", file.ConversionStatus == "edited" ? "edited" : "converted" },
                    { "filename", fn },
                    { "url", "/api/converted?name=" + Uri.EscapeDataString(fn) }
                });
                return;
            }

            if (ext == ".pdf")
            {
                HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                {
                    { "success", true },
                    { "source", "original" },
                    { "filename", file.OriginalFilename },
                    { "url", "/api/file?id=" + Uri.EscapeDataString(file.FileId) }
                });
                return;
            }

            // 2. If an edited .docx exists (saved without PDF earlier), convert that edited .docx; else convert original file
            string editedDocx = Path.Combine(_storage.Paths.ConvertedDir, "edited_" + StorageManager.SanitizeDisplayName(file.FileId) + ".docx");
            ConversionResult conv = File.Exists(editedDocx)
                ? _converter.ConvertPathToPdf(editedDocx, Path.GetFileNameWithoutExtension(file.OriginalFilename) + "_edited", ".docx")
                : _converter.ConvertFileToPdf(file);

            if (!conv.Success)
            {
                HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", conv.ErrorMessage } });
                return;
            }

            _db.UpdateFileConversion(file.FileId, File.Exists(editedDocx) ? "edited" : "converted", conv.OutputPath);
            FireStateChanged();
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "source", File.Exists(editedDocx) ? "edited" : "converted" },
                { "filename", conv.OutputFilename },
                { "engine", conv.EngineUsed },
                { "url", "/api/converted?name=" + Uri.EscapeDataString(conv.OutputFilename) }
            });
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

        private void HandleConvertPdfToDocx(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string fileId = HttpHelpers.GetStr(body, "fileId");
            var file = _db.GetFileById(fileId);
            if (file == null || !File.Exists(file.StoragePath))
            {
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "PDF file not found." } });
                return;
            }
            string ext = (file.FileExtension ?? "").ToLowerInvariant();
            if (ext != ".pdf")
            {
                HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", "Only .pdf files can be converted to Word (.docx)." } });
                return;
            }

            string baseName = Path.GetFileNameWithoutExtension(file.OriginalFilename);
            var conv = _converter.ConvertPdfToDocx(file.StoragePath, baseName);
            if (!conv.Success)
            {
                HttpHelpers.WriteJson(res, 422, new Dictionary<string, object> { { "success", false }, { "error", conv.ErrorMessage } });
                return;
            }

            _db.AddActivity("PDF_TO_DOCX", "shopkeeper", file.SubmissionId,
                "Converted " + file.OriginalFilename + " -> " + conv.OutputFilename + " (" + conv.EngineUsed + ")");
            FireStateChanged();

            string url = "/api/converted?name=" + Uri.EscapeDataString(conv.OutputFilename);
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "outputFilename", conv.OutputFilename },
                { "sizeBytes", conv.OutputSizeBytes },
                { "engine", conv.EngineUsed },
                { "url", url },
                { "downloadUrl", url + "&download=1" }
            });
        }

        private void HandleOpenConverted(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string name = Path.GetFileName(HttpHelpers.GetStr(body, "name") ?? "");
            if (string.IsNullOrWhiteSpace(name))
            {
                HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", "Converted filename required." } });
                return;
            }
            string fullPath = Path.Combine(_storage.Paths.ConvertedDir, name);
            if (!File.Exists(fullPath))
            {
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "Converted file not found." } });
                return;
            }
            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true } });
        }

        private void HandleTrashOrRestoreFiles(HttpListenerRequest req, HttpListenerResponse res, bool toTrash)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            var ids = ExtractStringIds(body, "fileId", "fileIds");
            if (ids.Count == 0)
            {
                HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", "No fileId provided." } });
                return;
            }

            int updated = 0;
            foreach (var fid in ids)
            {
                var f = _db.GetFileById(fid);
                if (f == null) continue;
                _db.SetFileTrashed(fid, toTrash);
                _db.AddActivity(toTrash ? "FILE_TRASHED" : "FILE_RESTORED", "shopkeeper", f.SubmissionId,
                    (toTrash ? "Moved " : "Restored ") + f.OriginalFilename + (toTrash ? " to Trash." : " from Trash."));
                updated++;
            }
            FireStateChanged();
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "updatedCount", updated },
                { "trashed", toTrash }
            });
        }

        private void HandleDeleteFilePermanent(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            var ids = ExtractStringIds(body, "fileId", "fileIds");
            long freed = 0;
            int deleted = 0;
            foreach (var fid in ids)
            {
                var f = _db.GetFileById(fid);
                if (f == null) continue;
                freed += _storage.DeleteSingleFilePermanently(fid);
                deleted++;
            }
            FireStateChanged();
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "deletedCount", deleted },
                { "freedBytes", freed }
            });
        }

        private void HandleEmptyTrash(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string subFilter = HttpHelpers.GetStr(body, "submissionId");
            string resolvedSubId = null;
            if (!string.IsNullOrWhiteSpace(subFilter))
            {
                var sub = _db.GetSubmissionByReceiptOrId(subFilter);
                if (sub != null) resolvedSubId = sub.SubmissionId;
            }

            var trashed = _db.GetAllTrashedFilesWithParent();
            int deleted = 0;
            long freed = 0;
            foreach (var row in trashed)
            {
                object v;
                string fid = row.TryGetValue("file_id", out v) && v != null ? Convert.ToString(v) : "";
                string sid = row.TryGetValue("submission_id", out v) && v != null ? Convert.ToString(v) : "";
                if (!string.IsNullOrEmpty(resolvedSubId) && !string.Equals(sid, resolvedSubId, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(fid)) continue;
                freed += _storage.DeleteSingleFilePermanently(fid);
                deleted++;
            }
            FireStateChanged();
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "deletedCount", deleted },
                { "freedBytes", freed }
            });
        }

        private void HandleAddFileToSubmission(HttpListenerRequest req, HttpListenerResponse res)
        {
            string subKey = req.Headers["X-Submission-Id"];
            string rawName = req.Headers["X-File-Name"];
            if (!string.IsNullOrEmpty(rawName))
            {
                try { rawName = Uri.UnescapeDataString(rawName); } catch { }
            }

            var sub = _db.GetSubmissionByReceiptOrId(subKey);
            if (sub == null)
            {
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "Submission not found." } });
                return;
            }

            string displayName = StorageManager.SanitizeDisplayName(string.IsNullOrWhiteSpace(rawName) ? "added_file.pdf" : rawName);
            string ext = Path.GetExtension(displayName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) ext = ".bin";

            string subDir = Path.Combine(_storage.Paths.ReceivedDir, sub.SubmissionId);
            if (!Directory.Exists(subDir)) Directory.CreateDirectory(subDir);

            string internalName = _storage.GenerateInternalFilename(ext);
            string destPath = Path.Combine(subDir, internalName);

            long writtenBytes = 0;
            string shaHex;
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buf = new byte[65536];
                int read;
                while ((read = req.InputStream.Read(buf, 0, buf.Length)) > 0)
                {
                    fs.Write(buf, 0, read);
                    sha.TransformBlock(buf, 0, read, null, 0);
                    writtenBytes += read;
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                shaHex = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }

            if (writtenBytes <= 0)
            {
                try { File.Delete(destPath); } catch { }
                HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", "Dropped file is empty." } });
                return;
            }

            string nowIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var fileRec = new FileRecord
            {
                FileId = "file_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                SubmissionId = sub.SubmissionId,
                OriginalFilename = displayName,
                InternalFilename = internalName,
                ContentType = string.IsNullOrWhiteSpace(req.ContentType) ? "application/octet-stream" : req.ContentType,
                FileExtension = ext,
                FileSize = writtenBytes,
                Checksum = shaHex,
                StoragePath = destPath,
                ConversionStatus = "none",
                ConvertedPath = null,
                ReceivedAt = nowIso,
                IsTrashed = false,
                TrashedAt = null,
                SortOrder = _db.GetNextFileSortOrder(sub.SubmissionId)
            };

            _db.InsertFile(fileRec);
            _db.RecalculateSubmissionTotalSize(sub.SubmissionId);
            _db.AddActivity("FILE_ADDED_LOCAL", "shopkeeper", sub.SubmissionId,
                "Added local file '" + displayName + "' (" + writtenBytes + " B) to " + sub.ReceiptCode + " for editing/merging.");
            FireStateChanged();

            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "file", new Dictionary<string, object>
                    {
                        { "id", fileRec.FileId },
                        { "submissionId", sub.ReceiptCode },
                        { "originalName", fileRec.OriginalFilename },
                        { "fileExtension", fileRec.FileExtension },
                        { "sizeBytes", fileRec.FileSize },
                        { "sortOrder", fileRec.SortOrder }
                    }
                }
            });
        }

        private void HandleReorderSubmissionFiles(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string subKey = HttpHelpers.GetStr(body, "submissionId");
            var sub = _db.GetSubmissionByReceiptOrId(subKey);
            if (sub == null)
            {
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "Submission not found." } });
                return;
            }
            var orderedIds = ExtractStringIds(body, null, "fileIds");
            _db.UpdateSubmissionFileOrder(sub.SubmissionId, orderedIds);
            FireStateChanged();
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true } });
        }

        private void HandleStreamTrayFile(HttpListenerRequest req, HttpListenerResponse res)
        {
            string trayId = req.QueryString["id"];
            bool download = req.QueryString["download"] == "1";
            var item = _db.GetTrayItemById(trayId);
            if (item == null || string.IsNullOrEmpty(item.StoragePath) || !File.Exists(item.StoragePath))
            {
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "error", "Tray item not found" } });
                return;
            }
            StreamDiskFile(res, item.StoragePath, item.ContentType, item.OriginalFilename, download);
        }

        private void HandleTrayAddFromFile(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string fileId = HttpHelpers.GetStr(body, "fileId");
            var file = _db.GetFileById(fileId);
            if (file == null || string.IsNullOrEmpty(file.StoragePath) || !File.Exists(file.StoragePath))
            {
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "Source file not found." } });
                return;
            }

            if (!Directory.Exists(_storage.Paths.TrayDir)) Directory.CreateDirectory(_storage.Paths.TrayDir);

            // If the file has an edited PDF saved, stage that version; otherwise copy original file
            bool useEdited = string.Equals(file.ConversionStatus, "edited", StringComparison.OrdinalIgnoreCase) &&
                             !string.IsNullOrEmpty(file.ConvertedPath) && File.Exists(file.ConvertedPath);
            string srcPath = useEdited ? file.ConvertedPath : file.StoragePath;
            string ext = useEdited ? ".pdf" : (file.FileExtension ?? Path.GetExtension(file.OriginalFilename) ?? ".bin").ToLowerInvariant();
            string displayName = useEdited && !file.OriginalFilename.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? (Path.GetFileNameWithoutExtension(file.OriginalFilename) + " (Edited).pdf")
                : file.OriginalFilename;
            string ct = useEdited ? "application/pdf" : (file.ContentType ?? "application/octet-stream");

            string trayId = "tray_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            string destPath = Path.Combine(_storage.Paths.TrayDir, trayId + ext);
            File.Copy(srcPath, destPath, true);

            var sub = _db.GetSubmissionByReceiptOrId(file.SubmissionId);
            string label = sub != null ? sub.ReceiptCode : "Inbox";

            var rec = new TrayItemRecord
            {
                TrayId = trayId,
                SourceFileId = file.FileId,
                SourceLabel = label,
                OriginalFilename = displayName,
                ContentType = ct,
                FileExtension = ext,
                FileSize = new FileInfo(destPath).Length,
                StoragePath = destPath,
                SortOrder = _db.GetNextTraySortOrder(),
                AddedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
            _db.InsertTrayItem(rec);
            FireStateChanged();

            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "item", new Dictionary<string, object>
                    {
                        { "id", rec.TrayId },
                        { "originalName", rec.OriginalFilename },
                        { "fileExtension", rec.FileExtension },
                        { "sizeBytes", rec.FileSize },
                        { "sourceLabel", rec.SourceLabel }
                    }
                }
            });
        }

        private void HandleTrayAddUpload(HttpListenerRequest req, HttpListenerResponse res)
        {
            string rawName = req.Headers["X-File-Name"];
            if (!string.IsNullOrEmpty(rawName))
            {
                try { rawName = Uri.UnescapeDataString(rawName); } catch { }
            }
            string displayName = StorageManager.SanitizeDisplayName(string.IsNullOrWhiteSpace(rawName) ? "tray_file.pdf" : rawName);
            string ext = Path.GetExtension(displayName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) ext = ".bin";

            if (!Directory.Exists(_storage.Paths.TrayDir)) Directory.CreateDirectory(_storage.Paths.TrayDir);
            string trayId = "tray_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            string destPath = Path.Combine(_storage.Paths.TrayDir, trayId + ext);

            long written = 0;
            using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buf = new byte[65536];
                int read;
                while ((read = req.InputStream.Read(buf, 0, buf.Length)) > 0)
                {
                    fs.Write(buf, 0, read);
                    written += read;
                }
            }

            if (written <= 0)
            {
                try { File.Delete(destPath); } catch { }
                HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", "Dropped file is empty." } });
                return;
            }

            var rec = new TrayItemRecord
            {
                TrayId = trayId,
                SourceFileId = null,
                SourceLabel = "Local PC",
                OriginalFilename = displayName,
                ContentType = string.IsNullOrWhiteSpace(req.ContentType) ? "application/octet-stream" : req.ContentType,
                FileExtension = ext,
                FileSize = written,
                StoragePath = destPath,
                SortOrder = _db.GetNextTraySortOrder(),
                AddedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
            _db.InsertTrayItem(rec);
            FireStateChanged();

            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "item", new Dictionary<string, object>
                    {
                        { "id", rec.TrayId },
                        { "originalName", rec.OriginalFilename },
                        { "fileExtension", rec.FileExtension },
                        { "sizeBytes", rec.FileSize },
                        { "sourceLabel", rec.SourceLabel }
                    }
                }
            });
        }

        private void HandleTrayRemove(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string trayId = HttpHelpers.GetStr(body, "trayId");
            var removed = _db.DeleteTrayItem(trayId);
            if (removed != null && !string.IsNullOrEmpty(removed.StoragePath))
            {
                try { if (File.Exists(removed.StoragePath)) File.Delete(removed.StoragePath); } catch { }
            }
            FireStateChanged();
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true } });
        }

        private void HandleTrayClear(HttpListenerRequest req, HttpListenerResponse res)
        {
            var items = _db.ClearTrayItems();
            foreach (var it in items)
            {
                if (!string.IsNullOrEmpty(it.StoragePath))
                {
                    try { if (File.Exists(it.StoragePath)) File.Delete(it.StoragePath); } catch { }
                }
            }
            FireStateChanged();
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true }, { "clearedCount", items.Count } });
        }

        private void HandleTrayReorder(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            var ids = ExtractStringIds(body, null, "trayIds");
            _db.UpdateTrayOrder(ids);
            FireStateChanged();
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true } });
        }

        private void HandleTrayCopyToSubmission(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string subKey = HttpHelpers.GetStr(body, "submissionId");
            var sub = _db.GetSubmissionByReceiptOrId(subKey);
            if (sub == null)
            {
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "Target submission not found." } });
                return;
            }
            var trayIds = ExtractStringIds(body, "trayId", "trayIds");
            if (trayIds.Count == 0)
            {
                HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", "No tray items specified." } });
                return;
            }

            string subDir = Path.Combine(_storage.Paths.ReceivedDir, sub.SubmissionId);
            if (!Directory.Exists(subDir)) Directory.CreateDirectory(subDir);

            int copied = 0;
            foreach (var tid in trayIds)
            {
                var item = _db.GetTrayItemById(tid);
                if (item == null || string.IsNullOrEmpty(item.StoragePath) || !File.Exists(item.StoragePath)) continue;

                string ext = string.IsNullOrEmpty(item.FileExtension) ? Path.GetExtension(item.OriginalFilename).ToLowerInvariant() : item.FileExtension;
                if (string.IsNullOrEmpty(ext)) ext = ".bin";
                string internalName = _storage.GenerateInternalFilename(ext);
                string destPath = Path.Combine(subDir, internalName);
                File.Copy(item.StoragePath, destPath, true);

                string shaHex = "";
                try
                {
                    using (var sha = SHA256.Create())
                    using (var fs = File.OpenRead(destPath))
                        shaHex = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                }
                catch { }

                var fRec = new FileRecord
                {
                    FileId = "file_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                    SubmissionId = sub.SubmissionId,
                    OriginalFilename = item.OriginalFilename,
                    InternalFilename = internalName,
                    ContentType = item.ContentType ?? "application/octet-stream",
                    FileExtension = ext,
                    FileSize = new FileInfo(destPath).Length,
                    Checksum = shaHex,
                    StoragePath = destPath,
                    ConversionStatus = "none",
                    ConvertedPath = null,
                    ReceivedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    IsTrashed = false,
                    TrashedAt = null,
                    SortOrder = _db.GetNextFileSortOrder(sub.SubmissionId)
                };
                _db.InsertFile(fRec);
                copied++;
            }

            _db.RecalculateSubmissionTotalSize(sub.SubmissionId);
            if (copied > 0)
            {
                _db.AddActivity("TRAY_TO_SUBMISSION", "shopkeeper", sub.SubmissionId,
                    "Copied " + copied + " file(s) from Holding Tray into " + sub.ReceiptCode);
            }
            FireStateChanged();
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true }, { "copiedCount", copied } });
        }

        private void HandleTrayPreparePdf(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string trayId = HttpHelpers.GetStr(body, "trayId");
            var item = _db.GetTrayItemById(trayId);
            if (item == null || string.IsNullOrEmpty(item.StoragePath) || !File.Exists(item.StoragePath))
            {
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "Tray item not found." } });
                return;
            }
            string ext = (item.FileExtension ?? "").ToLowerInvariant();
            if (ext == ".pdf")
            {
                HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                {
                    { "success", true },
                    { "url", "/api/tray/file?id=" + Uri.EscapeDataString(item.TrayId) },
                    { "filename", item.OriginalFilename },
                    { "source", "tray_pdf" }
                });
                return;
            }
            string outName = Path.GetFileNameWithoutExtension(item.OriginalFilename) + "_tray_" + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture) + ".pdf";
            var conv = _converter.ConvertPathToPdf(item.StoragePath, outName);
            if (!conv.Success)
            {
                HttpHelpers.WriteJson(res, 422, new Dictionary<string, object> { { "success", false }, { "error", conv.ErrorMessage } });
                return;
            }
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
            {
                { "success", true },
                { "url", "/api/converted?name=" + Uri.EscapeDataString(conv.OutputFilename) },
                { "filename", conv.OutputFilename },
                { "source", "tray_converted" }
            });
        }

        private void HandleTrayOpen(HttpListenerRequest req, HttpListenerResponse res)
        {
            var body = HttpHelpers.ReadJsonBody(req);
            string trayId = HttpHelpers.GetStr(body, "trayId");
            var item = _db.GetTrayItemById(trayId);
            if (item == null || string.IsNullOrEmpty(item.StoragePath) || !File.Exists(item.StoragePath))
            {
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "Tray item not found." } });
                return;
            }
            Process.Start(new ProcessStartInfo(item.StoragePath) { UseShellExecute = true });
            HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true } });
        }

        private static List<string> ExtractStringIds(Dictionary<string, object> body, string singleKey, string listKey)
        {
            var ids = new List<string>();
            if (!string.IsNullOrEmpty(singleKey))
            {
                string single = HttpHelpers.GetStr(body, singleKey);
                if (!string.IsNullOrWhiteSpace(single)) ids.Add(single.Trim());
            }
            object rawList;
            if (!string.IsNullOrEmpty(listKey) && body != null && body.TryGetValue(listKey, out rawList) && rawList is System.Collections.IEnumerable && !(rawList is string))
            {
                foreach (var item in (System.Collections.IEnumerable)rawList)
                {
                    string s = Convert.ToString(item);
                    if (!string.IsNullOrWhiteSpace(s) && !ids.Contains(s.Trim())) ids.Add(s.Trim());
                }
            }
            return ids;
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
