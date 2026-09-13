using PixelBackup.Core.Localization;

namespace PixelBackup.Core.Services;

/// <summary>Art der grafischen Sitzung unter Linux.</summary>
public enum LinuxSessionType
{
    /// <summary>Nicht ermittelbar oder kein Linux.</summary>
    Unknown,

    /// <summary>Klassische X11-Sitzung.</summary>
    X11,

    /// <summary>Wayland-Sitzung; Avalonia läuft darin über XWayland.</summary>
    Wayland,

    /// <summary>Keine grafische Sitzung (reine Konsole, SSH ohne Weiterleitung).</summary>
    None
}

/// <summary>Paketverwaltung der erkannten Linux-Verteilung.</summary>
public enum LinuxPackageManager
{
    Unknown,
    Apt,
    Pacman,
    Dnf,
    Zypper,
    Apk
}

/// <summary>
/// Erkennt die Linux-Verteilung und liefert dazu passende Hinweise – etwa den
/// Befehl, mit dem sich die Plattform-Tools aus den Paketquellen nachrüsten lassen.
/// </summary>
public static class LinuxEnvironment
{
    /// <summary>Kennung aus /etc/os-release, z. B. "ubuntu", "debian", "arch", "fedora".</summary>
    public static string DistributionId { get; } = ReadOsRelease("ID");

    /// <summary>Anzeigename, z. B. "Ubuntu 24.04.4 LTS".</summary>
    public static string DistributionName { get; } = ReadOsRelease("PRETTY_NAME");

    /// <summary>Verwandtschaft, z. B. "debian" bei Ubuntu oder Mint.</summary>
    public static string DistributionLike { get; } = ReadOsRelease("ID_LIKE");

    public static LinuxPackageManager PackageManager { get; } = DetectPackageManager();

    public static bool IsWayland => Session == LinuxSessionType.Wayland;

    /// <summary>Ist eine X11-Anzeige erreichbar? Unter Wayland heißt das: XWayland läuft.</summary>
    public static bool HasXDisplay =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

    /// <summary>Art der grafischen Sitzung.</summary>
    public static LinuxSessionType Session => OperatingSystem.IsLinux()
        ? DetectSession(
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            Environment.GetEnvironmentVariable("DISPLAY"))
        : LinuxSessionType.Unknown;

    /// <summary>
    /// Wertet die drei Sitzungsvariablen aus. Wayland hat Vorrang: läuft der
    /// Desktop unter Wayland, ist DISPLAY nur die Adresse von XWayland.
    /// </summary>
    public static LinuxSessionType DetectSession(string? sessionType, string? waylandDisplay, string? display)
    {
        var declared = sessionType?.Trim().ToLowerInvariant();

        if (!string.IsNullOrEmpty(waylandDisplay) || declared == "wayland")
        {
            return LinuxSessionType.Wayland;
        }

        if (!string.IsNullOrEmpty(display) || declared == "x11")
        {
            return LinuxSessionType.X11;
        }

        return LinuxSessionType.None;
    }

    /// <summary>Beschreibung der Sitzung für Oberfläche und Protokoll.</summary>
    public static string SessionText => DescribeSession(Session, HasXDisplay);

    /// <summary>Beschreibt eine Sitzung; getrennt von der Umgebung, damit prüfbar.</summary>
    public static string DescribeSession(LinuxSessionType session, bool hasXDisplay) => session switch
    {
        LinuxSessionType.Wayland => hasXDisplay
            ? Loc.Tr("Wayland (über XWayland)", "Wayland (through XWayland)")
            : Loc.Tr("Wayland – XWayland fehlt", "Wayland – XWayland missing"),
        LinuxSessionType.X11 => "X11",
        LinuxSessionType.None => Loc.Tr("keine grafische Sitzung", "no graphical session"),
        _ => string.Empty
    };

    /// <summary>Befehl, mit dem sich XWayland nachrüsten lässt.</summary>
    public static string? XWaylandPackageCommand => PackageManager switch
    {
        LinuxPackageManager.Apt => "sudo apt install xwayland",
        LinuxPackageManager.Pacman => "sudo pacman -S xorg-xwayland",
        LinuxPackageManager.Dnf => "sudo dnf install xorg-x11-server-Xwayland",
        LinuxPackageManager.Zypper => "sudo zypper install xwayland",
        LinuxPackageManager.Apk => "sudo apk add xwayland",
        _ => null
    };

    /// <summary>
    /// Erklärt, warum die Oberfläche nicht starten kann – etwa weil weder X11
    /// noch XWayland erreichbar sind. Ist alles in Ordnung, kommt null zurück.
    /// </summary>
    public static string? DisplayProblem =>
        OperatingSystem.IsLinux() ? DescribeDisplayProblem(Session, HasXDisplay) : null;

    /// <summary>Meldung zu einer Sitzung ohne nutzbare Anzeige; sonst null.</summary>
    public static string? DescribeDisplayProblem(LinuxSessionType session, bool hasXDisplay)
    {
        switch (session)
        {
            case LinuxSessionType.Wayland when !hasXDisplay:
                // Avalonia zeichnet unter Wayland über XWayland; ohne das fehlt die Anzeige.
                var hint = Loc.Tr(
                    "Diese Sitzung läuft unter Wayland, aber XWayland ist nicht erreichbar (DISPLAY ist leer).",
                    "This session runs on Wayland, but XWayland is not reachable (DISPLAY is empty).");
                var install = XWaylandPackageCommand;
                return install is null
                    ? hint
                    : $"{hint} {Loc.Tr("Nachrüsten mit:", "Install it with:")} {install}";

            case LinuxSessionType.None:
                return Loc.Tr(
                    "Es ist keine grafische Sitzung vorhanden (weder DISPLAY noch WAYLAND_DISPLAY gesetzt). "
                    + "Über SSH hilft \"ssh -X\", sonst die Anwendung im Desktop starten.",
                    "No graphical session is available (neither DISPLAY nor WAYLAND_DISPLAY is set). "
                    + "Over SSH use \"ssh -X\", otherwise start the application from the desktop.");

            default:
                return null;
        }
    }

    /// <summary>Kurzbeschreibung für die Oberfläche.</summary>
    public static string Describe()
    {
        if (!OperatingSystem.IsLinux())
        {
            return string.Empty;
        }

        var name = string.IsNullOrWhiteSpace(DistributionName)
            ? Loc.Tr("unbekannte Verteilung", "unknown distribution")
            : DistributionName;

        var session = SessionText;
        return session.Length == 0 ? name : $"{name} · {session}";
    }

    /// <summary>Befehl, mit dem die Verteilung adb selbst mitbringt.</summary>
    public static string? PlatformToolsPackageCommand => PackageManager switch
    {
        LinuxPackageManager.Apt => "sudo apt install android-sdk-platform-tools",
        LinuxPackageManager.Pacman => "sudo pacman -S android-tools",
        LinuxPackageManager.Dnf => "sudo dnf install android-tools",
        LinuxPackageManager.Zypper => "sudo zypper install android-tools",
        LinuxPackageManager.Apk => "sudo apk add android-tools",
        _ => null
    };

    /// <summary>Paket der Verteilung mit fertigen Geräteregeln.</summary>
    public static string? UdevPackageCommand => PackageManager switch
    {
        LinuxPackageManager.Apt => "sudo apt install android-sdk-platform-tools-common",
        LinuxPackageManager.Pacman => "sudo pacman -S android-udev",
        LinuxPackageManager.Dnf => "sudo dnf install android-tools",
        LinuxPackageManager.Zypper => "sudo zypper install android-tools",
        _ => null
    };

    private static LinuxPackageManager DetectPackageManager()
    {
        if (!OperatingSystem.IsLinux())
        {
            return LinuxPackageManager.Unknown;
        }

        // Erst die Kennung auswerten, dann nach den Programmen selbst sehen.
        var identifiers = $"{DistributionId} {DistributionLike}".ToLowerInvariant();

        if (identifiers.Contains("debian") || identifiers.Contains("ubuntu") || identifiers.Contains("mint"))
        {
            return LinuxPackageManager.Apt;
        }

        if (identifiers.Contains("arch") || identifiers.Contains("manjaro") || identifiers.Contains("endeavouros"))
        {
            return LinuxPackageManager.Pacman;
        }

        if (identifiers.Contains("fedora") || identifiers.Contains("rhel") || identifiers.Contains("centos"))
        {
            return LinuxPackageManager.Dnf;
        }

        if (identifiers.Contains("suse"))
        {
            return LinuxPackageManager.Zypper;
        }

        if (identifiers.Contains("alpine"))
        {
            return LinuxPackageManager.Apk;
        }

        foreach (var (file, manager) in new (string, LinuxPackageManager)[]
                 {
                     ("/usr/bin/apt", LinuxPackageManager.Apt),
                     ("/usr/bin/pacman", LinuxPackageManager.Pacman),
                     ("/usr/bin/dnf", LinuxPackageManager.Dnf),
                     ("/usr/bin/zypper", LinuxPackageManager.Zypper),
                     ("/sbin/apk", LinuxPackageManager.Apk)
                 })
        {
            if (File.Exists(file))
            {
                return manager;
            }
        }

        return LinuxPackageManager.Unknown;
    }

    /// <summary>Liest einen Wert aus /etc/os-release. Öffentlich für die Tests.</summary>
    public static string ParseOsRelease(string content, string key)
    {
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith(key + "=", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line[(key.Length + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            return value;
        }

        return string.Empty;
    }

    private static string ReadOsRelease(string key)
    {
        try
        {
            foreach (var path in new[] { "/etc/os-release", "/usr/lib/os-release" })
            {
                if (File.Exists(path))
                {
                    return ParseOsRelease(File.ReadAllText(path), key);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return string.Empty;
    }
}
