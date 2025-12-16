using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BackupCleaner.Models;

namespace BackupCleaner
{
    public partial class CleanupPreviewWindow : Window
    {
        private readonly List<FileToDelete> _filesToDelete;

        public CleanupPreviewWindow(List<FileToDelete> filesToDelete)
        {
            InitializeComponent();
            _filesToDelete = filesToDelete;
            
            lstFiles.ItemsSource = _filesToDelete.OrderBy(f => f.CustomerName).ThenBy(f => f.BackupDate);
            
            var totalSize = _filesToDelete.Sum(f => f.Size);
            var customerCount = _filesToDelete.Select(f => f.CustomerName).Distinct().Count();
            
            txtSummary.Text = $"{_filesToDelete.Count} bestand(en) van {customerCount} klant(en) - Totaal: {FormatBytes(totalSize)}";
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Weet je zeker dat je {_filesToDelete.Count} bestanden definitief wilt verwijderen?\n\nDeze actie kan niet ongedaan worden gemaakt!",
                "Bevestiging",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
                
            if (result == MessageBoxResult.Yes)
            {
                DialogResult = true;
                Close();
            }
        }
    }
}

