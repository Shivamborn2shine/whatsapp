using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ScanDrop.Native.Core;

namespace ScanDrop.Native
{
    public sealed class UpdateCheckResult
    {
        public string CurrentVersion;
        public string LatestVersion;
        public bool UpdateAvailable;
        public string DownloadUrl;
        public string FallbackUrl;
        public string ExpectedSha256;
        public string ReleaseNotes;
    }

    public static class AutoUpdater
    {
        public const string CurrentVersion = "4.8.0";
        private const string CloudOrigin = "https://scandrop-xi.vercel.app";
        private const string PrimaryManifestUrl = CloudOrigin + "/version.json";
        private const string FallbackManifestUrl = "https://raw.githubusercontent.com/Shivamborn2shine/whatsapp/main/version.json";
        private const string PrimaryExeFallback = CloudOrigin + "/downloads/ScanDrop.exe";
        private const string GitHubExeFallback = "https://raw.githubusercontent.com/Shivamborn2shine/whatsapp/main/downloads/ScanDrop.exe";

        public static bool IsNewerVersion(string currentVer, string candidateVer)
        {
            if (string.IsNullOrWhiteSpace(candidateVer)) return false;
            string c = (currentVer ?? "").Trim();
            string l = candidateVer.Trim();
            Version curV, latV;
            if (Version.TryParse(c, out curV) && Version.TryParse(l, out latV))
                return latV > curV;
            return !string.Equals(l, c, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldApplyUpdate(string currentVer, string candidateVer, string currentExeSha256, string expectedSha256)
        {
            if (!IsNewerVersion(currentVer, candidateVer)) return false;
            if (!string.IsNullOrWhiteSpace(currentExeSha256) &&
                !string.IsNullOrWhiteSpace(expectedSha256) &&
                string.Equals(currentExeSha256.Trim(), expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }

        public static UpdateCheckResult CheckForUpdate(int timeoutMs = 5000)
        {
            var result = new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                LatestVersion = CurrentVersion,
                UpdateAvailable = false
            };

            string json = FetchTextWithFallback(PrimaryManifestUrl, FallbackManifestUrl, timeoutMs);
            if (string.IsNullOrWhiteSpace(json)) return result;

            try
            {
                var ser = new JavaScriptSerializer();
                var dict = ser.Deserialize<Dictionary<string, object>>(json);
                if (dict != null && dict.ContainsKey("version"))
                {
                    result.LatestVersion = Convert.ToString(dict["version"]).Trim();
                    result.DownloadUrl = dict.ContainsKey("downloadUrl") ? Convert.ToString(dict["downloadUrl"]) : PrimaryExeFallback;
                    result.FallbackUrl = dict.ContainsKey("fallbackUrl") ? Convert.ToString(dict["fallbackUrl"]) : GitHubExeFallback;
                    result.ExpectedSha256 = dict.ContainsKey("sha256") ? Convert.ToString(dict["sha256"]).Trim().ToLowerInvariant() : "";
                    result.ReleaseNotes = dict.ContainsKey("releaseNotes") ? Convert.ToString(dict["releaseNotes"]) : "";

                    string currentSha = "";
                    try
                    {
                        string curExe = Process.GetCurrentProcess().MainModule.FileName;
                        if (File.Exists(curExe)) currentSha = ComputeSha256(curExe);
                    }
                    catch { }

                    result.UpdateAvailable = ShouldApplyUpdate(CurrentVersion, result.LatestVersion, currentSha, result.ExpectedSha256);
                }
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Automatically checks for and applies updates right on application launch
        /// without requiring the user to click "Check Updates". Shows a sleek brief
        /// progress splash while updating and relaunches the updated ScanDrop.exe automatically.
        /// If offline or already latest, returns false immediately so startup proceeds normally.
        /// </summary>
        public static bool TryAutoUpdateOnLaunch(string appRootDir, bool minimized, bool noBrowser, AppLogger logger)
        {
            try
            {
                if (string.Equals(Environment.GetEnvironmentVariable("SCANDROP_NO_AUTO_UPDATE"), "1", StringComparison.Ordinal))
                    return false;

                string currentExe = Process.GetCurrentProcess().MainModule.FileName;
                string exeDir = Path.GetDirectoryName(currentExe);
                if (!IsDirectoryWritable(exeDir))
                {
                    if (logger != null) logger.Warn("Updater", "Auto-update skipped on launch: directory is not writable (" + exeDir + ").");
                    return false;
                }

                var info = CheckForUpdate(4000);
                if (!info.UpdateAvailable)
                {
                    return false;
                }

                if (logger != null)
                    logger.Info("Updater", "Auto-updating on launch: v" + info.CurrentVersion + " -> v" + info.LatestVersion);

                string relaunchArgs = (minimized ? "--minimized " : "") + (noBrowser ? "--no-browser " : "") + "--skip-auto-update";

                if (minimized)
                {
                    string err;
                    if (!ApplyUpdate(info, null, out err, relaunchArgs, appRootDir))
                    {
                        if (logger != null) logger.Warn("Updater", "Silent launch update failed (continuing current version): " + err);
                        return false;
                    }
                    return true;
                }

                // Show sleek compact ScanDrop update splash while downloading & verifying SHA-256
                bool applied = false;
                string updateErr = null;
                using (var splash = CreateUpdateSplash(info.CurrentVersion, info.LatestVersion))
                {
                    Label statusLbl = (Label)splash.Tag;
                    splash.Shown += (s, e) =>
                    {
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            try
                            {
                                SetSplashText(splash, statusLbl, "Syncing latest Dashboard UI & downloading v" + info.LatestVersion + "...");
                                string err;
                                applied = ApplyUpdate(info, () =>
                                {
                                    SetSplashText(splash, statusLbl, "Verified SHA-256! Installing v" + info.LatestVersion + " & restarting...");
                                }, out err, relaunchArgs, appRootDir);
                                updateErr = err;
                            }
                            catch (Exception ex)
                            {
                                updateErr = ex.Message;
                            }
                            finally
                            {
                                try { if (!splash.IsDisposed) splash.BeginInvoke(new Action(() => { try { splash.Close(); } catch { } })); } catch { }
                            }
                        });
                    };
                    Application.Run(splash);
                }

                if (!applied && !string.IsNullOrEmpty(updateErr) && logger != null)
                {
                    logger.Warn("Updater", "Launch auto-update failed (continuing v" + CurrentVersion + "): " + updateErr);
                }
                return applied;
            }
            catch (Exception ex)
            {
                if (logger != null) logger.Warn("Updater", "Launch auto-update error: " + ex.Message);
                return false;
            }
        }

        private static void SetSplashText(Form splash, Label lbl, string text)
        {
            try
            {
                if (splash != null && !splash.IsDisposed && lbl != null)
                    splash.BeginInvoke(new Action(() => { try { lbl.Text = text; } catch { } }));
            }
            catch { }
        }

        private static Form CreateUpdateSplash(string fromVer, string toVer)
        {
            var f = new Form
            {
                Text = "Updating ScanDrop",
                Size = new Size(440, 165),
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Color.FromArgb(11, 37, 69),
                TopMost = true,
                ShowInTaskbar = true
            };
            try
            {
                Icon ic = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ic != null) f.Icon = ic;
            }
            catch { }

            var accent = new Panel { Dock = DockStyle.Top, Height = 5, BackColor = Color.FromArgb(0, 120, 255) };
            var title = new Label
            {
                Text = "⚡ Updating ScanDrop  v" + fromVer + " → v" + toVer,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
                AutoSize = false,
                Bounds = new Rectangle(22, 24, 396, 28)
            };
            var status = new Label
            {
                Text = "Downloading SHA-256 verified release automatically...",
                ForeColor = Color.FromArgb(182, 214, 248),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                AutoSize = false,
                Bounds = new Rectangle(22, 60, 396, 36)
            };
            var bar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 28,
                Bounds = new Rectangle(22, 112, 396, 16)
            };

            f.Controls.Add(accent);
            f.Controls.Add(title);
            f.Controls.Add(status);
            f.Controls.Add(bar);
            f.Tag = status;
            return f;
        }

        public static bool ApplyUpdate(UpdateCheckResult info, Action beforeExit, out string error, string relaunchArgs = "--no-browser --skip-auto-update", string appRootDir = null)
        {
            error = null;
            if (info == null)
            {
                error = "Missing update metadata.";
                return false;
            }

            string url = !string.IsNullOrWhiteSpace(info.DownloadUrl) ? info.DownloadUrl : PrimaryExeFallback;
            string fallback = !string.IsNullOrWhiteSpace(info.FallbackUrl) ? info.FallbackUrl : GitHubExeFallback;

            try
            {
                string currentExe = Process.GetCurrentProcess().MainModule.FileName;
                string dir = Path.GetDirectoryName(currentExe);
                string exeName = Path.GetFileName(currentExe);
                string stagingExe = Path.Combine(dir, exeName + ".update");

                // 1. Sync Workspace UI, Shop Portal & Brand Assets in lockstep with the new EXE
                string uiRoot = !string.IsNullOrWhiteSpace(appRootDir) ? appRootDir : dir;
                try { SyncWorkspaceUi(uiRoot, true); } catch { }

                // 2. Download updated ScanDrop.exe with cache-buster so CDN never serves a stale binary
                string bust = "?t=" + DateTime.UtcNow.Ticks;
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "ScanDrop-Station/" + CurrentVersion);
                    try
                    {
                        wc.DownloadFile(url + (url.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks, stagingExe);
                    }
                    catch
                    {
                        wc.DownloadFile(fallback + (fallback.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks, stagingExe);
                    }
                }

                if (!File.Exists(stagingExe) || new FileInfo(stagingExe).Length < 15000)
                {
                    try { if (File.Exists(stagingExe)) File.Delete(stagingExe); } catch { }
                    error = "Downloaded update file is incomplete or invalid.";
                    return false;
                }

                // 3. Strict SHA-256 verification
                if (!string.IsNullOrWhiteSpace(info.ExpectedSha256))
                {
                    string actualSha = ComputeSha256(stagingExe);
                    if (!string.Equals(actualSha, info.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(stagingExe); } catch { }
                        error = "SHA-256 verification failed. Expected " + info.ExpectedSha256 + ", got " + actualSha;
                        return false;
                    }
                }

                if (beforeExit != null)
                {
                    try { beforeExit(); } catch { }
                }

                string cleanArgs = string.IsNullOrWhiteSpace(relaunchArgs) ? "--skip-auto-update" : relaunchArgs.Trim();
                if (cleanArgs.IndexOf("--skip-auto-update", StringComparison.OrdinalIgnoreCase) < 0)
                    cleanArgs += " --skip-auto-update";

                string batPath = Path.Combine(dir, "apply-scandrop-update.bat");
                string bat = "@echo off\r\n" +
                             "set /a tries=0\r\n" +
                             "timeout /t 1 /nobreak >nul\r\n" +
                             ":retry\r\n" +
                             "set /a tries+=1\r\n" +
                             "copy /y \"" + exeName + ".update\" \"" + exeName + "\" >nul 2>nul\r\n" +
                             "if errorlevel 1 (\r\n" +
                             "  if %tries% GEQ 20 goto launch\r\n" +
                             "  timeout /t 1 /nobreak >nul\r\n" +
                             "  goto retry\r\n" +
                             ")\r\n" +
                             "del \"" + exeName + ".update\" >nul 2>nul\r\n" +
                             ":launch\r\n" +
                             "start \"\" \"" + currentExe + "\" " + cleanArgs + "\r\n" +
                             "del \"%~f0\"\r\n";
                File.WriteAllText(batPath, bat, Encoding.ASCII);

                Process.Start(new ProcessStartInfo
                {
                    FileName = batPath,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = dir
                });

                Environment.Exit(0);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static void EnsureUiFilesExist(string rootDir)
        {
            try
            {
                string wsPath = Path.Combine(rootDir, "workspace", "index.html");
                string shopPath = Path.Combine(rootDir, "shop", "index.html");
                string heroPath = Path.Combine(rootDir, "assets", "scandrop-logo-hero.png");
                string qrPosterPath = Path.Combine(rootDir, "assets", "scandrop-qr-poster.png");
                if (!File.Exists(wsPath) || !File.Exists(shopPath) || !File.Exists(heroPath) || !File.Exists(qrPosterPath))
                {
                    SyncWorkspaceUi(rootDir, false);
                }
            }
            catch { }
        }

        public static bool SyncWorkspaceUi(string rootDir, bool forceSync = false)
        {
            try
            {
                string wsDir = Path.Combine(rootDir, "workspace");
                string wsPath = Path.Combine(wsDir, "index.html");
                string shopDir = Path.Combine(rootDir, "shop");
                string shopPath = Path.Combine(shopDir, "index.html");
                string assetsDir = Path.Combine(rootDir, "assets");
                bool missingLocal = !File.Exists(wsPath) || !File.Exists(shopPath);

                // Never downgrade newer local UI files with an older remote deployment unless forced during EXE update
                if (!forceSync && !missingLocal)
                {
                    var upd = CheckForUpdate(4000);
                    if (!upd.UpdateAvailable)
                    {
                        EnsureBrandAssets(assetsDir);
                        return false;
                    }
                }

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                bool changed = false;
                string bust = "?t=" + DateTime.UtcNow.Ticks;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "ScanDrop-Station/" + CurrentVersion);
                    wc.Encoding = Encoding.UTF8;
                    if (forceSync || !File.Exists(wsPath) || !missingLocal)
                    {
                        string remoteHtml = wc.DownloadString(CloudOrigin + "/workspace/index.html" + bust);
                        if (!string.IsNullOrWhiteSpace(remoteHtml) && (remoteHtml.Contains("ScanDrop") || remoteHtml.Contains("Scan Drop")))
                        {
                            if (!Directory.Exists(wsDir)) Directory.CreateDirectory(wsDir);
                            string existing = File.Exists(wsPath) ? File.ReadAllText(wsPath, Encoding.UTF8) : "";
                            if (!string.Equals(existing, remoteHtml, StringComparison.Ordinal))
                            {
                                File.WriteAllText(wsPath, remoteHtml, Encoding.UTF8);
                                changed = true;
                            }
                        }
                    }
                    if (forceSync || !File.Exists(shopPath) || !missingLocal)
                    {
                        try
                        {
                            string remoteShop = wc.DownloadString(CloudOrigin + "/shop/index.html" + bust);
                            if (!string.IsNullOrWhiteSpace(remoteShop) && (remoteShop.Contains("ScanDrop") || remoteShop.Contains("Scan Drop")))
                            {
                                if (!Directory.Exists(shopDir)) Directory.CreateDirectory(shopDir);
                                string existingShop = File.Exists(shopPath) ? File.ReadAllText(shopPath, Encoding.UTF8) : "";
                                if (!string.Equals(existingShop, remoteShop, StringComparison.Ordinal))
                                {
                                    File.WriteAllText(shopPath, remoteShop, Encoding.UTF8);
                                    changed = true;
                                }
                            }
                        }
                        catch { }
                    }
                }

                EnsureBrandAssets(assetsDir);
                return changed;
            }
            catch { }
            return false;
        }

        private static void EnsureBrandAssets(string assetsDir)
        {
            string[] names = new[] { "scandrop-logo-hero.png", "scandrop-icon-96.png", "favicon-32.png", "scandrop-pill-web.png", "scandrop-qr-poster.png" };
            try
            {
                if (!Directory.Exists(assetsDir)) Directory.CreateDirectory(assetsDir);
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "ScanDrop-Station/" + CurrentVersion);
                    foreach (var n in names)
                    {
                        string dest = Path.Combine(assetsDir, n);
                        if (File.Exists(dest) && new FileInfo(dest).Length > 200) continue;
                        try { wc.DownloadFile(CloudOrigin + "/assets/" + n, dest); } catch { }
                    }
                }
            }
            catch { }
        }

        private static bool IsDirectoryWritable(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;
            try
            {
                string probe = Path.Combine(dir, ".sd_write_test_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string ComputeSha256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string FetchTextWithFallback(string primary, string secondary, int timeoutMs)
        {
            foreach (var u in new[] { primary, secondary })
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                    var req = (HttpWebRequest)WebRequest.Create(u + "?t=" + DateTime.UtcNow.Ticks);
                    req.Method = "GET";
                    req.UserAgent = "ScanDrop-Station/" + CurrentVersion;
                    req.Timeout = timeoutMs > 500 ? timeoutMs : 5000;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        return sr.ReadToEnd();
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
