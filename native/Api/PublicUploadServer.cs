using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using ScanDrop.Native.Core;
using ScanDrop.Native.Storage;
using ScanDrop.Native.Transfers;

namespace ScanDrop.Native.Api
{
    /// <summary>
    /// PUBLIC-FACING CUSTOMER UPLOAD RECEIVER (PRD Sec 3.1, 8.1, 11).
    /// Bound to 127.0.0.1:8787 (tunneled via Cloudflare/OpenSSH/Pinggy).
    /// ARCHITECTURAL ISOLATION: Mounts ONLY public customer upload & receipt-status routes.
    /// Zero admin endpoints, zero dashboard routes, zero file downloads, zero browsing of other submissions.
    /// </summary>
    public sealed class PublicUploadServer : IDisposable
    {
        private readonly string _appRootDir;
        private readonly int _preferredPort;
        private readonly SqliteDatabase _db;
        private readonly StorageManager _storage;
        private readonly TransferService _transfers;
        private readonly UploadSecurityGuard _guard;
        private readonly AppLogger _logger;

        private HttpListener _listener;
        private Thread _listenThread;
        private int _actualPort;
        private bool _disposed;

        public int Port { get { return _actualPort; } }
        public bool IsRunning { get { return !_disposed && _listener != null && _listener.IsListening; } }

        public PublicUploadServer(
            string appRootDir,
            int preferredPort,
            SqliteDatabase db,
            StorageManager storage,
            TransferService transfers,
            UploadSecurityGuard guard,
            AppLogger logger)
        {
            _appRootDir = appRootDir;
            _preferredPort = preferredPort;
            _db = db;
            _storage = storage;
            _transfers = transfers;
            _guard = guard;
            _logger = logger;
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

            _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "PublicUploadReceiver" };
            _listenThread.Start();
            _logger.Info("Receiver", "Public Upload Receiver listening on http://127.0.0.1:" + _actualPort + " (tunneled port, admin isolated)");
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
                catch (Exception ex) { _logger.Warn("Receiver", "Accept error: " + ex.Message); }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            HttpHelpers.AddPublicCors(res);

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            string path = (req.Url.AbsolutePath ?? "/").TrimEnd('/');
            if (string.IsNullOrEmpty(path)) path = "/";
            string clientIp = UploadSecurityGuard.ExtractClientIp(req);

            try
            {
                // 1. Public Customer Upload Page (GET / or GET /shop/{slug})
                if ((path == "/" || path.StartsWith("/shop/", StringComparison.OrdinalIgnoreCase)) && req.HttpMethod == "GET")
                {
                    ServeCustomerUploadHtml(res);
                    return;
                }

                // 2. Public Minimal Health / Readiness (NO internal paths or config exposed)
                if (path == "/api/health" && req.HttpMethod == "GET")
                {
                    var settings = _db.LoadSettings(_storage.Paths.DataRootDir);
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "ok", true },
                        { "success", true },
                        { "storageMode", "local-pc" },
                        { "shopName", settings.ShopName },
                        { "shopSlug", settings.ShopSlug },
                        { "acceptingUploads", !settings.PauseReceiving },
                        { "maxFileSizeBytes", settings.MaxFileSizeBytes },
                        { "maxSubmissionBytes", settings.MaxSubmissionBytes },
                        { "chunkSizeBytes", settings.ChunkSizeBytes },
                        { "allowedExtensions", settings.AllowedExtensions }
                    });
                    return;
                }

                // 3. Public Shop Info (when customer portal loads straight from tunnel URL)
                if (path == "/api/shop" && req.HttpMethod == "GET")
                {
                    var settings = _db.LoadSettings(_storage.Paths.DataRootDir);
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "shop", new Dictionary<string, object>
                            {
                                { "id", settings.ShopId },
                                { "slug", settings.ShopSlug },
                                { "name", settings.ShopName },
                                { "tagline", "Scan. Send. Done." },
                                { "subscriptionStatus", settings.PauseReceiving ? "paused" : "active" },
                                { "acceptingUploads", !settings.PauseReceiving },
                                { "tunnelOnline", true },
                                { "tunnelUrl", "" },
                                { "directReceiver", true },
                                { "maxFileSizeBytes", settings.MaxFileSizeBytes },
                                { "maxSubmissionBytes", settings.MaxSubmissionBytes },
                                { "chunkSizeBytes", settings.ChunkSizeBytes },
                                { "allowedExtensions", settings.AllowedExtensions }
                            }
                        }
                    });
                    return;
                }

                // 4. Create Expiring Upload Session (PRD Sec 4 & 11)
                if (path == "/api/upload/sessions" && req.HttpMethod == "POST")
                {
                    var body = HttpHelpers.ReadJsonBody(req);
                    string custName = HttpHelpers.GetStr(body, "customerName");
                    string custNote = HttpHelpers.GetStr(body, "customerNote") ?? HttpHelpers.GetStr(body, "instructions");
                    string idemKey = HttpHelpers.GetStr(body, "idempotencyKey") ?? req.Headers["X-Idempotency-Key"];

                    var decls = new List<SessionFileDeclaration>();
                    object rawFilesObj;
                    if (body.TryGetValue("files", out rawFilesObj) && rawFilesObj is System.Collections.IList)
                    {
                        foreach (var item in (System.Collections.IList)rawFilesObj)
                        {
                            var fd = item as Dictionary<string, object>;
                            if (fd == null) continue;
                            decls.Add(new SessionFileDeclaration
                            {
                                ClientFileId = HttpHelpers.GetStr(fd, "clientFileId") ?? HttpHelpers.GetStr(fd, "id"),
                                OriginalName = HttpHelpers.GetStr(fd, "name") ?? HttpHelpers.GetStr(fd, "originalName"),
                                ContentType = HttpHelpers.GetStr(fd, "contentType") ?? HttpHelpers.GetStr(fd, "mimeType"),
                                FileSize = HttpHelpers.GetLong(fd, "size", HttpHelpers.GetLong(fd, "sizeBytes", 0)),
                                ChunkCount = HttpHelpers.GetInt(fd, "chunkCount", 0)
                            });
                        }
                    }

                    int status;
                    string err;
                    var init = _transfers.CreateUploadSession(clientIp, custName, custNote, idemKey, decls, out status, out err);
                    if (init == null)
                    {
                        HttpHelpers.WriteJson(res, status, new Dictionary<string, object> { { "success", false }, { "error", err } });
                        return;
                    }

                    if (init.AlreadyCompleted && init.ExistingSubmission != null)
                    {
                        var ex = init.ExistingSubmission;
                        var shop = _db.GetPrimaryShop();
                        HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                        {
                            { "success", true },
                            { "alreadyCompleted", true },
                            { "receiptCode", ex.ReceiptCode },
                            { "receiptId", ex.ReceiptCode },
                            { "shopName", shop.ShopName },
                            { "fileCount", ex.Files.Count },
                            { "totalSizeBytes", ex.TotalSize },
                            { "submittedAt", ex.CompletedAt ?? ex.CreatedAt },
                            { "expiresAt", ex.ExpiresAt },
                            { "status", ex.Status }
                        });
                        return;
                    }

                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "alreadyCompleted", false },
                        { "sessionId", init.SessionId },
                        { "uploadToken", init.UploadToken },
                        { "chunkSizeBytes", init.ChunkSizeBytes },
                        { "expiresAt", init.ExpiresAt },
                        { "files", init.Files }
                    });
                    return;
                }

                // 5. Upload Chunk (streaming raw binary OR multipart)
                if (path == "/api/upload/chunk" && req.HttpMethod == "POST")
                {
                    string token = req.Headers["X-Upload-Token"] ?? req.QueryString["token"];
                    string sessionId = req.Headers["X-Session-Id"] ?? req.QueryString["sessionId"];
                    string fileId = req.Headers["X-File-Id"] ?? req.QueryString["fileId"];
                    string idxStr = req.Headers["X-Chunk-Index"] ?? req.QueryString["chunkIndex"];
                    string offStr = req.Headers["X-Byte-Offset"] ?? req.QueryString["byteOffset"] ?? "0";
                    string shaStr = req.Headers["X-Chunk-Sha256"] ?? req.QueryString["sha256"];

                    int chunkIndex;
                    long byteOffset;
                    int status;
                    string err;

                    bool isMultipart = !string.IsNullOrEmpty(req.ContentType) &&
                        req.ContentType.IndexOf("multipart/form-data", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isMultipart)
                    {
                        Dictionary<string, string> fields;
                        List<MultipartFilePart> parts;
                        HttpHelpers.ParseMultipart(req, TransferService.MaxChunkBytes + 512 * 1024, out fields, out parts);
                        if (string.IsNullOrEmpty(sessionId) && fields.ContainsKey("sessionId")) sessionId = fields["sessionId"];
                        if (string.IsNullOrEmpty(token) && fields.ContainsKey("uploadToken")) token = fields["uploadToken"];
                        if (string.IsNullOrEmpty(fileId) && fields.ContainsKey("fileId")) fileId = fields["fileId"];
                        if (string.IsNullOrEmpty(idxStr) && fields.ContainsKey("chunkIndex")) idxStr = fields["chunkIndex"];
                        if (fields.ContainsKey("byteOffset")) offStr = fields["byteOffset"];
                        if (fields.ContainsKey("chunkSha256")) shaStr = fields["chunkSha256"];

                        if (!int.TryParse(idxStr, out chunkIndex) || !long.TryParse(offStr, out byteOffset) || parts.Count == 0)
                        {
                            HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", "Missing chunk parameters." } });
                            return;
                        }
                        using (var ms = new MemoryStream(parts[0].Bytes, false))
                        {
                            if (!_transfers.SaveChunk(clientIp, sessionId, token, fileId, chunkIndex, byteOffset, ms, parts[0].Bytes.Length, shaStr, out status, out err))
                            {
                                HttpHelpers.WriteJson(res, status, new Dictionary<string, object> { { "success", false }, { "error", err } });
                                return;
                            }
                        }
                    }
                    else
                    {
                        if (!int.TryParse(idxStr, out chunkIndex) || !long.TryParse(offStr, out byteOffset))
                        {
                            HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", "Missing chunk headers." } });
                            return;
                        }
                        if (!_transfers.SaveChunk(clientIp, sessionId, token, fileId, chunkIndex, byteOffset, req.InputStream, req.ContentLength64, shaStr, out status, out err))
                        {
                            HttpHelpers.WriteJson(res, status, new Dictionary<string, object> { { "success", false }, { "error", err } });
                            return;
                        }
                    }

                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true }, { "chunkIndex", chunkIndex } });
                    return;
                }

                // 6. Complete & Commit Upload Session
                if (path == "/api/upload/complete" && req.HttpMethod == "POST")
                {
                    var body = HttpHelpers.ReadJsonBody(req);
                    string sessionId = HttpHelpers.GetStr(body, "sessionId") ?? req.Headers["X-Session-Id"];
                    string token = HttpHelpers.GetStr(body, "uploadToken") ?? req.Headers["X-Upload-Token"];

                    int status;
                    string err;
                    var sub = _transfers.CompleteUploadSession(clientIp, sessionId, token, out status, out err);
                    if (sub == null)
                    {
                        HttpHelpers.WriteJson(res, status, new Dictionary<string, object> { { "success", false }, { "error", err } });
                        return;
                    }

                    var shop = _db.GetPrimaryShop();
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "receiptCode", sub.ReceiptCode },
                        { "receiptId", sub.ReceiptCode },
                        { "shopName", shop.ShopName },
                        { "fileCount", sub.Files.Count },
                        { "totalSizeBytes", sub.TotalSize },
                        { "submittedAt", sub.CompletedAt ?? sub.CreatedAt },
                        { "expiresAt", sub.ExpiresAt },
                        { "status", sub.Status },
                        { "storageMode", "local-pc" }
                    });
                    return;
                }

                // 7. Non-Sensitive Receipt Status Check (zero file paths or downloads!)
                if (path == "/api/receipt" && req.HttpMethod == "GET")
                {
                    string rateErr;
                    if (!_guard.CheckRateLimit(clientIp, false, out rateErr))
                    {
                        HttpHelpers.WriteJson(res, 429, new Dictionary<string, object> { { "success", false }, { "error", rateErr } });
                        return;
                    }
                    string rid = (req.QueryString["id"] ?? "").Trim().ToUpperInvariant();
                    var sub = _db.GetSubmissionByReceiptOrId(rid);
                    if (sub == null || sub.Status == "expired" || sub.Status == "uploading")
                    {
                        HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "Receipt not found or expired." } });
                        return;
                    }
                    var shop = _db.GetPrimaryShop();
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "submission", new Dictionary<string, object>
                            {
                                { "id", sub.ReceiptCode },
                                { "receiptCode", sub.ReceiptCode },
                                { "shopName", shop.ShopName },
                                { "status", sub.Status },
                                { "fileCount", sub.Files.Count },
                                { "totalSizeBytes", sub.TotalSize },
                                { "createdAt", sub.CompletedAt ?? sub.CreatedAt },
                                { "expiresAt", sub.ExpiresAt }
                            }
                        }
                    });
                    return;
                }

                // 8. Backwards-Compatible Single-Shot Direct Upload (validated by same security pipeline)
                if (path == "/api/direct-upload" && req.HttpMethod == "POST")
                {
                    var settings = _db.LoadSettings(_storage.Paths.DataRootDir);
                    Dictionary<string, string> fields;
                    List<MultipartFilePart> parts;
                    HttpHelpers.ParseMultipart(req, Math.Min(settings.MaxSubmissionBytes, 32L * 1024L * 1024L), out fields, out parts);

                    var rawList = new List<KeyValuePair<string, byte[]>>();
                    var ctMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in parts)
                    {
                        rawList.Add(new KeyValuePair<string, byte[]>(p.FileName, p.Bytes));
                        ctMap[p.FileName] = p.ContentType;
                    }

                    string cName = fields.ContainsKey("customerName") ? fields["customerName"] : "";
                    string cNote = fields.ContainsKey("instructions") ? fields["instructions"] : (fields.ContainsKey("customerNote") ? fields["customerNote"] : "");
                    string idem = fields.ContainsKey("idempotencyKey") ? fields["idempotencyKey"] : req.Headers["X-Idempotency-Key"];

                    int status;
                    string err;
                    var sub = _transfers.HandleSingleShotUpload(clientIp, cName, cNote, idem, rawList, ctMap, out status, out err);
                    if (sub == null)
                    {
                        HttpHelpers.WriteJson(res, status, new Dictionary<string, object> { { "success", false }, { "error", err } });
                        return;
                    }
                    var shop = _db.GetPrimaryShop();
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "receiptCode", sub.ReceiptCode },
                        { "receiptId", sub.ReceiptCode },
                        { "shopName", shop.ShopName },
                        { "fileCount", sub.Files.Count },
                        { "totalSizeBytes", sub.TotalSize },
                        { "submittedAt", sub.CompletedAt ?? sub.CreatedAt },
                        { "expiresAt", sub.ExpiresAt },
                        { "status", sub.Status },
                        { "storageMode", "local-pc" }
                    });
                    return;
                }

                // 9. Backwards-Compatible Chunked Direct Upload (for v3.2.x cached Netlify portals > 25MB)
                if (path == "/api/direct-upload-chunk" && req.HttpMethod == "POST")
                {
                    Dictionary<string, string> fields;
                    List<MultipartFilePart> parts;
                    HttpHelpers.ParseMultipart(req, 12L * 1024L * 1024L + 512 * 1024, out fields, out parts);
                    string fileId = fields.ContainsKey("fileId") ? fields["fileId"] : null;
                    string idxStr = fields.ContainsKey("chunkIndex") ? fields["chunkIndex"] : null;
                    int chunkIdx;
                    if (!int.TryParse(idxStr, out chunkIdx) || parts.Count == 0)
                    {
                        HttpHelpers.WriteJson(res, 400, new Dictionary<string, object> { { "success", false }, { "error", "Missing chunk parameters." } });
                        return;
                    }
                    int status;
                    string err;
                    if (!_transfers.SaveLegacyDirectChunk(clientIp, fileId, chunkIdx, parts[0].Bytes, out status, out err))
                    {
                        HttpHelpers.WriteJson(res, status, new Dictionary<string, object> { { "success", false }, { "error", err } });
                        return;
                    }
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object> { { "success", true }, { "chunkIndex", chunkIdx } });
                    return;
                }

                if (path == "/api/direct-upload-complete" && req.HttpMethod == "POST")
                {
                    var body = HttpHelpers.ReadJsonBody(req);
                    string custName = HttpHelpers.GetStr(body, "customerName");
                    string custNote = HttpHelpers.GetStr(body, "instructions") ?? HttpHelpers.GetStr(body, "customerNote");
                    var decls = new List<SessionFileDeclaration>();
                    object rawFiles;
                    if (body.TryGetValue("files", out rawFiles) && rawFiles is System.Collections.IList)
                    {
                        foreach (var item in (System.Collections.IList)rawFiles)
                        {
                            var fm = item as Dictionary<string, object>;
                            if (fm == null) continue;
                            decls.Add(new SessionFileDeclaration
                            {
                                ClientFileId = HttpHelpers.GetStr(fm, "id"),
                                OriginalName = HttpHelpers.GetStr(fm, "originalName") ?? HttpHelpers.GetStr(fm, "name") ?? "file.bin",
                                ContentType = HttpHelpers.GetStr(fm, "mimeType") ?? "application/octet-stream",
                                FileSize = HttpHelpers.GetLong(fm, "sizeBytes", 1),
                                ChunkCount = HttpHelpers.GetInt(fm, "chunkCount", 1)
                            });
                        }
                    }

                    int status;
                    string err;
                    var sub = _transfers.CompleteLegacyDirectUpload(clientIp, custName, custNote, decls, out status, out err);
                    if (sub == null)
                    {
                        HttpHelpers.WriteJson(res, status, new Dictionary<string, object> { { "success", false }, { "error", err } });
                        return;
                    }
                    var shop = _db.GetPrimaryShop();
                    HttpHelpers.WriteJson(res, 200, new Dictionary<string, object>
                    {
                        { "success", true },
                        { "receiptCode", sub.ReceiptCode },
                        { "receiptId", sub.ReceiptCode },
                        { "shopName", shop.ShopName },
                        { "fileCount", sub.Files.Count },
                        { "totalSizeBytes", sub.TotalSize },
                        { "submittedAt", sub.CompletedAt ?? sub.CreatedAt },
                        { "expiresAt", sub.ExpiresAt },
                        { "status", sub.Status },
                        { "storageMode", "local-pc" }
                    });
                    return;
                }

                // ALL OTHER PATHS (including /workspace, /api/state, /api/file, /api/storage, /api/settings, /api/tunnel)
                // ARE STRICTLY NOT MOUNTED ON THE PUBLIC RECEIVER!
                HttpHelpers.WriteJson(res, 404, new Dictionary<string, object> { { "success", false }, { "error", "Not Found" } });
            }
            catch (Exception ex)
            {
                _logger.Warn("Receiver", "Public request error (" + path + "): " + ex.Message);
                try { HttpHelpers.WriteJson(res, 500, new Dictionary<string, object> { { "success", false }, { "error", ex.Message } }); } catch { }
            }
        }

        private void ServeCustomerUploadHtml(HttpListenerResponse res)
        {
            string[] candidates = new[]
            {
                Path.Combine(_appRootDir, "index.html"),
                Path.Combine(_appRootDir, "public", "index.html"),
                Path.Combine(_appRootDir, "dist_netlify", "index.html")
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c))
                {
                    HttpHelpers.WriteHtml(res, 200, File.ReadAllText(c, Encoding.UTF8));
                    return;
                }
            }
            HttpHelpers.WriteHtml(res, 200, "<!doctype html><html><body style='font-family:sans-serif;padding:40px;'><h2>Scan Drop</h2><p>Customer portal bundle (index.html) not found next to ScanDrop.exe.</p></body></html>");
        }

        public void Dispose()
        {
            _disposed = true;
            try { if (_listener != null && _listener.IsListening) _listener.Stop(); } catch { }
            try { if (_listener != null) _listener.Close(); } catch { }
        }
    }
}
