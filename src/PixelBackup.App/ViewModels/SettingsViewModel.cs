using System.Collections.ObjectModel;
using PixelBackup.App.Mvvm;
using PixelBackup.App.Services;
using PixelBackup.Core.Adb;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Services;

namespace PixelBackup.App.ViewModels;

public sealed record ThemeOption(AppTheme Theme, string DisplayName);

public sealed record LanguageOption(LanguageSetting Language, string DisplayName);

/// <summary>Seite "Einstellungen".</summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private ThemeOption _selectedTheme;
    private LanguageOption _selectedLanguage;
    private bool _suspendApply;

    public SettingsViewModel(AppSession session)
    {
        _session = session;
        Icon = "⚙";
        UpdateTitle();

        ThemeOptions = new ObservableCollection<ThemeOption>();
        LanguageOptions = new ObservableCollection<LanguageOption>();
        BuildOptions();

        _selectedTheme = ThemeOptions.First(t => t.Theme == session.Settings.Theme);
        _selectedLanguage = LanguageOptions.First(l => l.Language == session.Settings.Language);

        BrowseBackupRootCommand = new AsyncRelayCommand(BrowseBackupRootAsync, null, ReportError);
        BrowseAdbCommand = new AsyncRelayCommand(BrowseAdbAsync, null, ReportError);
        DetectAdbCommand = new AsyncRelayCommand(DetectAdbAsync, null, ReportError);
        OpenBackupFolderCommand = new RelayCommand(() => DialogService.OpenInFileManager(BackupRoot));
        OpenSettingsFolderCommand = new RelayCommand(() => DialogService.OpenInFileManager(
            Path.GetDirectoryName(_session.SettingsFilePath) ?? _session.SettingsFilePath));
        SaveCommand = new RelayCommand(Save);

        ThemeService.Apply(session.Settings.Theme);
    }

    protected override void UpdateTitle() => Title = Tr("Einstellungen", "Settings");

    public override void RefreshTexts()
    {
        // Die Auswahllisten enthalten übersetzte Texte und müssen neu aufgebaut werden.
        _suspendApply = true;
        var theme = _selectedTheme.Theme;
        var language = _selectedLanguage.Language;

        BuildOptions();
        SelectedTheme = ThemeOptions.First(t => t.Theme == theme);
        SelectedLanguage = LanguageOptions.First(l => l.Language == language);
        _suspendApply = false;

        base.RefreshTexts();
    }

    private void BuildOptions()
    {
        ThemeOptions.Clear();
        ThemeOptions.Add(new ThemeOption(AppTheme.System, Tr("Wie das System", "Follow the system")));
        ThemeOptions.Add(new ThemeOption(AppTheme.Light, Tr("Hell", "Light")));
        ThemeOptions.Add(new ThemeOption(AppTheme.Dark, Tr("Dunkel", "Dark")));

        LanguageOptions.Clear();
        LanguageOptions.Add(new LanguageOption(LanguageSetting.System, Tr("Wie das System", "Follow the system")));
        LanguageOptions.Add(new LanguageOption(LanguageSetting.German, "Deutsch"));
        LanguageOptions.Add(new LanguageOption(LanguageSetting.English, "English"));
    }

    public AppSession Session => _session;

    public ObservableCollection<ThemeOption> ThemeOptions { get; }

    public ObservableCollection<LanguageOption> LanguageOptions { get; }

    public AsyncRelayCommand BrowseBackupRootCommand { get; }

    public AsyncRelayCommand BrowseAdbCommand { get; }

    public AsyncRelayCommand DetectAdbCommand { get; }

    public RelayCommand OpenBackupFolderCommand { get; }

    public RelayCommand OpenSettingsFolderCommand { get; }

    public RelayCommand SaveCommand { get; }

    // ------------------------------------------------------------- Speicherorte

    public string BackupRoot
    {
        get => _session.Settings.BackupRoot;
        set
        {
            if (_session.Settings.BackupRoot == value)
            {
                return;
            }

            _session.Settings.BackupRoot = value;
            _session.Repository.Root = value;
            OnPropertyChanged();
        }
    }

    public string AdbPath
    {
        get => _session.Settings.AdbPath ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (_session.Settings.AdbPath == normalized)
            {
                return;
            }

            _session.Settings.AdbPath = normalized;
            OnPropertyChanged();
        }
    }

    // ---------------------------------------------------------------- Sicherung

    public bool ComputeHashes
    {
        get => _session.Settings.ComputeHashes;
        set
        {
            _session.Settings.ComputeHashes = value;
            OnPropertyChanged();
        }
    }

    public bool IncrementalByDefault
    {
        get => _session.Settings.IncrementalByDefault;
        set
        {
            _session.Settings.IncrementalByDefault = value;
            OnPropertyChanged();
        }
    }

    public bool RemoveDeletedFiles
    {
        get => _session.Settings.RemoveDeletedFiles;
        set
        {
            _session.Settings.RemoveDeletedFiles = value;
            OnPropertyChanged();
        }
    }

    public bool CreateArchive
    {
        get => _session.Settings.CreateArchive;
        set
        {
            _session.Settings.CreateArchive = value;
            OnPropertyChanged();
        }
    }

    public int KeepSetsPerDevice
    {
        get => _session.Settings.KeepSetsPerDevice;
        set
        {
            _session.Settings.KeepSetsPerDevice = Math.Max(0, value);
            OnPropertyChanged();
        }
    }

    // ----------------------------------------------------------------- Automatik

    public bool AutoBackupOnConnect
    {
        get => _session.Settings.AutoBackupOnConnect;
        set
        {
            _session.Settings.AutoBackupOnConnect = value;
            OnPropertyChanged();
        }
    }

    public bool WatchDevices
    {
        get => _session.Settings.WatchDevices;
        set
        {
            _session.Settings.WatchDevices = value;
            OnPropertyChanged();
            if (!value)
            {
                _session.StopWatcher();
            }
        }
    }

    public bool CheckComponentsOnStart
    {
        get => _session.Settings.CheckComponentsOnStart;
        set
        {
            _session.Settings.CheckComponentsOnStart = value;
            OnPropertyChanged();
        }
    }

    public bool CheckUpdatesOnStart
    {
        get => _session.Settings.CheckUpdatesOnStart;
        set
        {
            _session.Settings.CheckUpdatesOnStart = value;
            OnPropertyChanged();
        }
    }

    public bool AutoInstallAdb
    {
        get => _session.Settings.AutoInstallAdb;
        set
        {
            _session.Settings.AutoInstallAdb = value;
            OnPropertyChanged();
        }
    }

    // -------------------------------------------------------- Sprache & Aussehen

    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!SetProperty(ref _selectedTheme, value) || _suspendApply)
            {
                return;
            }

            _session.Settings.Theme = value.Theme;
            ThemeService.Apply(value.Theme);
            OnPropertyChanged(nameof(ThemeDetectedText));
            Save();
        }
    }

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!SetProperty(ref _selectedLanguage, value) || _suspendApply)
            {
                return;
            }

            _session.Settings.Language = value.Language;
            _session.ApplyLanguage();
            Save();
        }
    }

    /// <summary>Zeigt an, welches Erscheinungsbild gerade tatsächlich verwendet wird.</summary>
    public string ThemeDetectedText => _session.Settings.Theme == AppTheme.System
        ? $"({ThemeService.DescribeDetected()})"
        : string.Empty;

    public string LanguageDetectedText => _session.Settings.Language == LanguageSetting.System
        ? Loc.Lang == AppLanguage.De ? "(erkannt: Deutsch)" : "(detected: English)"
        : string.Empty;

    public string AdbStatusText => _session.AdbStatus;

    // ------------------------------------------------------------------ Aktionen

    private async Task BrowseBackupRootAsync()
    {
        var folder = await DialogService.PickFolderAsync(Tr("Ordner für die Sicherungen", "Folder for the backups"), BackupRoot);
        if (folder is not null)
        {
            BackupRoot = folder;
            Save();
        }
    }

    private async Task BrowseAdbAsync()
    {
        var file = await DialogService.PickFileAsync(Tr("adb auswählen", "Select adb"), Path.GetDirectoryName(AdbPath));
        if (file is not null)
        {
            AdbPath = file;
            Save();
            await _session.ConnectAdbAsync().ConfigureAwait(true);
            OnPropertyChanged(nameof(AdbStatusText));
        }
    }

    private async Task DetectAdbAsync()
    {
        var found = AdbLocator.Locate(_session.Settings.AdbPath);
        StatusMessage = found is null
            ? Tr("adb wurde nicht gefunden. Bitte unter „Komponenten“ einrichten lassen.",
                 "adb was not found. Please set it up under \"Components\".")
            : Tr("adb gefunden: ", "adb found: ") + found;

        await _session.ConnectAdbAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(AdbStatusText));
    }

    private void Save()
    {
        _session.SaveSettings();
        StatusMessage = Tr("Einstellungen gespeichert.", "Settings saved.");
    }

    private void ReportError(Exception ex)
    {
        _session.Log.Error(Tr("Einstellung konnte nicht übernommen werden", "The setting could not be applied"), ex);
        StatusMessage = Tr("Fehler: ", "Error: ") + ex.Message;
    }

    #region Beschriftungen

    public string PageHint => Tr(
        "Alle Angaben werden sofort übernommen und beim Beenden gespeichert.",
        "Every change takes effect immediately and is saved on exit.");

    public string SectionPaths => Tr("Speicherorte", "Locations");

    public string SectionBackup => Tr("Sicherung", "Backup");

    public string SectionAutomation => Tr("Automatik", "Automation");

    public string SectionAppearance => Tr("Sprache & Darstellung", "Language & appearance");

    public string LabelBackupRoot => Tr("Ordner für die Sicherungen", "Folder for the backups");

    public string LabelAdbPath => Tr("Pfad zu adb (leer lassen für die automatische Suche)", "Path to adb (leave empty to search automatically)");

    public string LabelBrowse => Tr("Auswählen …", "Browse …");

    public string LabelOpen => Tr("Öffnen", "Open");

    public string LabelDetectAdb => Tr("Suchen und verbinden", "Find and connect");

    public string LabelIncrementalDefault => Tr("Standardmäßig nur Neues und Geändertes sichern", "Back up new and changed items only, by default");

    public string LabelHashes => Tr("Prüfsummen (SHA-256) berechnen", "Calculate checksums (SHA-256)");

    public string LabelRemoveDeleted => Tr("Auf dem Gerät gelöschte Dateien auch aus der Sicherung entfernen", "Also remove files from the backup that were deleted on the device");

    public string LabelArchive => Tr("Nach jeder Sicherung ein ZIP-Archiv erstellen", "Create a ZIP archive after every backup");

    public string LabelRetention => Tr("Aufbewahrung je Gerät (0 = alle behalten):", "Retention per device (0 = keep all):");

    public string LabelRetentionUnit => Tr("Sicherungssätze", "backup sets");

    public string LabelWatchDevices => Tr("Angeschlossene Geräte laufend überwachen", "Keep watching connected devices");

    public string LabelAutoBackup => Tr("Sicherung automatisch starten, sobald ein bekanntes Gerät angeschlossen wird", "Start a backup automatically as soon as a known device is connected");

    public string HintAutoBackup => Tr(
        "Die automatische Sicherung verwendet die zuletzt gewählten Gruppen und läuft ohne Rückfrage.",
        "The automatic backup uses the groups selected last time and runs without asking.");

    public string LabelCheckComponents => Tr("Beim Start prüfen, ob adb und Treiber aktuell sind", "Check at startup whether adb and drivers are up to date");

    public string LabelAutoInstallAdb => Tr("Fehlende Plattform-Tools selbsttätig nachinstallieren", "Install missing platform tools automatically");

    public string LabelLanguage => Tr("Sprache:", "Language:");

    public string LabelTheme => Tr("Erscheinungsbild:", "Appearance:");

    public string LabelCheckUpdates => Tr(
        "Beim Start nach einer neueren Fassung suchen",
        "Look for a newer version at startup");

    /// <summary>Eigene Fassung und Einbauart – dieselbe Angabe wie auf der Seite „Komponenten“.</summary>
    public string VersionText => InstallationInfo.Describe();

    public string HintUpdates => Tr(
        "Geprüft wird eine einfache Datei aus der jüngsten Veröffentlichung (update.json). " +
        "Eingespielt wird nichts ohne Rückfrage – das erledigt die Seite „Komponenten“.",
        "The check reads a plain file from the latest release (update.json). Nothing is installed " +
        "without asking – that happens on the \"Components\" page.");

    public string LabelSave => Tr("Einstellungen speichern", "Save settings");

    public string LabelOpenSettingsFolder => Tr("Einstellungsordner öffnen", "Open settings folder");

    #endregion
}
