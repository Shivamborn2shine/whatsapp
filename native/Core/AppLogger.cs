using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ScanDrop.Native.Core
{
    public sealed class AppLogger
    {
        private readonly string _logsDir;
        private readonly object _lock = new object();

        public event Action<string> OnLogLine;

        public AppLogger(string logsDir)
        {
            _logsDir = logsDir;
            try
            {
                if (!Directory.Exists(_logsDir)) Directory.CreateDirectory(_logsDir);
            }
            catch { }
        }

        public void Info(string category, string message)
        {
            Write("INF", category, message);
        }

        public void Warn(string category, string message)
        {
            Write("WRN", category, message);
        }

        public void Error(string category, string message)
        {
            Write("ERR", category, message);
        }

        private void Write(string level, string category, string message)
        {
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0} [{1}] [{2}] {3}",
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                level,
                category,
                message
            );

            lock (_lock)
            {
                try
                {
                    if (!Directory.Exists(_logsDir)) Directory.CreateDirectory(_logsDir);
                    string file = Path.Combine(_logsDir, "scandrop-" + DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
                    if (File.Exists(file) && new FileInfo(file).Length > 10L * 1024L * 1024L)
                    {
                        string rotated = Path.Combine(_logsDir, "scandrop-" + DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture) + ".log");
                        try { File.Move(file, rotated); } catch { }
                    }
                    File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }

            var handler = OnLogLine;
            if (handler != null)
            {
                try { handler("[" + category + "] " + message); } catch { }
            }
        }
    }
}
