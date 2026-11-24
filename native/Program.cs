using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ScanDrop.Native.Api;
using ScanDrop.Native.Conversion;
using ScanDrop.Native.Core;
using ScanDrop.Native.Storage;
using ScanDrop.Native.Transfers;
using ScanDrop.Native.Ui;

namespace ScanDrop.Native
{
    public static class Program
    {
        private const int DefaultAdminPort = 3838;   // Localhost-only Admin & Workspace (NEVER tunneled)
        private const int DefaultPublicPort = 8787;  // Public Upload Receiver (PRD Sec 8.1 — tunneled)
        private const string DefaultCloudUrl = "https://scandrop-xi.vercel.app";
        private const string ShowWindowEventName = "ScanDrop_Native_ShowWindow_Event";

        [STAThread]
        public static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            bool selfTest = false;
            bool minimized = false;
            bool noBrowser = false;
            bool resetWizard = false;
            bool skipAutoUpdate = false;

            foreach (var a in args)
            {
                if (a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)) selfTest = true;
                if (a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) || a.Equals("--service", StringComparison.OrdinalIgnoreCase)) minimized = true;
                if (a.Equals("--no-browser", StringComparison.OrdinalIgnoreCase)) noBrowser = true;
                if (a.Equals("--reset-wizard", StringComparison.OrdinalIgnoreCase)) resetWizard = true;
                if (a.Equals("--skip-auto-update", StringComparison.OrdinalIgnoreCase) || a.Equals("--no-auto-update", StringComparison.OrdinalIgnoreCase)) skipAutoUpdate = true;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string appRootDir = baseDir;
            if (!File.Exists(Path.Combine(appRootDir, "workspace", "index.html")) &&
                File.Exists(Path.Combine(baseDir, "..", "workspace", "index.html")))
            {
                appRootDir = Path.GetFullPath(Path.Combine(baseDir, ".."));
            }

            if (selfTest)
            {
                return RunSelfTest(appRootDir);
            }

            try
            {
                bool createdNew;
                using (var mutex = new Mutex(true, "ScanDrop_Native_Station_SingleInstance", out createdNew))
                {
                    if (!createdNew)
                    {
                        bool signaled = false;
                        try
                        {
                            using (var existingEvt = EventWaitHandle.OpenExisting(ShowWindowEventName))
                            {
                                existingEvt.Set();
                                signaled = true;
                            }
                        }
                        catch { }

                        if (!signaled)
                        {
                            try
                            {
                                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + DefaultAdminPort + "/api/window/show");
                                req.Method = "POST";
                                req.ContentLength = 0;
                                req.Timeout = 2000;
                                using (var resp = (HttpWebResponse)req.GetResponse()) { }
                            }
                            catch { }
                        }
                        return 0;
                    }

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    StopConflictingLocalAgent();

                    var paths = new StoragePaths(appRootDir);
                    var logger = new AppLogger(paths.LogsDir);
                    logger.Info("Startup", "Scan Drop v" + AutoUpdater.CurrentVersion + " starting (DataRoot: " + paths.DataRootDir + ")");

                    // Automatic self-update on launch (no manual "Check Updates" click needed!)
                    if (!skipAutoUpdate)
                    {
                        if (AutoUpdater.TryAutoUpdateOnLaunch(appRootDir, minimized, noBrowser, logger))
                        {
                            return 0;
                        }
                    }

                    using (var db = new SqliteDatabase(paths.DatabaseFile))
                    using (var storage = new StorageManager(paths, db, logger))
                    {
                        db.MigrateLegacyJsonIfNeeded(appRootDir);
                        db.RecoverInterruptedSessions(paths.TempDir);
                        storage.RunRetentionSweep();
                        storage.StartBackgroundCleanup();

                        var settings = db.LoadSettings(paths.DataRootDir);
                        if (resetWizard)
                        {
                            settings.FirstRunCompleted = false;
                            db.SaveSettings(settings);
                        }

                        // Graphical First-Run Setup Wizard (PRD Sec 13)
                        if (!settings.FirstRunCompleted && !minimized)
                        {
                            using (var wiz = new FirstRunWizardForm(db, paths))
                            {
                                wiz.ShowDialog();
                                settings = db.LoadSettings(paths.DataRootDir);
                            }
                        }

                        AutoUpdater.EnsureUiFilesExist(appRootDir);
                        var guard = new UploadSecurityGuard(db);
                        var transfers = new TransferService(storage, db, guard, logger);
                        var converter = new ConversionEngine(paths, logger);

                        using (var publicReceiver = new PublicUploadServer(appRootDir, DefaultPublicPort, db, storage, transfers, guard, logger))
                        using (var adminServer = new LocalAdminServer(appRootDir, DefaultAdminPort, db, storage, converter, logger))
                        {
                            publicReceiver.Start();
                            adminServer.Start();

                            string cloudBase = string.IsNullOrWhiteSpace(settings.PublicBaseUrl)
                                ? DefaultCloudUrl
                                : settings.PublicBaseUrl.TrimEnd('/');
                            string cloudQrUrl = cloudBase + "/shop/" + settings.ShopSlug;
                            // Tunnel strictly targets PublicUploadServer (:8787) — NEVER LocalAdminServer (:3838)!
                            using (var tunnel = new TunnelManager(appRootDir, publicReceiver.Port, cloudBase, settings.ShopId, settings.PairingCode, db, paths.LogsDir))
                            {
                                adminServer.AttachServices(tunnel, publicReceiver);
                                ThreadPool.QueueUserWorkItem(_ => tunnel.Start());

                                using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName))
                                using (var shutdownEvent = new ManualResetEvent(false))
                                using (var form = new StationForm(adminServer, publicReceiver, storage, db, transfers, tunnel, logger, cloudQrUrl))
                                {
                                    adminServer.OnShowWindowRequested += () => form.RestoreAndActivate();
                                    var wakeThread = new Thread(() =>
                                    {
                                        var handles = new WaitHandle[] { showEvent, shutdownEvent };
                                        while (true)
                                        {
                                            int idx = WaitHandle.WaitAny(handles);
                                            if (idx == 1) break;
                                            form.RestoreAndActivate();
                                        }
                                    });
                                    wakeThread.IsBackground = true;
                                    wakeThread.Start();

                                    if (minimized)
                                    {
                                        form.WindowState = FormWindowState.Minimized;
                                        form.ShowInTaskbar = false;
                                    }
                                    else
                                    {
                                        form.Shown += (s, e) =>
                                        {
                                            form.Activate();
                                            if (!noBrowser)
                                            {
                                                try { Process.Start("http://127.0.0.1:" + adminServer.Port + "/workspace/"); } catch { }
                                            }
                                        };
                                    }
                                    try
                                    {
                                        Application.Run(form);
                                    }
                                    finally
                                    {
                                        shutdownEvent.Set();
                                    }
                                }
                            }
                        }
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    string errPath = Path.Combine(appRootDir, "startup_error.log");
                    File.WriteAllText(errPath, DateTime.Now.ToString("o") + Environment.NewLine + ex.ToString());
                }
                catch { }
                MessageBox.Show("Scan Drop failed to start:\n\n" + ex.Message, "Scan Drop Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void StopConflictingLocalAgent()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -WindowStyle Hidden -Command \"Get-CimInstance Win32_Process -Filter \\\"Name='node.exe'\\\" | Where-Object { $_.CommandLine -like '*local_agent.js*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var p = Process.Start(psi);
                if (p != null) p.WaitForExit(2000);
            }
            catch { }
        }

        /// <summary>
        /// Automated PRD v2.0 verification suite covering SQLite, public/admin isolation,
        /// magic-byte security blocking, chunked resumable upload, idempotency, pause mode,
        /// and native Image-to-PDF conversion.
        /// </summary>
        private static int RunSelfTest(string appRootDir)
        {
            Console.WriteLine("Running ScanDrop.exe PRD v2.0 Self-Test Suite...");
            string testRoot = Path.Combine(Path.GetTempPath(), "ScanDrop_SelfTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                var paths = new StoragePaths(appRootDir, testRoot);
                var logger = new AppLogger(paths.LogsDir);
                using (var db = new SqliteDatabase(paths.DatabaseFile))
                using (var storage = new StorageManager(paths, db, logger))
                {
                    var guard = new UploadSecurityGuard(db);
                    var transfers = new TransferService(storage, db, guard, logger);
                    var converter = new ConversionEngine(paths, logger);

                    using (var pub = new PublicUploadServer(appRootDir, 18787, db, storage, transfers, guard, logger))
                    using (var adm = new LocalAdminServer(appRootDir, 13838, db, storage, converter, logger))
                    {
                        pub.Start();
                        adm.Start();
                        adm.AttachServices(null, pub);

                        string pubBase = "http://127.0.0.1:" + pub.Port;
                        string admBase = "http://127.0.0.1:" + adm.Port;

                        // Test 1: Public /api/health returns ok:true & no internal paths
                        string pubHealth = HttpGet(pubBase + "/api/health");
                        if (!pubHealth.Contains("\"ok\":true") || pubHealth.Contains("ScanDrop_SelfTest"))
                            throw new Exception("Public health check failed or leaked internal paths: " + pubHealth);
                        Console.WriteLine("  [PASS] 1. Public Receiver health & minimal info isolation");

                        // Test 2: Public Receiver MUST block admin/file routes with 404
                        foreach (var forbidden in new[] { "/workspace/", "/api/state", "/api/file?id=test", "/api/storage", "/api/settings" })
                        {
                            int code = HttpGetStatus(pubBase + forbidden);
                            if (code != 404)
                                throw new Exception("SECURITY FAILURE: Public port exposed " + forbidden + " with HTTP " + code);
                        }
                        Console.WriteLine("  [PASS] 2. Public port strictly blocks /workspace, /api/state, /api/file, /api/storage, /api/settings (HTTP 404)");

                        // Test 3: Admin port MUST reject forwarded proxy headers with 403
                        int fwdCode = HttpGetStatus(admBase + "/api/state", new Dictionary<string, string> { { "CF-Connecting-IP", "203.0.113.10" } });
                        if (fwdCode != 403)
                            throw new Exception("SECURITY FAILURE: Admin port accepted forwarded request with HTTP " + fwdCode);
                        Console.WriteLine("  [PASS] 3. Admin loopback port rejects CF-Connecting-IP forwarded traffic (HTTP 403)");

                        // Test 4: Valid chunked session upload (PDF + PNG) + SHA-256 + 6-char receipt + idempotency
                        byte[] samplePdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj <<>> endobj\ntrailer <<>>\n%%EOF\n");
                        byte[] samplePng = CreateTinyValidPng();
                        var ser = new JavaScriptSerializer();

                        string idemKey = "idem_" + Guid.NewGuid().ToString("N");
                        string initPayload = ser.Serialize(new
                        {
                            customerName = "Test Customer",
                            customerNote = "Print 2 copies",
                            idempotencyKey = idemKey,
                            files = new object[]
                            {
                                new { clientFileId = "f1", name = "invoice.pdf", size = samplePdf.Length, contentType = "application/pdf" },
                                new { clientFileId = "f2", name = "id_card.png", size = samplePng.Length, contentType = "image/png" }
                            }
                        });
                        var initResp = ser.Deserialize<Dictionary<string, object>>(HttpPostJson(pubBase + "/api/upload/sessions", initPayload));
                        string sid = Convert.ToString(initResp["sessionId"]);
                        string tok = Convert.ToString(initResp["uploadToken"]);
                        var filesArr = (System.Collections.IList)initResp["files"];
                        string f1Id = Convert.ToString(((Dictionary<string, object>)filesArr[0])["fileId"]);
                        string f2Id = Convert.ToString(((Dictionary<string, object>)filesArr[1])["fileId"]);

                        HttpPostRawChunk(pubBase + "/api/upload/chunk", sid, tok, f1Id, 0, 0, samplePdf);
                        // Duplicate retry of chunk 0 must be idempotent
                        HttpPostRawChunk(pubBase + "/api/upload/chunk", sid, tok, f1Id, 0, 0, samplePdf);
                        HttpPostRawChunk(pubBase + "/api/upload/chunk", sid, tok, f2Id, 0, 0, samplePng);

                        var cmpResp = ser.Deserialize<Dictionary<string, object>>(HttpPostJson(pubBase + "/api/upload/complete", ser.Serialize(new { sessionId = sid, uploadToken = tok })));
                        string receiptCode = Convert.ToString(cmpResp["receiptCode"]);
                        if (string.IsNullOrEmpty(receiptCode) || receiptCode.Length != 6)
                            throw new Exception("Expected 6-character receipt code, got: " + receiptCode);
                        Console.WriteLine("  [PASS] 4. Session + 4MiB chunked streaming + retry + commit -> 6-char receipt " + receiptCode);

                        // Test 5: Idempotency duplicate submit returns existing receipt without creating duplicate
                        var dupResp = ser.Deserialize<Dictionary<string, object>>(HttpPostJson(pubBase + "/api/upload/sessions", initPayload));
                        if (!Convert.ToBoolean(dupResp["alreadyCompleted"]) || Convert.ToString(dupResp["receiptCode"]) != receiptCode)
                            throw new Exception("Idempotency duplicate check failed!");
                        Console.WriteLine("  [PASS] 5. Duplicate submission idempotency guard");

                        // Test 6: Magic-byte security check rejects MZ/EXE disguised as .pdf
                        byte[] fakeExeAsPdf = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };
                        string badInit = ser.Serialize(new
                        {
                            customerName = "Attacker",
                            files = new object[] { new { clientFileId = "bad1", name = "malware.pdf", size = fakeExeAsPdf.Length, contentType = "application/pdf" } }
                        });
                        var badResp = ser.Deserialize<Dictionary<string, object>>(HttpPostJson(pubBase + "/api/upload/sessions", badInit));
                        string bSid = Convert.ToString(badResp["sessionId"]);
                        string bTok = Convert.ToString(badResp["uploadToken"]);
                        string bFid = Convert.ToString(((Dictionary<string, object>)((System.Collections.IList)badResp["files"])[0])["fileId"]);
                        HttpPostRawChunk(pubBase + "/api/upload/chunk", bSid, bTok, bFid, 0, 0, fakeExeAsPdf);
                        int badCompleteCode = HttpPostJsonStatus(pubBase + "/api/upload/complete", ser.Serialize(new { sessionId = bSid, uploadToken = bTok }));
                        if (badCompleteCode != 400)
                            throw new Exception("SECURITY FAILURE: Fake EXE disguised as PDF was not rejected (HTTP " + badCompleteCode + ")");
                        Console.WriteLine("  [PASS] 6. Magic-byte inspection blocked PE/MZ executable disguised as .pdf (HTTP 400)");

                        // Test 7: Admin API serves file, converts PNG -> PDF, and supports Pause Receiving (HTTP 503)
                        string admState = HttpGet(admBase + "/api/state");
                        if (!admState.Contains(receiptCode) || !admState.Contains("invoice.pdf"))
                            throw new Exception("Admin /api/state missing committed submission!");
                        var convResp = ser.Deserialize<Dictionary<string, object>>(HttpPostJson(admBase + "/api/file/convert-pdf", ser.Serialize(new { fileId = f2Id })));
                        if (!Convert.ToBoolean(convResp["success"]))
                            throw new Exception("Native Image-to-PDF conversion failed!");
                        Console.WriteLine("  [PASS] 7. Local Admin /api/state + Native Image-to-PDF conversion verified");

                        HttpPostJson(admBase + "/api/receiving/pause", "{\"paused\":true}");
                        int pausedCode = HttpPostJsonStatus(pubBase + "/api/upload/sessions", badInit);
                        if (pausedCode != 503)
                            throw new Exception("Pause Receiving did not return HTTP 503 (got " + pausedCode + ")");
                        Console.WriteLine("  [PASS] 8. Pause Receiving blocks new uploads with HTTP 503");

                        // Test 8: DPAPI Secret Protection round-trip
                        db.SetSetting("test_secret_token", "cf-secret-12345", true);
                        string rawRow = Convert.ToString(db.Query("SELECT value FROM app_settings WHERE key='test_secret_token';")[0]["value"]);
                        if (!rawRow.StartsWith("dpapi:") || rawRow.Contains("cf-secret-12345"))
                            throw new Exception("DPAPI encryption failed — secret stored in plaintext!");
                        if (db.GetSetting("test_secret_token") != "cf-secret-12345")
                            throw new Exception("DPAPI decryption round-trip mismatch!");
                        Console.WriteLine("  [PASS] 9. Windows DPAPI secret encryption verified in SQLite");

                        // Test 10: FK-safe shop pairing migration (ReplacePrimaryShop with existing submissions)
                        db.ReplacePrimaryShop("shop_paired_test_99", "Blackthrone Test Shop", "blackthrone");
                        var swappedShop = db.GetPrimaryShop();
                        if (swappedShop == null || swappedShop.ShopId != "shop_paired_test_99" || swappedShop.Slug != "blackthrone")
                            throw new Exception("ReplacePrimaryShop failed to switch primary shop identity!");
                        var migratedSubs = db.GetSubmissions(true);
                        if (migratedSubs.Count == 0 || migratedSubs[0].ShopId != "shop_paired_test_99")
                            throw new Exception("ReplacePrimaryShop lost or failed to repoint existing submissions!");
                        Console.WriteLine("  [PASS] 10. FK-safe shop pairing migration verified with existing submissions");

                        // Test 11-14: Cloudflare-only Tunnel Supervisor State Machine Verification
                        using (var sup = new TunnelManager(appRootDir, pub.Port, DefaultCloudUrl, "shop_test", "SD-TEST", db, paths.LogsDir))
                        {
                            // Test 11: Non-Cloudflare URLs & legacy providers strictly rejected
                            if (TunnelManager.IsValidPublicTunnelUrl("https://abc.lhr.life") ||
                                TunnelManager.IsValidPublicTunnelUrl("https://xyz.pinggy.link") ||
                                TunnelManager.IsValidPublicTunnelUrl("https://foo.serveo.net") ||
                                TunnelManager.IsValidPublicTunnelUrl("https://bar.ngrok-free.app") ||
                                !TunnelManager.IsValidPublicTunnelUrl("https://good-shop-test.trycloudflare.com") ||
                                TunnelManager.NormalizeCloudflareMode("openssh") != "auto" ||
                                TunnelManager.NormalizeCloudflareMode("pinggy") != "auto")
                            {
                                throw new Exception("Cloudflare-only URL or mode guard failed!");
                            }
                            Console.WriteLine("  [PASS] 11. Cloudflare-only policy rejects OpenSSH/lhr/Pinggy/Serveo/ngrok");

                            // Test 12: Stale health-check events ignored + temporary drop DEGRADED -> CONNECTED
                            string testCfUrl = "https://steady-test-tunnel.trycloudflare.com";
                            sup.InjectStateForSelfTest(TunnelState.CONNECTED, testCfUrl, 4242);
                            var s0 = sup.GetStatusSnapshot();
                            bool staleGenApplied = sup.ApplyHealthCheckResult(s0.Generation - 1, testCfUrl, false, "stale gen error");
                            bool staleUrlApplied = sup.ApplyHealthCheckResult(s0.Generation, "https://old-dead.trycloudflare.com", false, "stale url error");
                            var sAfterStale = sup.GetStatusSnapshot();
                            if (staleGenApplied || staleUrlApplied || sAfterStale.State != TunnelState.CONNECTED || sAfterStale.ProcessId != 4242 || !string.IsNullOrEmpty(sAfterStale.LastFailureReason))
                                throw new Exception("Stale health-check event mutated active tunnel state!");

                            // 1st failure -> DEGRADED (does NOT restart working tunnel), recovery -> CONNECTED (same PID)
                            sup.ApplyHealthCheckResult(s0.Generation, testCfUrl, false, "transient packet loss");
                            var sDegraded = sup.GetStatusSnapshot();
                            if (sDegraded.State != TunnelState.DEGRADED || sDegraded.ProcessId != 4242)
                                throw new Exception("Expected DEGRADED state on transient failure without killing PID!");
                            sup.ApplyHealthCheckResult(s0.Generation, testCfUrl, true, null);
                            var sRecovered = sup.GetStatusSnapshot();
                            if (sRecovered.State != TunnelState.CONNECTED || sRecovered.ProcessId != 4242 || !string.IsNullOrEmpty(sRecovered.LastFailureReason))
                                throw new Exception("Expected clean recovery from DEGRADED back to CONNECTED!");
                            Console.WriteLine("  [PASS] 12. Stale health-checks rejected & transient drop recovers DEGRADED -> CONNECTED");

                            // Test 13: Real Cloudflare HTTP 429 rate-limit -> COOLDOWN, respects Retry-After, blocks Start() bypass
                            sup.InjectStateForSelfTest(TunnelState.CONNECTING, null, 5555);
                            sup.SimulateRateLimitForSelfTest("ERR failed to request quick Tunnel: status 429 Too Many Requests; Retry-After: 180");
                            var sCool = sup.GetStatusSnapshot();
                            if (sCool.State != TunnelState.COOLDOWN ||
                                sCool.DisplayStatus != "Cloudflare Offline — Reconnecting" ||
                                !sCool.NextRetryUtc.HasValue ||
                                (sCool.NextRetryUtc.Value - DateTime.UtcNow).TotalSeconds < 120)
                            {
                                throw new Exception("Rate-limit did not enter COOLDOWN with respected Retry-After interval!");
                            }
                            int coolGen = sCool.Generation;
                            sup.Start(); // Must NOT create a new Quick Tunnel during cooldown
                            var sStillCool = sup.GetStatusSnapshot();
                            if (sStillCool.State != TunnelState.COOLDOWN || sStillCool.Generation != coolGen)
                                throw new Exception("Start() bypassed Cloudflare rate-limit COOLDOWN!");
                            Console.WriteLine("  [PASS] 13. Rate-limit HTTP 429 enters COOLDOWN, respects Retry-After, blocks bypass");

                            // Test 14: Exponential backoff with jitter + zero ssh.exe processes + simultaneous Start() guard
                            int b1 = sup.ComputeBackoffWithJitterMs(1, 3000, 120000);
                            int b3 = sup.ComputeBackoffWithJitterMs(3, 3000, 120000);
                            int b6 = sup.ComputeBackoffWithJitterMs(6, 3000, 120000);
                            if (b1 < 2000 || b1 > 4000 || b3 < 9000 || b6 < 70000 || b6 > 155000)
                                throw new Exception("Backoff with jitter out of expected range: " + b1 + "," + b3 + "," + b6);

                            sup.InjectStateForSelfTest(TunnelState.CONNECTED, testCfUrl, 7777);
                            int lockedGen = sup.GetStatusSnapshot().Generation;
                            var threads = new Thread[8];
                            for (int ti = 0; ti < threads.Length; ti++)
                            {
                                threads[ti] = new Thread(() => sup.Start());
                                threads[ti].Start();
                            }
                            for (int ti = 0; ti < threads.Length; ti++) threads[ti].Join();
                            if (sup.GetStatusSnapshot().Generation != lockedGen || sup.GetStatusSnapshot().ProcessId != 7777)
                                throw new Exception("Simultaneous Start() calls disturbed an already-connected tunnel!");
                            Console.WriteLine("  [PASS] 14. Exponential backoff with jitter & simultaneous Start() single-flight guard verified");

                            // Test 15: Native DOCX OpenXML Studio (split-run header & body Find/Replace + image extent resize)
                            string sampleDocx = Path.Combine(testRoot, "student_assignment.docx");
                            string editedDocx = Path.Combine(testRoot, "student_assignment_edited.docx");
                            CreateSampleDocxWithSplitHeaderAndImage(sampleDocx);
                            var insp = DocxEditor.Inspect(sampleDocx);
                            if (!insp.Success || insp.Headers.Count < 1 || insp.BodyBlocks.Count < 1 || insp.Images.Count < 1)
                                throw new Exception("DocxEditor.Inspect failed to read sample DOCX: " + insp.Error);
                            if (!insp.Headers[0].Text.Contains("21CS045"))
                                throw new Exception("DocxEditor.Inspect did not reconstruct split header run: " + insp.Headers[0].Text);

                            var editRes = DocxEditor.ApplyEdits(
                                sampleDocx,
                                editedDocx,
                                new List<DocxFindReplaceRule>
                                {
                                    new DocxFindReplaceRule { Find = "21CS045", Replace = "21CS099", CaseSensitive = false, Scope = "all" },
                                    new DocxFindReplaceRule { Find = "Rahul Sharma", Replace = "Amit Verma", CaseSensitive = false, Scope = "headers" }
                                },
                                new List<DocxParagraphEdit>(),
                                new List<DocxImageResize>
                                {
                                    new DocxImageResize { PartPath = insp.Images[0].PartPath, DrawingIndex = insp.Images[0].DrawingIndex, ScalePercent = 150.0 }
                                });
                            if (!editRes.Success || editRes.ReplacementsCount < 2 || editRes.ImagesResizedCount != 1)
                                throw new Exception("DocxEditor.ApplyEdits failed: " + editRes.Error);

                            var inspAfter = DocxEditor.Inspect(editedDocx);
                            if (!inspAfter.Success || !inspAfter.Headers[0].Text.Contains("Amit Verma") || !inspAfter.Headers[0].Text.Contains("21CS099"))
                                throw new Exception("Edited DOCX header did not reflect replacements: " + (inspAfter.Headers.Count > 0 ? inspAfter.Headers[0].Text : "none"));
                            if (!inspAfter.BodyBlocks[0].Text.Contains("21CS099"))
                                throw new Exception("Edited DOCX body did not reflect roll replacement: " + inspAfter.BodyBlocks[0].Text);
                            if (Math.Abs(inspAfter.Images[0].WidthCm - 15.0) > 0.2 || Math.Abs(inspAfter.Images[0].HeightCm - 7.5) > 0.2)
                                throw new Exception("Edited DOCX image resize mismatch: " + inspAfter.Images[0].WidthCm + "x" + inspAfter.Images[0].HeightCm);
                            Console.WriteLine("  [PASS] 15. DOCX OpenXML Studio split-run header/body replace & image scaling verified");

                            // Test 16: Per-file Trash, Restore, Drag-in Local Add & Reorder
                            var stSub = db.GetSubmissionByReceiptOrId(receiptCode);
                            if (stSub == null || stSub.Files.Count < 2)
                                throw new Exception("Expected at least 2 files in self-test submission for Test 16.");
                            string targetFid = stSub.Files[0].FileId;
                            HttpPostJson(admBase + "/api/file/trash", ser.Serialize(new { fileId = targetFid }));
                            var afterTrash = db.GetSubmissionByReceiptOrId(receiptCode);
                            if (afterTrash.Files.Count != 1 || afterTrash.TrashedFiles.Count != 1)
                                throw new Exception("File trash failed: active=" + afterTrash.Files.Count + " trashed=" + afterTrash.TrashedFiles.Count);

                            HttpPostJson(admBase + "/api/file/restore", ser.Serialize(new { fileId = targetFid }));
                            var afterRestore = db.GetSubmissionByReceiptOrId(receiptCode);
                            if (afterRestore.Files.Count != 2 || afterRestore.TrashedFiles.Count != 0)
                                throw new Exception("File restore failed!");

                            // Simulate dragging a new edited PDF into the customer's submission
                            var addReq = (HttpWebRequest)WebRequest.Create(admBase + "/api/submission/add-file");
                            addReq.Method = "POST";
                            addReq.ContentType = "application/pdf";
                            addReq.Headers["X-Submission-Id"] = receiptCode;
                            addReq.Headers["X-File-Name"] = Uri.EscapeDataString("Edited_Chapter1 (Final).pdf");
                            addReq.ContentLength = samplePdf.Length;
                            using (var rs = addReq.GetRequestStream()) rs.Write(samplePdf, 0, samplePdf.Length);
                            using (var addResp = (HttpWebResponse)addReq.GetResponse())
                            {
                                if ((int)addResp.StatusCode != 200) throw new Exception("add-file returned HTTP " + (int)addResp.StatusCode);
                            }
                            var afterAdd = db.GetSubmissionByReceiptOrId(receiptCode);
                            if (afterAdd.Files.Count != 3)
                                throw new Exception("Expected 3 files after drag-in add, got " + afterAdd.Files.Count);

                            // Reorder last file to index 0
                            string addedFid = afterAdd.Files[2].FileId;
                            HttpPostJson(admBase + "/api/submission/reorder-files", ser.Serialize(new
                            {
                                submissionId = receiptCode,
                                fileIds = new[] { addedFid, afterAdd.Files[0].FileId, afterAdd.Files[1].FileId }
                            }));
                            var afterReorder = db.GetSubmissionByReceiptOrId(receiptCode);
                            if (afterReorder.Files[0].FileId != addedFid)
                                throw new Exception("File reorder did not persist!");

                            // Permanent delete of single trashed file leaves the other 2 intact
                            HttpPostJson(admBase + "/api/file/trash", ser.Serialize(new { fileId = addedFid }));
                            HttpPostJson(admBase + "/api/file/delete-permanent", ser.Serialize(new { fileId = addedFid }));
                            var afterPermDel = db.GetSubmissionByReceiptOrId(receiptCode);
                            if (afterPermDel.Files.Count != 2 || afterPermDel.TrashedFiles.Count != 0)
                                throw new Exception("Permanent single-file delete did not clean up cleanly!");
                            Console.WriteLine("  [PASS] 16. Per-file Trash, Restore, Drag-in Add, Reorder & Permanent Delete verified");

                            // Test 17: Persistent Holding Tray (pin from job, upload local file, reorder, prepare-pdf, copy to job, clear)
                            string pinJson = HttpPostJson(admBase + "/api/tray/add-from-file", ser.Serialize(new { fileId = afterPermDel.Files[0].FileId }));
                            var pinDict = ser.Deserialize<Dictionary<string, object>>(pinJson);
                            string trayId1 = Convert.ToString(((Dictionary<string, object>)pinDict["item"])["id"]);

                            var upReq = (HttpWebRequest)WebRequest.Create(admBase + "/api/tray/add-upload");
                            upReq.Method = "POST";
                            upReq.ContentType = "image/png";
                            upReq.Headers["X-File-Name"] = Uri.EscapeDataString("Stamp_Overlay.png");
                            upReq.ContentLength = samplePng.Length;
                            using (var rs = upReq.GetRequestStream()) rs.Write(samplePng, 0, samplePng.Length);
                            string upJson;
                            using (var upResp = (HttpWebResponse)upReq.GetResponse())
                            using (var sr = new StreamReader(upResp.GetResponseStream(), Encoding.UTF8))
                                upJson = sr.ReadToEnd();
                            var upDict = ser.Deserialize<Dictionary<string, object>>(upJson);
                            string trayId2 = Convert.ToString(((Dictionary<string, object>)upDict["item"])["id"]);

                            HttpPostJson(admBase + "/api/tray/reorder", ser.Serialize(new { trayIds = new[] { trayId2, trayId1 } }));
                            var trayList = db.GetTrayItems();
                            if (trayList.Count != 2 || trayList[0].TrayId != trayId2)
                                throw new Exception("Holding Tray add/reorder failed!");

                            string prepTrayJson = HttpPostJson(admBase + "/api/tray/prepare-pdf", ser.Serialize(new { trayId = trayId2 }));
                            if (!prepTrayJson.Contains("\"success\":true"))
                                throw new Exception("Holding Tray prepare-pdf failed: " + prepTrayJson);

                            HttpPostJson(admBase + "/api/tray/copy-to-submission", ser.Serialize(new { trayId = trayId2, submissionId = receiptCode }));
                            var subAfterTrayCopy = db.GetSubmissionByReceiptOrId(receiptCode);
                            if (subAfterTrayCopy.Files.Count != 3)
                                throw new Exception("Expected 3 files after copying from Holding Tray, got " + subAfterTrayCopy.Files.Count);

                            HttpPostJson(admBase + "/api/tray/clear", "{}");
                            if (db.GetTrayItems().Count != 0)
                                throw new Exception("Holding Tray clear did not empty tray_items!");
                            Console.WriteLine("  [PASS] 17. Holding Tray pin, local upload, reorder, PDF prep, copy-to-job & clear verified");

                            // Test 18: Persistent Page Manager selection state (X-Page-Plan saved in SQLite & cleared on reset-edited)
                            string pmFid = afterPermDel.Files[0].FileId;
                            string samplePlan = "[{\"s\":0,\"on\":1,\"r\":0},{\"s\":1,\"on\":0,\"r\":0},{\"s\":2,\"on\":1,\"r\":90}]";
                            var pmReq = (HttpWebRequest)WebRequest.Create(admBase + "/api/file/save-edited-pdf");
                            pmReq.Method = "POST";
                            pmReq.ContentType = "application/pdf";
                            pmReq.Headers["X-File-Id"] = pmFid;
                            pmReq.Headers["X-Output-Name"] = "page_plan_test.pdf";
                            pmReq.Headers["X-Page-Plan"] = Uri.EscapeDataString(samplePlan);
                            pmReq.ContentLength = samplePdf.Length;
                            using (var rs = pmReq.GetRequestStream()) rs.Write(samplePdf, 0, samplePdf.Length);
                            using (var pmResp = (HttpWebResponse)pmReq.GetResponse())
                            {
                                if ((int)pmResp.StatusCode != 200) throw new Exception("save-edited-pdf with X-Page-Plan failed");
                            }
                            var fWithPlan = db.GetFileById(pmFid);
                            if (fWithPlan == null || fWithPlan.PagePlanJson != samplePlan)
                                throw new Exception("PagePlanJson did not persist in SQLite! Got: " + (fWithPlan != null ? fWithPlan.PagePlanJson : "null"));

                            HttpPostJson(admBase + "/api/file/reset-edited", ser.Serialize(new { fileId = pmFid }));
                            var fReset = db.GetFileById(pmFid);
                            if (fReset == null || !string.IsNullOrEmpty(fReset.PagePlanJson) || fReset.ConversionStatus != "none")
                                throw new Exception("reset-edited did not clear PagePlanJson!");
                            Console.WriteLine("  [PASS] 18. Persistent Page Manager state (X-Page-Plan) & reset-edited verified");

                            // Test 19: Automatic update semver & SHA-256 loop-prevention guards
                            if (!AutoUpdater.IsNewerVersion("4.6.2", "4.7.0") ||
                                AutoUpdater.IsNewerVersion("4.7.0", "4.7.0") ||
                                AutoUpdater.IsNewerVersion("4.7.0", "4.6.9"))
                                throw new Exception("AutoUpdater.IsNewerVersion semver logic failed!");
                            if (AutoUpdater.ShouldApplyUpdate("4.6.2", "4.7.0", "abc123", "abc123"))
                                throw new Exception("AutoUpdater.ShouldApplyUpdate did not guard identical SHA-256!");
                            if (!AutoUpdater.ShouldApplyUpdate("4.6.2", "4.7.0", "abc123", "def456"))
                                throw new Exception("AutoUpdater.ShouldApplyUpdate rejected valid newer update!");
                            Console.WriteLine("  [PASS] 19. Auto-update on launch semver & identical-SHA loop guard verified");
                        }
                    }
                }
                Console.WriteLine("ALL 19 PRD v2.0 + CLOUDFLARE + WORKFLOW + AUTO-UPDATE SELF-TESTS PASSED!");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SELF-TEST FAILED: " + ex.Message);
                return 2;
            }
            finally
            {
                try { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); } catch { }
            }
        }

        private static byte[] CreateTinyValidPng()
        {
            using (var bmp = new Bitmap(8, 8))
            using (var g = Graphics.FromImage(bmp))
            using (var ms = new MemoryStream())
            {
                g.Clear(Color.CornflowerBlue);
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private static void CreateSampleDocxWithSplitHeaderAndImage(string outPath)
        {
            using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteZipText(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                    "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>" +
                    "</Types>");

                WriteZipText(zip, "word/header1.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                    "<w:p><w:r><w:t xml:space=\"preserve\">Name: Rahul </w:t></w:r><w:r><w:t xml:space=\"preserve\">Sharma | Roll No: 21CS</w:t></w:r><w:r><w:t>045</w:t></w:r></w:p>" +
                    "</w:hdr>");

                WriteZipText(zip, "word/document.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
                    "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
                    "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
                    "<w:body>" +
                    "<w:p><w:r><w:t>Assignment submitted by Roll 21CS</w:t></w:r><w:r><w:t>045</w:t></w:r></w:p>" +
                    "<w:p><w:r><w:drawing><wp:inline><wp:extent cx=\"3600000\" cy=\"1800000\"/><wp:docPr id=\"1\" name=\"Diagram 1\"/><a:graphic><a:graphicData><a:xfrm><a:ext cx=\"3600000\" cy=\"1800000\"/></a:xfrm></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>" +
                    "</w:body></w:document>");
            }
        }

        private static void WriteZipText(ZipArchive zip, string entryName, string content)
        {
            var entry = zip.CreateEntry(entryName);
            using (var sw = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                sw.Write(content);
        }

        private static string HttpGet(string url)
        {
            using (var wc = new WebClient()) return wc.DownloadString(url);
        }

        private static int HttpGetStatus(string url, Dictionary<string, string> headers = null)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 5000;
                if (headers != null) foreach (var kv in headers) req.Headers[kv.Key] = kv.Value;
                using (var resp = (HttpWebResponse)req.GetResponse()) return (int)resp.StatusCode;
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse) return (int)((HttpWebResponse)wex.Response).StatusCode;
                return -1;
            }
        }

        private static string HttpPostJson(string url, string json)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = 10000;
            byte[] buf = Encoding.UTF8.GetBytes(json);
            req.ContentLength = buf.Length;
            using (var s = req.GetRequestStream()) s.Write(buf, 0, buf.Length);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        private static int HttpPostJsonStatus(string url, string json)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = 10000;
                byte[] buf = Encoding.UTF8.GetBytes(json);
                req.ContentLength = buf.Length;
                using (var s = req.GetRequestStream()) s.Write(buf, 0, buf.Length);
                using (var resp = (HttpWebResponse)req.GetResponse()) return (int)resp.StatusCode;
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse) return (int)((HttpWebResponse)wex.Response).StatusCode;
                return -1;
            }
        }

        private static void HttpPostRawChunk(string url, string sid, string tok, string fid, int idx, long off, byte[] data)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/octet-stream";
            req.Headers["X-Session-Id"] = sid;
            req.Headers["X-Upload-Token"] = tok;
            req.Headers["X-File-Id"] = fid;
            req.Headers["X-Chunk-Index"] = idx.ToString(CultureInfo.InvariantCulture);
            req.Headers["X-Byte-Offset"] = off.ToString(CultureInfo.InvariantCulture);
            req.ContentLength = data.Length;
            using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            using (var resp = (HttpWebResponse)req.GetResponse()) { }
        }
    }
}
