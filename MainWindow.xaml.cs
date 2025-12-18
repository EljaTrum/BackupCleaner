using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using BackupCleaner.Models;
using BackupCleaner.Services;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace BackupCleaner
{
    public partial class MainWindow : Window
    {
        private AppSettings _settings = new();
        private ObservableCollection<CustomerFolder> _customers = new();
        private int _defaultBackupsToKeep = 5;
        private int _minimumAgeMonths = 1;
        private System.Windows.Threading.DispatcherTimer? _autoCleanupTimer;
        private bool _isInitialized = false;
        private bool _isScanning = false;
        private NotifyIcon? _notifyIcon;
        private CancellationTokenSource? _scanCancellation;

        public MainWindow()
        {
            InitializeComponent();
            
            lstCustomers.ItemsSource = _customers;
            
            LoadSettings();
            IgnoreService.Load(); // Laad ignore patterns
            SetupAutoCleanupTimer();
            SetupSystemTray();
            
            // Controleer of de map nog bestaat en toon het pad (maar scan niet automatisch)
            if (!string.IsNullOrEmpty(_settings.BackupFolderPath) && Directory.Exists(_settings.BackupFolderPath))
            {
                txtBackupPath.Text = _settings.BackupFolderPath;
                txtStatus.Text = "Druk op 'Scannen' om te beginnen";
                emptyState.Visibility = Visibility.Visible;
            }
            else if (!string.IsNullOrEmpty(_settings.BackupFolderPath))
            {
                // Map niet gevonden, vraag om nieuwe map
                ShowFolderNotFoundMessage();
            }
            
            _isInitialized = true;
            UpdateAutoCleanupInfo();
            
            // Check voor command line argument om direct te starten voor scheduled task
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("--auto-cleanup"))
            {
                // Minimaliseer naar tray en voer cleanup uit
                WindowState = WindowState.Minimized;
                Hide();
                _ = PerformAutomaticCleanupAsync();
            }
        }

        private void SetupSystemTray()
        {
            var appIcon = IconGenerator.CreateAppIcon();
            
            _notifyIcon = new NotifyIcon
            {
                Icon = appIcon,
                Visible = false,
                Text = "Backup Cleaner"
            };
            
            // Stel ook het venster icoon in
            Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                appIcon.Handle,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Openen", null, (s, e) => ShowWindow());
            contextMenu.Items.Add("Opruimen starten", null, async (s, e) => await PerformAutomaticCleanupAsync());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Afsluiten", null, (s, e) => ExitApplication());
            
            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _notifyIcon!.Visible = false;
        }

        private void ExitApplication()
        {
            _notifyIcon?.Dispose();
            Application.Current.Shutdown();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            
            // Minimaliseer naar system tray als auto cleanup aan staat
            if (WindowState == WindowState.Minimized && _settings.AutoCleanupEnabled)
            {
                Hide();
                _notifyIcon!.Visible = true;
                _notifyIcon.ShowBalloonTip(2000, "Backup Cleaner", "Applicatie draait in de achtergrond", ToolTipIcon.Info);
            }
        }

        private void LoadSettings()
        {
            _settings = SettingsService.Load();
            _defaultBackupsToKeep = _settings.DefaultBackupsToKeep > 0 ? _settings.DefaultBackupsToKeep : 5;
            _minimumAgeMonths = _settings.MinimumAgeMonths > 0 ? _settings.MinimumAgeMonths : 1;
            txtDefaultKeep.Text = _defaultBackupsToKeep.ToString();
            txtMinAge.Text = _minimumAgeMonths.ToString();
            chkAutoCleanup.IsChecked = _settings.AutoCleanupEnabled;
        }

        private void SaveSettings()
        {
            _settings.DefaultBackupsToKeep = _defaultBackupsToKeep;
            _settings.MinimumAgeMonths = _minimumAgeMonths;
            _settings.AutoCleanupEnabled = chkAutoCleanup.IsChecked == true;
            
            // Bewaar klantinstellingen
            _settings.CustomerSettings.Clear();
            foreach (var customer in _customers)
            {
                _settings.CustomerSettings[customer.FolderName] = new CustomerSettings
                {
                    IsSelected = customer.IsSelected,
                    BackupsToKeep = customer.BackupsToKeep
                };
            }
            
            SettingsService.Save(_settings);
        }

        private void SetupAutoCleanupTimer()
        {
            _autoCleanupTimer = new System.Windows.Threading.DispatcherTimer();
            _autoCleanupTimer.Interval = TimeSpan.FromMinutes(1); // Check elke minuut
            _autoCleanupTimer.Tick += AutoCleanupTimer_Tick;
            
            if (_settings.AutoCleanupEnabled)
            {
                _autoCleanupTimer.Start();
            }
        }

        private async void AutoCleanupTimer_Tick(object? sender, EventArgs e)
        {
            if (!_settings.AutoCleanupEnabled) return;
            
            // Controleer of er vandaag al een cleanup is geweest
            if (_settings.LastAutoCleanup?.Date == DateTime.Today) return;
            
            // Controleer of het 02:00 uur is (tussen 02:00 en 02:59)
            var now = DateTime.Now;
            if (now.Hour != 2) return;
            
            // Voer auto cleanup uit met verse scan
            await PerformAutomaticCleanupAsync();
        }

        private async Task PerformAutomaticCleanupAsync()
        {
            if (string.IsNullOrEmpty(_settings.BackupFolderPath) || !Directory.Exists(_settings.BackupFolderPath))
                return;

            try
            {
                txtStatus.Text = "Automatische opruiming: scannen...";
                
                // Herlaad ignore patterns
                IgnoreService.Load();
                
                // Doe eerst een verse scan, filter genegeerde mappen
                var allDirectories = await Task.Run(() => Directory.GetDirectories(_settings.BackupFolderPath!));
                var directories = allDirectories
                    .Where(d => !IgnoreService.ShouldIgnoreFolder(Path.GetFileName(d)))
                    .ToArray();
                    
                var allFilesToDelete = new List<FileToDelete>();
                long totalSize = 0;
                var newFolders = new List<string>();

                foreach (var dir in directories)
                {
                    var folderName = Path.GetFileName(dir);
                    
                    // Check of deze map bekend is in de settings
                    if (!_settings.CustomerSettings.TryGetValue(folderName, out var savedSettings))
                    {
                        // Nieuwe map - overslaan bij automatische opruiming
                        newFolders.Add(folderName);
                        continue;
                    }
                    
                    // Skip als niet geselecteerd
                    if (!savedSettings.IsSelected) continue;
                    
                    // Scan backup sets
                    var backupSets = await Task.Run(() => BackupService.GetBackupSets(dir));
                    var cutoffDate = DateTime.Today.AddMonths(-_minimumAgeMonths);
                    
                    // Alleen sets die niet in de top X zitten EN ouder zijn dan minimum leeftijd
                    var setsToDelete = backupSets
                        .Skip(savedSettings.BackupsToKeep)
                        .Where(s => s.Date < cutoffDate)
                        .ToList();
                    
                    foreach (var set in setsToDelete)
                    {
                        foreach (var file in set.Files)
                        {
                            allFilesToDelete.Add(new FileToDelete
                            {
                                CustomerName = folderName,
                                FileName = file.FileName,
                                FilePath = file.FilePath,
                                BackupDate = set.Date,
                                Size = file.Size
                            });
                            totalSize += file.Size;
                        }
                    }
                }

                // Bouw status bericht
                var statusParts = new List<string>();
                
                if (allFilesToDelete.Any())
                {
                    // Verwijder de bestanden
                    var (deletedFiles, freedSpace, errors) = await Task.Run(() => BackupService.DeleteFiles(allFilesToDelete));
                    statusParts.Add($"{deletedFiles} bestanden verwijderd ({FormatBytes(freedSpace)})");
                }
                else
                {
                    statusParts.Add("Geen bestanden om te verwijderen");
                }
                
                if (newFolders.Any())
                {
                    statusParts.Add($"{newFolders.Count} nieuwe map(pen) overgeslagen");
                }

                txtStatus.Text = $"Automatische opruiming: {string.Join(" • ", statusParts)}";
                
                // Toon notificatie als er nieuwe mappen zijn
                if (newFolders.Any() && _notifyIcon != null)
                {
                    _notifyIcon.ShowBalloonTip(
                        5000, 
                        "Nieuwe mappen gevonden", 
                        $"Er zijn {newFolders.Count} nieuwe klantmap(pen) gevonden die niet automatisch worden opgeruimd. Open de app om ze te configureren.",
                        ToolTipIcon.Info);
                }

                _settings.LastAutoCleanup = DateTime.Now;
                SaveSettings();
                UpdateAutoCleanupInfo();
                
                // Ververs de UI lijst
                ScanFolders();
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Automatische opruiming mislukt: {ex.Message}";
            }
        }

        private void ShowFolderNotFoundMessage()
        {
            var result = MessageBox.Show(
                $"De backup map '{_settings.BackupFolderPath}' bestaat niet meer.\n\nWil je een nieuwe map selecteren?",
                "Map niet gevonden",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
                
            if (result == MessageBoxResult.Yes)
            {
                SelectFolder();
            }
        }

        private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            SelectFolder();
        }

        private void BtnOpenIgnoreFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ignorePath = IgnoreService.GetIgnoreFilePath();
                
                // Zorg dat het bestand bestaat (laad indien nodig)
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

        private void SelectFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Selecteer de hoofdmap met klant backup mappen",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            
            if (!string.IsNullOrEmpty(_settings.BackupFolderPath) && Directory.Exists(_settings.BackupFolderPath))
            {
                dialog.SelectedPath = _settings.BackupFolderPath;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _settings.BackupFolderPath = dialog.SelectedPath;
                txtBackupPath.Text = dialog.SelectedPath;
                SaveSettings();
                _customers.Clear();
                emptyState.Visibility = Visibility.Visible;
                txtStatus.Text = "Druk op 'Scannen' om te beginnen";
                txtCustomerCount.Text = "0";
                txtFilesToDelete.Text = "0 bestanden";
                txtSpaceToFree.Text = "0 B";
            }
        }

        private void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning)
            {
                // Annuleer de lopende scan
                _scanCancellation?.Cancel();
            }
            else
            {
                // Start een nieuwe scan
                ScanFolders();
            }
        }

        private async void ScanFolders()
        {
            if (string.IsNullOrEmpty(_settings.BackupFolderPath) || !Directory.Exists(_settings.BackupFolderPath))
            {
                txtStatus.Text = "Selecteer eerst een backup map";
                emptyState.Visibility = Visibility.Visible;
                btnScan.IsEnabled = true;
                btnCleanup.IsEnabled = true;
                return;
            }

            // Maak nieuwe cancellation token
            _scanCancellation?.Dispose();
            _scanCancellation = new CancellationTokenSource();
            var cancellationToken = _scanCancellation.Token;

            // Herlaad ignore patterns voor elke scan
            IgnoreService.Load();

            _isScanning = true;
            txtStatus.Text = "Scannen gestart...";
            btnScan.Content = "⏹ Afbreken";
            btnCleanup.IsEnabled = false;
            _customers.Clear();
            emptyState.Visibility = Visibility.Collapsed;
            
            var backupPath = _settings.BackupFolderPath!;
            var wasCancelled = false;
            
            try
            {
                // Eerst alle directories ophalen en filter genegeerde mappen
                var allDirectories = await Task.Run(() => Directory.GetDirectories(backupPath), cancellationToken);
                var directories = allDirectories
                    .Where(d => !IgnoreService.ShouldIgnoreFolder(Path.GetFileName(d)))
                    .ToArray();
                
                var ignoredCount = allDirectories.Length - directories.Length;
                
                // Check of dit een directe backup map is (geen submappen, maar wel backup bestanden)
                var isDirectBackupFolder = directories.Length == 0 && allDirectories.Length == 0;
                if (isDirectBackupFolder)
                {
                    // Controleer of er backup bestanden in de map staan
                    var hasBackupFiles = await Task.Run(() => BackupService.GetBackupSets(backupPath).Any(), cancellationToken);
                    if (!hasBackupFiles)
                    {
                        // Geen submappen en geen backup bestanden
                        txtStatus.Text = "Geen backup bestanden of klantmappen gevonden";
                        emptyState.Visibility = Visibility.Visible;
                        return;
                    }
                    
                    // Behandel de gekozen map zelf als een backup map
                    directories = new[] { backupPath };
                }
                
                var total = directories.Length;
                var processedCount = 0;
                long totalSizeToFree = 0;
                int totalFilesToDelete = 0;

                txtStatus.Text = isDirectBackupFolder 
                    ? "Scannen: directe backup map..." 
                    : $"Scannen: 0 van {total} mappen...";
                txtCustomerCount.Text = "0";

                foreach (var dir in directories)
                {
                    // Check of de scan geannuleerd is
                    if (cancellationToken.IsCancellationRequested)
                    {
                        wasCancelled = true;
                        break;
                    }

                    var folderName = Path.GetFileName(dir);
                    
                    // Update status
                    processedCount++;
                    if (!isDirectBackupFolder)
                    {
                        txtStatus.Text = $"Scannen: {processedCount} van {total} - {folderName}";
                    }

                    // Scan deze map in background thread
                    var backupSets = await Task.Run(() => BackupService.GetBackupSets(dir), cancellationToken);
                    
                    // Check nogmaals na de Task.Run
                    if (cancellationToken.IsCancellationRequested)
                    {
                        wasCancelled = true;
                        break;
                    }

                    var customer = new CustomerFolder
                    {
                        FolderName = folderName,
                        FolderPath = dir,
                        TotalBackups = backupSets.Count
                    };

                    // Herstel opgeslagen instellingen of markeer als nieuw
                    if (_settings.CustomerSettings.TryGetValue(customer.FolderName, out var savedSettings))
                    {
                        customer.IsSelected = savedSettings.IsSelected;
                        customer.BackupsToKeep = savedSettings.BackupsToKeep;
                        customer.IsNew = false;
                    }
                    else
                    {
                        customer.BackupsToKeep = _defaultBackupsToKeep;
                        customer.IsNew = true; // Nieuwe map, nog niet eerder gezien
                    }
                    
                    // Bereken stats met minimum leeftijd
                    var cutoffDate = DateTime.Today.AddMonths(-_minimumAgeMonths);
                    var setsToDelete = backupSets
                        .Skip(customer.BackupsToKeep)
                        .Where(s => s.Date < cutoffDate)
                        .ToList();
                    customer.FilesToDelete = setsToDelete.Sum(s => s.Files.Count);
                    customer.SizeToFree = setsToDelete.Sum(s => s.TotalSize);
                    
                    // Subscribe to changes
                    customer.PropertyChanged += Customer_PropertyChanged;
                    
                    // Voeg toe aan lijst (dit update de UI direct)
                    _customers.Add(customer);
                    
                    // Update totalen
                    if (customer.IsSelected)
                    {
                        totalFilesToDelete += customer.FilesToDelete;
                        totalSizeToFree += customer.SizeToFree;
                    }
                    
                    // Update stats in UI
                    txtCustomerCount.Text = processedCount.ToString();
                    txtFilesToDelete.Text = $"{totalFilesToDelete} bestanden";
                    txtSpaceToFree.Text = FormatBytes(totalSizeToFree);
                }

                emptyState.Visibility = _customers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                UpdateStats();
                
                var ignoredText = ignoredCount > 0 ? $" ({ignoredCount} genegeerd)" : "";
                
                if (wasCancelled)
                {
                    txtStatus.Text = $"Scan afgebroken - {_customers.Count} map(pen) verwerkt{ignoredText}";
                }
                else if (isDirectBackupFolder)
                {
                    txtStatus.Text = $"Scan voltooid - directe backup map gescand";
                }
                else
                {
                    txtStatus.Text = $"Scan voltooid - {_customers.Count} klant(en) gevonden{ignoredText}";
                }
            }
            catch (OperationCanceledException)
            {
                txtStatus.Text = $"Scan afgebroken - {_customers.Count} klant(en) verwerkt";
                emptyState.Visibility = _customers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                UpdateStats();
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Fout bij scannen: {ex.Message}";
            }
            finally
            {
                _isScanning = false;
                btnScan.Content = "🔍 Scannen";
                btnScan.IsEnabled = true;
                btnCleanup.IsEnabled = true;
            }
        }

        private async void Customer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Negeer wijzigingen tijdens scannen om vastlopen te voorkomen
            if (_isScanning) return;
            
            if (sender is CustomerFolder customer && (e.PropertyName == nameof(CustomerFolder.BackupsToKeep) || e.PropertyName == nameof(CustomerFolder.IsSelected)))
            {
                // Als de klant nieuw was en nu wordt aangepast, is hij niet meer nieuw
                if (customer.IsNew)
                {
                    customer.IsNew = false;
                }
                
                // Bereken stats in achtergrond thread met minimum leeftijd
                var minAge = _minimumAgeMonths;
                await Task.Run(() => BackupService.CalculateStats(customer, minAge));
                UpdateStats();
                SaveSettings();
            }
        }

        private void UpdateStats()
        {
            var selectedCustomers = _customers.Where(c => c.IsSelected).ToList();
            
            txtCustomerCount.Text = _customers.Count.ToString();
            
            var totalFiles = selectedCustomers.Sum(c => c.FilesToDelete);
            txtFilesToDelete.Text = $"{totalFiles} bestanden";
            
            var totalSize = selectedCustomers.Sum(c => c.SizeToFree);
            txtSpaceToFree.Text = FormatBytes(totalSize);
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

        private void BtnCleanup_Click(object sender, RoutedEventArgs e)
        {
            PerformCleanup(isAutomatic: false);
        }

        private void PerformCleanup(bool isAutomatic)
        {
            var selectedCustomers = _customers.Where(c => c.IsSelected).ToList();
            if (!selectedCustomers.Any())
            {
                if (!isAutomatic)
                {
                    MessageBox.Show("Selecteer eerst één of meer klanten om op te ruimen.", "Geen selectie", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            var allFilesToDelete = new List<FileToDelete>();
            foreach (var customer in selectedCustomers)
            {
                var files = BackupService.GetFilesToDelete(customer, _minimumAgeMonths);
                allFilesToDelete.AddRange(files);
            }

            if (!allFilesToDelete.Any())
            {
                if (!isAutomatic)
                {
                    MessageBox.Show("Er zijn geen bestanden om te verwijderen met de huidige instellingen.", "Geen bestanden", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            // Toon preview venster
            var previewWindow = new CleanupPreviewWindow(allFilesToDelete);
            previewWindow.Owner = this;
            
            if (previewWindow.ShowDialog() == true)
            {
                // Als de gebruiker bevestigt, verwijder de bestanden
                var (deletedFiles, freedSpace, errors) = BackupService.DeleteFiles(allFilesToDelete);
                
                if (errors.Any())
                {
                    MessageBox.Show(
                        $"Er zijn {deletedFiles} bestanden verwijderd ({FormatBytes(freedSpace)} vrijgemaakt).\n\nEr waren {errors.Count} fouten:\n{string.Join("\n", errors.Take(5))}",
                        "Opruimen voltooid met fouten",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show(
                        $"Er zijn {deletedFiles} bestanden verwijderd.\n{FormatBytes(freedSpace)} schijfruimte vrijgemaakt.",
                        "Opruimen voltooid",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                
                // Refresh de lijst
                ScanFolders();
            }
        }

        private void ChkSelectAll_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _isScanning) return;
            
            var isChecked = chkSelectAll.IsChecked == true;
            foreach (var customer in _customers)
            {
                customer.IsSelected = isChecked;
            }
            UpdateStats();
            SaveSettings();
        }

        private void ChkAutoCleanup_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            
            _settings.AutoCleanupEnabled = chkAutoCleanup.IsChecked == true;
            
            if (_autoCleanupTimer != null)
            {
                if (_settings.AutoCleanupEnabled)
                {
                    _autoCleanupTimer.Start();
                }
                else
                {
                    _autoCleanupTimer.Stop();
                }
            }
            
            // Maak of verwijder de Windows scheduled task
            UpdateScheduledTask();
            
            UpdateAutoCleanupInfo();
            SaveSettings();
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
                    txtAutoCleanupInfo.Text = $"✓ Uitgevoerd om {_settings.LastAutoCleanup:HH:mm} • Volgende: morgen 02:00";
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

        private void BtnDecrease_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning) return;
            
            if (sender is System.Windows.Controls.Button btn && btn.Tag is CustomerFolder customer)
            {
                if (customer.BackupsToKeep > 1)
                {
                    customer.BackupsToKeep--;
                }
            }
        }

        private void BtnIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning) return;
            
            if (sender is System.Windows.Controls.Button btn && btn.Tag is CustomerFolder customer)
            {
                customer.BackupsToKeep++;
            }
        }

        private void BtnDecreaseDefault_Click(object sender, RoutedEventArgs e)
        {
            if (_defaultBackupsToKeep > 1)
            {
                _defaultBackupsToKeep--;
                txtDefaultKeep.Text = _defaultBackupsToKeep.ToString();
                SaveSettings();
            }
        }

        private void BtnIncreaseDefault_Click(object sender, RoutedEventArgs e)
        {
            _defaultBackupsToKeep++;
            txtDefaultKeep.Text = _defaultBackupsToKeep.ToString();
            SaveSettings();
        }

        private void BtnDecreaseMinAge_Click(object sender, RoutedEventArgs e)
        {
            if (_minimumAgeMonths > 0)
            {
                _minimumAgeMonths--;
                txtMinAge.Text = _minimumAgeMonths.ToString();
                SaveSettings();
                // Herbereken stats voor alle klanten
                RecalculateAllStats();
            }
        }

        private void BtnIncreaseMinAge_Click(object sender, RoutedEventArgs e)
        {
            if (_minimumAgeMonths < 12)
            {
                _minimumAgeMonths++;
                txtMinAge.Text = _minimumAgeMonths.ToString();
                SaveSettings();
                // Herbereken stats voor alle klanten
                RecalculateAllStats();
            }
        }

        private async void RecalculateAllStats()
        {
            if (_isScanning) return;
            
            foreach (var customer in _customers)
            {
                await Task.Run(() => RecalculateCustomerStats(customer));
            }
            UpdateStats();
        }

        private void RecalculateCustomerStats(CustomerFolder customer)
        {
            var backupSets = BackupService.GetBackupSets(customer.FolderPath);
            var cutoffDate = DateTime.Today.AddMonths(-_minimumAgeMonths);
            
            // Filter: behoud backups die jonger zijn dan minimum leeftijd OF binnen het aantal te bewaren
            var setsToDelete = backupSets
                .Where(s => s.Date < cutoffDate) // Alleen sets ouder dan minimum leeftijd komen in aanmerking
                .Skip(Math.Max(0, customer.BackupsToKeep - backupSets.Count(s => s.Date >= cutoffDate))) // Houd rekening met al bewaarde recente
                .ToList();
            
            // Simpelere logica: bepaal eerst welke sets sowieso bewaard blijven
            var recentSets = backupSets.Take(customer.BackupsToKeep).ToList();
            var protectedByAge = backupSets.Where(s => s.Date >= cutoffDate).ToList();
            
            // Sets die verwijderd kunnen worden zijn: niet in recentSets EN niet protected by age
            setsToDelete = backupSets
                .Except(recentSets)
                .Where(s => s.Date < cutoffDate)
                .ToList();
            
            customer.FilesToDelete = setsToDelete.Sum(s => s.Files.Count);
            customer.SizeToFree = setsToDelete.Sum(s => s.TotalSize);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Als auto cleanup aan staat, minimaliseer naar tray in plaats van sluiten
            if (_settings.AutoCleanupEnabled)
            {
                e.Cancel = true;
                WindowState = WindowState.Minimized;
                return;
            }
            
            SaveSettings();
            _scanCancellation?.Dispose();
            _notifyIcon?.Dispose();
            base.OnClosing(e);
        }

        #region Windows Task Scheduler

        private void UpdateScheduledTask()
        {
            if (_settings.AutoCleanupEnabled)
            {
                CreateScheduledTask();
            }
            else
            {
                RemoveScheduledTask();
            }
        }

        private void CreateScheduledTask()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                // Maak een PowerShell script om de taak aan te maken
                var taskName = "BackupCleanerDaily";
                var script = $@"
$taskName = '{taskName}'
$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existingTask) {{
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}}

$action = New-ScheduledTaskAction -Execute '{exePath}' -Argument '--auto-cleanup'
$trigger = New-ScheduledTaskTrigger -Daily -At 2:00AM
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description 'Dagelijkse backup opruiming door Backup Cleaner'
";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                process?.WaitForExit(10000);
            }
            catch
            {
                // Silently fail - scheduled task is optioneel
            }
        }

        private void RemoveScheduledTask()
        {
            try
            {
                var taskName = "BackupCleanerDaily";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Unregister-ScheduledTask -TaskName '{taskName}' -Confirm:$false -ErrorAction SilentlyContinue\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                process?.WaitForExit(5000);
            }
            catch
            {
                // Silently fail
            }
        }

        #endregion
    }
}

