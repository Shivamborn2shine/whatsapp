using System;
using System.Collections.Generic;
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
            foreach (var a in args)
            {
                if (a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)) selfTest = true;
                if (a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)) minimized = true;
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

            bool createdNew;
            using (var mutex = new Mutex(true, "ScanDrop_Native_Station_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Scan Drop Station is already running in the system tray.", "Scan Drop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                string cloudQrUrl = DefaultCloudUrl + "/shop/quickprint";
                using (var tunnel = new TunnelManager(rootDir, DefaultPort, DefaultCloudUrl, "shop_quickprint_01", "SD-PAIR-7890"))
                using (var server = new LocalHttpServer(rootDir, DefaultPort, tunnel))
                {
                    var state = server.LoadState();
                    var shops = state["shops"] as List<object>;
                    if (shops != null && shops.Count > 0)
                    {
                        var s0 = shops[0] as Dictionary<string, object>;
                        if (s0 != null)
                        {
                            string id = s0.ContainsKey("id") ? Convert.ToString(s0["id"]) : "shop_quickprint_01";
                            string pair = s0.ContainsKey("pairingCode") ? Convert.ToString(s0["pairingCode"]) : "SD-PAIR-7890";
                            string slug = s0.ContainsKey("slug") ? Convert.ToString(s0["slug"]) : "quickprint";
                            cloudQrUrl = DefaultCloudUrl + "/shop/" + slug;
                            tunnel.UpdateIdentity(id, pair);
                        }
                    }

                    server.Start();
                    ThreadPool.QueueUserWorkItem(_ => tunnel.Start());

                    using (var form = new StationForm(server, tunnel, cloudQrUrl, DefaultPort))
                    {
                        if (minimized)
                        {
                            form.WindowState = FormWindowState.Minimized;
                            form.ShowInTaskbar = false;
                        }
                        Application.Run(form);
                    }
                }
                return 0;
            }
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
                    string health = wc.DownloadString("http://127.0.0.1:" + testPort + "/api/health");
                    if (!health.Contains("\"storageMode\":\"local-pc\""))
                        throw new Exception("Health check failed: " + health);

                    string ws = wc.DownloadString("http://127.0.0.1:" + testPort + "/workspace/");
                    if (!ws.Contains("Scan Drop"))
                        throw new Exception("Workspace HTML check failed.");
                }
                Console.WriteLine("PASS: LocalHttpServer /api/health & /workspace/ verified.");
                return 0;
            }
        }
    }
}
