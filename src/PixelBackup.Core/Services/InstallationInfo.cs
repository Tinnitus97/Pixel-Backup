using System.Reflection;
using PixelBackup.Core.Adb;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;

namespace PixelBackup.Core.Services;

/// <summary>
/// Woher diese Fassung stammt und welche Nummer sie trägt. Der Versionscheck
/// braucht beides: die Nummer zum Vergleichen und die Einbauart, um die
/// richtige Datei anzubieten (EXE, .deb, .rpm, AppImage, Flatpak, Archiv).
/// </summary>
public static class InstallationInfo
{
    /// <summary>Fassung aus den Programmangaben, z. B. "0.9.0".</summary>
    public static string CurrentVersion { get; } = ReadVersion();

    /// <summary>Die laufende Programmdatei.</summary>
    public static string? ExecutablePath => Environment.ProcessPath;

    /// <summary>Wie diese Fassung eingespielt wurde.</summary>
    public static InstallationKind Kind { get; } = Detect(
        Environment.ProcessPath,
        Environment.GetEnvironmentVariable("APPIMAGE"),
        Environment.GetEnvironmentVariable("FLATPAK_ID"),
        File.Exists("/.flatpak-info"),
        OperatingSystem.IsWindows(),
        OperatingSystem.IsLinux(),
        PackageOwnerOf);

    /// <summary>Die Architektur, wie sie in update.json steht.</summary>
    public static string Arch => ArchOf(Kind, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);

    /// <summary>
    /// Bestimmt die Einbauart. Alle Angaben kommen von außen, damit sich die
    /// Entscheidung ohne echte Umgebung prüfen lässt.
    /// <paramref name="packageOwner"/> beantwortet, ob ein Pfad zu einem
    /// deb- oder rpm-Paket gehört.
    /// </summary>
    public static InstallationKind Detect(
        string? executablePath,
        string? appImageVariable,
        string? flatpakId,
        bool hasFlatpakInfo,
        bool isWindows,
        bool isLinux,
        Func<string, InstallationKind>? packageOwner = null)
    {
        // Flatpak und AppImage melden sich selbst – das ist die sicherste Auskunft.
        if (!string.IsNullOrEmpty(flatpakId) || hasFlatpakInfo)
        {
            return InstallationKind.Flatpak;
        }

        if (!string.IsNullOrEmpty(appImageVariable))
        {
            return InstallationKind.AppImage;
        }

        if (isWindows)
        {
            return InstallationKind.WindowsExe;
        }

        if (!isLinux)
        {
            return InstallationKind.Unknown;
        }

        if (string.IsNullOrEmpty(executablePath))
        {
            return InstallationKind.Unknown;
        }

        // Unter /usr liegt nur, was eine Paketverwaltung dorthin gelegt hat.
        var fromPackage = packageOwner?.Invoke(executablePath) ?? InstallationKind.Unknown;
        if (fromPackage != InstallationKind.Unknown)
        {
            return fromPackage;
        }

        return InstallationKind.Portable;
    }

    /// <summary>Fragt dpkg und rpm, ob die Datei zu einem ihrer Pakete gehört.</summary>
    private static InstallationKind PackageOwnerOf(string path)
    {
        if (!OperatingSystem.IsLinux() || !path.StartsWith("/usr/", StringComparison.Ordinal))
        {
            return InstallationKind.Unknown;
        }

        // Der Starter /usr/bin/pixel-backup ist ein Skript; gefragt wird nach der
        // Datei selbst und nach der Anwendung darunter.
        foreach (var candidate in new[] { path, "/usr/lib/pixel-backup/PixelBackup" })
        {
            if (ProcessRunner.TryRunQuick("dpkg", $"-S {candidate}", out _))
            {
                return InstallationKind.Deb;
            }

            if (ProcessRunner.TryRunQuick("rpm", $"-qf {candidate}", out _))
            {
                return InstallationKind.Rpm;
            }
        }

        return InstallationKind.Unknown;
    }

    /// <summary>Die Architekturbezeichnung, die das jeweilige Format verwendet.</summary>
    public static string ArchOf(InstallationKind kind, System.Runtime.InteropServices.Architecture architecture)
    {
        var isArm = architecture == System.Runtime.InteropServices.Architecture.Arm64;

        return kind switch
        {
            InstallationKind.Deb => isArm ? "arm64" : "amd64",
            InstallationKind.Rpm => isArm ? "aarch64" : "x86_64",
            InstallationKind.AppImage => isArm ? "aarch64" : "x86_64",
            InstallationKind.Flatpak => isArm ? "aarch64" : "x86_64",
            _ => isArm ? "arm64" : "x64"
        };
    }

    /// <summary>Name der Einbauart für die Oberfläche.</summary>
    public static string KindText(InstallationKind kind) => kind switch
    {
        InstallationKind.WindowsExe => Loc.Tr("eigenständige Windows-Datei", "standalone Windows file"),
        InstallationKind.Deb => Loc.Tr("Debian-Paket", "Debian package"),
        InstallationKind.Rpm => Loc.Tr("RPM-Paket", "RPM package"),
        InstallationKind.AppImage => "AppImage",
        InstallationKind.Flatpak => "Flatpak",
        InstallationKind.Portable => Loc.Tr("portable Fassung", "portable copy"),
        _ => Loc.Tr("unbekannte Herkunft", "unknown origin")
    };

    /// <summary>Kurzbeschreibung: Fassung und Einbauart.</summary>
    public static string Describe() =>
        Loc.Tr($"Fassung {CurrentVersion} ({KindText(Kind)})", $"Version {CurrentVersion} ({KindText(Kind)})");

    private static string ReadVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(InstallationInfo).Assembly;

            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                // Aus "0.9.0+abc123" wird "0.9.0".
                var plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            var version = assembly.GetName().Version;
            return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch (Exception)
        {
            return "0.0.0";
        }
    }
}
