using System.Runtime.Versioning;
using System.Text;
using PixelBackup.Core.Adb;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;

namespace PixelBackup.Core.Services;

/// <summary>
/// Sorgt unter Linux dafür, dass angeschlossene Android-Geräte ohne Root-Rechte
/// erreichbar sind. Ohne passende udev-Regeln meldet adb "no permissions".
/// Das Gegenstück zum USB-Treiber unter Windows.
/// </summary>
public sealed class LinuxDeviceAccessService
{
    public const string RulesFileName = "51-android.rules";
    public const string DeviceGroup = "plugdev";

    /// <summary>Zielort der Regeldatei.</summary>
    public static string RulesPath => "/etc/udev/rules.d/" + RulesFileName;

    private static readonly string[] RuleDirectories =
    {
        "/etc/udev/rules.d",
        "/usr/lib/udev/rules.d",
        "/lib/udev/rules.d"
    };

    /// <summary>Hersteller-Kennungen, wie Google sie für die Geräteregeln nennt.</summary>
    public static IReadOnlyList<(string VendorId, string Vendor)> Vendors { get; } = new[]
    {
        ("0502", "Acer"), ("0b05", "ASUS"), ("413c", "Dell"), ("0489", "Foxconn"),
        ("091e", "Garmin-Asus"), ("18d1", "Google"), ("201e", "Haier"), ("109b", "Hisense"),
        ("12d1", "Huawei"), ("0bb4", "HTC"), ("24e3", "K-Touch"), ("2116", "KT Tech"),
        ("0482", "Kyocera"), ("17ef", "Lenovo"), ("1004", "LG"), ("0e8d", "MediaTek"),
        ("22b8", "Motorola"), ("0409", "NEC"), ("2080", "Nook"), ("0955", "Nvidia"),
        ("2a70", "OnePlus"), ("22d9", "OPPO / Realme"), ("10a9", "Pantech"), ("1d4d", "Pegatron"),
        ("0471", "Philips"), ("05c6", "Qualcomm"), ("04e8", "Samsung"), ("04dd", "Sharp"),
        ("0fce", "Sony"), ("2340", "Teleepoch"), ("0930", "Toshiba"), ("2d95", "Vivo"),
        ("2717", "Xiaomi"), ("19d2", "ZTE"), ("2ae5", "Fairphone"), ("1bbb", "TCL / Alcatel"),
        ("2916", "Android"), ("0e79", "Archos")
    };

    private readonly ILogSink _log;

    public LinuxDeviceAccessService(ILogSink? log = null) => _log = log ?? NullLogSink.Instance;

    /// <summary>Die Kennzeichnung sagt dem Übersetzer, dass hinter dieser Prüfung Linux gilt.</summary>
    [SupportedOSPlatformGuard("linux")]
    public static bool IsLinux => OperatingSystem.IsLinux();

    /// <summary>Erzeugt den Inhalt der Regeldatei.</summary>
    public static string BuildRules()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Android-Geräteregeln, erzeugt von Pixel Backup.");
        builder.AppendLine("# Erlaubt dem angemeldeten Benutzer den Zugriff auf angeschlossene Geräte,");
        builder.AppendLine("# damit adb nicht \"no permissions\" meldet.");
        builder.AppendLine("#");
        builder.AppendLine("# TAG+=\"uaccess\" gibt das Gerät an die laufende Sitzung frei (systemd),");
        builder.AppendLine($"# GROUP=\"{DeviceGroup}\" deckt Systeme ohne logind ab.");
        builder.AppendLine();

        foreach (var (vendorId, vendor) in Vendors)
        {
            builder.AppendLine($"# {vendor}");
            builder.AppendLine(
                $"SUBSYSTEM==\"usb\", ATTR{{idVendor}}==\"{vendorId}\", MODE=\"0660\", GROUP=\"{DeviceGroup}\", TAG+=\"uaccess\"");
        }

        return builder.ToString();
    }

    /// <summary>Sucht eine vorhandene Regeldatei (eigene oder die der Verteilung).</summary>
    public static string? FindInstalledRules()
    {
        foreach (var directory in RuleDirectories)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(directory, "*.rules"))
                {
                    var name = Path.GetFileName(file);
                    if (!name.Contains("android", StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains("adb", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var content = File.ReadAllText(file);
                    if (content.Contains("18d1", StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
            }
            catch (Exception)
            {
                // Nicht lesbare Verzeichnisse werden übersprungen.
            }
        }

        return null;
    }

    /// <summary>Prüft, ob der angemeldete Benutzer in der Gerätegruppe ist.</summary>
    public static async Task<bool> IsUserInDeviceGroupAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await ProcessRunner.RunAsync("id", new[] { "-nG" }, ct).ConfigureAwait(false);
            var groups = result.StandardOutput.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return groups.Any(g => g is DeviceGroup or "adbusers" or "android" or "root");
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Ermittelt den Zustand. <paramref name="devicesWithoutPermission"/> meldet, ob adb
    /// gerade ein Gerät ohne Zugriffsrechte sieht – das ist der eindeutige Fall.
    /// </summary>
    public async Task<ComponentStatus> CheckAsync(bool devicesWithoutPermission = false, CancellationToken ct = default)
    {
        var status = new ComponentStatus { Name = Loc.Tr("Geräteregeln (udev)", "Device rules (udev)") };

        if (!IsLinux)
        {
            status.State = ComponentState.NotRequired;
            status.Message = Loc.Tr("Nur unter Linux erforderlich.", "Only required on Linux.");
            return status;
        }

        var rules = FindInstalledRules();
        var inGroup = await IsUserInDeviceGroupAsync(ct).ConfigureAwait(false);

        status.Location = rules;
        status.InstalledVersion = rules is null
            ? null
            : Path.GetFileName(rules);

        if (devicesWithoutPermission)
        {
            status.State = ComponentState.Problem;
            status.Message = Loc.Tr(
                "adb sieht ein Gerät, darf aber nicht darauf zugreifen. Die Geräteregeln richten das ein; " +
                "danach das Gerät einmal ab- und wieder anstecken.",
                "adb can see a device but is not allowed to access it. The device rules fix that; " +
                "afterwards unplug the device once and plug it back in.");
            return status;
        }

        if (rules is null)
        {
            status.State = ComponentState.Missing;
            var hint = LinuxEnvironment.UdevPackageCommand;
            status.Message = Loc.Tr(
                "Es sind keine Android-Geräteregeln eingerichtet. Ohne sie meldet adb bei vielen Geräten " +
                "„no permissions“." + (hint is null ? string.Empty : $" Alternativ über die Paketverwaltung: {hint}"),
                "No Android device rules are installed. Without them adb reports \"no permissions\" for many devices." +
                (hint is null ? string.Empty : $" Alternatively through the package manager: {hint}"));
            return status;
        }

        if (!inGroup)
        {
            status.State = ComponentState.Problem;
            status.Message = Loc.Tr(
                $"Regeln vorhanden ({Path.GetFileName(rules)}), der Benutzer ist aber in keiner Gerätegruppe. " +
                $"Auf Systemen ohne logind wird die Gruppe „{DeviceGroup}“ benötigt.",
                $"Rules are present ({Path.GetFileName(rules)}), but the user is in no device group. " +
                $"Systems without logind need the \"{DeviceGroup}\" group.");
            return status;
        }

        status.State = ComponentState.UpToDate;
        status.Message = Loc.Tr(
            $"Geräteregeln sind eingerichtet ({rules}).",
            $"Device rules are in place ({rules}).");
        return status;
    }

    /// <summary>
    /// Schreibt die Regeldatei mit erhöhten Rechten, legt die Gerätegruppe an,
    /// nimmt den Benutzer auf und lädt die Regeln neu.
    /// </summary>
    public async Task<(bool Success, string Message)> InstallAsync(CancellationToken ct = default)
    {
        if (!IsLinux)
        {
            return (false, Loc.Tr("Nur unter Linux möglich.", "Only possible on Linux."));
        }

        var rulesFile = Path.Combine(Path.GetTempPath(), $"pixelbackup-{RulesFileName}");
        var scriptFile = Path.Combine(Path.GetTempPath(), $"pixelbackup-udev-{Guid.NewGuid():n}.sh");
        var user = Environment.UserName;

        try
        {
            await File.WriteAllTextAsync(rulesFile, BuildRules(), ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(scriptFile, BuildInstallScript(rulesFile, user), ct).ConfigureAwait(false);
            File.SetUnixFileMode(scriptFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var (tool, arguments) = FindElevationTool(scriptFile);
            if (tool is null)
            {
                return (false, Loc.Tr(
                    "Es wurde kein Weg gefunden, Administratorrechte anzufordern (pkexec oder sudo). " +
                    $"Bitte von Hand ausführen: sudo sh {scriptFile}",
                    "No way to request administrator rights was found (pkexec or sudo). " +
                    $"Please run manually: sudo sh {scriptFile}"));
            }

            _log.Info(Loc.Tr(
                $"Geräteregeln werden mit {tool} eingerichtet …",
                $"Installing device rules with {tool} …"));

            var result = await ProcessRunner.RunAsync(tool, arguments, ct).ConfigureAwait(false);

            if (result.Success)
            {
                _log.Info(Loc.Tr("Geräteregeln eingerichtet.", "Device rules installed."));
                return (true, Loc.Tr(
                    "Die Geräteregeln sind eingerichtet. Bitte das Gerät einmal ab- und wieder anstecken. " +
                    "Falls der Zugriff weiterhin fehlt, einmal ab- und wieder anmelden (neue Gruppenzugehörigkeit).",
                    "The device rules are in place. Please unplug the device once and plug it back in. " +
                    "If access is still missing, log out and back in (new group membership)."));
            }

            return (false, Loc.Tr(
                "Die Regeln konnten nicht eingerichtet werden: " + result.ErrorSummary,
                "The rules could not be installed: " + result.ErrorSummary));
        }
        catch (OperationCanceledException)
        {
            return (false, Loc.Tr("Abgebrochen.", "Cancelled."));
        }
        catch (Exception ex)
        {
            _log.Error(Loc.Tr("Geräteregeln konnten nicht eingerichtet werden", "Device rules could not be installed"), ex);
            return (false, ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(scriptFile))
                {
                    File.Delete(scriptFile);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Baut das Einrichtungsskript. Öffentlich, damit es sich prüfen lässt.</summary>
    public static string BuildInstallScript(string rulesSourceFile, string user) =>
        $"""
         #!/bin/sh
         # Von Pixel Backup erzeugt: Android-Geräteregeln einrichten.
         set -e
         install -D -m 0644 -o root -g root '{rulesSourceFile}' '{RulesPath}'
         getent group {DeviceGroup} >/dev/null 2>&1 || groupadd -f {DeviceGroup}
         id -nG '{user}' | tr ' ' '\n' | grep -qx {DeviceGroup} || usermod -aG {DeviceGroup} '{user}'
         if command -v udevadm >/dev/null 2>&1; then
           udevadm control --reload-rules || true
           udevadm trigger --subsystem-match=usb || true
         fi
         exit 0
         """;

    private static (string? Tool, IReadOnlyList<string> Arguments) FindElevationTool(string scriptFile)
    {
        if (File.Exists("/usr/bin/pkexec"))
        {
            return ("pkexec", new[] { "/bin/sh", scriptFile });
        }

        if (File.Exists("/usr/bin/sudo") || File.Exists("/bin/sudo"))
        {
            // -n: ohne Kennwortabfrage; im Terminal gestartet, klappt das mit gültiger Sitzung.
            return ("sudo", new[] { "-n", "/bin/sh", scriptFile });
        }

        if (Environment.UserName == "root")
        {
            return ("/bin/sh", new[] { scriptFile });
        }

        return (null, Array.Empty<string>());
    }

    /// <summary>Gibt den Befehl zurück, den der Benutzer notfalls selbst ausführen kann.</summary>
    public static string ManualCommandHint() =>
        $"sudo install -m 0644 {Path.Combine(Path.GetTempPath(), "pixelbackup-" + RulesFileName)} {RulesPath} && sudo udevadm control --reload-rules && sudo udevadm trigger";
}
