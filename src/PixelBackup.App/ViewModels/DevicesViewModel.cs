using PixelBackup.App.Mvvm;
using PixelBackup.App.Services;
using PixelBackup.Core.Diagnostics;

namespace PixelBackup.App.ViewModels;

/// <summary>Seite "Gerät": Verbindungsstatus, Geräteinformationen und WLAN-Kopplung.</summary>
public sealed class DevicesViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private string _wirelessAddress = string.Empty;

    public DevicesViewModel(AppSession session)
    {
        _session = session;
        Title = "Gerät";
        Icon = "📱";

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _session.AdbAvailable, ReportError);
        RestartAdbCommand = new AsyncRelayCommand(RestartAdbAsync, () => _session.AdbAvailable, ReportError);
        ConnectWirelessCommand = new AsyncRelayCommand(ConnectWirelessAsync, () => _session.AdbAvailable, ReportError);
        EnableWirelessCommand = new AsyncRelayCommand(EnableWirelessAsync, () => _session.HasDevice, ReportError);

        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSession.AdbAvailable) or nameof(AppSession.SelectedDevice))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                RestartAdbCommand.RaiseCanExecuteChanged();
                ConnectWirelessCommand.RaiseCanExecuteChanged();
                EnableWirelessCommand.RaiseCanExecuteChanged();
            }
        };
    }

    public AppSession Session => _session;

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand RestartAdbCommand { get; }

    public AsyncRelayCommand ConnectWirelessCommand { get; }

    public AsyncRelayCommand EnableWirelessCommand { get; }

    public string WirelessAddress
    {
        get => _wirelessAddress;
        set => SetProperty(ref _wirelessAddress, value);
    }

    public string SetupHint =>
        "1. Am Gerät die Entwickleroptionen aktivieren (Einstellungen ▸ Über das Telefon ▸ siebenmal auf die Build-Nummer tippen)." +
        Environment.NewLine +
        "2. In den Entwickleroptionen „USB-Debugging“ einschalten." +
        Environment.NewLine +
        "3. Gerät per USB anschließen und die Abfrage „USB-Debugging zulassen?“ bestätigen." +
        Environment.NewLine +
        "Das funktioniert mit jedem Android-Gerät – Pixel, Samsung, Xiaomi, OnePlus und anderen.";

    public override async Task ActivateAsync()
    {
        if (_session.AdbAvailable)
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    private async Task RefreshAsync()
    {
        StatusMessage = "Geräteliste wird gelesen …";
        await _session.RefreshDevicesAsync().ConfigureAwait(true);
        await _session.RefreshDeviceInfoAsync().ConfigureAwait(true);
        StatusMessage = _session.Devices.Count switch
        {
            0 => "Kein Gerät gefunden. Bitte USB-Debugging prüfen.",
            1 => "Ein Gerät gefunden.",
            _ => $"{_session.Devices.Count} Geräte gefunden."
        };
    }

    private async Task RestartAdbAsync()
    {
        if (_session.Adb is null)
        {
            return;
        }

        StatusMessage = "adb-Server wird neu gestartet …";
        await _session.Adb.KillServerAsync().ConfigureAwait(true);
        await _session.Adb.StartServerAsync().ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task ConnectWirelessAsync()
    {
        if (_session.Adb is null || string.IsNullOrWhiteSpace(WirelessAddress))
        {
            StatusMessage = "Bitte IP-Adresse und Port angeben, zum Beispiel 192.168.1.50:5555.";
            return;
        }

        var result = await _session.Adb.ConnectAsync(WirelessAddress.Trim()).ConfigureAwait(true);
        StatusMessage = result.CombinedOutput.Trim();
        await _session.RefreshDevicesAsync().ConfigureAwait(true);
    }

    private async Task EnableWirelessAsync()
    {
        if (_session.Adb is null || _session.SelectedDevice is null)
        {
            return;
        }

        var serial = _session.SelectedDevice.Serial;
        StatusMessage = "WLAN-Modus wird aktiviert …";

        var tcp = await _session.Adb.EnableTcpIpAsync(serial, 5555).ConfigureAwait(true);
        if (!tcp.Success)
        {
            StatusMessage = "Der WLAN-Modus konnte nicht aktiviert werden: " + tcp.ErrorSummary;
            return;
        }

        var address = await _session.Adb.GetWlanAddressAsync(serial).ConfigureAwait(true);
        if (address is null)
        {
            StatusMessage = "Das Gerät ist im WLAN-Modus, es wurde aber keine WLAN-Adresse gefunden. Bitte manuell verbinden.";
            return;
        }

        WirelessAddress = address + ":5555";
        var connect = await _session.Adb.ConnectAsync(WirelessAddress).ConfigureAwait(true);
        StatusMessage = connect.CombinedOutput.Trim();
        await _session.RefreshDevicesAsync().ConfigureAwait(true);
    }

    private void ReportError(Exception ex)
    {
        _session.Log.Error("Gerätezugriff fehlgeschlagen", ex);
        StatusMessage = "Fehler: " + ex.Message;
    }
}
