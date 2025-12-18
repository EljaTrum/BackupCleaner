using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BackupCleaner.Models;

namespace BackupCleaner.Services
{
    public static class BackupService
    {
        // Ondersteunde backup extensies
        private static readonly string[] BackupExtensions = { ".bak", ".trn", ".zip", ".7z", ".rar", ".gz", ".tar" };

        /// <summary>
        /// Scan de hoofdmap en vind alle klantenmappen
        /// </summary>
        public static List<CustomerFolder> ScanCustomerFolders(string backupPath, Action<int, int, string>? progressCallback = null)
        {
            var customers = new List<CustomerFolder>();

            if (!Directory.Exists(backupPath))
                return customers;

            var directories = Directory.GetDirectories(backupPath);
            var total = directories.Length;
            var current = 0;

            foreach (var dir in directories)
            {
                current++;
                var folderName = Path.GetFileName(dir);
                
                progressCallback?.Invoke(current, total, folderName);
                
                var backupSets = GetBackupSets(dir);

                customers.Add(new CustomerFolder
                {
                    FolderName = folderName,
                    FolderPath = dir,
                    TotalBackups = backupSets.Count
                });
            }

            return customers.OrderBy(c => c.FolderName).ToList();
        }

        /// <summary>
        /// Groepeer bestanden in backup sets gebaseerd op datum
        /// </summary>
        public static List<BackupSet> GetBackupSets(string customerPath)
        {
            if (!Directory.Exists(customerPath))
                return new List<BackupSet>();

            var files = Directory.GetFiles(customerPath)
                .Where(f => BackupExtensions.Contains(Path.GetExtension(f).ToLower()))
                .Where(f => !IgnoreService.ShouldIgnoreFile(Path.GetFileName(f))) // Filter genegeerde bestanden
                .Select(f => new FileInfo(f))
                .ToList();

            // Groepeer bestanden op datum (yyyy-MM-dd) of op datum in bestandsnaam
            var grouped = new Dictionary<DateTime, List<BackupFile>>();

            foreach (var file in files)
            {
                var date = ExtractDateFromFile(file);
                var dateKey = date.Date;

                if (!grouped.ContainsKey(dateKey))
                    grouped[dateKey] = new List<BackupFile>();

                grouped[dateKey].Add(new BackupFile
                {
                    FileName = file.Name,
                    FilePath = file.FullName,
                    Extension = file.Extension.ToLower(),
                    Size = file.Length,
                    DateModified = date
                });
            }

            return grouped
                .Select(g => new BackupSet { Date = g.Key, Files = g.Value })
                .OrderByDescending(b => b.Date)
                .ToList();
        }

        /// <summary>
        /// Probeer een datum uit het bestand te halen (bestandsnaam of modified date)
        /// </summary>
        private static DateTime ExtractDateFromFile(FileInfo file)
        {
            // Probeer datum uit bestandsnaam te halen
            // Patronen: 2024-01-15, 20240115, 15-01-2024, etc.
            var patterns = new[]
            {
                @"(\d{4})-(\d{2})-(\d{2})",     // 2024-01-15
                @"(\d{4})(\d{2})(\d{2})",       // 20240115
                @"(\d{2})-(\d{2})-(\d{4})",     // 15-01-2024
                @"(\d{2})(\d{2})(\d{4})",       // 15012024
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(file.Name, pattern);
                if (match.Success)
                {
                    try
                    {
                        // Bepaal welk formaat het is
                        if (pattern.StartsWith(@"(\d{4})"))
                        {
                            // yyyy-MM-dd of yyyyMMdd
                            var year = int.Parse(match.Groups[1].Value);
                            var month = int.Parse(match.Groups[2].Value);
                            var day = int.Parse(match.Groups[3].Value);
                            if (IsValidDate(year, month, day))
                                return new DateTime(year, month, day);
                        }
                        else
                        {
                            // dd-MM-yyyy of ddMMyyyy
                            var day = int.Parse(match.Groups[1].Value);
                            var month = int.Parse(match.Groups[2].Value);
                            var year = int.Parse(match.Groups[3].Value);
                            if (IsValidDate(year, month, day))
                                return new DateTime(year, month, day);
                        }
                    }
                    catch
                    {
                        // Doorgaan naar volgende patroon
                    }
                }
            }

            // Fallback: gebruik de LastWriteTime van het bestand
            return file.LastWriteTime;
        }

        private static bool IsValidDate(int year, int month, int day)
        {
            if (year < 2000 || year > 2100) return false;
            if (month < 1 || month > 12) return false;
            if (day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
            return true;
        }

        /// <summary>
        /// Bepaal welke bestanden verwijderd moeten worden voor een klant
        /// </summary>
        public static List<FileToDelete> GetFilesToDelete(CustomerFolder customer, int minimumAgeMonths = 0)
        {
            var filesToDelete = new List<FileToDelete>();
            var backupSets = GetBackupSets(customer.FolderPath);
            var cutoffDate = DateTime.Today.AddMonths(-minimumAgeMonths);

            // Stap 1: Bepaal welke sets bewaard moeten blijven (de nieuwste X)
            var setsToKeep = backupSets.Take(customer.BackupsToKeep).ToList();
            
            // Stap 2: Van de rest, verwijder alleen sets die ouder zijn dan de minimum leeftijd
            var setsToDelete = backupSets
                .Skip(customer.BackupsToKeep)           // Sla de te bewaren sets over
                .Where(s => s.Date < cutoffDate)        // Alleen sets ouder dan minimum leeftijd
                .ToList();

            foreach (var set in setsToDelete)
            {
                foreach (var file in set.Files)
                {
                    filesToDelete.Add(new FileToDelete
                    {
                        CustomerName = customer.FolderName,
                        FileName = file.FileName,
                        FilePath = file.FilePath,
                        BackupDate = set.Date,
                        Size = file.Size
                    });
                }
            }

            return filesToDelete;
        }

        /// <summary>
        /// Bereken statistieken voor een klant
        /// </summary>
        public static void CalculateStats(CustomerFolder customer, int minimumAgeMonths = 0)
        {
            var backupSets = GetBackupSets(customer.FolderPath);
            customer.TotalBackups = backupSets.Count;
            
            var cutoffDate = DateTime.Today.AddMonths(-minimumAgeMonths);

            // Alleen sets die NIET in de top X zitten EN ouder zijn dan minimum leeftijd
            var setsToDelete = backupSets
                .Skip(customer.BackupsToKeep)
                .Where(s => s.Date < cutoffDate)
                .ToList();
                
            customer.FilesToDelete = setsToDelete.Sum(s => s.Files.Count);
            customer.SizeToFree = setsToDelete.Sum(s => s.TotalSize);
        }

        /// <summary>
        /// Verwijder de bestanden daadwerkelijk
        /// </summary>
        public static (int deletedFiles, long freedSpace, List<string> errors) DeleteFiles(List<FileToDelete> files)
        {
            int deletedCount = 0;
            long freedSpace = 0;
            var errors = new List<string>();

            foreach (var file in files)
            {
                try
                {
                    if (File.Exists(file.FilePath))
                    {
                        File.Delete(file.FilePath);
                        deletedCount++;
                        freedSpace += file.Size;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Kon {file.FileName} niet verwijderen: {ex.Message}");
                }
            }

            return (deletedCount, freedSpace, errors);
        }
    }
}

