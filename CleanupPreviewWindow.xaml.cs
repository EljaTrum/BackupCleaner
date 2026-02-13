using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using BackupCleaner.Models;
using BackupCleaner.Services;

namespace BackupCleaner
{
    public partial class CleanupPreviewWindow : Window
    {
        private readonly List<FileToDelete> _filesToDelete;
        private CancellationTokenSource? _deleteCancellation;
        private bool _isDeleting;

        public int DeletedFiles { get; private set; }
        public long FreedSpace { get; private set; }
        public List<string> Errors { get; private set; } = new();

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

        private async void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Weet je zeker dat je {_filesToDelete.Count} bestanden definitief wilt verwijderen?\n\nDeze actie kan niet ongedaan worden gemaakt!",
                "Bevestiging",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            // Schakel over naar progress weergave
            _isDeleting = true;
            pnlFileList.Visibility = Visibility.Collapsed;
            pnlProgress.Visibility = Visibility.Visible;
            pnlButtons.Visibility = Visibility.Collapsed;

            _deleteCancellation = new CancellationTokenSource();

            var progress = new Progress<DeleteProgress>(p =>
            {
                progressBar.Value = p.Percentage;
                txtProgressCount.Text = $"{p.CurrentIndex} van {p.TotalFiles} bestanden";
                txtProgressFile.Text = p.CurrentFile;
            });

            LogService.Info($"Opruimen gestart: {_filesToDelete.Count} bestanden");

            try
            {
                var (deletedFiles, freedSpace, errors) = await BackupService.DeleteFilesAsync(
                    _filesToDelete, progress, _deleteCancellation.Token);

                DeletedFiles = deletedFiles;
                FreedSpace = freedSpace;
                Errors = errors;

                LogService.Info($"Opruimen voltooid: {deletedFiles} verwijderd, {FormatBytes(freedSpace)} vrijgemaakt, {errors.Count} fouten");

                _isDeleting = false;
                DialogResult = true;
                Close();
            }
            catch (OperationCanceledException)
            {
                LogService.Info("Opruimen geannuleerd door gebruiker");
                _isDeleting = false;

                // Toon hoeveel er al verwijderd is
                txtProgressTitle.Text = "Opruimen geannuleerd";
                txtProgressFile.Text = "";
                btnCancel.Content = "Sluiten";
                btnCancel.Click -= BtnCancelDelete_Click;
                btnCancel.Click += (_, _) => { DialogResult = false; Close(); };
            }
            finally
            {
                _deleteCancellation?.Dispose();
                _deleteCancellation = null;
            }
        }

        private void BtnCancelDelete_Click(object sender, RoutedEventArgs e)
        {
            _deleteCancellation?.Cancel();
            btnCancel.IsEnabled = false;
            btnCancel.Content = "Afbreken...";
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (_isDeleting)
            {
                e.Cancel = true;
            }
        }
    }
}
