using System.Collections.ObjectModel;
using Avalonia.Threading;
using PixelBackup.App.Mvvm;
using PixelBackup.Core.Adb;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;
using PixelBackup.Core.Services;

namespace PixelBackup.App.Services;

/// <summary>
/// Gemeinsamer Zustand der Anwendung: Einstellungen, adb-Verbindung, Geräte, Protokoll
/// und die Fachdienste. Alle Seiten greifen auf dieselbe Instanz zu.
/// </summary>
public sealed class AppSession : ObservableObject, IDisposable
{
    private const int MaxLogEntries = 5000;

    private readonly SettingsStore _settingsStore = new();
    private readonly FileLogSink _fileLog;

    private AdbClient? _adb;
    private DeviceWatcher? _watcher;
    private AdbDevice? _selectedDevice;
    private DeviceInfo? _deviceInfo;
    private string _adbStatus = Loc.Tr("adb wird gesucht …", "looking for adb …");
    private bool _adbAvailable;
    private bool _isBusy;
    private string _busyDescription = string.Empty;

    public AppSession()
    {
        Settings = _settingsStore.Load();

        // Sprache zuerst: alle folgenden Meldungen sollen schon stimmen.
        ApplyLanguage();

        _fileLog = new FileLogSink(Path.Combine(_settingsStore.Directory, "logs"));
        Log = new CompositeLogSink(_fileLog, new DelegateLogSink(AppendLogEntry));
        Repository = new BackupRepository(Settings.BackupRoot, Log);
        Components = new ComponentService(this);
    }

    /// <summary>Prüfung und Einrichtung von adb und USB-Treiber.</summary>
    public ComponentService Components { get; }

    /// <summary>Übernimmt die eingestellte Sprache (oder die des Systems).</summary>
    public void ApplyLanguage()
    {
        Localizer.I.Lang = Settings.Language switch
        {
            LanguageSetting.German => AppLanguage.De,
            LanguageSetting.English => AppLanguage.En,
            _ => Localizer.DetectFromSystem()
        };

        Localizer.I.ApplyCulture();
    }

    /// <summary>Übernimmt das eingestellte Erscheinungsbild (oder das des Systems).</summary>
    public void ApplyTheme() => ThemeService.Apply(Settings.Theme);

    public AppSettings Settings { get; }

    public ILogSink Log { get; }

    public BackupRepository Repository { get; }

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public ObservableCollection<AdbDevice> Devices { get; } = new();

    /// <summary>Wird ausgelöst, wenn ein betriebsbereites Gerät neu angeschlossen wurde.</summary>
    public event Action<AdbDevice>? DeviceConnected;

    /// <summary>Wird ausgelöst, wenn sich die Sicherungssätze geändert haben.</summary>
    public event Action? BackupsChanged;

    public AdbClient? Adb => _adb;

    public bool AdbAvailable
    {
        get => _adbAvailable;
        private set => SetProperty(ref _adbAvailable, value);
    }

    public string AdbStatus
    {
        get => _adbStatus;
        private set => SetProperty(ref _adbStatus, value);
    }

    public AdbDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                OnPropertiesChanged(nameof(HasDevice), nameof(DeviceHeadline));
                _ = RefreshDeviceInfoAsync();
            }
        }
    }

    public DeviceInfo? DeviceInfo
    {
        get => _deviceInfo;
        private set
        {
            if (SetProperty(ref _deviceInfo, value))
            {
                OnPropertyChanged(nameof(DeviceHeadline));
            }
        }
    }

    public bool HasDevice => SelectedDevice is { IsReady: true };

    public string DeviceHeadline => SelectedDevice is null
        ? "Kein Gerät ausgewählt"
        : DeviceInfo is null
            ? SelectedDevice.DisplayName
            : $"{DeviceInfo.DisplayName} · {DeviceInfo.AndroidText}";

    /// <summary>Gesetzt, solange eine Sicherung oder Wiederherstellung läuft.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsIdle));

            // Während einer Sicherung keine Geräteabfragen dazwischenfunken lassen.
            if (value)
            {
                _watcher?.Stop();
            }
            else if (Settings.WatchDevices)
            {
                StartWatcher();
            }
        }
    }

    public bool IsIdle => !IsBusy;

    public string BusyDescription
    {
        get => _busyDescription;
        set => SetProperty(ref _busyDescription, value);
    }

    // ------------------------------------------------------------------ Start

    public async Task InitializeAsync()
    {
        Log.Info(Loc.Tr("Pixel Backup gestartet.", "Pixel Backup started."));
        Log.Info(Loc.Tr(
            $"Sprache: {(Localizer.I.Lang == AppLanguage.De ? "Deutsch" : "Englisch")}, Erscheinungsbild: {ThemeService.DescribeDetected()}",
            $"Language: {(Localizer.I.Lang == AppLanguage.De ? "German" : "English")}, appearance: {ThemeService.DescribeDetected()}"));

        // Fehlt adb, wird es auf Wunsch gleich eingerichtet.
        if (Settings.AutoInstallAdb && AdbLocator.Locate(Settings.AdbPath) is null)
        {
            await Components.EnsureAdbAsync().ConfigureAwait(false);
        }

        await ConnectAdbAsync().ConfigureAwait(false);

        if (Settings.CheckComponentsOnStart)
        {
            _ = Components.RefreshAsync();
        }
    }

    /// <summary>Sucht adb, startet den Server und beginnt mit der Geräteüberwachung.</summary>
    public async Task ConnectAdbAsync()
    {
        var path = AdbLocator.Locate(Settings.AdbPath);
        if (path is null)
        {
            AdbAvailable = false;
            AdbStatus = Loc.Tr(
                "adb wurde nicht gefunden. Bitte unter „Komponenten“ einrichten lassen.",
                "adb was not found. Please set it up under \"Components\".");
            Log.Warn(AdbStatus);
            return;
        }

        _adb = new AdbClient(path, Log);

        try
        {
            await _adb.StartServerAsync().ConfigureAwait(false);
            var version = await _adb.GetVersionAsync().ConfigureAwait(false);
            AdbAvailable = true;
            AdbStatus = $"{version} · {path}";
            Log.Info(Loc.Tr("adb bereit: ", "adb ready: ") + AdbStatus);
        }
        catch (Exception ex)
        {
            AdbAvailable = false;
            AdbStatus = Loc.Tr("adb konnte nicht gestartet werden: ", "adb could not be started: ") + ex.Message;
            Log.Error(Loc.Tr("adb konnte nicht gestartet werden", "adb could not be started"), ex);
            return;
        }

        StartWatcher();
        await RefreshDevicesAsync().ConfigureAwait(false);
    }

    private void StartWatcher()
    {
        if (_adb is null || !Settings.WatchDevices)
        {
            return;
        }

        if (_watcher is null)
        {
            _watcher = new DeviceWatcher(_adb, Log);
            _watcher.DevicesChanged += devices => Dispatcher.UIThread.Post(() => ApplyDevices(devices));
            _watcher.DeviceConnected += device => Dispatcher.UIThread.Post(() => DeviceConnected?.Invoke(device));
        }
        else
        {
            _watcher.Adb = _adb;
        }

        _watcher.Start();
    }

    public void StopWatcher() => _watcher?.Stop();

    // ----------------------------------------------------------------- Geräte

    public async Task RefreshDevicesAsync()
    {
        if (_adb is null)
        {
            return;
        }

        try
        {
            var devices = await _adb.ListDevicesAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => ApplyDevices(devices));
        }
        catch (Exception ex)
        {
            Log.Error(Loc.Tr("Geräteliste konnte nicht gelesen werden", "The device list could not be read"), ex);
        }
    }

    private void ApplyDevices(IReadOnlyList<AdbDevice> devices)
    {
        var previousSerial = SelectedDevice?.Serial;

        Devices.Clear();
        foreach (var device in devices)
        {
            Devices.Add(device);
        }

        var next = devices.FirstOrDefault(d => d.Serial == previousSerial)
                   ?? devices.FirstOrDefault(d => d.IsReady)
                   ?? devices.FirstOrDefault();

        if (next?.Serial != SelectedDevice?.Serial || next?.State != SelectedDevice?.State)
        {
            SelectedDevice = next;
        }
    }

    public async Task RefreshDeviceInfoAsync()
    {
        var device = SelectedDevice;
        if (_adb is null || device is null || !device.IsReady)
        {
            DeviceInfo = null;
            return;
        }

        try
        {
            var info = await _adb.GetDeviceInfoAsync(device.Serial).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => DeviceInfo = info);
        }
        catch (Exception ex)
        {
            Log.Error(Loc.Tr("Geräteinformationen konnten nicht gelesen werden", "The device information could not be read"), ex);
        }
    }

    // --------------------------------------------------------------- Dienste

    public BackupService CreateBackupService() =>
        new(_adb ?? throw new InvalidOperationException(Loc.Tr("adb ist nicht verfügbar.", "adb is not available.")), Log);

    public RestoreService CreateRestoreService() =>
        new(_adb ?? throw new InvalidOperationException(Loc.Tr("adb ist nicht verfügbar.", "adb is not available.")), Log);

    public VerificationService CreateVerificationService() => new(Log);

    public void NotifyBackupsChanged() => Dispatcher.UIThread.Post(() => BackupsChanged?.Invoke());

    // ----------------------------------------------------------- Einstellungen

    public void SaveSettings()
    {
        try
        {
            Repository.Root = Settings.BackupRoot;
            _settingsStore.Save(Settings);
            Log.Debug(Loc.Tr("Einstellungen gespeichert.", "Settings saved."));
        }
        catch (Exception ex)
        {
            Log.Error(Loc.Tr("Einstellungen konnten nicht gespeichert werden", "Settings could not be saved"), ex);
        }
    }

    public string SettingsFilePath => _settingsStore.FilePath;

    public string LogDirectory => _fileLog.Directory;

    // ---------------------------------------------------------------- Protokoll

    private void AppendLogEntry(LogEntry entry)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Add(entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Add(entry));
        }

        void Add(LogEntry item)
        {
            LogEntries.Add(item);
            while (LogEntries.Count > MaxLogEntries)
            {
                LogEntries.RemoveAt(0);
            }
        }
    }

    public void Shutdown()
    {
        SaveSettings();
        Dispose();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _fileLog.Dispose();
    }
}
