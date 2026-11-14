using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ScanDrop.Native
{
    public sealed class TunnelManager : IDisposable
    {
        private static readonly Regex CloudflareUrlRegex = new Regex(
            @"https://(?!api\.|pkg\.|update\.|staging\.|www\.)([a-zA-Z0-9-]{5,}\.trycloudflare\.com)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SshUrlRegex = new Regex(
            @"https://[a-zA-Z0-9.-]+\.lhr\.(?:life|rocks|pro)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PinggyUrlRegex = new Regex(
            @"https://[a-zA-Z0-9.-]+\.(?:free\.pinggy\.link|pinggy\.link|pinggy\.io)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly string _rootDir;
        private readonly int _localPort;
        private readonly string _cloudUrl;
        private string _shopId;
        private string _pairingCode;
        private readonly object _lock = new object();

        private Process _tunnelProcess;
        private Timer _heartbeatTimer;
        private Timer _watchdogTimer;
        private string _liveTunnelUrl;
        private string _activeProviderName = "Initializing";
        private string _healthStatus = "Connecting";
        private DateTime? _lastHealthOkUtc;
        private DateTime _cloudflareCooldownUntilUtc = DateTime.MinValue;
        private DateTime _localhostRunCooldownUntilUtc = DateTime.MinValue;
        private DateTime _pinggyCooldownUntilUtc = DateTime.MinValue;
        private int _generation;
        private int _consecutiveHealthFailures;
        private bool _disposed;
        private bool _isSwitchingToFallback;
        private volatile bool _probeInFlight;
        private bool _eventsHooked;

        public event Action<string> OnTunnelUrlCaptured;
        public event Action<string> OnStatusMessage;

        public string LiveTunnelUrl { get { return _liveTunnelUrl; } }
        public string ActiveProviderName { get { return _activeProviderName; } }
        public string CurrentHealthStatus { get { return _healthStatus; } }
        public DateTime? LastHealthOkUtc { get { return _lastHealthOkUtc; } }

        public TunnelManager(string rootDir, int localPort, string cloudUrl, string shopId, string pairingCode)
        {
            _rootDir = rootDir;
            _localPort = localPort;
            _cloudUrl = (cloudUrl ?? "https://scandropdone.netlify.app").TrimEnd('/');
            _shopId = string.IsNullOrWhiteSpace(shopId) ? "shop_quickprint_01" : shopId.Trim();
            _pairingCode = string.IsNullOrWhiteSpace(pairingCode) ? "SD-PAIR-7890" : pairingCode.Trim();

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.DefaultConnectionLimit = 64;
            HookSystemRecoveryEvents();
        }

        private void HookSystemRecoveryEvents()
        {
            if (_eventsHooked) return;
            _eventsHooked = true;
            try
            {
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            }
            catch { }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (_disposed) return;
            if (e.Mode == PowerModes.Resume)
            {
                Log("PC resumed from sleep. Verifying tunnel connectivity...");
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Thread.Sleep(2500);
                    if (_disposed) return;
                    if (string.IsNullOrEmpty(_liveTunnelUrl)) Start();
                    else RunWatchdogCheck();
                });
            }
        }

        private void OnNetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            if (_disposed || !e.IsAvailable) return;
            Log("Network connection restored. Verifying tunnel link...");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(2500);
                if (_disposed) return;
                if (string.IsNullOrEmpty(_liveTunnelUrl)) Start();
                else RunWatchdogCheck();
            });
        }

        public static bool IsValidPublicTunnelUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string clean = url.Trim().TrimEnd('/');
            if (!clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
            if (clean.Length < 12) return false;
            if (clean.IndexOf("api.trycloudflare.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                clean.IndexOf("pkg.trycloudflare.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                clean.IndexOf("update.trycloudflare.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                clean.IndexOf("staging.trycloudflare.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                clean.IndexOf("www.trycloudflare.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                clean.IndexOf("localhost.run", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
            Uri parsed;
            if (!Uri.TryCreate(clean, UriKind.Absolute, out parsed)) return false;
            return !string.IsNullOrEmpty(parsed.Host) && parsed.Host.IndexOf('.') >= 0;
        }

        public void UpdateIdentity(string shopId, string pairingCode)
        {
            if (!string.IsNullOrWhiteSpace(shopId)) _shopId = shopId.Trim();
            if (!string.IsNullOrWhiteSpace(pairingCode)) _pairingCode = pairingCode.Trim();
            string url = _liveTunnelUrl;
            if (IsValidPublicTunnelUrl(url))
            {
                RegisterWithCloud(url);
            }
        }

        public void ReadConfig(out string token, out string customUrl, out string providerMode)
        {
            token = null;
            customUrl = null;
            providerMode = "auto";

            try
            {
                string cfgPath = Path.Combine(_rootDir, "config", "scandrop.json");
                if (!File.Exists(cfgPath)) cfgPath = Path.Combine(_rootDir, "scandrop.json");
                if (File.Exists(cfgPath))
                {
                    var ser = new JavaScriptSerializer();
                    var dict = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(cfgPath, Encoding.UTF8));
                    if (dict != null)
                    {
                        if (dict.ContainsKey("cloudflareTunnelToken") && dict["cloudflareTunnelToken"] != null)
                            token = Convert.ToString(dict["cloudflareTunnelToken"]).Trim();
                        if (dict.ContainsKey("customTunnelUrl") && dict["customTunnelUrl"] != null)
                            customUrl = Convert.ToString(dict["customTunnelUrl"]).Trim().TrimEnd('/');
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
        }

        public void SaveConfig(string token, string customUrl, string providerMode)
        {
            string cfgDir = Path.Combine(_rootDir, "config");
            if (!Directory.Exists(cfgDir)) Directory.CreateDirectory(cfgDir);
            string cfgPath = Path.Combine(cfgDir, "scandrop.json");

            var dict = new Dictionary<string, object>();
            if (File.Exists(cfgPath))
            {
                try
                {
                    var existing = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(cfgPath, Encoding.UTF8));
                    if (existing != null) dict = existing;
                }
                catch { }
            }

            dict["cloudflareTunnelToken"] = (token ?? "").Trim();
            dict["customTunnelUrl"] = (customUrl ?? "").Trim().TrimEnd('/');
            dict["tunnelProvider"] = string.IsNullOrWhiteSpace(providerMode) ? "auto" : providerMode.Trim().ToLowerInvariant();
            File.WriteAllText(cfgPath, new JavaScriptSerializer().Serialize(dict), Encoding.UTF8);

            Restart();
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_disposed) return;

                int gen = Interlocked.Increment(ref _generation);
                StopProcessOnly();
                KillOrphanCloudflaredProcesses();

                _liveTunnelUrl = null;
                _consecutiveHealthFailures = 0;
                _isSwitchingToFallback = false;
                _healthStatus = "Connecting";

                string token, customUrl, providerMode;
                ReadConfig(out token, out customUrl, out providerMode);

                StartHeartbeatTimer();
                StartWatchdogTimer();

                if (!string.IsNullOrWhiteSpace(customUrl) && (providerMode == "custom" || string.IsNullOrWhiteSpace(token)))
                {
                    _activeProviderName = "Custom Domain";
                    _liveTunnelUrl = customUrl;
                    _healthStatus = "Live";
                    Log("Custom static tunnel configured: " + customUrl);
                    RegisterWithCloud(customUrl);
                    FireCaptured(customUrl);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(token))
                {
                    StartCloudflareNamedTunnel(token, customUrl, gen);
                    return;
                }

                if (providerMode == "openssh")
                {
                    _localhostRunCooldownUntilUtc = DateTime.MinValue;
                    StartOpenSshTunnel(gen);
                    return;
                }

                if (providerMode == "pinggy")
                {
                    _pinggyCooldownUntilUtc = DateTime.MinValue;
                    StartPinggySshTunnel(gen);
                    return;
                }

                if (providerMode == "cloudflare")
                {
                    _cloudflareCooldownUntilUtc = DateTime.MinValue;
                    StartCloudflareQuickTunnel(gen);
                    return;
                }

                // Auto resilient cascade: Cloudflare Quick -> OpenSSH (localhost.run) -> Pinggy SSH
                if (DateTime.UtcNow < _cloudflareCooldownUntilUtc)
                {
                    Log("Cloudflare Quick Tunnel in rate-limit cooldown. Launching resilient SSH tunnel...");
                    if (DateTime.UtcNow < _localhostRunCooldownUntilUtc)
                    {
                        StartPinggySshTunnel(gen);
                    }
                    else
                    {
                        StartOpenSshTunnel(gen);
                    }
                }
                else
                {
                    StartCloudflareQuickTunnel(gen);
                }
            }
        }

        private void StartCloudflareNamedTunnel(string token, string staticUrl, int gen)
        {
            if (_disposed || Volatile.Read(ref _generation) != gen) return;

            string exePath = EnsureCloudflared();
            if (string.IsNullOrEmpty(exePath))
            {
                Log("cloudflared unavailable. Falling back to OpenSSH tunnel...");
                StartOpenSshTunnel(gen);
                return;
            }

            try
            {
                _activeProviderName = "Cloudflare Named Tunnel";
                Log("Starting authenticated Cloudflare Named Tunnel (Zero Rate-Limit)...");
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = string.Format(CultureInfo.InvariantCulture, "tunnel --no-autoupdate --protocol http2 --edge-ip-version 4 run --token {0}", token),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };

                _tunnelProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _tunnelProcess.OutputDataReceived += (s, e) => HandleNamedTunnelOutput(e.Data, staticUrl, gen);
                _tunnelProcess.ErrorDataReceived += (s, e) => HandleNamedTunnelOutput(e.Data, staticUrl, gen);
                _tunnelProcess.Exited += (s, e) =>
                {
                    if (_disposed || Volatile.Read(ref _generation) != gen) return;
                    _liveTunnelUrl = null;
                    _healthStatus = "Reconnecting";
                    FireCaptured("");
                    Log("Cloudflare Named Tunnel exited. Reconnecting in 5s...");
                    ScheduleRestart(5000, gen);
                };

                _tunnelProcess.Start();
                _tunnelProcess.BeginOutputReadLine();
                _tunnelProcess.BeginErrorReadLine();

                if (IsValidPublicTunnelUrl(staticUrl))
                {
                    _liveTunnelUrl = staticUrl;
                    _healthStatus = "Live";
                    _consecutiveHealthFailures = 0;
                    RegisterWithCloud(staticUrl);
                    FireCaptured(staticUrl);
                }
            }
            catch (Exception ex)
            {
                Log("Named tunnel error: " + ex.Message + ". Falling back to OpenSSH...");
                StartOpenSshTunnel(gen);
            }
        }

        private void HandleNamedTunnelOutput(string line, string staticUrl, int gen)
        {
            if (string.IsNullOrEmpty(line) || Volatile.Read(ref _generation) != gen) return;
            AppendTunnelLog("[CF-Named] " + line);

            if (line.IndexOf("Registered tunnel connection", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (IsValidPublicTunnelUrl(staticUrl))
                {
                    _liveTunnelUrl = staticUrl;
                    _healthStatus = "Live";
                    _consecutiveHealthFailures = 0;
                    _lastHealthOkUtc = DateTime.UtcNow;
                    RegisterWithCloud(staticUrl);
                    FireCaptured(staticUrl);
                }
            }
            else if (line.IndexOf("Provided Tunnel token is not valid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     line.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Log("Cloudflare Named Tunnel token invalid. Falling back to OpenSSH...");
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    lock (_lock)
                    {
                        if (_disposed || Volatile.Read(ref _generation) != gen) return;
                        SwitchToFallbackLocked(gen, true);
                    }
                });
            }
        }

        private void StartCloudflareQuickTunnel(int gen)
        {
            if (_disposed || Volatile.Read(ref _generation) != gen) return;

            string exePath = EnsureCloudflared();
            if (string.IsNullOrEmpty(exePath))
            {
                Log("cloudflared not found. Activating built-in OpenSSH tunnel...");
                StartOpenSshTunnel(gen);
                return;
            }

            try
            {
                _activeProviderName = "Cloudflare Quick Tunnel";
                _isSwitchingToFallback = false;
                Log("Connecting Cloudflare HTTP/2 Quick Tunnel (IPv4) -> 127.0.0.1:" + _localPort + "...");

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = string.Format(CultureInfo.InvariantCulture, "tunnel --no-autoupdate --protocol http2 --edge-ip-version 4 --url http://127.0.0.1:{0}", _localPort),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };

                _tunnelProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _tunnelProcess.OutputDataReceived += (s, e) => HandleCloudflareOutput(e.Data, gen);
                _tunnelProcess.ErrorDataReceived += (s, e) => HandleCloudflareOutput(e.Data, gen);
                _tunnelProcess.Exited += (s, e) =>
                {
                    if (_disposed || _isSwitchingToFallback || Volatile.Read(ref _generation) != gen) return;

                    bool hadUrl = !string.IsNullOrEmpty(_liveTunnelUrl);
                    _liveTunnelUrl = null;
                    _healthStatus = "Reconnecting";
                    FireCaptured("");

                    if (!hadUrl)
                    {
                        Log("Cloudflare Quick Tunnel unavailable/rate-limited. Switching immediately to OpenSSH fallback...");
                        _cloudflareCooldownUntilUtc = DateTime.UtcNow.AddMinutes(5);
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            lock (_lock)
                            {
                                SwitchToFallbackLocked(gen, true);
                            }
                        });
                    }
                    else
                    {
                        Log("Cloudflare Tunnel disconnected. Reconnecting automatically...");
                        ScheduleRestart(3000, gen);
                    }
                };

                _tunnelProcess.Start();
                _tunnelProcess.BeginOutputReadLine();
                _tunnelProcess.BeginErrorReadLine();

                // 18s startup guard: if Cloudflare stalls before issuing URL, fail over cleanly without hanging
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Thread.Sleep(18000);
                    lock (_lock)
                    {
                        if (_disposed || _isSwitchingToFallback || Volatile.Read(ref _generation) != gen) return;
                        if (string.IsNullOrEmpty(_liveTunnelUrl))
                        {
                            Log("Cloudflare Quick Tunnel timed out after 18s. Switching to OpenSSH fallback...");
                            _cloudflareCooldownUntilUtc = DateTime.UtcNow.AddMinutes(5);
                            SwitchToFallbackLocked(gen, true);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Log("Cloudflare start error: " + ex.Message + ". Switching to OpenSSH...");
                _cloudflareCooldownUntilUtc = DateTime.UtcNow.AddMinutes(5);
                StartOpenSshTunnel(gen);
            }
        }

        private void HandleCloudflareOutput(string line, int gen)
        {
            if (string.IsNullOrEmpty(line) || Volatile.Read(ref _generation) != gen) return;
            AppendTunnelLog("[CF-Quick] " + line);

            if (line.IndexOf("status 429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("too many requests", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("quick tunnel provisioning failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("failed to unmarshal quick Tunnel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("failed to request quick Tunnel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _cloudflareCooldownUntilUtc = DateTime.UtcNow.AddMinutes(5);
                if (!_isSwitchingToFallback)
                {
                    Log("Cloudflare Quick Tunnel rate-limited (HTTP 429). Cooling down 5m & activating OpenSSH fallback...");
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        lock (_lock)
                        {
                            SwitchToFallbackLocked(gen, true);
                        }
                    });
                }
                return;
            }

            var match = CloudflareUrlRegex.Match(line);
            if (match.Success)
            {
                string captured = match.Value.Trim().TrimEnd('/');
                if (!IsValidPublicTunnelUrl(captured)) return;

                if (!string.Equals(_liveTunnelUrl, captured, StringComparison.OrdinalIgnoreCase))
                {
                    _liveTunnelUrl = captured;
                    _activeProviderName = "Cloudflare Quick Tunnel";
                    _healthStatus = "Live";
                    _consecutiveHealthFailures = 0;
                    _lastHealthOkUtc = DateTime.UtcNow;
                    Log("⚡ Tunnel Live (Cloudflare): " + captured);
                    RegisterWithCloud(captured);
                    FireCaptured(captured);
                }
            }
        }

        private void SwitchToFallbackLocked(int expectedGen, bool useOpenSshFirst)
        {
            if (_disposed || Volatile.Read(ref _generation) != expectedGen) return;
            _isSwitchingToFallback = true;
            _liveTunnelUrl = null;
            _healthStatus = "Reconnecting";
            int newGen = Interlocked.Increment(ref _generation);
            StopProcessOnly();
            _isSwitchingToFallback = false;

            if (useOpenSshFirst && DateTime.UtcNow >= _localhostRunCooldownUntilUtc)
            {
                StartOpenSshTunnel(newGen);
            }
            else
            {
                StartPinggySshTunnel(newGen);
            }
        }

        private void StartOpenSshTunnel(int gen)
        {
            lock (_lock)
            {
                if (_disposed || Volatile.Read(ref _generation) != gen) return;
                StopProcessOnly();
                _isSwitchingToFallback = false;
                _liveTunnelUrl = null;

                string sshExe = FindOpenSsh();
                _activeProviderName = "OpenSSH (localhost.run)";
                Log("Starting OpenSSH Tunnel (" + Path.GetFileName(sshExe) + " -> localhost.run)...");

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = sshExe,
                        Arguments = string.Format(
                            CultureInfo.InvariantCulture,
                            "-T -o BatchMode=yes -o StrictHostKeyChecking=no -o UserKnownHostsFile=NUL -o ServerAliveInterval=15 -o ServerAliveCountMax=3 -o ExitOnForwardFailure=yes -o ConnectTimeout=10 -o TCPKeepAlive=yes -R 80:127.0.0.1:{0} nokey@localhost.run",
                            _localPort),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    _tunnelProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    _tunnelProcess.OutputDataReceived += (s, e) => HandleSshOutput(e.Data, gen);
                    _tunnelProcess.ErrorDataReceived += (s, e) => HandleSshOutput(e.Data, gen);
                    _tunnelProcess.Exited += (s, e) =>
                    {
                        if (_disposed || _isSwitchingToFallback || Volatile.Read(ref _generation) != gen) return;

                        bool hadUrl = !string.IsNullOrEmpty(_liveTunnelUrl);
                        _liveTunnelUrl = null;
                        _healthStatus = "Reconnecting";
                        FireCaptured("");

                        if (!hadUrl)
                        {
                            Log("localhost.run SSH did not establish tunnel. Activating Pinggy SSH backup...");
                            _localhostRunCooldownUntilUtc = DateTime.UtcNow.AddMinutes(3);
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                lock (_lock)
                                {
                                    if (_disposed || Volatile.Read(ref _generation) != gen) return;
                                    int nextGen = Interlocked.Increment(ref _generation);
                                    StopProcessOnly();
                                    StartPinggySshTunnel(nextGen);
                                }
                            });
                        }
                        else
                        {
                            Log("OpenSSH Tunnel disconnected. Reconnecting in 3s...");
                            ScheduleRestart(3000, gen);
                        }
                    };

                    _tunnelProcess.Start();
                    try { _tunnelProcess.StandardInput.Close(); } catch { }
                    _tunnelProcess.BeginOutputReadLine();
                    _tunnelProcess.BeginErrorReadLine();

                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Thread.Sleep(18000);
                        lock (_lock)
                        {
                            if (_disposed || _isSwitchingToFallback || Volatile.Read(ref _generation) != gen) return;
                            if (string.IsNullOrEmpty(_liveTunnelUrl))
                            {
                                Log("localhost.run timed out after 18s. Switching to Pinggy SSH backup...");
                                _localhostRunCooldownUntilUtc = DateTime.UtcNow.AddMinutes(3);
                                int nextGen = Interlocked.Increment(ref _generation);
                                StopProcessOnly();
                                StartPinggySshTunnel(nextGen);
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log("OpenSSH Tunnel error: " + ex.Message + ". Switching to Pinggy SSH...");
                    StartPinggySshTunnel(gen);
                }
            }
        }

        private void HandleSshOutput(string line, int gen)
        {
            if (string.IsNullOrEmpty(line) || Volatile.Read(ref _generation) != gen) return;
            AppendTunnelLog("[SSH-LHR] " + line);

            var match = SshUrlRegex.Match(line);
            if (match.Success)
            {
                string captured = match.Value.Trim().TrimEnd('/');
                if (!IsValidPublicTunnelUrl(captured)) return;

                if (!string.Equals(_liveTunnelUrl, captured, StringComparison.OrdinalIgnoreCase))
                {
                    _liveTunnelUrl = captured;
                    _activeProviderName = "OpenSSH (localhost.run)";
                    _healthStatus = "Live";
                    _consecutiveHealthFailures = 0;
                    _lastHealthOkUtc = DateTime.UtcNow;
                    Log("⚡ Tunnel Live (OpenSSH): " + captured);
                    RegisterWithCloud(captured);
                    FireCaptured(captured);
                }
            }
        }

        private void StartPinggySshTunnel(int gen)
        {
            lock (_lock)
            {
                if (_disposed || Volatile.Read(ref _generation) != gen) return;
                StopProcessOnly();
                _isSwitchingToFallback = false;
                _liveTunnelUrl = null;

                string sshExe = FindOpenSsh();
                _activeProviderName = "Pinggy SSH (Port 443)";
                Log("Starting backup Pinggy SSH Tunnel over Port 443...");

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = sshExe,
                        Arguments = string.Format(
                            CultureInfo.InvariantCulture,
                            "-T -p 443 -o BatchMode=yes -o StrictHostKeyChecking=no -o UserKnownHostsFile=NUL -o ServerAliveInterval=15 -o ServerAliveCountMax=3 -o ExitOnForwardFailure=yes -o ConnectTimeout=10 -R0:127.0.0.1:{0} qr@a.pinggy.io",
                            _localPort),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    _tunnelProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    _tunnelProcess.OutputDataReceived += (s, e) => HandlePinggyOutput(e.Data, gen);
                    _tunnelProcess.ErrorDataReceived += (s, e) => HandlePinggyOutput(e.Data, gen);
                    _tunnelProcess.Exited += (s, e) =>
                    {
                        if (_disposed || _isSwitchingToFallback || Volatile.Read(ref _generation) != gen) return;
                        _liveTunnelUrl = null;
                        _healthStatus = "Reconnecting";
                        _pinggyCooldownUntilUtc = DateTime.UtcNow.AddMinutes(2);
                        FireCaptured("");
                        Log("Pinggy SSH Tunnel disconnected. Reconnecting in 5s...");
                        ScheduleRestart(5000, gen);
                    };

                    _tunnelProcess.Start();
                    try { _tunnelProcess.StandardInput.Close(); } catch { }
                    _tunnelProcess.BeginOutputReadLine();
                    _tunnelProcess.BeginErrorReadLine();

                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Thread.Sleep(18000);
                        lock (_lock)
                        {
                            if (_disposed || _isSwitchingToFallback || Volatile.Read(ref _generation) != gen) return;
                            if (string.IsNullOrEmpty(_liveTunnelUrl))
                            {
                                Log("All public tunnel providers busy right now. Retrying in 8s...");
                                ScheduleRestart(8000, gen);
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log("Pinggy SSH backup failed: " + ex.Message + ". Retrying in 10s...");
                    ScheduleRestart(10000, gen);
                }
            }
        }

        private void HandlePinggyOutput(string line, int gen)
        {
            if (string.IsNullOrEmpty(line) || Volatile.Read(ref _generation) != gen) return;
            AppendTunnelLog("[SSH-Pinggy] " + line);

            var match = PinggyUrlRegex.Match(line);
            if (match.Success)
            {
                string captured = match.Value.Trim().TrimEnd('/');
                if (!IsValidPublicTunnelUrl(captured)) return;

                if (!string.Equals(_liveTunnelUrl, captured, StringComparison.OrdinalIgnoreCase))
                {
                    _liveTunnelUrl = captured;
                    _activeProviderName = "Pinggy SSH (Port 443)";
                    _healthStatus = "Live";
                    _consecutiveHealthFailures = 0;
                    _lastHealthOkUtc = DateTime.UtcNow;
                    Log("⚡ Tunnel Live (Pinggy): " + captured);
                    RegisterWithCloud(captured);
                    FireCaptured(captured);
                }
            }
        }

        private string FindOpenSsh()
        {
            string systemSsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
            if (File.Exists(systemSsh)) return systemSsh;
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
                string url = _liveTunnelUrl;
                if (IsValidPublicTunnelUrl(url))
                {
                    RegisterWithCloud(url);
                }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private void StartWatchdogTimer()
        {
            if (_watchdogTimer != null) return;
            _watchdogTimer = new Timer(_ => RunWatchdogCheck(), null, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(25));
        }

        private void RunWatchdogCheck()
        {
            if (_disposed || _probeInFlight) return;
            _probeInFlight = true;
            try
            {
                if (!ProbeLocalServer()) return;

                string url = _liveTunnelUrl;
                if (!IsValidPublicTunnelUrl(url)) return;

                string err;
                if (ProbePublicTunnel(url, out err))
                {
                    _consecutiveHealthFailures = 0;
                    _lastHealthOkUtc = DateTime.UtcNow;
                    _healthStatus = "Live";
                    return;
                }

                // Debounce momentary network jitter: wait 2.5s and re-confirm before rotating tunnel
                _consecutiveHealthFailures++;
                Thread.Sleep(2500);
                if (_disposed) return;
                if (ProbePublicTunnel(url, out err))
                {
                    _consecutiveHealthFailures = 0;
                    _lastHealthOkUtc = DateTime.UtcNow;
                    _healthStatus = "Live";
                    return;
                }

                _consecutiveHealthFailures++;
                Log("Watchdog detected dead/stalled tunnel (" + err + "). Auto-recovering...");
                _healthStatus = "Recovering";

                string tok, cust, mode;
                ReadConfig(out tok, out cust, out mode);
                if (string.IsNullOrWhiteSpace(tok) && mode != "cloudflare" &&
                    (_activeProviderName ?? "").IndexOf("Cloudflare", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _cloudflareCooldownUntilUtc = DateTime.UtcNow.AddMinutes(5);
                }
                else if ((_activeProviderName ?? "").IndexOf("localhost.run", StringComparison.OrdinalIgnoreCase) >= 0 && mode == "auto")
                {
                    _localhostRunCooldownUntilUtc = DateTime.UtcNow.AddMinutes(3);
                }

                if (mode != "custom")
                {
                    _liveTunnelUrl = null;
                    DisconnectFromCloud();
                    FireCaptured("");
                    Start();
                }
            }
            catch { }
            finally
            {
                _probeInFlight = false;
            }
        }

        private bool ProbeLocalServer()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(string.Format(CultureInfo.InvariantCulture, "http://127.0.0.1:{0}/api/health", _localPort));
                req.Method = "GET";
                req.Timeout = 3000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    return (int)resp.StatusCode == 200;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool ProbePublicTunnel(string baseUrl, out string error)
        {
            error = null;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                var req = (HttpWebRequest)WebRequest.Create(baseUrl.TrimEnd('/') + "/api/health");
                req.Method = "GET";
                req.Timeout = 6500;
                req.ReadWriteTimeout = 6500;
                req.UserAgent = "ScanDrop-Watchdog/1.0";
                req.Headers["X-Pinggy-No-Screen"] = "true";
                req.Headers["Bypass-Tunnel-Reminder"] = "true";
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    int code = (int)resp.StatusCode;
                    string body = sr.ReadToEnd();
                    if (code >= 200 && code < 300 && body.IndexOf("local-pc", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                    error = "HTTP " + code + " unexpected response";
                    return false;
                }
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse)
                {
                    error = "HTTP " + (int)((HttpWebResponse)wex.Response).StatusCode;
                }
                else
                {
                    error = wex.Message;
                }
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
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
                using (var resp = (HttpWebResponse)req.GetResponse()) { }
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

        private void KillOrphanCloudflaredProcesses()
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

        private void ScheduleRestart(int delayMs, int expectedGen)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(delayMs);
                if (_disposed || Volatile.Read(ref _generation) != expectedGen) return;
                Start();
            });
        }

        public void Restart()
        {
            Log("Restarting tunnel connection...");
            _cloudflareCooldownUntilUtc = DateTime.MinValue;
            _localhostRunCooldownUntilUtc = DateTime.MinValue;
            _pinggyCooldownUntilUtc = DateTime.MinValue;
            _liveTunnelUrl = null;
            _healthStatus = "Connecting";
            FireCaptured("");
            Start();
        }

        private void StopProcessOnly()
        {
            try
            {
                if (_tunnelProcess != null && !_tunnelProcess.HasExited)
                    _tunnelProcess.Kill();
            }
            catch { }
            _tunnelProcess = null;
        }

        private void AppendTunnelLog(string line)
        {
            try
            {
                string dataDir = Path.Combine(_rootDir, "data");
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                string logFile = Path.Combine(dataDir, "tunnel.log");
                if (File.Exists(logFile) && new FileInfo(logFile).Length > 2 * 1024 * 1024)
                {
                    File.WriteAllText(logFile, "");
                }
                File.AppendAllText(logFile, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + " " + line + Environment.NewLine, Encoding.UTF8);
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
            AppendTunnelLog("[Manager] " + msg);
            var h = OnStatusMessage;
            if (h != null) try { h(msg); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Interlocked.Increment(ref _generation);
            try
            {
                if (_eventsHooked)
                {
                    SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                    NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
                }
            }
            catch { }
            if (_heartbeatTimer != null) { _heartbeatTimer.Dispose(); _heartbeatTimer = null; }
            if (_watchdogTimer != null) { _watchdogTimer.Dispose(); _watchdogTimer = null; }
            StopProcessOnly();
            DisconnectFromCloud();
        }
    }
}
