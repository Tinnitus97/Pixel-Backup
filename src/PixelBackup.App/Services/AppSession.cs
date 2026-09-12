using System.Collections.ObjectModel;
using Avalonia.Threading;
using PixelBackup.App.Mvvm;
using PixelBackup.Core.Adb;
using PixelBackup.Core.Diagnostics;
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
    private string _adbStatus = "adb wird gesucht …";
    private bool _adbAvailable;
    private bool _isBusy;
    private string _busyDescription = string.Empty;

    public AppSession()
    {
        Settings = _settingsStore.Load();
        _fileLog = new FileLogSink(Path.Combine(_settingsStore.Directory, "logs"));
        Log = new CompositeLogSink(_fileLog, new DelegateLogSink(AppendLogEntry));
        Repository = new BackupRepository(Settings.BackupRoot, Log);
    }

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
        Log.Info("Pixel Backup gestartet.");
        await ConnectAdbAsync().ConfigureAwait(false);
    }

    /// <summary>Sucht adb, startet den Server und beginnt mit der Geräteüberwachung.</summary>
    public async Task ConnectAdbAsync()
    {
        var path = AdbLocator.Locate(Settings.AdbPath);
        if (path is null)
        {
            AdbAvailable = false;
            AdbStatus = "adb wurde nicht gefunden. Bitte die Android-Plattform-Tools installieren oder den Pfad in den Einstellungen setzen.";
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
            Log.Info("adb bereit: " + AdbStatus);
        }
        catch (Exception ex)
        {
            AdbAvailable = false;
            AdbStatus = "adb konnte nicht gestartet werden: " + ex.Message;
            Log.Error("adb konnte nicht gestartet werden", ex);
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
            Log.Error("Geräteliste konnte nicht gelesen werden", ex);
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
            Log.Error("Geräteinformationen konnten nicht gelesen werden", ex);
        }
    }

    // --------------------------------------------------------------- Dienste

    public BackupService CreateBackupService() =>
        new(_adb ?? throw new InvalidOperationException("adb ist nicht verfügbar."), Log);

    public RestoreService CreateRestoreService() =>
        new(_adb ?? throw new InvalidOperationException("adb ist nicht verfügbar."), Log);

    public VerificationService CreateVerificationService() => new(Log);

    public void NotifyBackupsChanged() => Dispatcher.UIThread.Post(() => BackupsChanged?.Invoke());

    // ----------------------------------------------------------- Einstellungen

    public void SaveSettings()
    {
        try
        {
            Repository.Root = Settings.BackupRoot;
            _settingsStore.Save(Settings);
            Log.Debug("Einstellungen gespeichert.");
        }
        catch (Exception ex)
        {
            Log.Error("Einstellungen konnten nicht gespeichert werden", ex);
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
