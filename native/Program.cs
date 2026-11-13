using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace ScanDrop.Native
{
    public static class Program
    {
        private const int DefaultPort = 3838;
        private const string DefaultCloudUrl = "https://scandropdone.netlify.app";

        [STAThread]
        public static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            bool selfTest = false;
            bool minimized = false;
            bool noBrowser = false;
            foreach (var a in args)
            {
                if (a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)) selfTest = true;
                if (a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)) minimized = true;
                if (a.Equals("--no-browser", StringComparison.OrdinalIgnoreCase)) noBrowser = true;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string rootDir = baseDir;
            if (!File.Exists(Path.Combine(rootDir, "workspace", "index.html")) &&
                File.Exists(Path.Combine(baseDir, "..", "workspace", "index.html")))
            {
                rootDir = Path.GetFullPath(Path.Combine(baseDir, ".."));
            }

            if (selfTest)
            {
                return RunSelfTest(rootDir);
            }

            try
            {
                bool createdNew;
                using (var mutex = new Mutex(true, "ScanDrop_Native_Station_SingleInstance", out createdNew))
                {
                    if (!createdNew)
                    {
                        try { Process.Start("http://127.0.0.1:" + DefaultPort + "/workspace/"); } catch { }
                        MessageBox.Show("Scan Drop Station is already running (check your system tray).", "Scan Drop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return 0;
                    }

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    // Stop any stale local_agent.js node process holding port 3838
                    StopConflictingLocalAgent();

                    string cloudQrUrl = DefaultCloudUrl + "/shop/quickprint";
                    string shopId = "shop_quickprint_01";
                    string pairCode = "SD-PAIR-7890";

                    // Create server first so it selects the active port cleanly
                    TunnelManager tunnel = null;
                    LocalHttpServer server = null;
                    try
                    {
                        // Temporary placeholder tunnel updated once port is known
                        tunnel = new TunnelManager(rootDir, DefaultPort, DefaultCloudUrl, shopId, pairCode);
                        server = new LocalHttpServer(rootDir, DefaultPort, tunnel);

                        var state = server.LoadState();
                        var shops = state["shops"] as List<object>;
                        if (shops != null && shops.Count > 0)
                        {
                            var s0 = shops[0] as Dictionary<string, object>;
                            if (s0 != null)
                            {
                                shopId = s0.ContainsKey("id") ? Convert.ToString(s0["id"]) : shopId;
                                pairCode = s0.ContainsKey("pairingCode") ? Convert.ToString(s0["pairingCode"]) : pairCode;
                                string slug = s0.ContainsKey("slug") ? Convert.ToString(s0["slug"]) : "quickprint";
                                cloudQrUrl = DefaultCloudUrl + "/shop/" + slug;
                            }
                        }

                        server.Start();
                        int activePort = server.Port;

                        if (activePort != DefaultPort)
                        {
                            tunnel.Dispose();
                            tunnel = new TunnelManager(rootDir, activePort, DefaultCloudUrl, shopId, pairCode);
                            server.SetTunnel(tunnel);
                        }
                        else
                        {
                            tunnel.UpdateIdentity(shopId, pairCode);
                        }

                        ThreadPool.QueueUserWorkItem(_ => tunnel.Start());

                        using (var form = new StationForm(server, tunnel, cloudQrUrl, activePort))
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
                                        try { Process.Start("http://127.0.0.1:" + activePort + "/workspace/"); } catch { }
                                    }
                                };
                            }
                            Application.Run(form);
                        }
                    }
                    finally
                    {
                        if (server != null) server.Dispose();
                        if (tunnel != null) tunnel.Dispose();
                    }
                    return 0;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string logDir = Path.Combine(rootDir, "ScanDrop_Local_Inbox");
                    if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                    File.AppendAllText(Path.Combine(logDir, "startup_error.log"), DateTime.Now.ToString("o") + " " + ex + Environment.NewLine);
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
                    Arguments = "-NoProfile -Command \"Get-CimInstance Win32_Process -Filter \\\"Name='node.exe' AND CommandLine LIKE '%local_agent.js%'\\\" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (var p = Process.Start(psi))
                {
                    if (p != null) p.WaitForExit(2500);
                }
            }
            catch { }
        }

        private static int RunSelfTest(string rootDir)
        {
            Console.WriteLine("Running ScanDrop.exe Self-Test...");
            int testPort = 3939;
            using (var tunnel = new TunnelManager(rootDir, testPort, DefaultCloudUrl, "shop_quickprint_01", "SD-PAIR-7890"))
            using (var server = new LocalHttpServer(rootDir, testPort, tunnel))
            {
                server.Start();
                using (var wc = new WebClient())
                {
                    string health = wc.DownloadString("http://127.0.0.1:" + server.Port + "/api/health");
                    if (!health.Contains("\"storageMode\":\"local-pc\""))
                        throw new Exception("Health check failed: " + health);

                    string ws = wc.DownloadString("http://127.0.0.1:" + server.Port + "/workspace/");
                    if (!ws.Contains("Scan Drop"))
                        throw new Exception("Workspace HTML check failed.");
                }
                Console.WriteLine("PASS: LocalHttpServer /api/health & /workspace/ verified on port " + server.Port);
                return 0;
            }
        }
    }
}
