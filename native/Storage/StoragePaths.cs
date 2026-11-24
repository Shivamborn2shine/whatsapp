using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ScanDrop.Native.Storage
{
    /// <summary>
    /// Manages the PRD Section 6 standardized local directory hierarchy:
    ///   ScanDrop/
    ///     database/
    ///     uploads/temp/
    ///     uploads/received/
    ///     uploads/quarantine/
    ///     converted/
    ///     previews/
    ///     logs/
    ///     config/
    /// Defaults to C:\ProgramData\ScanDrop when writable, falling back gracefully to %LOCALAPPDATA%\ScanDrop.
    /// </summary>
    public sealed class StoragePaths
    {
        public string AppRootDir { get; private set; }
        public string DataRootDir { get; private set; }
        public string DatabaseDir { get; private set; }
        public string DatabaseFile { get; private set; }
        public string UploadsDir { get; private set; }
        public string TempDir { get; private set; }
        public string ReceivedDir { get; private set; }
        public string QuarantineDir { get; private set; }
        public string ConvertedDir { get; private set; }
        public string PreviewsDir { get; private set; }
        public string TrayDir { get; private set; }
        public string LogsDir { get; private set; }
        public string ConfigDir { get; private set; }

        public StoragePaths(string appRootDir, string customDataRootOverride = null)
        {
            AppRootDir = Path.GetFullPath(appRootDir);
            string resolved = ResolveDataRoot(appRootDir, customDataRootOverride);
            ApplyDataRoot(resolved);
        }

        public void ApplyDataRoot(string dataRoot)
        {
            DataRootDir = Path.GetFullPath(dataRoot);
            DatabaseDir = Path.Combine(DataRootDir, "database");
            DatabaseFile = Path.Combine(DatabaseDir, "scandrop.db");
            UploadsDir = Path.Combine(DataRootDir, "uploads");
            TempDir = Path.Combine(UploadsDir, "temp");
            ReceivedDir = Path.Combine(UploadsDir, "received");
            QuarantineDir = Path.Combine(UploadsDir, "quarantine");
            ConvertedDir = Path.Combine(DataRootDir, "converted");
            PreviewsDir = Path.Combine(DataRootDir, "previews");
            TrayDir = Path.Combine(DataRootDir, "tray");
            LogsDir = Path.Combine(DataRootDir, "logs");
            ConfigDir = Path.Combine(DataRootDir, "config");

            EnsureAllDirectories();
        }

        public void EnsureAllDirectories()
        {
            foreach (var dir in new[]
            {
                DataRootDir, DatabaseDir, UploadsDir, TempDir, ReceivedDir,
                QuarantineDir, ConvertedDir, PreviewsDir, TrayDir, LogsDir, ConfigDir
            })
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
        }

        public static string GetBootstrapConfigPath()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(localApp, "ScanDrop");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "bootstrap.json");
        }

        public static void SaveBootstrapDataRoot(string chosenPath)
        {
            if (string.IsNullOrWhiteSpace(chosenPath)) return;
            try
            {
                string full = Path.GetFullPath(chosenPath.Trim());
                Directory.CreateDirectory(full);
                var payload = new Dictionary<string, object> { { "dataRoot", full } };
                File.WriteAllText(GetBootstrapConfigPath(), new JavaScriptSerializer().Serialize(payload), Encoding.UTF8);
            }
            catch { }
        }

        private static string ResolveDataRoot(string appRootDir, string customOverride)
        {
            if (!string.IsNullOrWhiteSpace(customOverride) && TryVerifyWritableDir(customOverride))
            {
                return customOverride;
            }

            // 1. Check user-selected storage folder in bootstrap.json
            try
            {
                string boot = GetBootstrapConfigPath();
                if (File.Exists(boot))
                {
                    var dict = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(boot, Encoding.UTF8));
                    if (dict != null && dict.ContainsKey("dataRoot") && dict["dataRoot"] != null)
                    {
                        string saved = Convert.ToString(dict["dataRoot"]).Trim();
                        if (!string.IsNullOrWhiteSpace(saved) && TryVerifyWritableDir(saved))
                            return saved;
                    }
                }
            }
            catch { }

            // 2. PRD Section 6 default: C:\ProgramData\ScanDrop
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(programData))
            {
                string pdCandidate = Path.Combine(programData, "ScanDrop");
                if (TryVerifyWritableDir(pdCandidate))
                    return pdCandidate;
            }

            // 3. Fallback: %LOCALAPPDATA%\ScanDrop
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localApp))
            {
                string laCandidate = Path.Combine(localApp, "ScanDrop");
                if (TryVerifyWritableDir(laCandidate))
                    return laCandidate;
            }

            // 4. Last resort: portable next to executable
            return Path.Combine(appRootDir, "ScanDrop_Data");
        }

        public static bool TryVerifyWritableDir(string dir)
        {
            try
            {
                string full = Path.GetFullPath(dir);
                if (!Directory.Exists(full)) Directory.CreateDirectory(full);
                string probe = Path.Combine(full, ".write_probe_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Guards against directory traversal attacks by verifying that candidatePath
        /// resolves inside allowedRoot.
        /// </summary>
        public static string EnsureSafeSubPath(string allowedRoot, string candidatePath)
        {
            string rootFull = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candFull = Path.GetFullPath(candidatePath);
            if (!candFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Path traversal blocked.");
            }
            return candFull;
        }
    }
}
