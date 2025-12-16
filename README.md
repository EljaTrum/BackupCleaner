# Backup Cleaner

Een Windows applicatie voor het opruimen van oude klant backup bestanden.

## Functionaliteiten

- **Map selectie**: Selecteer de hoofdmap waar klant backup mappen staan
- **Klant overzicht**: Zie alle klantenmappen met het aantal backups per klant
- **Configureerbaar bewaren**: Stel per klant in hoeveel recente backups bewaard moeten blijven (standaard 5)
- **Preview functie**: Bekijk welke bestanden verwijderd zouden worden voordat je daadwerkelijk verwijdert
- **Automatisch opruimen**: Dagelijkse automatische opschoning om 02:00 's nachts
- **Nieuwe mappen detectie**: Nieuwe klantmappen worden gemarkeerd en pas opgeruimd na handmatige goedkeuring
- **System tray**: App kan in de achtergrond draaien via de system tray
- **Windows Taakplanner**: Automatische scheduled task voor dagelijkse opruiming

## Ondersteunde bestandstypes

De applicatie herkent de volgende backup extensies:
- `.bak` - Database backups
- `.trn` - Transaction log backups
- `.zip` - Gecomprimeerde backups
- `.7z` - 7-Zip archieven
- `.rar` - RAR archieven
- `.gz` - Gzip bestanden
- `.tar` - Tar archieven

## Backup groepering

Bestanden worden gegroepeerd op datum tot "backup sets". De datum wordt bepaald door:

1. **Datum in bestandsnaam** (heeft prioriteit):
   - `yyyy-MM-dd` → `backup_2024-01-15.bak`
   - `yyyyMMdd` → `backup_20240115.bak`
   - `dd-MM-yyyy` → `backup_15-01-2024.bak`
   - `ddMMyyyy` → `backup_15012024.bak`

2. **Laatste wijzigingsdatum** van het bestand (fallback)

Alle bestanden met dezelfde datum worden beschouwd als één backup-set. Als je 5 backups bewaart, blijven de 5 meest recente datums behouden (inclusief alle bestanden van die datums).

## Automatisch opruimen

Wanneer "Automatisch dagelijks opruimen" is ingeschakeld:

- **Tijdstip**: Elke dag om 02:00 's nachts
- **Windows Task**: Er wordt automatisch een Windows Scheduled Task aangemaakt
- **Verse scan**: Bij elke automatische opruiming wordt eerst een nieuwe scan gedaan
- **Nieuwe mappen**: Worden overgeslagen - alleen eerder geconfigureerde mappen worden opgeruimd
- **Notificatie**: Bij nieuwe mappen krijg je een Windows notificatie

### Achtergrond draaien

- **Minimaliseren**: App gaat naar system tray (naast de klok)
- **Sluiten (X)**: App gaat naar system tray (blijft draaien)
- **Echt afsluiten**: Rechtermuisklik op tray icon → "Afsluiten"

## Installatie

### Vereisten
- Windows 10/11
- .NET 8.0 Runtime (of gebruik de self-contained versie)

### Bouwen
```bash
cd BackupCleaner
dotnet restore
dotnet build --configuration Release
```

De uitvoerbare bestanden staan in `bin/Release/net8.0-windows/`

### Publiceren als enkele .exe (self-contained)
```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o publish
```

Dit maakt een standalone `BackupCleaner.exe` (~150MB) die geen .NET installatie vereist.

## Gebruik

1. **Start de applicatie**
2. **Selecteer een map** - Klik op "Map Wijzigen" om de hoofdmap te selecteren
3. **Scan** - Klik op "Scannen" om alle klantenmappen te vinden
4. **Configureer** - Pas per klant het aantal te bewaren backups aan
5. **Preview** - Klik op "Opruimen" om een preview te zien van wat verwijderd wordt
6. **Bevestig** - Bevestig om daadwerkelijk te verwijderen
7. **Automatisch** - Vink "Automatisch dagelijks opruimen" aan voor nachtelijke opruiming

### Nieuwe klanten

Nieuwe klantmappen worden gemarkeerd met een oranje "NIEUW" badge:
- Ze worden **niet** automatisch opgeruimd
- Pas na het aanpassen van instellingen (vinkje of aantal) worden ze meegenomen
- Dit voorkomt onbedoeld verwijderen van nieuwe klantdata

## Instellingen

Instellingen worden opgeslagen in:
`%APPDATA%\BackupCleaner\settings.json`

Dit bevat:
- Het pad naar de backup map
- De standaard waarde voor te bewaren backups
- Per klant de instellingen (aangevinkt, aantal te bewaren)
- Status van automatisch opruimen
- Laatste automatische opruiming datum/tijd

## Command line argumenten

| Argument | Beschrijving |
|----------|--------------|
| `--auto-cleanup` | Start de app, voert opruiming uit, en minimaliseert naar tray |

Dit argument wordt gebruikt door de Windows Scheduled Task.

## Projectstructuur

```
BackupCleaner/
├── App.xaml                    # Application resources en styling
├── MainWindow.xaml             # Hoofd UI
├── CleanupPreviewWindow.xaml   # Preview venster voor te verwijderen bestanden
├── IconGenerator.cs            # Genereert het applicatie icoon
├── Models/
│   ├── CustomerFolder.cs       # Model voor klantmap
│   ├── BackupSet.cs            # Model voor backup set (groep bestanden)
│   └── FileToDelete.cs         # Model voor te verwijderen bestand
└── Services/
    ├── BackupService.cs        # Logica voor scannen en verwijderen
    └── SettingsService.cs      # Opslaan/laden van instellingen
```

## Licentie

© Rentpro
