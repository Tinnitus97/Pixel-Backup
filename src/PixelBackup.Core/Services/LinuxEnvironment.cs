using PixelBackup.Core.Localization;

namespace PixelBackup.Core.Services;

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

    public static bool IsWayland =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

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

        return IsWayland ? $"{name} · Wayland" : name;
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
