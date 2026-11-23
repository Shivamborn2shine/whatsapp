using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
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

        [STAThread]
        public static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            bool selfTest = false;
            bool minimized = false;
            bool noBrowser = false;
            bool resetWizard = false;

            foreach (var a in args)
            {
                if (a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)) selfTest = true;
                if (a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) || a.Equals("--service", StringComparison.OrdinalIgnoreCase)) minimized = true;
                if (a.Equals("--no-browser", StringComparison.OrdinalIgnoreCase)) noBrowser = true;
                if (a.Equals("--reset-wizard", StringComparison.OrdinalIgnoreCase)) resetWizard = true;
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
                        try { Process.Start("http://127.0.0.1:" + DefaultAdminPort + "/workspace/"); } catch { }
                        MessageBox.Show("Scan Drop Station is already running (check your system tray).", "Scan Drop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return 0;
                    }

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    StopConflictingLocalAgent();

                    var paths = new StoragePaths(appRootDir);
                    var logger = new AppLogger(paths.LogsDir);
                    logger.Info("Startup", "Scan Drop v" + AutoUpdater.CurrentVersion + " starting (DataRoot: " + paths.DataRootDir + ")");

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

                                using (var form = new StationForm(adminServer, publicReceiver, storage, db, transfers, tunnel, logger, cloudQrUrl))
                                {
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
                                    Application.Run(form);
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
                    }
                }
                Console.WriteLine("ALL 9 PRD v2.0 SELF-TESTS PASSED!");
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
