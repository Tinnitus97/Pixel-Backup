using PixelBackup.App.Mvvm;
using PixelBackup.App.Services;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;
using PixelBackup.Core.Services;

namespace PixelBackup.App.ViewModels;

/// <summary>
/// Seite "Komponenten": zeigt, ob adb und der USB-Treiber vorhanden und aktuell
/// sind, und richtet beides auf Wunsch ein.
/// </summary>
public sealed class ComponentsViewModel : ViewModelBase
{
    private readonly AppSession _session;

    public ComponentsViewModel(AppSession session)
    {
        _session = session;
        Icon = "🧩";
        UpdateTitle();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => Components.IsIdle, ReportError);
        InstallAdbCommand = new AsyncRelayCommand(InstallAdbAsync, () => Components.IsIdle, ReportError);
        InstallDriverCommand = new AsyncRelayCommand(
            InstallDriverAsync,
            () => Components.IsIdle && UsbDriverService.IsWindows,
            ReportError);

        Components.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(string.Empty);
            RaiseCommandStates();
        };
    }

    protected override void UpdateTitle() => Title = Tr("Komponenten", "Components");

    public override void RefreshTexts()
    {
        base.RefreshTexts();

        // Die Statusmeldungen stecken als fertiger Text in den Ergebnissen,
        // deshalb einmal ohne Netzabfrage neu erzeugen.
        _ = Components.RefreshAsync(online: false);
    }

    public AppSession Session => _session;

    public ComponentService Components => _session.Components;

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand InstallAdbCommand { get; }

    public AsyncRelayCommand InstallDriverCommand { get; }

    public ComponentStatus Adb => Components.Adb;

    public ComponentStatus UsbDriver => Components.UsbDriver;

    public bool IsWindows => UsbDriverService.IsWindows;

    public string LastCheckText => Components.LastCheck == default
        ? Tr("noch nicht geprüft", "not checked yet")
        : Tr($"zuletzt geprüft: {Components.LastCheck:g}", $"last checked: {Components.LastCheck:g}");

    public string SummaryText => Components.AllHealthy
        ? Tr("Alles eingerichtet – Pixel Backup ist einsatzbereit.", "Everything is in place – Pixel Backup is ready.")
        : Tr("Es fehlt noch etwas. Die Schaltflächen daneben richten es ein.",
             "Something is still missing. The buttons next to each entry will set it up.");

    public override async Task ActivateAsync()
    {
        if (Components.LastCheck == default)
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    private async Task RefreshAsync()
    {
        StatusMessage = Tr("Komponenten werden geprüft …", "Checking components …");
        await Components.RefreshAsync().ConfigureAwait(true);
        StatusMessage = SummaryText;
    }

    private async Task InstallAdbAsync()
    {
        var success = await Components.InstallOrUpdateAdbAsync().ConfigureAwait(true);

        StatusMessage = success
            ? Tr("Die Plattform-Tools sind eingerichtet.", "The platform tools are installed.")
            : Tr("Die Plattform-Tools konnten nicht eingerichtet werden – siehe Protokoll.",
                 "The platform tools could not be installed – see the log.");

        await DialogService.ShowInfoAsync(Tr("Plattform-Tools", "Platform tools"), StatusMessage);
    }

    private async Task InstallDriverAsync()
    {
        var confirmed = await DialogService.ConfirmAsync(
            Tr("USB-Treiber einrichten", "Install USB driver"),
            Tr("Der Google-USB-Treiber wird geladen und an Windows übergeben. " +
               "Dafür erscheint die Rückfrage der Benutzerkontensteuerung.",
               "The Google USB driver will be downloaded and handed to Windows. " +
               "Windows will ask for administrator rights."));

        if (!confirmed)
        {
            return;
        }

        var (success, message) = await Components.InstallUsbDriverAsync().ConfigureAwait(true);
        StatusMessage = message;
        await DialogService.ShowInfoAsync(
            success ? Tr("USB-Treiber", "USB driver") : Tr("USB-Treiber nicht eingerichtet", "USB driver not installed"),
            message);
    }

    private void RaiseCommandStates()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        InstallAdbCommand.RaiseCanExecuteChanged();
        InstallDriverCommand.RaiseCanExecuteChanged();
    }

    private void ReportError(Exception ex)
    {
        _session.Log.Error(Tr("Komponentenprüfung fehlgeschlagen", "Component check failed"), ex);
        StatusMessage = Tr("Fehler: ", "Error: ") + ex.Message;
    }

    #region Beschriftungen

    public string PageHint => Tr(
        "Pixel Backup benötigt die Android-Plattform-Tools (adb). Unter Windows hilft zusätzlich " +
        "der Google-USB-Treiber, wenn ein Gerät nicht erkannt wird. Beides lässt sich hier einrichten.",
        "Pixel Backup needs the Android platform tools (adb). On Windows the Google USB driver helps " +
        "as well when a device is not recognised. Both can be set up here.");

    public string LabelRefresh => Tr("Jetzt prüfen", "Check now");

    public string LabelInstallAdb => Adb.State == ComponentState.UpdateAvailable
        ? Tr("Plattform-Tools aktualisieren", "Update platform tools")
        : Tr("Plattform-Tools installieren", "Install platform tools");

    public string LabelInstallDriver => Tr("USB-Treiber einrichten", "Install USB driver");

    public string LabelVersion => Tr("Fassung", "Version");

    public string LabelLocation => Tr("Ort", "Location");

    public string SourceHint => Tr(
        "Bezugsquelle ist die offizielle Paketliste von Google (dl.google.com) – dieselbe, die auch " +
        "Android Studio verwendet. Jede Datei wird über ihre Prüfsumme überprüft.",
        "The source is Google's official package list (dl.google.com) – the same one Android Studio uses. " +
        "Every file is verified against its checksum.");

    #endregion
}
