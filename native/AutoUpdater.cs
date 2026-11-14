using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

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
        public const string CurrentVersion = "3.3.0";
        private const string PrimaryManifestUrl = "https://scandropdone.netlify.app/version.json";
        private const string FallbackManifestUrl = "https://raw.githubusercontent.com/Shivamborn2shine/whatsapp/main/version.json";
        private const string PrimaryExeFallback = "https://scandropdone.netlify.app/downloads/ScanDrop.exe";
        private const string GitHubExeFallback = "https://raw.githubusercontent.com/Shivamborn2shine/whatsapp/main/downloads/ScanDrop.exe";

        public static UpdateCheckResult CheckForUpdate()
        {
            var result = new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                LatestVersion = CurrentVersion,
                UpdateAvailable = false
            };

            string json = FetchTextWithFallback(PrimaryManifestUrl, FallbackManifestUrl);
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

                    Version curV, latV;
                    if (Version.TryParse(CurrentVersion, out curV) && Version.TryParse(result.LatestVersion, out latV))
                        result.UpdateAvailable = latV > curV;
                    else
                        result.UpdateAvailable = !string.Equals(result.LatestVersion, CurrentVersion, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }

            return result;
        }

        public static bool ApplyUpdate(UpdateCheckResult info, Action beforeExit, out string error)
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

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "ScanDrop-Station/" + CurrentVersion);
                    try
                    {
                        wc.DownloadFile(url, stagingExe);
                    }
                    catch
                    {
                        wc.DownloadFile(fallback, stagingExe);
                    }
                }

                if (!File.Exists(stagingExe) || new FileInfo(stagingExe).Length < 15000)
                {
                    try { if (File.Exists(stagingExe)) File.Delete(stagingExe); } catch { }
                    error = "Downloaded update file is incomplete or invalid.";
                    return false;
                }

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

                string batPath = Path.Combine(dir, "apply-scandrop-update.bat");
                string bat = "@echo off\r\n" +
                             "timeout /t 2 /nobreak >nul\r\n" +
                             ":retry\r\n" +
                             "copy /y \"" + exeName + ".update\" \"" + exeName + "\" >nul 2>nul\r\n" +
                             "if errorlevel 1 (\r\n" +
                             "  timeout /t 1 /nobreak >nul\r\n" +
                             "  goto retry\r\n" +
                             ")\r\n" +
                             "del \"" + exeName + ".update\" >nul 2>nul\r\n" +
                             "start \"\" \"" + currentExe + "\" --no-browser\r\n" +
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

        public static bool SyncWorkspaceUi(string rootDir)
        {
            try
            {
                string wsDir = Path.Combine(rootDir, "workspace");
                string wsPath = Path.Combine(wsDir, "index.html");
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "ScanDrop-Station/" + CurrentVersion);
                    wc.Encoding = Encoding.UTF8;
                    string remoteHtml = wc.DownloadString("https://scandropdone.netlify.app/workspace/index.html");
                    if (!string.IsNullOrWhiteSpace(remoteHtml) && remoteHtml.Contains("Scan Drop"))
                    {
                        if (!Directory.Exists(wsDir)) Directory.CreateDirectory(wsDir);
                        string existing = File.Exists(wsPath) ? File.ReadAllText(wsPath, Encoding.UTF8) : "";
                        if (!string.Equals(existing, remoteHtml, StringComparison.Ordinal))
                        {
                            File.WriteAllText(wsPath, remoteHtml, Encoding.UTF8);
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static string ComputeSha256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string FetchTextWithFallback(string primary, string secondary)
        {
            foreach (var u in new[] { primary, secondary })
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                    var req = (HttpWebRequest)WebRequest.Create(u + "?t=" + DateTime.UtcNow.Ticks);
                    req.Method = "GET";
                    req.UserAgent = "ScanDrop-Station/" + CurrentVersion;
                    req.Timeout = 6000;
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

