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
        InstallDeviceAccessCommand = new AsyncRelayCommand(
            InstallDeviceAccessAsync,
            () => Components.IsIdle && ComponentService.CanSetUpDeviceAccess,
            ReportError);
        InstallUpdateCommand = new AsyncRelayCommand(
            InstallUpdateAsync,
            () => Components.IsIdle && Components.Application.CanUpdate,
            ReportError);
        OpenReleasesCommand = new RelayCommand(() => DialogService.OpenUrl(
            Components.UpdateCheck?.Notes ?? UpdateService.ReleasesPageUrl));

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

    public AsyncRelayCommand InstallDeviceAccessCommand { get; }

    public AsyncRelayCommand InstallUpdateCommand { get; }

    public RelayCommand OpenReleasesCommand { get; }

    public ComponentStatus Adb => Components.Adb;

    public ComponentStatus DeviceAccess => Components.DeviceAccess;

    /// <summary>Pixel Backup selbst – Fassung und angebotene Aktualisierung.</summary>
    public ComponentStatus Application => Components.Application;

    /// <summary>Woher die laufende Fassung stammt (EXE, .deb, AppImage …).</summary>
    public string InstallationKindText => InstallationInfo.KindText(InstallationInfo.Kind);

    /// <summary>Gibt es auf diesem System überhaupt etwas einzurichten?</summary>
    public bool CanSetUpDeviceAccess => ComponentService.CanSetUpDeviceAccess;

    public bool ShowVersionOfDeviceAccess => OperatingSystem.IsWindows();

    public string LastCheckText => Components.LastCheck == default
        ? Tr("noch nicht geprüft", "not checked yet")
        : Tr($"zuletzt geprüft: {Components.LastCheck:g}", $"last checked: {Components.LastCheck:g}");

    public string SummaryText
    {
        get
        {
            if (!Adb.IsHealthy || !DeviceAccess.IsHealthy)
            {
                return Tr("Es fehlt noch etwas. Die Schaltflächen daneben richten es ein.",
                          "Something is still missing. The buttons next to each entry will set it up.");
            }

            // Eine neuere Fassung ist kein Mangel, aber eine Meldung wert.
            return HasUpdate
                ? Tr($"Alles eingerichtet – für Pixel Backup selbst gibt es Fassung {Application.LatestVersion}.",
                     $"Everything is in place – version {Application.LatestVersion} of Pixel Backup is available.")
                : Tr("Alles eingerichtet – Pixel Backup ist einsatzbereit.",
                     "Everything is in place – Pixel Backup is ready.");
        }
    }

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

    private async Task InstallDeviceAccessAsync()
    {
        var question = OperatingSystem.IsLinux()
            ? Tr("Pixel Backup legt die Datei /etc/udev/rules.d/51-android.rules an, nimmt dich in die " +
                 "Gruppe „plugdev“ auf und lädt die Regeln neu. Dafür werden einmalig Administratorrechte " +
                 "abgefragt (pkexec oder sudo).",
                 "Pixel Backup creates /etc/udev/rules.d/51-android.rules, adds you to the \"plugdev\" group " +
                 "and reloads the rules. This asks for administrator rights once (pkexec or sudo).")
            : Tr("Der Google-USB-Treiber wird geladen und an Windows übergeben. " +
                 "Dafür erscheint die Rückfrage der Benutzerkontensteuerung.",
                 "The Google USB driver will be downloaded and handed to Windows. " +
                 "Windows will ask for administrator rights.");

        var confirmed = await DialogService.ConfirmAsync(LabelInstallDeviceAccess, question);
        if (!confirmed)
        {
            return;
        }

        var (success, message) = await Components.InstallDeviceAccessAsync().ConfigureAwait(true);
        StatusMessage = message;

        await DialogService.ShowInfoAsync(
            success
                ? ComponentService.DeviceAccessName
                : Tr("Nicht eingerichtet", "Not installed"),
            message);
    }

    private async Task InstallUpdateAsync()
    {
        var check = Components.UpdateCheck;
        if (check is null)
        {
            return;
        }

        var question = check.Kind switch
        {
            InstallationKind.Deb or InstallationKind.Rpm => Tr(
                $"Fassung {check.OnlineVersion} wird geladen und über die Paketverwaltung eingespielt. " +
                "Dafür werden einmalig Administratorrechte abgefragt.",
                $"Version {check.OnlineVersion} will be downloaded and installed through the package manager. " +
                "This asks for administrator rights once."),
            InstallationKind.Flatpak => Tr(
                $"Fassung {check.OnlineVersion} wird als Flatpak-Bündel geladen und für dich eingespielt.",
                $"Version {check.OnlineVersion} will be downloaded as a Flatpak bundle and installed for you."),
            _ => Tr(
                $"Fassung {check.OnlineVersion} wird geladen, die Prüfsumme verglichen und die Programmdatei " +
                "ausgetauscht. Pixel Backup beendet sich dafür kurz und startet neu.",
                $"Version {check.OnlineVersion} will be downloaded, its checksum verified and the program file " +
                "replaced. Pixel Backup will exit briefly and restart.")
        };

        var confirmed = await DialogService.ConfirmAsync(LabelInstallUpdate, question);
        if (!confirmed)
        {
            return;
        }

        var (success, message) = await Components.InstallUpdateAsync().ConfigureAwait(true);
        StatusMessage = message;

        if (!success)
        {
            await DialogService.ShowInfoAsync(Tr("Nicht eingespielt", "Not installed"), message);
        }
    }

    private void RaiseCommandStates()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        InstallAdbCommand.RaiseCanExecuteChanged();
        InstallDeviceAccessCommand.RaiseCanExecuteChanged();
        InstallUpdateCommand.RaiseCanExecuteChanged();
    }

    private void ReportError(Exception ex)
    {
        _session.Log.Error(Tr("Komponentenprüfung fehlgeschlagen", "Component check failed"), ex);
        StatusMessage = Tr("Fehler: ", "Error: ") + ex.Message;
    }

    #region Beschriftungen

    /// <summary>Der Hinweis nennt die Bestandteile, die auf diesem System wirklich gebraucht werden.</summary>
    public string PageHint
    {
        get
        {
            if (OperatingSystem.IsLinux())
            {
                return Tr(
                    "Pixel Backup benötigt die Android-Plattform-Tools (adb). Die Geräteregeln (udev) sorgen " +
                    "dafür, dass adb ohne Root-Rechte auf das Telefon zugreifen darf. Beides lässt sich hier einrichten.",
                    "Pixel Backup needs the Android platform tools (adb). The device rules (udev) let adb reach " +
                    "the phone without root privileges. Both can be set up here.");
            }

            if (OperatingSystem.IsWindows())
            {
                return Tr(
                    "Pixel Backup benötigt die Android-Plattform-Tools (adb). Der Google-USB-Treiber hilft, " +
                    "wenn Windows ein Gerät nicht erkennt. Beides lässt sich hier einrichten.",
                    "Pixel Backup needs the Android platform tools (adb). The Google USB driver helps when " +
                    "Windows does not recognise a device. Both can be set up here.");
            }

            return Tr(
                "Pixel Backup benötigt die Android-Plattform-Tools (adb). Weitere Bestandteile werden auf " +
                "diesem System nicht gebraucht.",
                "Pixel Backup needs the Android platform tools (adb). No further components are needed on this system.");
        }
    }

    public string LabelRefresh => Tr("Jetzt prüfen", "Check now");

    public string LabelInstallAdb => Adb.State == ComponentState.UpdateAvailable
        ? Tr("Plattform-Tools aktualisieren", "Update platform tools")
        : Tr("Plattform-Tools installieren", "Install platform tools");

    public string LabelInstallDeviceAccess => OperatingSystem.IsLinux()
        ? Tr("Geräteregeln einrichten", "Install device rules")
        : Tr("USB-Treiber einrichten", "Install USB driver");

    /// <summary>Betriebssystem samt Verteilung – hilft beim Nachvollziehen von Meldungen.</summary>
    public string SystemText
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return $"Windows · {Environment.OSVersion.Version}";
            }

            if (OperatingSystem.IsLinux())
            {
                var distribution = LinuxEnvironment.Describe();
                return string.IsNullOrEmpty(distribution) ? "Linux" : "Linux · " + distribution;
            }

            return OperatingSystem.IsMacOS() ? "macOS" : Environment.OSVersion.Platform.ToString();
        }
    }

    public string LabelSystem => Tr("System", "System");

    /// <summary>Unter Linux der Befehl, mit dem die Verteilung adb selbst mitbringt.</summary>
    public string PackageHint
    {
        get
        {
            if (!OperatingSystem.IsLinux())
            {
                return string.Empty;
            }

            var tools = LinuxEnvironment.PlatformToolsPackageCommand;
            return tools is null
                ? string.Empty
                : Tr($"Aus den Paketquellen ginge es auch: {tools}",
                     $"The package manager would work as well: {tools}");
        }
    }

    public bool HasPackageHint => PackageHint.Length > 0;

    public string LabelVersion => Tr("Fassung", "Version");

    public string LabelInstallUpdate => Tr("Jetzt aktualisieren", "Update now");

    public string LabelReleases => Tr("Änderungen ansehen", "View changes");

    /// <summary>Die Änderungsliste lohnt nur, wenn es auch etwas Neues gibt.</summary>
    public bool HasUpdate => Application.State == ComponentState.UpdateAvailable;

    public string UpdateSourceHint => Tr(
        "Der Versionscheck liest eine einfache Datei (update.json) aus der jüngsten Veröffentlichung – " +
        "ohne Anmeldung und ohne Abrufgrenze. Eingespielt wird nur, was der hinterlegten Prüfsumme entspricht, " +
        "und nur nach deiner Zustimmung.",
        "The version check reads a plain file (update.json) from the latest release – no sign-in, no rate limit. " +
        "Only a file matching the recorded checksum is installed, and only after you agree.");

    public string LabelLocation => Tr("Ort", "Location");

    public string LabelInstallation => Tr("Einbauart", "Installed as");

    public string SourceHint => Tr(
        "Bezugsquelle ist die offizielle Paketliste von Google (dl.google.com) – dieselbe, die auch " +
        "Android Studio verwendet. Jede Datei wird über ihre Prüfsumme überprüft.",
        "The source is Google's official package list (dl.google.com) – the same one Android Studio uses. " +
        "Every file is verified against its checksum.");

    #endregion
}
