using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace ScanDrop.Native
{
    public sealed class TunnelManager : IDisposable
    {
        private static readonly Regex CloudflareUrlRegex = new Regex(
            @"https://(?!api\.|pkg\.|update\.|staging\.)([a-zA-Z0-9-]{5,}\.trycloudflare\.com)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SshUrlRegex = new Regex(
            @"https://[a-zA-Z0-9-]+\.lhr\.(life|rocks|pro)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly string _rootDir;
        private readonly int _localPort;
        private readonly string _cloudUrl;
        private string _shopId;
        private string _pairingCode;
        private readonly object _lock = new object();

        private Process _tunnelProcess;
        private Timer _heartbeatTimer;
        private string _liveTunnelUrl;
        private string _activeProviderName = "Initializing";
        private bool _disposed;
        private bool _isSwitchingToFallback;

        public event Action<string> OnTunnelUrlCaptured;
        public event Action<string> OnStatusMessage;

        public string LiveTunnelUrl { get { return _liveTunnelUrl; } }
        public string ActiveProviderName { get { return _activeProviderName; } }

        public TunnelManager(string rootDir, int localPort, string cloudUrl, string shopId, string pairingCode)
        {
            _rootDir = rootDir;
            _localPort = localPort;
            _cloudUrl = (cloudUrl ?? "https://scandropdone.netlify.app").TrimEnd('/');
            _shopId = string.IsNullOrWhiteSpace(shopId) ? "shop_quickprint_01" : shopId.Trim();
            _pairingCode = string.IsNullOrWhiteSpace(pairingCode) ? "SD-PAIR-7890" : pairingCode.Trim();
        }

        public void UpdateIdentity(string shopId, string pairingCode)
        {
            if (!string.IsNullOrWhiteSpace(shopId)) _shopId = shopId.Trim();
            if (!string.IsNullOrWhiteSpace(pairingCode)) _pairingCode = pairingCode.Trim();
            if (!string.IsNullOrEmpty(_liveTunnelUrl))
            {
                RegisterWithCloud(_liveTunnelUrl);
            }
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_disposed) return;
                KillOrphanTunnelProcesses();
                if (_tunnelProcess != null && !_tunnelProcess.HasExited) return;

                string token = null;
                string customUrl = null;
                string providerMode = "auto";

                try
                {
                    string cfgPath = Path.Combine(_rootDir, "config", "scandrop.json");
                    if (!File.Exists(cfgPath)) cfgPath = Path.Combine(_rootDir, "scandrop.json");
                    if (File.Exists(cfgPath))
                    {
                        var ser = new JavaScriptSerializer();
                        var dict = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(cfgPath));
                        if (dict != null)
                        {
                            if (dict.ContainsKey("cloudflareTunnelToken") && dict["cloudflareTunnelToken"] != null)
                                token = Convert.ToString(dict["cloudflareTunnelToken"]).Trim();
                            if (dict.ContainsKey("customTunnelUrl") && dict["customTunnelUrl"] != null)
                                customUrl = Convert.ToString(dict["customTunnelUrl"]).Trim();
                            if (dict.ContainsKey("tunnelProvider") && dict["tunnelProvider"] != null)
                                providerMode = Convert.ToString(dict["tunnelProvider"]).Trim().ToLowerInvariant();
                        }
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(token))
                    token = Environment.GetEnvironmentVariable("CLOUDFLARE_TUNNEL_TOKEN");
                if (string.IsNullOrWhiteSpace(customUrl))
                    customUrl = Environment.GetEnvironmentVariable("CUSTOM_TUNNEL_URL");

                if (!string.IsNullOrWhiteSpace(customUrl) && (providerMode == "custom" || string.IsNullOrWhiteSpace(token)))
                {
                    _activeProviderName = "Custom Domain";
                    _liveTunnelUrl = customUrl;
                    Log("Custom static tunnel configured: " + customUrl);
                    RegisterWithCloud(customUrl);
                    FireCaptured(customUrl);
                    StartHeartbeatTimer();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(token))
                {
                    StartCloudflareNamedTunnel(token, customUrl);
                    return;
                }

                if (providerMode == "openssh")
                {
                    StartOpenSshTunnel();
                    return;
                }

                StartCloudflareQuickTunnel();
            }
        }

        private void StartCloudflareNamedTunnel(string token, string staticUrl)
        {
            string exePath = EnsureCloudflared();
            if (string.IsNullOrEmpty(exePath))
            {
                Log("cloudflared unavailable. Falling back to OpenSSH tunnel...");
                StartOpenSshTunnel();
                return;
            }

            try
            {
                _activeProviderName = "Cloudflare Named Tunnel";
                Log("Starting Cloudflare Named Tunnel...");
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "tunnel run --token " + token,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };

                _tunnelProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _tunnelProcess.Exited += (s, e) =>
                {
                    if (!_disposed)
                    {
                        Log("Cloudflare Named Tunnel exited. Reconnecting in 10s...");
                        ScheduleRestart(10000);
                    }
                };
                _tunnelProcess.Start();
                _tunnelProcess.BeginOutputReadLine();
                _tunnelProcess.BeginErrorReadLine();

                if (!string.IsNullOrWhiteSpace(staticUrl))
                {
                    _liveTunnelUrl = staticUrl;
                    RegisterWithCloud(staticUrl);
                    FireCaptured(staticUrl);
                }
                StartHeartbeatTimer();
            }
            catch (Exception ex)
            {
                Log("Named tunnel error: " + ex.Message + ". Falling back to OpenSSH...");
                StartOpenSshTunnel();
            }
        }

        private void StartCloudflareQuickTunnel()
        {
            string exePath = EnsureCloudflared();
            if (string.IsNullOrEmpty(exePath))
            {
                Log("cloudflared not found. Activating built-in OpenSSH tunnel...");
                StartOpenSshTunnel();
                return;
            }

            try
            {
                _activeProviderName = "Cloudflare Quick Tunnel";
                _isSwitchingToFallback = false;
                Log("Connecting Cloudflare HTTP/2 Quick Tunnel -> 127.0.0.1:" + _localPort + "...");

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = string.Format(CultureInfo.InvariantCulture, "tunnel --protocol http2 --no-autoupdate --url http://127.0.0.1:{0}", _localPort),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };

                _tunnelProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _tunnelProcess.OutputDataReceived += HandleCloudflareOutput;
                _tunnelProcess.ErrorDataReceived += HandleCloudflareOutput;
                _tunnelProcess.Exited += (s, e) =>
                {
                    if (_disposed || _isSwitchingToFallback) return;
                    if (string.IsNullOrEmpty(_liveTunnelUrl))
                    {
                        Log("Cloudflare Quick Tunnel failed/rate-limited. Switching to OpenSSH fallback...");
                        _isSwitchingToFallback = true;
                        ThreadPool.QueueUserWorkItem(_ => StartOpenSshTunnel());
                    }
                    else
                    {
                        Log("Cloudflare Tunnel disconnected. Reconnecting in 8s...");
                        _liveTunnelUrl = null;
                        ScheduleRestart(8000);
                    }
                };

                _tunnelProcess.Start();
                _tunnelProcess.BeginOutputReadLine();
                _tunnelProcess.BeginErrorReadLine();
                StartHeartbeatTimer();
            }
            catch (Exception ex)
            {
                Log("Cloudflare start error: " + ex.Message + ". Switching to OpenSSH...");
                StartOpenSshTunnel();
            }
        }

        private void HandleCloudflareOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var match = CloudflareUrlRegex.Match(e.Data);
            if (match.Success)
            {
                string captured = match.Value.Trim();
                if (captured != _liveTunnelUrl)
                {
                    _liveTunnelUrl = captured;
                    Log("⚡ Tunnel Live (Cloudflare): " + captured);
                    RegisterWithCloud(captured);
                    FireCaptured(captured);
                }
            }
        }

        private void StartOpenSshTunnel()
        {
            lock (_lock)
            {
                if (_disposed) return;
                try { if (_tunnelProcess != null && !_tunnelProcess.HasExited) _tunnelProcess.Kill(); } catch { }

                string sshExe = FindOpenSsh();
                _activeProviderName = "OpenSSH (localhost.run)";
                Log("Starting OpenSSH Tunnel (" + sshExe + ")...");

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = sshExe,
                        Arguments = string.Format(CultureInfo.InvariantCulture,
                            "-o StrictHostKeyChecking=no -o ServerAliveInterval=30 -R 80:127.0.0.1:{0} nokey@localhost.run", _localPort),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    _tunnelProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    _tunnelProcess.OutputDataReceived += HandleSshOutput;
                    _tunnelProcess.ErrorDataReceived += HandleSshOutput;
                    _tunnelProcess.Exited += (s, e) =>
                    {
                        if (_disposed) return;
                        Log("OpenSSH Tunnel exited. Reconnecting in 10s...");
                        _liveTunnelUrl = null;
                        ScheduleRestart(10000);
                    };

                    _tunnelProcess.Start();
                    _tunnelProcess.BeginOutputReadLine();
                    _tunnelProcess.BeginErrorReadLine();
                    StartHeartbeatTimer();
                }
                catch (Exception ex)
                {
                    Log("OpenSSH Tunnel error: " + ex.Message);
                }
            }
        }

        private void HandleSshOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var match = SshUrlRegex.Match(e.Data);
            if (match.Success)
            {
                string captured = match.Value.Trim();
                if (captured != _liveTunnelUrl)
                {
                    _liveTunnelUrl = captured;
                    Log("⚡ Tunnel Live (OpenSSH): " + captured);
                    RegisterWithCloud(captured);
                    FireCaptured(captured);
                }
            }
        }

        private string FindOpenSsh()
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string sysNative = Path.Combine(winDir, "sysnative", "OpenSSH", "ssh.exe");
            if (File.Exists(sysNative)) return sysNative;
            string sys32 = Path.Combine(winDir, "System32", "OpenSSH", "ssh.exe");
            if (File.Exists(sys32)) return sys32;
            return "ssh.exe";
        }

        private string EnsureCloudflared()
        {
            string binDir = Path.Combine(_rootDir, "bin");
            if (!Directory.Exists(binDir)) Directory.CreateDirectory(binDir);
            string exePath = Path.Combine(binDir, "cloudflared.exe");
            if (File.Exists(exePath)) return exePath;

            string rootExe = Path.Combine(_rootDir, "cloudflared.exe");
            if (File.Exists(rootExe)) return rootExe;

            Log("Downloading official cloudflared.exe (one-time setup)...");
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "ScanDrop-NativeAgent");
                    wc.DownloadFile("https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe", exePath);
                }
                Log("Downloaded cloudflared.exe successfully.");
                return exePath;
            }
            catch (Exception ex)
            {
                Log("Could not download cloudflared.exe: " + ex.Message);
                return null;
            }
        }

        private void StartHeartbeatTimer()
        {
            if (_heartbeatTimer != null) return;
            _heartbeatTimer = new Timer(_ =>
            {
                if (!string.IsNullOrWhiteSpace(_liveTunnelUrl))
                {
                    RegisterWithCloud(_liveTunnelUrl);
                }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private void RegisterWithCloud(string tunnelUrl)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                var ser = new JavaScriptSerializer();
                string payload = ser.Serialize(new
                {
                    shopId = _shopId,
                    pairingCode = _pairingCode,
                    tunnelUrl = tunnelUrl
                });

                var req = (HttpWebRequest)WebRequest.Create(_cloudUrl + "/api/tunnel/register");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = 10000;
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    // Registered cleanly
                }
            }
            catch (Exception ex)
            {
                Log("Cloud registry heartbeat warning: " + ex.Message);
            }
        }

        private void DisconnectFromCloud()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                var ser = new JavaScriptSerializer();
                string payload = ser.Serialize(new { shopId = _shopId, pairingCode = _pairingCode });
                var req = (HttpWebRequest)WebRequest.Create(_cloudUrl + "/api/tunnel/disconnect");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = 4000;
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                using (var resp = (HttpWebResponse)req.GetResponse()) { }
            }
            catch { }
        }

        private void KillOrphanTunnelProcesses()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("cloudflared"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }
        }

        private void ScheduleRestart(int delayMs)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(delayMs);
                if (!_disposed) Start();
            });
        }

        public void Restart()
        {
            Log("Restarting tunnel...");
            StopProcess();
            _liveTunnelUrl = null;
            Start();
        }

        private void StopProcess()
        {
            try
            {
                if (_tunnelProcess != null && !_tunnelProcess.HasExited)
                    _tunnelProcess.Kill();
            }
            catch { }
        }

        private void FireCaptured(string url)
        {
            var h = OnTunnelUrlCaptured;
            if (h != null) try { h(url); } catch { }
        }

        private void Log(string msg)
        {
            var h = OnStatusMessage;
            if (h != null) try { h(msg); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_heartbeatTimer != null) { _heartbeatTimer.Dispose(); _heartbeatTimer = null; }
            StopProcess();
            DisconnectFromCloud();
        }
    }
}
