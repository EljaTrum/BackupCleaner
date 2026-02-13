using System;
using System.IO;

namespace BackupCleaner.Services
{
    public static class LogService
    {
        private static readonly object _lock = new();
        private static readonly string _logDirectory;

        static LogService()
        {
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BackupCleaner", "logs");
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", $"{message}: {ex}");
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(_logDirectory);
                    var logFile = Path.Combine(_logDirectory, $"backupcleaner-{DateTime.Now:yyyy-MM-dd}.log");
                    var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(logFile, line);
                }
            }
            catch
            {
                // Logging mag nooit de applicatie laten crashen
            }
        }

        public static void CleanOldLogs(int keepDays = 30)
        {
            try
            {
                if (!Directory.Exists(_logDirectory)) return;

                var cutoff = DateTime.Now.AddDays(-keepDays);
                foreach (var file in Directory.GetFiles(_logDirectory, "backupcleaner-*.log"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
                // Opschonen mag nooit de applicatie laten crashen
            }
        }
    }
}
