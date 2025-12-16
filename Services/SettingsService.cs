using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace BackupCleaner.Services
{
    public class AppSettings
    {
        public string? BackupFolderPath { get; set; }
        public bool AutoCleanupEnabled { get; set; }
        public DateTime? LastAutoCleanup { get; set; }
        public int DefaultBackupsToKeep { get; set; } = 5;
        public int MinimumAgeMonths { get; set; } = 1;
        public Dictionary<string, CustomerSettings> CustomerSettings { get; set; } = new();
    }

    public class CustomerSettings
    {
        public bool IsSelected { get; set; } = true;
        public int BackupsToKeep { get; set; } = 5;
    }

    public static class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BackupCleaner",
            "settings.json"
        );

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch
            {
                // Bij fout, return default settings
            }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Silently fail on save errors
            }
        }
    }
}

