# Agents.md - AI Assistant Instructies

Dit bestand bevat context en instructies voor AI assistenten die werken aan dit project.

## Project Overzicht

**Backup Cleaner** is een Windows WPF applicatie (.NET 8) voor het opruimen van oude klant backup bestanden. De app is gebouwd voor Rentpro.

### Belangrijke kenmerken
- WPF met moderne UI styling (custom buttons, checkboxes, kleuren)
- Async/await voor UI responsiviteit tijdens scans
- System tray integratie voor achtergrond draaien
- Windows Task Scheduler integratie voor automatische opruiming
- JSON-based settings opslag

## Architectuur

### UI Layer
- `MainWindow.xaml/.cs` - Hoofdvenster met klantlijst, knoppen, statusbalk
- `CleanupPreviewWindow.xaml/.cs` - Preview van te verwijderen bestanden
- `App.xaml` - Globale resources, kleuren, button/checkbox styles

### Models
- `CustomerFolder` - Representeert een klantmap met instellingen (INotifyPropertyChanged)
- `BackupSet` - Groep bestanden met dezelfde datum
- `FileToDelete` - Individueel bestand voor verwijdering
- `BackupFile` - Metadata van een backup bestand

### Services
- `BackupService` - Core logica: scannen, groeperen op datum, berekenen wat te verwijderen
- `SettingsService` - JSON opslag in %APPDATA%

### Hulpklassen
- `IconGenerator` - Genereert programmatisch het app icoon (System.Drawing)

## Belangrijke logica

### Backup groepering
Bestanden worden gegroepeerd op **datum** (niet op naam). De datum komt uit:
1. Regex patronen in bestandsnaam (yyyy-MM-dd, yyyyMMdd, dd-MM-yyyy, ddMMyyyy)
2. Fallback: LastWriteTime van het bestand

### Nieuwe mappen detectie
- Mappen die niet in `settings.CustomerSettings` staan zijn "nieuw"
- Nieuwe mappen krijgen `IsNew = true` en worden gemarkeerd in UI
- Bij automatische opruiming worden nieuwe mappen **overgeslagen**
- Na eerste wijziging (checkbox/aantal) verdwijnt de badge

### Automatische opruiming
- Timer checkt elke minuut of het 02:00 is
- Bij automatische run: eerst verse scan, dan verwijderen
- Windows Scheduled Task wordt aangemaakt/verwijderd bij checkbox toggle

## Code conventies

- Nederlandse UI teksten en comments
- C# 12 features (.NET 8)
- Async void alleen voor event handlers
- `_` prefix voor private fields
- Null-coalescing en null-conditional operators waar passend

## UI/UX richtlijnen

### Kleuren (uit App.xaml)
- Primary: #1E3A5F (donkerblauw)
- Secondary: #2E5A8F (middenblauw)
- Accent: #FF6B35 (oranje)
- Background: #F5F7FA (lichtgrijs)
- Success: #10B981 (groen)
- Warning: #F59E0B (oranje/geel)
- Danger: #EF4444 (rood)

### Interactie tijdens scannen
- `_isScanning` flag voorkomt UI interactie tijdens scan
- Knoppen worden disabled
- Live voortgang in statusbalk en tellers

## Bekende beperkingen

1. Icoon wordt programmatisch gegenereerd (geen .ico bestand)
2. Scheduled task vereist dat gebruiker ingelogd is (geen SYSTEM service)
3. Geen logging naar bestand (alleen UI status)
4. Geen undo functionaliteit na verwijderen

## Test scenario's

Bij wijzigingen, test:
1. Eerste start (geen settings) - map selectie dialoog
2. Scan met veel mappen - voortgang en responsiviteit
3. Nieuwe map toevoegen aan backup folder - NIEUW badge
4. Checkbox toggle tijdens scan - moet genegeerd worden
5. Minimaliseren met auto-cleanup aan - system tray
6. Sluiten met auto-cleanup aan - moet naar tray gaan, niet afsluiten

## Dependencies

- Newtonsoft.Json (settings serialization)
- System.Drawing.Common (icon generation)
- Windows Forms (FolderBrowserDialog, NotifyIcon)

