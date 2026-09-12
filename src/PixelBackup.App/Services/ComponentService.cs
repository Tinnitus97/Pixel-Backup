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

    private ComponentStatus _adb;
    private ComponentStatus _usb;
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

        _adb = new ComponentStatus { Name = Loc.Tr("Android-Plattform-Tools (adb)", "Android platform tools (adb)") };
        _usb = new ComponentStatus { Name = Loc.Tr("USB-Treiber (Google)", "USB driver (Google)") };
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

    public ComponentStatus UsbDriver
    {
        get => _usb;
        private set
        {
            _usb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AllHealthy));
        }
    }

    public bool AllHealthy => Adb.IsHealthy && UsbDriver.IsHealthy;

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

    /// <summary>Prüft beide Komponenten; <paramref name="online"/> steuert den Abgleich mit Google.</summary>
    public async Task RefreshAsync(bool online = true, CancellationToken ct = default)
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

            var (usbStatus, usbPackage) = await _usbDriver.CheckAsync(online, ct).ConfigureAwait(false);
            _usbPackage = usbPackage;
            UsbDriver = usbStatus;

            _lastCheck = DateTimeOffset.Now;
            _session.Log.Info(Loc.Tr(
                $"Komponenten geprüft – adb: {adbStatus.StateText}, USB-Treiber: {usbStatus.StateText}.",
                $"Components checked – adb: {adbStatus.StateText}, USB driver: {usbStatus.StateText}."));
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

    /// <summary>Lädt den Google-USB-Treiber und übergibt ihn an Windows.</summary>
    public async Task<(bool Success, string Message)> InstallUsbDriverAsync(CancellationToken ct = default)
    {
        if (!UsbDriverService.IsWindows)
        {
            return (false, Loc.Tr("Nur unter Windows nötig.", "Only needed on Windows."));
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

    private async Task RefreshAfterChangeAsync(CancellationToken ct)
    {
        IsBusy = false;
        await RefreshAsync(online: true, ct).ConfigureAwait(false);
    }

    /// <summary>Nach einem Sprachwechsel die Texte neu erzeugen.</summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Adb));
        OnPropertyChanged(nameof(UsbDriver));
        OnPropertyChanged(nameof(AllHealthy));
    }
}
