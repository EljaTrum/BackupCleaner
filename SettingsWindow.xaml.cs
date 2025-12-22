using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using BackupCleaner.Services;

namespace BackupCleaner
{
    public partial class SettingsWindow : Window
    {
        private AppSettings _settings;
        private bool _isInitialized = false;
        
        public event EventHandler? AutoCleanupChanged;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            
            // Initialiseer UI
            chkAutoCleanup.IsChecked = _settings.AutoCleanupEnabled;
            UpdateAutoCleanupInfo();
            
            // Toon het pad naar het negeerbestand
            txtIgnorePath.Text = $"Bestandslocatie: {IgnoreService.GetIgnoreFilePath()}";
            
            _isInitialized = true;
        }

        private void ChkAutoCleanup_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            
            _settings.AutoCleanupEnabled = chkAutoCleanup.IsChecked == true;
            UpdateAutoCleanupInfo();
            
            // Trigger event zodat MainWindow kan reageren
            AutoCleanupChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateAutoCleanupInfo()
        {
            if (chkAutoCleanup.IsChecked == true)
            {
                txtAutoCleanupInfo.Visibility = Visibility.Visible;
                
                // Bepaal wanneer de volgende cleanup is
                if (_settings.LastAutoCleanup?.Date == DateTime.Today)
                {
                    // Vandaag al uitgevoerd, morgen weer
                    txtAutoCleanupInfo.Text = $"✓ Laatste opruiming: {_settings.LastAutoCleanup:HH:mm} vandaag • Volgende: morgen 02:00";
                    txtAutoCleanupInfo.Foreground = FindResource("SuccessBrush") as System.Windows.Media.SolidColorBrush;
                }
                else
                {
                    // Nog niet uitgevoerd vandaag
                    var now = DateTime.Now;
                    string nextRunText = now.Hour < 2 ? "vannacht om 02:00" : "morgen om 02:00";
                    
                    txtAutoCleanupInfo.Text = $"⏱ Volgende opruiming: {nextRunText}";
                    txtAutoCleanupInfo.Foreground = FindResource("TextSecondaryBrush") as System.Windows.Media.SolidColorBrush;
                }
            }
            else
            {
                txtAutoCleanupInfo.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnOpenIgnoreFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ignorePath = IgnoreService.GetIgnoreFilePath();
                
                // Zorg dat het bestand bestaat
                if (!File.Exists(ignorePath))
                {
                    IgnoreService.Load();
                }
                
                // Open het bestand in de standaard teksteditor
                Process.Start(new ProcessStartInfo
                {
                    FileName = ignorePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kon het negeerbestand niet openen: {ex.Message}", "Fout", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

