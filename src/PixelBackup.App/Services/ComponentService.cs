using PixelBackup.App.Mvvm;
using PixelBackup.Core.Adb;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;
using PixelBackup.Core.Services;

namespace PixelBackup.App.Services;

/// <summary>
/// Kümmert sich um die benötigten Fremdbestandteile: die Android-Plattform-Tools
/// (adb) und unter Windows den USB-Treiber. Prüft Vorhandensein und Stand,
/// installiert auf Wunsch und meldet den Fortschritt an die Oberfläche.
/// </summary>
public sealed class ComponentService : ObservableObject
{
    private readonly AppSession _session;
    private readonly PlatformToolsInstaller _installer;
    private readonly UsbDriverService _usbDriver;
    private readonly LinuxDeviceAccessService _linuxAccess;
    private readonly UpdateService _updater;
    private readonly UpdateInstaller _updateInstaller;

    private ComponentStatus _adb;
    private ComponentStatus _usb;
    private ComponentStatus _app;
    private UpdateCheckResult? _updateCheck;
    private SdkPackage? _adbPackage;
    private SdkPackage? _usbPackage;
    private bool _isBusy;
    private string _busyText = string.Empty;
    private double _percent;
    private DateTimeOffset _lastCheck;

    public ComponentService(AppSession session)
    {
        _session = session;
        _installer = new PlatformToolsInstaller(session.Log);
        _usbDriver = new UsbDriverService(session.Log);
        _linuxAccess = new LinuxDeviceAccessService(session.Log);

        _updater = new UpdateService(session.Log);
        _updateInstaller = new UpdateInstaller(session.Log);

        _adb = new ComponentStatus { Name = Loc.Tr("Android-Plattform-Tools (adb)", "Android platform tools (adb)") };
        _usb = new ComponentStatus { Name = DeviceAccessName };
        _app = NewAppStatus();
    }

    public ComponentStatus Adb
    {
        get => _adb;
        private set
        {
            _adb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AllHealthy));
        }
    }

    /// <summary>
    /// Der Zugang zum Gerät: unter Windows der USB-Treiber, unter Linux die udev-Regeln.
    /// </summary>
    public ComponentStatus DeviceAccess
    {
        get => _usb;
        private set
        {
            _usb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AllHealthy));
        }
    }

    /// <summary>Pixel Backup selbst: eigene Fassung und angebotene Fassung.</summary>
    public ComponentStatus Application
    {
        get => _app;
        private set
        {
            _app = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AllHealthy));
        }
    }

    /// <summary>Das Ergebnis der letzten Abfrage – enthält die Datei zum Einspielen.</summary>
    public UpdateCheckResult? UpdateCheck => _updateCheck;

    public bool AllHealthy => Adb.IsHealthy && DeviceAccess.IsHealthy && Application.IsHealthy;

    /// <summary>Meldet die Anwendung, dass sie sich für den Austausch beenden soll.</summary>
    public event Action? ExitRequested;

    private ComponentStatus NewAppStatus() => new()
    {
        Name = Loc.Tr("Pixel Backup", "Pixel Backup"),
        InstalledVersion = InstallationInfo.CurrentVersion,
        State = ComponentState.Unknown,
        Location = InstallationInfo.ExecutablePath,
        Message = Loc.Tr(
            $"Eingespielt als {InstallationInfo.KindText(InstallationInfo.Kind)}.",
            $"Installed as {InstallationInfo.KindText(InstallationInfo.Kind)}.")
    };

    /// <summary>Name der plattformabhängigen Zugangskomponente.</summary>
    public static string DeviceAccessName => OperatingSystem.IsWindows()
        ? Loc.Tr("USB-Treiber (Google)", "USB driver (Google)")
        : OperatingSystem.IsLinux()
            ? Loc.Tr("Geräteregeln (udev)", "Device rules (udev)")
            : Loc.Tr("Gerätezugriff", "Device access");

    /// <summary>Lässt sich die Zugangskomponente auf diesem System einrichten?</summary>
    public static bool CanSetUpDeviceAccess => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    public bool IsIdle => !IsBusy;

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    public double Percent
    {
        get => _percent;
        private set => SetProperty(ref _percent, value);
    }

    public DateTimeOffset LastCheck => _lastCheck;

    /// <summary>
    /// Prüft die Komponenten; <paramref name="online"/> steuert den Abgleich mit
    /// Google, <paramref name="includeUpdate"/> den Versionscheck der Anwendung.
    /// </summary>
    public async Task RefreshAsync(bool online = true, CancellationToken ct = default, bool includeUpdate = true)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyText = Loc.Tr("Komponenten werden geprüft …", "Checking components …");

        try
        {
            var (adbStatus, adbPackage) = await _installer
                .CheckAsync(_session.Settings.AdbPath, online, ct)
                .ConfigureAwait(false);

            _adbPackage = adbPackage;
            Adb = adbStatus;

            ComponentStatus accessStatus;
            if (OperatingSystem.IsLinux())
            {
                // Sieht adb ein Gerät, darf aber nicht zugreifen, fehlen die Regeln sicher.
                var blocked = _session.Devices.Any(d => d.State == AdbDeviceState.NoPermissions);
                accessStatus = await _linuxAccess.CheckAsync(blocked, ct).ConfigureAwait(false);
            }
            else
            {
                var (usbStatus, usbPackage) = await _usbDriver.CheckAsync(online, ct).ConfigureAwait(false);
                _usbPackage = usbPackage;
                accessStatus = usbStatus;
            }

            DeviceAccess = accessStatus;

            if (online && includeUpdate)
            {
                await CheckUpdateCoreAsync(ct).ConfigureAwait(false);
            }

            _lastCheck = DateTimeOffset.Now;
            _session.Log.Info(Loc.Tr(
                $"Komponenten geprüft – adb: {adbStatus.StateText}, {DeviceAccessName}: {accessStatus.StateText}.",
                $"Components checked – adb: {adbStatus.StateText}, {DeviceAccessName}: {accessStatus.StateText}."));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _session.Log.Error(Loc.Tr("Komponenten konnten nicht geprüft werden", "Components could not be checked"), ex);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            Percent = 0;
        }
    }

    /// <summary>Fragt nach, ob es eine neuere Fassung von Pixel Backup gibt.</summary>
    public async Task CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyText = Loc.Tr("Fassung wird geprüft …", "Checking version …");

        try
        {
            await CheckUpdateCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    /// <summary>Die eigentliche Abfrage – ohne eigene Sperre, damit sie auch im Sammel-Check läuft.</summary>
    private async Task CheckUpdateCoreAsync(CancellationToken ct)
    {
        var status = NewAppStatus();

        try
        {
            var result = await _updater
                .CheckAsync(_session.Settings.UpdateManifestUrl, ct)
                .ConfigureAwait(false);

            _updateCheck = result;
            status.Message = result.Describe();

            if (result.Error is not null)
            {
                // Ohne Netz ist das kein Fehler der Anwendung – der Stand bleibt offen.
                status.State = ComponentState.Unknown;
            }
            else if (result.UpdateAvailable)
            {
                status.State = ComponentState.UpdateAvailable;
                status.LatestVersion = result.OnlineVersion;
            }
            else
            {
                status.State = ComponentState.UpToDate;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            status.State = ComponentState.Unknown;
            status.Message = ex.Message;
        }

        Application = status;
    }

    /// <summary>
    /// Lädt die angebotene Fassung und spielt sie ein. Bei EXE, AppImage und
    /// portabler Fassung beendet sich die Anwendung danach selbst.
    /// </summary>
    public async Task<(bool Success, string Message)> InstallUpdateAsync(CancellationToken ct = default)
    {
        var check = _updateCheck;

        if (check is null || !check.UpdateAvailable)
        {
            return (false, Loc.Tr("Es liegt keine neuere Fassung vor.", "There is no newer version."));
        }

        if (check.Package is null)
        {
            return (false, Loc.Tr(
                $"Für diese Einbauart gibt es keine Datei – bitte von Hand holen: {UpdateService.ReleasesPageUrl}",
                $"There is no file for this installation kind – please fetch it manually: {UpdateService.ReleasesPageUrl}"));
        }

        if (IsBusy)
        {
            return (false, Loc.Tr("Es läuft bereits ein Vorgang.", "Another operation is already running."));
        }

        IsBusy = true;
        Percent = 0;
        BusyText = Loc.Tr($"Fassung {check.OnlineVersion} wird geladen …", $"Downloading version {check.OnlineVersion} …");

        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                Percent = p.Percent;
                BusyText = p.BytesTotal > 0 ? $"{p.Phase} … {p.Percent:F0} %" : p.Phase;
            });

            var download = await _updater.DownloadAsync(check.Package, progress, ct).ConfigureAwait(false);
            if (!download.Success)
            {
                return (false, download.Error ?? Loc.Tr("Der Download ist fehlgeschlagen.", "The download failed."));
            }

            BusyText = Loc.Tr("Die neue Fassung wird eingespielt …", "Installing the new version …");

            var applied = await _updateInstaller
                .ApplyAsync(check.Package, download.File, ct)
                .ConfigureAwait(false);

            if (applied.ShouldExit)
            {
                // Der Austausch läuft erst, wenn diese Anwendung beendet ist.
                ExitRequested?.Invoke();
            }
            else if (applied.Success)
            {
                await CheckUpdateCoreAsync(ct).ConfigureAwait(false);
            }

            return (applied.Success, applied.Message);
        }
        catch (OperationCanceledException)
        {
            return (false, Loc.Tr("Abgebrochen.", "Cancelled."));
        }
        catch (Exception ex)
        {
            _session.Log.Error(Loc.Tr("Die neue Fassung konnte nicht eingespielt werden", "The new version could not be installed"), ex);
            return (false, ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            Percent = 0;
        }
    }

    /// <summary>Richtet adb ein, falls es fehlt. Wird beim Start verwendet.</summary>
    public async Task<bool> EnsureAdbAsync(CancellationToken ct = default)
    {
        if (AdbLocator.Locate(_session.Settings.AdbPath) is not null)
        {
            return true;
        }

        _session.Log.Info(Loc.Tr(
            "adb fehlt – die Plattform-Tools werden automatisch eingerichtet.",
            "adb is missing – the platform tools are being installed automatically."));

        return await InstallOrUpdateAdbAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Lädt die aktuellen Plattform-Tools und stellt adb darauf um.</summary>
    public async Task<bool> InstallOrUpdateAdbAsync(CancellationToken ct = default)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        Percent = 0;
        BusyText = Loc.Tr("Plattform-Tools werden geladen …", "Downloading platform tools …");

        try
        {
            var package = _adbPackage ?? await _installer.QueryLatestAsync(ct).ConfigureAwait(false);
            if (package is null)
            {
                _session.Log.Warn(Loc.Tr(
                    "Die Paketliste von Google ist nicht erreichbar – bitte Internetverbindung prüfen.",
                    "Google's package list is unreachable – please check the internet connection."));
                return false;
            }

            // Laufende adb-Prozesse geben die Dateien sonst nicht frei.
            _session.StopWatcher();
            if (_session.Adb is not null)
            {
                await _session.Adb.KillServerAsync(ct).ConfigureAwait(false);
            }

            var progress = new Progress<DownloadProgress>(p =>
            {
                Percent = p.Percent;
                BusyText = p.BytesTotal > 0
                    ? $"{p.Phase} … {p.Percent:F0} %"
                    : p.Phase;
            });

            var adbPath = await _installer.InstallAsync(package, progress, ct).ConfigureAwait(false);

            _session.Settings.AdbPath = adbPath;
            _session.SaveSettings();
            _adbPackage = null;

            await _session.ConnectAdbAsync().ConfigureAwait(false);
            await RefreshAfterChangeAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _session.Log.Error(Loc.Tr("Plattform-Tools konnten nicht eingerichtet werden", "Platform tools could not be installed"), ex);
            return false;
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            Percent = 0;
        }
    }

    /// <summary>
    /// Richtet den Gerätezugang ein: unter Windows den Google-USB-Treiber,
    /// unter Linux die udev-Regeln.
    /// </summary>
    public async Task<(bool Success, string Message)> InstallDeviceAccessAsync(CancellationToken ct = default)
    {
        if (OperatingSystem.IsLinux())
        {
            return await InstallUdevRulesAsync(ct).ConfigureAwait(false);
        }

        if (!UsbDriverService.IsWindows)
        {
            return (false, Loc.Tr("Auf diesem System nicht nötig.", "Not needed on this system."));
        }

        if (IsBusy)
        {
            return (false, Loc.Tr("Es läuft bereits ein Vorgang.", "Another operation is already running."));
        }

        IsBusy = true;
        Percent = 0;
        BusyText = Loc.Tr("USB-Treiber wird geladen …", "Downloading USB driver …");

        try
        {
            var package = _usbPackage ?? await _usbDriver.QueryLatestAsync(ct).ConfigureAwait(false);
            if (package is null)
            {
                return (false, Loc.Tr(
                    "Das Treiberpaket ist nicht erreichbar – bitte Internetverbindung prüfen.",
                    "The driver package is unreachable – please check the internet connection."));
            }

            var progress = new Progress<DownloadProgress>(p =>
            {
                Percent = p.Percent;
                BusyText = p.BytesTotal > 0 ? $"{p.Phase} … {p.Percent:F0} %" : p.Phase;
            });

            var directory = await _usbDriver.DownloadAsync(package, progress, ct).ConfigureAwait(false);

            BusyText = Loc.Tr("Treiber wird eingerichtet (Rückfrage von Windows bestätigen) …",
                "Installing driver (please confirm the Windows prompt) …");

            var result = await _usbDriver.InstallAsync(directory, ct).ConfigureAwait(false);
            await RefreshAfterChangeAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException)
        {
            return (false, Loc.Tr("Abgebrochen.", "Cancelled."));
        }
        catch (Exception ex)
        {
            _session.Log.Error(Loc.Tr("USB-Treiber konnte nicht eingerichtet werden", "USB driver could not be installed"), ex);
            return (false, ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            Percent = 0;
        }
    }

    /// <summary>Schreibt die udev-Regeln (fragt dabei nach Administratorrechten).</summary>
    private async Task<(bool Success, string Message)> InstallUdevRulesAsync(CancellationToken ct)
    {
        if (IsBusy)
        {
            return (false, Loc.Tr("Es läuft bereits ein Vorgang.", "Another operation is already running."));
        }

        IsBusy = true;
        Percent = 0;
        BusyText = Loc.Tr(
            "Geräteregeln werden eingerichtet (Rückfrage bestätigen) …",
            "Installing device rules (please confirm the prompt) …");

        try
        {
            var result = await _linuxAccess.InstallAsync(ct).ConfigureAwait(false);
            await RefreshAfterChangeAsync(ct).ConfigureAwait(false);
            return result;
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            Percent = 0;
        }
    }

    private async Task RefreshAfterChangeAsync(CancellationToken ct)
    {
        IsBusy = false;
        await RefreshAsync(online: true, ct).ConfigureAwait(false);
    }

    /// <summary>Nach einem Sprachwechsel die Texte neu erzeugen.</summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Adb));
        OnPropertyChanged(nameof(DeviceAccess));
        OnPropertyChanged(nameof(Application));
        OnPropertyChanged(nameof(AllHealthy));
    }
}
