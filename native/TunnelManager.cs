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
using ScanDrop.Native.Storage;

namespace ScanDrop.Native
{
    public enum TunnelState
    {
        STOPPED,
        CONNECTING,
        CONNECTED,
        DEGRADED,
        COOLDOWN,
        RECONNECTING,
        OFFLINE
    }

    public sealed class TunnelStatusSnapshot
    {
        public TunnelState State;
        public string StateName;
        public string DisplayStatus;
        public string Mode;
        public string ModeLabel;
        public int? ProcessId;
        public string PublicUrl;
        public bool IsOnline;
        public DateTime? LastSuccessfulHealthCheckUtc;
        public string LastFailureReason;
        public int RetryCount;
        public DateTime? NextRetryUtc;
        public int Generation;
        public int ActiveUploadsCount;
    }

    /// <summary>
    /// Single Cloudflare-only tunnel supervisor for Scan Drop.
    /// Exclusively manages Cloudflare Named Tunnels (Production, including Windows Service mode)
    /// and Cloudflare Quick Tunnels (Development). Never launches OpenSSH, localhost.run,
    /// ngrok, Serveo, Pinggy, or any other third-party tunneling provider.
    /// </summary>
    public sealed class TunnelManager : IDisposable
    {
        private static readonly Regex CloudflareQuickUrlRegex = new Regex(
            @"https://(?!api\.|pkg\.|update\.|staging\.|www\.)([a-zA-Z0-9-]{5,}\.trycloudflare\.com)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex RetryAfterSecondsRegex = new Regex(
            @"retry[-\s]?after[\s:=]+(\d{1,5})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ServicePidRegex = new Regex(
            @"PID\s*:\s*(\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly string _rootDir;
        private readonly int _localPort;
        private readonly string _cloudUrl;
        private readonly SqliteDatabase _db;
        private readonly string _logsDir;
        private readonly object _lock = new object();
        private readonly Random _rng = new Random();

        private string _shopId;
        private string _pairingCode;
        private string _deviceId;
        private string _deviceSecret;

        // Supervisor state (strictly guarded by _lock)
        private TunnelState _state = TunnelState.STOPPED;
        private string _currentMode = "development_quick";
        private string _modeLabel = "Cloudflare Quick Tunnel (Development)";
        private Process _tunnelProcess;
        private int? _processId;
        private bool _usingWindowsService;
        private string _publicUrl;
        private string _pendingCandidateUrl;
        private int _edgeRegisteredGen;
        private int _verificationStartedGen;
        private DateTime? _lastSuccessfulHealthCheckUtc;
        private string _lastFailureReason;
        private string _lastStderrError;
        private int _lastStderrErrorGen;
        private int _retryCount;
        private int _rateLimitHits;
        private int _consecutiveHealthFailures;
        private DateTime? _nextRetryUtc;
        private DateTime _cooldownUntilUtc = DateTime.MinValue;
        private int _generation;
        private bool _disposed;
        private bool _probeInFlight;
        private bool _eventsHooked;

        private Timer _heartbeatTimer;
        private Timer _watchdogTimer;
        private Timer _retryTimer;
        private Timer _startupGuardTimer;

        public event Action<string> OnTunnelUrlCaptured;
        public event Action<string> OnStatusMessage;
        public event Action<TunnelStatusSnapshot> OnStateChanged;

        // Backward-compatible accessors backed by thread-safe snapshot
        public string LiveTunnelUrl { get { return GetStatusSnapshot().PublicUrl; } }
        public string ActiveProviderName { get { return GetStatusSnapshot().ModeLabel; } }
        public string CurrentHealthStatus { get { return GetStatusSnapshot().DisplayStatus; } }
        public DateTime? LastHealthOkUtc { get { return GetStatusSnapshot().LastSuccessfulHealthCheckUtc; } }
        public TunnelState State { get { return GetStatusSnapshot().State; } }

        public TunnelManager(string rootDir, int localPort, string cloudUrl, string shopId, string pairingCode, SqliteDatabase db = null, string logsDir = null)
        {
            _rootDir = rootDir;
            _localPort = localPort;
            _cloudUrl = (cloudUrl ?? "https://scandrop-xi.vercel.app").TrimEnd('/');
            _db = db;
            _logsDir = logsDir;
            _shopId = string.IsNullOrWhiteSpace(shopId) ? "shop_quickprint_01" : shopId.Trim();
            _pairingCode = string.IsNullOrWhiteSpace(pairingCode) ? "SD-PAIR-7890" : pairingCode.Trim();
            if (_db != null)
            {
                try
                {
                    _deviceId = _db.GetSetting("device_id") ?? "";
                    _deviceSecret = _db.GetSetting("device_secret") ?? "";
                }
                catch { }
            }

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.DefaultConnectionLimit = 64;
            HookSystemRecoveryEvents();
        }

        public TunnelStatusSnapshot GetStatusSnapshot()
        {
            int activeUploads = 0;
            if (_db != null)
            {
                try { activeUploads = _db.CountActiveUploadSessions(); } catch { }
            }

            lock (_lock)
            {
                bool online = (_state == TunnelState.CONNECTED || _state == TunnelState.DEGRADED) &&
                              IsValidPublicTunnelUrl(_publicUrl);
                return new TunnelStatusSnapshot
                {
                    State = _state,
                    StateName = _state.ToString(),
                    DisplayStatus = FormatDisplayStatus(_state),
                    Mode = _currentMode,
                    ModeLabel = _modeLabel,
                    ProcessId = _processId,
                    PublicUrl = _publicUrl,
                    IsOnline = online,
                    LastSuccessfulHealthCheckUtc = _lastSuccessfulHealthCheckUtc,
                    LastFailureReason = _lastFailureReason,
                    RetryCount = _retryCount,
                    NextRetryUtc = _nextRetryUtc,
                    Generation = _generation,
                    ActiveUploadsCount = activeUploads
                };
            }
        }

        public static string FormatDisplayStatus(TunnelState state)
        {
            switch (state)
            {
                case TunnelState.CONNECTED:
                    return "Cloudflare Connected";
                case TunnelState.DEGRADED:
                    return "Cloudflare Degraded — Verifying";
                case TunnelState.CONNECTING:
                    return "Connecting Cloudflare...";
                case TunnelState.COOLDOWN:
                case TunnelState.RECONNECTING:
                case TunnelState.OFFLINE:
                    return "Cloudflare Offline — Reconnecting";
                default:
                    return "Stopped";
            }
        }

        public static bool IsValidPublicTunnelUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string clean = url.Trim().TrimEnd('/');
            if (!clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
            if (clean.Length < 12) return false;

            string lower = clean.ToLowerInvariant();
            // Reject Cloudflare utility/API hostnames and ALL third-party tunnel providers
            string[] forbidden = new[]
            {
                "api.trycloudflare.com",
                "pkg.trycloudflare.com",
                "update.trycloudflare.com",
                "staging.trycloudflare.com",
                "www.trycloudflare.com",
                "localhost.run",
                ".lhr.life",
                ".lhr.rocks",
                ".lhr.pro",
                "pinggy.link",
                "pinggy.io",
                "serveo.net",
                "ngrok.io",
                "ngrok-free.app",
                "ngrok-free.dev",
                "loca.lt"
            };
            for (int i = 0; i < forbidden.Length; i++)
            {
                if (lower.IndexOf(forbidden[i], StringComparison.Ordinal) >= 0) return false;
            }

            Uri parsed;
            if (!Uri.TryCreate(clean, UriKind.Absolute, out parsed)) return false;
            return !string.IsNullOrEmpty(parsed.Host) && parsed.Host.IndexOf('.') >= 0;
        }

        public static bool IsCloudflareRateLimit(string line, out int retryAfterSeconds)
        {
            retryAfterSeconds = 0;
            if (string.IsNullOrWhiteSpace(line)) return false;

            bool hasExplicitRateLimit =
                line.IndexOf("status 429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("429 too many requests", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("too many requests", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("ratelimit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("tunnelstore request failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("\"code\":10000", StringComparison.OrdinalIgnoreCase) >= 0;

            bool isNetworkOrTimeoutError =
                line.IndexOf("context deadline exceeded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("i/o timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("dial tcp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("connection refused", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("connection reset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("no such host", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("tls handshake timeout", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isNetworkOrTimeoutError && !hasExplicitRateLimit) return false;
            if (!hasExplicitRateLimit) return false;

            var m = RetryAfterSecondsRegex.Match(line);
            if (m.Success)
            {
                int parsed;
                if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    retryAfterSeconds = Math.Max(60, Math.Min(1800, parsed));
                }
            }
            return true;
        }

        private static string ExtractStderrSummary(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            if (line.IndexOf("ERR", StringComparison.OrdinalIgnoreCase) < 0 &&
                line.IndexOf("failed to", StringComparison.OrdinalIgnoreCase) < 0 &&
                line.IndexOf("context deadline", StringComparison.OrdinalIgnoreCase) < 0 &&
                line.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) < 0 &&
                line.IndexOf("error", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }
            string clean = line.Trim();
            int errIdx = clean.IndexOf("failed to ", StringComparison.OrdinalIgnoreCase);
            if (errIdx > 0 && errIdx < 40) clean = clean.Substring(errIdx);
            if (clean.Length > 160) clean = clean.Substring(0, 157) + "...";
            return clean;
        }

        public int ComputeBackoffWithJitterMs(int retryCount, int baseDelayMs = 3000, int maxDelayMs = 120000)
        {
            int attempt = Math.Max(1, retryCount);
            int shift = Math.Min(attempt - 1, 6);
            long raw = (long)baseDelayMs << shift;
            int capped = (int)Math.Min(maxDelayMs, raw);
            double jitterFactor;
            lock (_lock)
            {
                // Uniform jitter in [0.80, 1.25]
                jitterFactor = 0.80 + (_rng.NextDouble() * 0.45);
            }
            return Math.Max(1000, (int)Math.Round(capped * jitterFactor));
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
                Log("Windows resumed from sleep. Verifying Cloudflare Tunnel...");
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Thread.Sleep(2500);
                    if (_disposed) return;
                    TunnelState st;
                    bool inCooldown;
                    lock (_lock)
                    {
                        st = _state;
                        inCooldown = _state == TunnelState.COOLDOWN && DateTime.UtcNow < _cooldownUntilUtc;
                    }
                    if (inCooldown) return;
                    if (st == TunnelState.CONNECTED || st == TunnelState.DEGRADED) RunWatchdogCheck();
                    else Start();
                });
            }
        }

        private void OnNetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            if (_disposed) return;
            if (!e.IsAvailable)
            {
                lock (_lock)
                {
                    if (_state == TunnelState.CONNECTED)
                    {
                        _lastFailureReason = "Network interface disconnected";
                        TransitionStateLocked(TunnelState.DEGRADED);
                        Log("Network connection lost. Marking Cloudflare Tunnel DEGRADED until confirmed...");
                    }
                }
                return;
            }

            Log("Network connection restored. Verifying Cloudflare Tunnel...");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(2000);
                if (_disposed) return;
                lock (_lock)
                {
                    if (_state == TunnelState.COOLDOWN && DateTime.UtcNow < _cooldownUntilUtc)
                    {
                        Log("Cloudflare rate-limit cooldown still active until " + _cooldownUntilUtc.ToLocalTime().ToString("HH:mm:ss") + ". Respecting retry interval.");
                        return;
                    }
                    if (_state == TunnelState.OFFLINE || _state == TunnelState.RECONNECTING || _state == TunnelState.STOPPED)
                    {
                        _retryCount = 0;
                        _nextRetryUtc = null;
                        CancelRetryTimerLocked();
                        BeginConnectLocked(TunnelState.RECONNECTING);
                        return;
                    }
                }
                RunWatchdogCheck();
            });
        }

        public void UpdateIdentity(string shopId, string pairingCode, string deviceId = null, string deviceSecret = null)
        {
            string url;
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(shopId)) _shopId = shopId.Trim();
                if (!string.IsNullOrWhiteSpace(pairingCode)) _pairingCode = pairingCode.Trim();
                if (deviceId != null) _deviceId = deviceId.Trim();
                if (deviceSecret != null) _deviceSecret = deviceSecret.Trim();
                url = (_state == TunnelState.CONNECTED || _state == TunnelState.DEGRADED) ? _publicUrl : null;
            }
            if (IsValidPublicTunnelUrl(url))
            {
                ThreadPool.QueueUserWorkItem(_ => RegisterWithCloud(url));
            }
        }

        public void ReadConfig(out string token, out string customUrl, out string providerMode)
        {
            token = null;
            customUrl = null;
            providerMode = "auto";

            if (_db != null)
            {
                try
                {
                    token = _db.GetSetting("cloudflare_tunnel_token", "");
                    customUrl = _db.GetSetting("custom_tunnel_url", "");
                    string m = _db.GetSetting("tunnel_provider", "auto");
                    if (!string.IsNullOrWhiteSpace(m)) providerMode = NormalizeCloudflareMode(m);
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(customUrl))
            {
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
                                providerMode = NormalizeCloudflareMode(Convert.ToString(dict["tunnelProvider"]));
                        }
                        if (_db != null && !string.IsNullOrWhiteSpace(token))
                        {
                            _db.SetSetting("cloudflare_tunnel_token", token, true);
                            _db.SetSetting("custom_tunnel_url", customUrl ?? "", false);
                            _db.SetSetting("tunnel_provider", providerMode ?? "auto", false);
                            dict["cloudflareTunnelToken"] = "";
                            File.WriteAllText(cfgPath, ser.Serialize(dict), Encoding.UTF8);
                        }
                    }
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(token))
                token = Environment.GetEnvironmentVariable("CLOUDFLARE_TUNNEL_TOKEN");
            if (string.IsNullOrWhiteSpace(customUrl))
                customUrl = Environment.GetEnvironmentVariable("CUSTOM_TUNNEL_URL");

            providerMode = NormalizeCloudflareMode(providerMode);
        }

        public static string NormalizeCloudflareMode(string raw)
        {
            string m = (raw ?? "auto").Trim().ToLowerInvariant();
            if (m == "named" || m == "production" || m == "production_named" || m == "service") return "named";
            if (m == "quick" || m == "development" || m == "development_quick") return "quick";
            // Legacy openssh / pinggy / cloudflare values all normalize strictly to Cloudflare auto (prefers Named)
            return "auto";
        }

        public void SaveConfig(string token, string customUrl, string providerMode)
        {
            string cleanToken = (token ?? "").Trim();
            string cleanUrl = (customUrl ?? "").Trim().TrimEnd('/');
            string cleanMode = NormalizeCloudflareMode(providerMode);

            if (_db != null)
            {
                _db.SetSetting("cloudflare_tunnel_token", cleanToken, true);
                _db.SetSetting("custom_tunnel_url", cleanUrl, false);
                _db.SetSetting("tunnel_provider", cleanMode, false);
            }
            else
            {
                string cfgDir = Path.Combine(_rootDir, "config");
                if (!Directory.Exists(cfgDir)) Directory.CreateDirectory(cfgDir);
                string cfgPath = Path.Combine(cfgDir, "scandrop.json");
                var dict = new Dictionary<string, object>
                {
                    { "cloudflareTunnelToken", "" },
                    { "customTunnelUrl", cleanUrl },
                    { "tunnelProvider", cleanMode }
                };
                File.WriteAllText(cfgPath, new JavaScriptSerializer().Serialize(dict), Encoding.UTF8);
            }

            Restart();
        }

        /// <summary>
        /// Idempotent single-flight Start(). Multiple simultaneous calls will NEVER launch
        /// duplicate cloudflared processes or interrupt an active connection/cooldown.
        /// </summary>
        public void Start()
        {
            lock (_lock)
            {
                if (_disposed) return;

                // Single-flight guard: never start concurrently if already connecting, connected, or degraded
                if (_state == TunnelState.CONNECTING || _state == TunnelState.CONNECTED || _state == TunnelState.DEGRADED)
                {
                    return;
                }

                // Respect active Cloudflare rate-limit cooldown — never bypass by spawning new Quick Tunnels
                if (_state == TunnelState.COOLDOWN && DateTime.UtcNow < _cooldownUntilUtc)
                {
                    Log("Start() ignored during Cloudflare rate-limit cooldown until " + _cooldownUntilUtc.ToLocalTime().ToString("HH:mm:ss"));
                    return;
                }

                StartHeartbeatTimerLocked();
                StartWatchdogTimerLocked();
                BeginConnectLocked(TunnelState.CONNECTING);
            }
        }

        /// <summary>
        /// Explicit manual restart requested by user or config change.
        /// Cancels pending retry/cooldown timers, stops the current owned cloudflared process,
        /// and initiates one fresh Cloudflare connection attempt.
        /// </summary>
        public void Restart()
        {
            lock (_lock)
            {
                if (_disposed) return;
                Log("Manual Cloudflare Tunnel restart requested.");
                _retryCount = 0;
                _rateLimitHits = 0;
                _consecutiveHealthFailures = 0;
                _cooldownUntilUtc = DateTime.MinValue;
                _nextRetryUtc = null;
                _lastFailureReason = null;
                CancelRetryTimerLocked();
                StartHeartbeatTimerLocked();
                StartWatchdogTimerLocked();
                BeginConnectLocked(TunnelState.CONNECTING);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_disposed) return;
                Interlocked.Increment(ref _generation);
                CancelRetryTimerLocked();
                CancelStartupGuardLocked();
                StopOwnedProcessOnlyLocked();
                _publicUrl = null;
                _pendingCandidateUrl = null;
                _processId = null;
                _usingWindowsService = false;
                _nextRetryUtc = null;
                TransitionStateLocked(TunnelState.STOPPED);
            }
            FireCaptured("");
        }

        private void BeginConnectLocked(TunnelState initialState)
        {
            int gen = Interlocked.Increment(ref _generation);
            CancelRetryTimerLocked();
            CancelStartupGuardLocked();
            StopOwnedProcessOnlyLocked();

            _publicUrl = null;
            _pendingCandidateUrl = null;
            _edgeRegisteredGen = 0;
            _verificationStartedGen = 0;
            _lastStderrError = null;
            _lastStderrErrorGen = 0;
            _processId = null;
            _usingWindowsService = false;
            _consecutiveHealthFailures = 0;
            _nextRetryUtc = null;
            TransitionStateLocked(initialState);

            string token, customUrl, mode;
            ReadConfig(out token, out customUrl, out mode);

            // Prefer Production Named Tunnel whenever configured (token or running cloudflared Windows service + hostname)
            bool hasToken = !string.IsNullOrWhiteSpace(token);
            bool hasHostname = IsValidPublicTunnelUrl(customUrl);
            int? servicePid;
            bool serviceRunning = TryDetectRunningCloudflaredService(out servicePid);

            if (mode != "quick" && hasHostname && serviceRunning)
            {
                _currentMode = "production_named";
                _modeLabel = "Cloudflare Named Tunnel (Windows Service)";
                _usingWindowsService = true;
                _processId = servicePid;
                Log("Detected running Cloudflare Windows Service (PID " + (servicePid.HasValue ? servicePid.Value.ToString(CultureInfo.InvariantCulture) : "service") + ") -> verifying " + customUrl + "...");
                _verificationStartedGen = gen;
                StartEndpointVerificationAsync(customUrl, gen, 500);
                return;
            }

            if (mode != "quick" && hasToken)
            {
                _currentMode = "production_named";
                _modeLabel = "Cloudflare Named Tunnel (Production)";
                StartCloudflareNamedTunnelLocked(token, customUrl, gen);
                return;
            }

            if (mode == "named" && !hasToken && !serviceRunning)
            {
                ScheduleCloudflareRetryLocked(gen, "Production Named Tunnel selected, but no Cloudflare Tunnel Token or running cloudflared Windows Service is configured.", false, 0);
                return;
            }

            // Development Mode: Cloudflare Quick Tunnel (trycloudflare.com)
            _currentMode = "development_quick";
            _modeLabel = "Cloudflare Quick Tunnel (Development)";
            StartCloudflareQuickTunnelLocked(gen);
        }

        private void StartCloudflareNamedTunnelLocked(string token, string staticUrl, int gen)
        {
            if (_disposed || _generation != gen) return;

            // Kill only orphan cloudflared.exe if not running as Windows service
            KillOrphanCloudflaredProcesses();

            string exePath = EnsureCloudflared();
            if (string.IsNullOrEmpty(exePath))
            {
                ScheduleCloudflareRetryLocked(gen, "cloudflared.exe could not be found or downloaded.", false, 0);
                return;
            }

            try
            {
                Log("Starting Cloudflare Named Tunnel (Production) -> 127.0.0.1:" + _localPort + "...");
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

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.OutputDataReceived += (s, e) => HandleNamedTunnelOutput(e.Data, staticUrl, gen);
                proc.ErrorDataReceived += (s, e) => HandleNamedTunnelOutput(e.Data, staticUrl, gen);
                proc.Exited += (s, e) => HandleProcessExited(gen, "Cloudflare Named Tunnel process exited unexpectedly");

                proc.Start();
                _tunnelProcess = proc;
                try { _processId = proc.Id; } catch { _processId = null; }
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                StartStartupGuardLocked(gen, 45000, "Cloudflare Named Tunnel timed out waiting for edge registration");
                FireStateChangedLocked();
            }
            catch (Exception ex)
            {
                ScheduleCloudflareRetryLocked(gen, "Failed to start Cloudflare Named Tunnel: " + ex.Message, false, 0);
            }
        }

        private void HandleNamedTunnelOutput(string line, string staticUrl, int gen)
        {
            if (string.IsNullOrEmpty(line)) return;
            AppendTunnelLog("[CF-Named] " + line);

            bool shouldVerify = false;
            lock (_lock)
            {
                if (_disposed || _generation != gen) return;

                if (line.IndexOf("Provided Tunnel token is not valid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ScheduleCloudflareRetryLocked(gen, "Cloudflare Named Tunnel token is invalid or unauthorized.", false, 0);
                    return;
                }

                int retryAfterSec;
                if (IsCloudflareRateLimit(line, out retryAfterSec))
                {
                    ScheduleCloudflareRetryLocked(gen, "Cloudflare rate limit (HTTP 429)", true, retryAfterSec);
                    return;
                }

                string errSummary = ExtractStderrSummary(line);
                if (!string.IsNullOrEmpty(errSummary))
                {
                    _lastStderrError = errSummary;
                    _lastStderrErrorGen = gen;
                }

                if (line.IndexOf("Registered tunnel connection", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (IsValidPublicTunnelUrl(staticUrl))
                    {
                        if (_state != TunnelState.CONNECTED && !string.Equals(_pendingCandidateUrl, staticUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            _pendingCandidateUrl = staticUrl;
                            shouldVerify = true;
                        }
                    }
                    else
                    {
                        // Token connected cleanly even without static hostname entered locally
                        CancelStartupGuardLocked();
                        _lastSuccessfulHealthCheckUtc = DateTime.UtcNow;
                        _lastFailureReason = null;
                        _retryCount = 0;
                        _rateLimitHits = 0;
                        _consecutiveHealthFailures = 0;
                        _nextRetryUtc = null;
                        TransitionStateLocked(TunnelState.CONNECTED);
                        Log("Cloudflare Named Tunnel edge registered (configure Static Hostname URL in Settings to enable public health probe).");
                    }
                }
            }

            if (shouldVerify)
            {
                StartEndpointVerificationAsync(staticUrl, gen, 2000);
            }
        }

        private void StartCloudflareQuickTunnelLocked(int gen)
        {
            if (_disposed || _generation != gen) return;

            KillOrphanCloudflaredProcesses();

            string exePath = EnsureCloudflared();
            if (string.IsNullOrEmpty(exePath))
            {
                ScheduleCloudflareRetryLocked(gen, "cloudflared.exe unavailable (download failed or offline).", false, 0);
                return;
            }

            try
            {
                Log("Starting Cloudflare Quick Tunnel (Development) -> http://127.0.0.1:" + _localPort + "...");
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

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.OutputDataReceived += (s, e) => HandleQuickTunnelOutput(e.Data, gen);
                proc.ErrorDataReceived += (s, e) => HandleQuickTunnelOutput(e.Data, gen);
                proc.Exited += (s, e) => HandleProcessExited(gen, "Cloudflare Quick Tunnel process crashed or exited");

                proc.Start();
                _tunnelProcess = proc;
                try { _processId = proc.Id; } catch { _processId = null; }
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                StartStartupGuardLocked(gen, 45000, "Cloudflare Quick Tunnel timed out before verifying public URL");
                FireStateChangedLocked();
            }
            catch (Exception ex)
            {
                ScheduleCloudflareRetryLocked(gen, "Failed to launch cloudflared.exe: " + ex.Message, false, 0);
            }
        }

        private void HandleQuickTunnelOutput(string line, int gen)
        {
            if (string.IsNullOrEmpty(line)) return;
            AppendTunnelLog("[CF-Quick] " + line);

            string candidateToVerify = null;
            lock (_lock)
            {
                if (_disposed || _generation != gen) return;

                int retryAfterSec;
                if (IsCloudflareRateLimit(line, out retryAfterSec))
                {
                    ScheduleCloudflareRetryLocked(gen, "Cloudflare Quick Tunnel rate-limited (HTTP 429)", true, retryAfterSec);
                    return;
                }

                string errSummary = ExtractStderrSummary(line);
                if (!string.IsNullOrEmpty(errSummary))
                {
                    _lastStderrError = errSummary;
                    _lastStderrErrorGen = gen;
                }

                var match = CloudflareQuickUrlRegex.Match(line);
                if (match.Success)
                {
                    string captured = match.Value.Trim().TrimEnd('/');
                    if (!IsValidPublicTunnelUrl(captured)) return;

                    if (!string.Equals(_publicUrl, captured, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(_pendingCandidateUrl, captured, StringComparison.OrdinalIgnoreCase))
                    {
                        _pendingCandidateUrl = captured;
                        Log("Cloudflare issued URL " + captured + " — waiting for edge registration before endpoint verification...");
                        if (_edgeRegisteredGen == gen && _verificationStartedGen != gen)
                        {
                            _verificationStartedGen = gen;
                            candidateToVerify = captured;
                        }
                    }
                }

                if (line.IndexOf("Registered tunnel connection", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _edgeRegisteredGen = gen;
                    if (!string.IsNullOrEmpty(_pendingCandidateUrl) && _verificationStartedGen != gen)
                    {
                        _verificationStartedGen = gen;
                        candidateToVerify = _pendingCandidateUrl;
                        Log("Cloudflare edge registered — verifying actual endpoint availability (" + candidateToVerify + ")...");
                    }
                }
            }

            if (!string.IsNullOrEmpty(candidateToVerify))
            {
                // Initial delay avoids querying Windows DNS before Cloudflare propagates the new trycloudflare.com record
                StartEndpointVerificationAsync(candidateToVerify, gen, 3500);
            }
        }

        private void StartEndpointVerificationAsync(string url, int gen, int initialDelayMs)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (initialDelayMs > 0) Thread.Sleep(initialDelayMs);

                // Wait for Cloudflare's authoritative DNS to publish the new hostname BEFORE querying Windows DNS
                // (prevents Windows DNS Client from negatively caching NXDOMAIN for 30-60s).
                WaitForCloudflareDnsReady(url, gen, 12000);

                string lastErr = "Endpoint verification failed";
                bool verified = false;

                // Patiently verify across Cloudflare edge & local ISP DNS propagation
                for (int attempt = 1; attempt <= 7; attempt++)
                {
                    lock (_lock)
                    {
                        if (_disposed || _generation != gen) return;
                    }
                    if (ProbePublicTunnel(url, out lastErr))
                    {
                        verified = true;
                        break;
                    }
                    if (attempt < 7) Thread.Sleep(3500);
                }

                bool notifyCaptured = false;
                lock (_lock)
                {
                    if (_disposed || _generation != gen) return;

                    if (verified)
                    {
                        CancelStartupGuardLocked();
                        _publicUrl = url;
                        _pendingCandidateUrl = null;
                        _lastSuccessfulHealthCheckUtc = DateTime.UtcNow;
                        _lastFailureReason = null;
                        _retryCount = 0;
                        _rateLimitHits = 0;
                        _consecutiveHealthFailures = 0;
                        _nextRetryUtc = null;
                        TransitionStateLocked(TunnelState.CONNECTED);
                        Log("⚡ Cloudflare Tunnel verified & CONNECTED: " + url + " (PID " + (_processId.HasValue ? _processId.Value.ToString(CultureInfo.InvariantCulture) : "n/a") + ")");
                        notifyCaptured = true;
                    }
                    else
                    {
                        _pendingCandidateUrl = null;
                        ScheduleCloudflareRetryLocked(gen, "Cloudflare URL unreachable during endpoint verification: " + lastErr, false, 0);
                    }
                }

                if (notifyCaptured)
                {
                    RegisterWithCloud(url);
                    FireCaptured(url);
                }
            });
        }

        private void HandleProcessExited(int gen, string reason)
        {
            bool shouldNotifyClear = false;
            lock (_lock)
            {
                if (_disposed || _generation != gen) return;
                int? oldPid = _processId;
                _tunnelProcess = null;
                _processId = null;
                bool hadUrl = !string.IsNullOrEmpty(_publicUrl);
                _publicUrl = null;
                _pendingCandidateUrl = null;
                shouldNotifyClear = hadUrl;
                string detail = (_lastStderrErrorGen == gen && !string.IsNullOrWhiteSpace(_lastStderrError))
                    ? (_lastStderrError + (oldPid.HasValue ? " (PID " + oldPid.Value + ")" : ""))
                    : (reason + (oldPid.HasValue ? " (PID " + oldPid.Value + ")" : ""));
                ScheduleCloudflareRetryLocked(gen, detail, false, 0);
            }

            if (shouldNotifyClear)
            {
                DisconnectFromCloud();
                FireCaptured("");
            }
        }

        private void StartStartupGuardLocked(int gen, int timeoutMs, string timeoutReason)
        {
            CancelStartupGuardLocked();
            _startupGuardTimer = new Timer(_ =>
            {
                lock (_lock)
                {
                    if (_disposed || _generation != gen) return;
                    if (_state == TunnelState.CONNECTING || _state == TunnelState.RECONNECTING)
                    {
                        if (string.IsNullOrEmpty(_publicUrl))
                        {
                            ScheduleCloudflareRetryLocked(gen, timeoutReason, false, 0);
                        }
                    }
                }
            }, null, timeoutMs, Timeout.Infinite);
        }

        private void CancelStartupGuardLocked()
        {
            if (_startupGuardTimer != null)
            {
                try { _startupGuardTimer.Dispose(); } catch { }
                _startupGuardTimer = null;
            }
        }

        private void CancelRetryTimerLocked()
        {
            if (_retryTimer != null)
            {
                try { _retryTimer.Dispose(); } catch { }
                _retryTimer = null;
            }
        }

        private void ScheduleCloudflareRetryLocked(int expectedGen, string failureReason, bool isRateLimit, int retryAfterSeconds)
        {
            if (_disposed || _generation != expectedGen) return;

            // Advance generation BEFORE stopping the process so synchronous Exited callbacks cannot re-enter and double-count
            int scheduledGen = Interlocked.Increment(ref _generation);
            CancelStartupGuardLocked();
            CancelRetryTimerLocked();
            StopOwnedProcessOnlyLocked();

            _publicUrl = null;
            _pendingCandidateUrl = null;
            _edgeRegisteredGen = 0;
            _verificationStartedGen = 0;
            _lastStderrError = null;
            _lastStderrErrorGen = 0;
            _processId = null;
            _usingWindowsService = false;
            _lastFailureReason = failureReason;
            _retryCount++;

            int delayMs;
            TunnelState nextState;
            if (isRateLimit)
            {
                _rateLimitHits++;
                if (retryAfterSeconds > 0)
                {
                    delayMs = retryAfterSeconds * 1000;
                }
                else
                {
                    int[] quickCooldownSecs = new[] { 35, 75, 150, 300 };
                    int baseSec = quickCooldownSecs[Math.Min(_rateLimitHits - 1, quickCooldownSecs.Length - 1)];
                    double jitter = 0.85 + (_rng.NextDouble() * 0.30);
                    delayMs = (int)Math.Round(baseSec * 1000.0 * jitter);
                }
                _cooldownUntilUtc = DateTime.UtcNow.AddMilliseconds(delayMs);
                nextState = TunnelState.COOLDOWN;
            }
            else
            {
                delayMs = ComputeBackoffWithJitterMs(_retryCount, 3000, 120000);
                bool netUp = true;
                try { netUp = NetworkInterface.GetIsNetworkAvailable(); } catch { }
                nextState = (!netUp || _retryCount >= 3) ? TunnelState.OFFLINE : TunnelState.RECONNECTING;
            }

            _nextRetryUtc = DateTime.UtcNow.AddMilliseconds(delayMs);
            TransitionStateLocked(nextState);

            Log(string.Format(
                CultureInfo.InvariantCulture,
                "{0} | Reason: {1} | Retry #{2} scheduled in {3:F1}s (Cloudflare-only, no fallback).",
                FormatDisplayStatus(nextState),
                failureReason,
                _retryCount,
                delayMs / 1000.0));
            if (isRateLimit && _currentMode == "development_quick" && _rateLimitHits <= 2)
            {
                Log("Tip: Add a free Cloudflare Named Tunnel token in Tunnel Settings for permanent zero-429 uptime.");
            }

            _retryTimer = new Timer(_ =>
            {
                lock (_lock)
                {
                    if (_disposed || _generation != scheduledGen) return;
                    if (_state == TunnelState.COOLDOWN && DateTime.UtcNow < _cooldownUntilUtc)
                    {
                        int remMs = (int)Math.Max(1000, (_cooldownUntilUtc - DateTime.UtcNow).TotalMilliseconds);
                        _retryTimer.Change(remMs, Timeout.Infinite);
                        return;
                    }
                    BeginConnectLocked(TunnelState.RECONNECTING);
                }
            }, null, delayMs, Timeout.Infinite);
        }

        private void StartHeartbeatTimerLocked()
        {
            if (_heartbeatTimer != null) return;
            _heartbeatTimer = new Timer(_ =>
            {
                string url = null;
                lock (_lock)
                {
                    if (!_disposed && (_state == TunnelState.CONNECTED || _state == TunnelState.DEGRADED))
                    {
                        url = _publicUrl;
                    }
                }
                if (IsValidPublicTunnelUrl(url))
                {
                    RegisterWithCloud(url);
                }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private void StartWatchdogTimerLocked()
        {
            if (_watchdogTimer != null) return;
            _watchdogTimer = new Timer(_ => RunWatchdogCheck(), null, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(25));
        }

        public void RunWatchdogCheck()
        {
            int probeGen;
            string probeUrl;
            bool usingService;
            Process procRef;

            lock (_lock)
            {
                if (_disposed || _probeInFlight) return;
                if (_state != TunnelState.CONNECTED && _state != TunnelState.DEGRADED) return;
                if (!IsValidPublicTunnelUrl(_publicUrl)) return;

                _probeInFlight = true;
                probeGen = _generation;
                probeUrl = _publicUrl;
                usingService = _usingWindowsService;
                procRef = _tunnelProcess;
            }

            try
            {
                if (!ProbeLocalServer()) return;

                // If managed process vanished silently, let supervisor recover immediately
                if (!usingService && procRef != null)
                {
                    bool exited = false;
                    try { exited = procRef.HasExited; } catch { exited = true; }
                    if (exited)
                    {
                        HandleProcessExited(probeGen, "Watchdog detected terminated cloudflared.exe process");
                        return;
                    }
                }

                string err;
                if (ProbePublicTunnel(probeUrl, out err))
                {
                    ApplyHealthCheckResult(probeGen, probeUrl, true, null);
                    return;
                }

                // Mark DEGRADED first — do NOT tear down a working tunnel on a single transient blip
                ApplyHealthCheckResult(probeGen, probeUrl, false, err);

                // Short debounce re-check so temporary jitter recovers cleanly without breaking uploads
                Thread.Sleep(4000);
                lock (_lock)
                {
                    if (_disposed || _generation != probeGen || !string.Equals(_publicUrl, probeUrl, StringComparison.OrdinalIgnoreCase))
                        return;
                }

                if (ProbePublicTunnel(probeUrl, out err))
                {
                    ApplyHealthCheckResult(probeGen, probeUrl, true, null);
                    return;
                }

                ApplyHealthCheckResult(probeGen, probeUrl, false, err);
            }
            catch { }
            finally
            {
                lock (_lock) { _probeInFlight = false; }
            }
        }

        /// <summary>
        /// Applies a health-check result strictly guarded by (probeGen, probeUrl).
        /// Any stale event from an older generation or previous URL is completely discarded.
        /// Preserves active customer uploads by requiring extra consecutive failures before teardown.
        /// </summary>
        public bool ApplyHealthCheckResult(int probeGen, string probeUrl, bool healthy, string failureReason)
        {
            bool shouldDisconnectAndClear = false;
            lock (_lock)
            {
                if (_disposed) return false;
                if (probeGen != _generation) return false;
                if (!string.Equals(probeUrl ?? "", _publicUrl ?? "", StringComparison.OrdinalIgnoreCase)) return false;
                if (_state != TunnelState.CONNECTED && _state != TunnelState.DEGRADED) return false;

                if (healthy)
                {
                    bool wasDegraded = _state == TunnelState.DEGRADED;
                    _consecutiveHealthFailures = 0;
                    _lastSuccessfulHealthCheckUtc = DateTime.UtcNow;
                    _lastFailureReason = null;
                    if (wasDegraded)
                    {
                        TransitionStateLocked(TunnelState.CONNECTED);
                        Log("Cloudflare Tunnel recovered cleanly without restart (" + probeUrl + ").");
                    }
                    else
                    {
                        FireStateChangedLocked();
                    }
                    return true;
                }

                _consecutiveHealthFailures++;
                _lastFailureReason = string.IsNullOrWhiteSpace(failureReason) ? "Public health check failed" : failureReason;

                int activeUploads = 0;
                if (_db != null)
                {
                    try { activeUploads = _db.CountActiveUploadSessions(); } catch { }
                }
                int failureThreshold = activeUploads > 0 ? 3 : 2;

                if (_consecutiveHealthFailures < failureThreshold)
                {
                    TransitionStateLocked(TunnelState.DEGRADED);
                    Log(string.Format(
                        CultureInfo.InvariantCulture,
                        "Cloudflare health probe warning ({0}/{1}, active uploads: {2}): {3}. Keeping tunnel alive while verifying...",
                        _consecutiveHealthFailures, failureThreshold, activeUploads, _lastFailureReason));
                    return true;
                }

                Log("Cloudflare endpoint confirmed unreachable (" + _lastFailureReason + "). Reconnecting via Cloudflare...");
                shouldDisconnectAndClear = true;
                ScheduleCloudflareRetryLocked(probeGen, "Health check failed: " + _lastFailureReason, false, 0);
            }

            if (shouldDisconnectAndClear)
            {
                DisconnectFromCloud();
                FireCaptured("");
            }
            return true;
        }

        /// <summary>
        /// Deterministic test hooks used by --self-test to verify rate-limit COOLDOWN,
        /// stale health-check rejection, and temporary degradation recovery without mutating production config.
        /// </summary>
        public void InjectStateForSelfTest(TunnelState state, string publicUrl, int? fakePid)
        {
            lock (_lock)
            {
                CancelRetryTimerLocked();
                CancelStartupGuardLocked();
                Interlocked.Increment(ref _generation);
                _publicUrl = publicUrl;
                _processId = fakePid;
                _consecutiveHealthFailures = 0;
                _lastFailureReason = null;
                _lastStderrError = null;
                _lastStderrErrorGen = 0;
                _retryCount = 0;
                _rateLimitHits = 0;
                _nextRetryUtc = null;
                _cooldownUntilUtc = DateTime.MinValue;
                if (state == TunnelState.CONNECTED) _lastSuccessfulHealthCheckUtc = DateTime.UtcNow;
                TransitionStateLocked(state);
            }
        }

        public void SimulateRateLimitForSelfTest(string cloudflareLogLine)
        {
            int gen;
            lock (_lock) { gen = _generation; }
            HandleQuickTunnelOutput(cloudflareLogLine, gen);
        }

        public static bool TryDetectRunningCloudflaredService(out int? servicePid)
        {
            servicePid = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = "queryex cloudflared",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    if (p.ExitCode == 0 && output.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var m = ServicePidRegex.Match(output);
                        int pidVal;
                        if (m.Success && int.TryParse(m.Groups[1].Value, out pidVal) && pidVal > 0)
                        {
                            servicePid = pidVal;
                        }
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public bool InstallCloudflareWindowsService(string token, out string message)
        {
            message = null;
            string cleanToken = (token ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cleanToken))
            {
                message = "A Cloudflare Named Tunnel token is required to install the Windows service.";
                return false;
            }
            string exePath = EnsureCloudflared();
            if (string.IsNullOrEmpty(exePath))
            {
                message = "cloudflared.exe is not available.";
                return false;
            }
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "service install " + cleanToken,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    if (p.ExitCode == 0)
                    {
                        message = "Cloudflare Windows Service installed successfully.";
                        return true;
                    }
                    message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    return false;
                }
            }
            catch (Exception ex)
            {
                message = "Service install requires Administrator privileges: " + ex.Message;
                return false;
            }
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
                    wc.Headers.Add("User-Agent", "ScanDrop-CloudflareSupervisor");
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

        private void WaitForCloudflareDnsReady(string baseUrl, int gen, int maxWaitMs)
        {
            Uri uri;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out uri) || string.IsNullOrEmpty(uri.Host)) return;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);
            while (DateTime.UtcNow < deadline)
            {
                lock (_lock)
                {
                    if (_disposed || _generation != gen) return;
                }
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create("https://cloudflare-dns.com/dns-query?name=" + Uri.EscapeDataString(uri.Host) + "&type=A");
                    req.Method = "GET";
                    req.Accept = "application/dns-json";
                    req.Timeout = 3500;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        string json = sr.ReadToEnd();
                        if (json.IndexOf("\"Status\":0", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            json.IndexOf("\"Answer\"", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Thread.Sleep(1500); // Brief pause for ISP recursive resolver sync
                            return;
                        }
                    }
                }
                catch { return; } // If DoH is blocked by corporate proxy, proceed directly to HTTP probes
                Thread.Sleep(1500);
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
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.UserAgent = "ScanDrop-CloudflareWatchdog/2.0";
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
                string slug = "quickprint";
                string shopName = "QuickPrint Xerox & Stationery";
                if (_db != null)
                {
                    try
                    {
                        var primary = _db.GetPrimaryShop();
                        if (primary != null)
                        {
                            if (!string.IsNullOrWhiteSpace(primary.Slug)) slug = primary.Slug;
                            if (!string.IsNullOrWhiteSpace(primary.ShopName)) shopName = primary.ShopName;
                        }
                    }
                    catch { }
                }
                string shopId, pairingCode, deviceId, deviceSecret;
                lock (_lock)
                {
                    shopId = _shopId;
                    pairingCode = _pairingCode;
                    deviceId = _deviceId ?? "";
                    deviceSecret = _deviceSecret ?? "";
                }

                string payload = ser.Serialize(new
                {
                    shopId = shopId,
                    shopSlug = slug,
                    shopName = shopName,
                    pairingCode = pairingCode,
                    deviceId = deviceId,
                    deviceSecret = deviceSecret,
                    appVersion = AutoUpdater.CurrentVersion,
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
                string slug = "quickprint";
                if (_db != null)
                {
                    try
                    {
                        var primary = _db.GetPrimaryShop();
                        if (primary != null && !string.IsNullOrWhiteSpace(primary.Slug)) slug = primary.Slug;
                    }
                    catch { }
                }
                string shopId, pairingCode, deviceId, deviceSecret;
                lock (_lock)
                {
                    shopId = _shopId;
                    pairingCode = _pairingCode;
                    deviceId = _deviceId ?? "";
                    deviceSecret = _deviceSecret ?? "";
                }

                string payload = ser.Serialize(new
                {
                    shopId = shopId,
                    shopSlug = slug,
                    pairingCode = pairingCode,
                    deviceId = deviceId,
                    deviceSecret = deviceSecret
                });
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
            int? svcPid;
            TryDetectRunningCloudflaredService(out svcPid);
            try
            {
                foreach (var p in Process.GetProcessesByName("cloudflared"))
                {
                    try
                    {
                        if (svcPid.HasValue && p.Id == svcPid.Value) continue; // Never kill Production Windows Service
                        p.Kill();
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void StopOwnedProcessOnlyLocked()
        {
            if (_usingWindowsService)
            {
                _tunnelProcess = null;
                return;
            }
            var proc = _tunnelProcess;
            _tunnelProcess = null;
            if (proc != null)
            {
                try { proc.EnableRaisingEvents = false; } catch { }
                try
                {
                    if (!proc.HasExited)
                        proc.Kill();
                }
                catch { }
            }
        }

        private void TransitionStateLocked(TunnelState newState)
        {
            _state = newState;
            FireStateChangedLocked();
        }

        private void FireStateChangedLocked()
        {
            var h = OnStateChanged;
            if (h == null) return;
            var snap = new TunnelStatusSnapshot
            {
                State = _state,
                StateName = _state.ToString(),
                DisplayStatus = FormatDisplayStatus(_state),
                Mode = _currentMode,
                ModeLabel = _modeLabel,
                ProcessId = _processId,
                PublicUrl = _publicUrl,
                IsOnline = (_state == TunnelState.CONNECTED || _state == TunnelState.DEGRADED) && IsValidPublicTunnelUrl(_publicUrl),
                LastSuccessfulHealthCheckUtc = _lastSuccessfulHealthCheckUtc,
                LastFailureReason = _lastFailureReason,
                RetryCount = _retryCount,
                NextRetryUtc = _nextRetryUtc,
                Generation = _generation
            };
            ThreadPool.QueueUserWorkItem(_ => { try { h(snap); } catch { } });
        }

        private void AppendTunnelLog(string line)
        {
            try
            {
                string logDir = !string.IsNullOrEmpty(_logsDir) ? _logsDir : Path.Combine(_rootDir, "data");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "tunnel.log");
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
            AppendTunnelLog("[CF-Supervisor] " + msg);
            var h = OnStatusMessage;
            if (h != null) try { h(msg); } catch { }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                Interlocked.Increment(ref _generation);
                CancelRetryTimerLocked();
                CancelStartupGuardLocked();
                if (_heartbeatTimer != null) { _heartbeatTimer.Dispose(); _heartbeatTimer = null; }
                if (_watchdogTimer != null) { _watchdogTimer.Dispose(); _watchdogTimer = null; }
                StopOwnedProcessOnlyLocked();
                _state = TunnelState.STOPPED;
                _processId = null;
                _publicUrl = null;
            }
            try
            {
                if (_eventsHooked)
                {
                    SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                    NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
                }
            }
            catch { }
            DisconnectFromCloud();
        }
    }
}
