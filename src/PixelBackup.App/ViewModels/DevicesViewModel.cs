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
        Icon = "📱";
        UpdateTitle();

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

    protected override void UpdateTitle() => Title = Tr("Gerät", "Device");

    #region Beschriftungen

    public string PageHint => Tr(
        "Pixel Backup spricht jedes Android-Gerät direkt über adb an – unabhängig vom Hersteller.",
        "Pixel Backup talks to any Android device directly through adb – no matter the manufacturer.");

    public string SectionDevices => Tr("Verbundene Geräte", "Connected devices");

    public string SectionInfo => Tr("Geräteinformationen", "Device information");

    public string SectionWireless => Tr("Drahtlos verbinden", "Connect wirelessly");

    public string SectionFirstSteps => Tr("Erste Schritte", "Getting started");

    public string LabelRefresh => Tr("Aktualisieren", "Refresh");

    public string LabelRestartAdb => Tr("adb-Server neu starten", "Restart adb server");

    public string LabelConnect => Tr("Verbinden", "Connect");

    public string LabelEnableWireless => Tr("WLAN-Modus aktivieren", "Enable Wi-Fi mode");

    public string LabelModel => Tr("Modell", "Model");

    public string LabelAndroid => Tr("Android", "Android");

    public string LabelBuild => Tr("Build", "Build");

    public string LabelPatch => Tr("Sicherheitspatch", "Security patch");

    public string LabelStorage => Tr("Speicher", "Storage");

    public string LabelSerial => Tr("Seriennummer", "Serial number");

    public string LabelRoot => Tr("Root-Zugriff", "Root access");

    public string WirelessHint => Tr(
        "Gerät einmal per USB anschließen, den WLAN-Modus aktivieren und das Kabel danach abziehen.",
        "Connect the device by USB once, enable Wi-Fi mode and then unplug the cable.");

    public string SetupHint => Tr(
        "1. Am Gerät die Entwickleroptionen aktivieren (Einstellungen ▸ Über das Telefon ▸ siebenmal auf die Build-Nummer tippen)." +
        Environment.NewLine +
        "2. In den Entwickleroptionen „USB-Debugging“ einschalten." +
        Environment.NewLine +
        "3. Gerät per USB anschließen und die Abfrage „USB-Debugging zulassen?“ bestätigen." +
        Environment.NewLine +
        "Das funktioniert mit jedem Android-Gerät – Pixel, Samsung, Xiaomi, OnePlus und anderen.",
        "1. Enable the developer options on the device (Settings ▸ About phone ▸ tap the build number seven times)." +
        Environment.NewLine +
        "2. Switch on \"USB debugging\" in the developer options." +
        Environment.NewLine +
        "3. Connect the device by USB and confirm the \"Allow USB debugging?\" prompt." +
        Environment.NewLine +
        "This works with every Android device – Pixel, Samsung, Xiaomi, OnePlus and others.");

    #endregion

    public override async Task ActivateAsync()
    {
        if (_session.AdbAvailable)
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    private async Task RefreshAsync()
    {
        StatusMessage = Tr("Geräteliste wird gelesen …", "Reading the device list …");
        await _session.RefreshDevicesAsync().ConfigureAwait(true);
        await _session.RefreshDeviceInfoAsync().ConfigureAwait(true);
        StatusMessage = _session.Devices.Count switch
        {
            0 => Tr("Kein Gerät gefunden. Bitte USB-Debugging prüfen.", "No device found. Please check USB debugging."),
            1 => Tr("Ein Gerät gefunden.", "One device found."),
            _ => Tr($"{_session.Devices.Count} Geräte gefunden.", $"{_session.Devices.Count} devices found.")
        };
    }

    private async Task RestartAdbAsync()
    {
        if (_session.Adb is null)
        {
            return;
        }

        StatusMessage = Tr("adb-Server wird neu gestartet …", "Restarting the adb server …");
        await _session.Adb.KillServerAsync().ConfigureAwait(true);
        await _session.Adb.StartServerAsync().ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task ConnectWirelessAsync()
    {
        if (_session.Adb is null || string.IsNullOrWhiteSpace(WirelessAddress))
        {
            StatusMessage = Tr(
                "Bitte IP-Adresse und Port angeben, zum Beispiel 192.168.1.50:5555.",
                "Please enter an IP address and port, for example 192.168.1.50:5555.");
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
        StatusMessage = Tr("WLAN-Modus wird aktiviert …", "Enabling Wi-Fi mode …");

        var tcp = await _session.Adb.EnableTcpIpAsync(serial, 5555).ConfigureAwait(true);
        if (!tcp.Success)
        {
            StatusMessage = Tr("Der WLAN-Modus konnte nicht aktiviert werden: ", "Wi-Fi mode could not be enabled: ") + tcp.ErrorSummary;
            return;
        }

        var address = await _session.Adb.GetWlanAddressAsync(serial).ConfigureAwait(true);
        if (address is null)
        {
            StatusMessage = Tr(
                "Das Gerät ist im WLAN-Modus, es wurde aber keine WLAN-Adresse gefunden. Bitte manuell verbinden.",
                "The device is in Wi-Fi mode but no Wi-Fi address was found. Please connect manually.");
            return;
        }

        WirelessAddress = address + ":5555";
        var connect = await _session.Adb.ConnectAsync(WirelessAddress).ConfigureAwait(true);
        StatusMessage = connect.CombinedOutput.Trim();
        await _session.RefreshDevicesAsync().ConfigureAwait(true);
    }

    private void ReportError(Exception ex)
    {
        _session.Log.Error(Tr("Gerätezugriff fehlgeschlagen", "Device access failed"), ex);
        StatusMessage = Tr("Fehler: ", "Error: ") + ex.Message;
    }
}
