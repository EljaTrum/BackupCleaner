using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BackupCleaner.Services
{
    public static class IgnoreService
    {
        private static readonly string IgnoreFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BackupCleaner",
            "ignore.txt"
        );

        private static List<string> _patterns = new();
        private static List<Regex> _regexPatterns = new();

        /// <summary>
        /// Laad ignore patterns uit het bestand. Maakt een standaard bestand aan als het niet bestaat.
        /// </summary>
        public static List<string> Load()
        {
            try
            {
                if (!File.Exists(IgnoreFilePath))
                {
                    // Maak standaard ignore bestand aan met voorbeelden
                    CreateDefaultIgnoreFile();
                }

                var lines = File.ReadAllLines(IgnoreFilePath)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#"))
                    .ToList();

                _patterns = lines;
                _regexPatterns = lines.Select(PatternToRegex).ToList();

                return _patterns;
            }
            catch
            {
                _patterns = new List<string>();
                _regexPatterns = new List<Regex>();
                return _patterns;
            }
        }

        /// <summary>
        /// Controleer of een mapnaam genegeerd moet worden
        /// </summary>
        public static bool ShouldIgnoreFolder(string folderName)
        {
            if (_patterns.Count == 0) return false;

            foreach (var regex in _regexPatterns)
            {
                if (regex.IsMatch(folderName))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Controleer of een bestandsnaam genegeerd moet worden
        /// </summary>
        public static bool ShouldIgnoreFile(string fileName)
        {
            if (_patterns.Count == 0) return false;

            foreach (var regex in _regexPatterns)
            {
                if (regex.IsMatch(fileName))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Geeft het pad naar het ignore bestand terug
        /// </summary>
        public static string GetIgnoreFilePath()
        {
            return IgnoreFilePath;
        }

        /// <summary>
        /// Converteer een simpel pattern naar een regex
        /// Ondersteunt:
        /// - * = willekeurige tekens
        /// - ? = één willekeurig teken
        /// - Prefix match met * aan het eind (bijv. "_*" matcht alles dat begint met _)
        /// </summary>
        private static Regex PatternToRegex(string pattern)
        {
            // Escape speciale regex karakters behalve * en ?
            var escaped = Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".");

            // Maak er een volledige match van (case insensitive)
            return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        /// <summary>
        /// Maak een standaard ignore bestand aan met voorbeelden en uitleg
        /// </summary>
        private static void CreateDefaultIgnoreFile()
        {
            var directory = Path.GetDirectoryName(IgnoreFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var content = @"# Backup Cleaner - Ignore Bestand
# ================================
# Voeg hier patronen toe van mappen of bestanden die genegeerd moeten worden.
# Elke regel is één patroon. Regels die beginnen met # zijn commentaar.
#
# Ondersteunde wildcards:
#   *  = nul of meer willekeurige tekens
#   ?  = precies één willekeurig teken
#
# Voorbeelden:
#   _*           = Negeer alles dat begint met een underscore
#   temp*        = Negeer alles dat begint met 'temp'
#   *.log        = Negeer alle .log bestanden
#   test_?       = Negeer test_1, test_2, etc.
#   _Archive     = Negeer precies de map/bestand '_Archive'
#
# Actieve patronen hieronder:

_*
";

            File.WriteAllText(IgnoreFilePath, content);
        }
    }
}

