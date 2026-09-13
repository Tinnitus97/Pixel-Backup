using PixelBackup.Core.Localization;

namespace PixelBackup.Core.Model;

/// <summary>
/// Wie diese Fassung auf den Rechner gekommen ist. Davon hängt ab, womit sie
/// sich erneuern lässt – eine EXE wird ausgetauscht, ein .deb über die
/// Paketverwaltung eingespielt.
/// </summary>
public enum InstallationKind
{
    /// <summary>Nicht ermittelbar.</summary>
    Unknown,

    /// <summary>Eigenständige Windows-Datei (PixelBackup.exe).</summary>
    WindowsExe,

    /// <summary>Über ein Debian-Paket eingespielt.</summary>
    Deb,

    /// <summary>Über ein RPM-Paket eingespielt.</summary>
    Rpm,

    /// <summary>Als AppImage gestartet.</summary>
    AppImage,

    /// <summary>Als Flatpak gestartet.</summary>
    Flatpak,

    /// <summary>Portabel: entpacktes Archiv oder Ordner mit der Einzeldatei.</summary>
    Portable
}

/// <summary>Eine angebotene Datei aus update.json.</summary>
public sealed class UpdatePackage
{
    /// <summary>Für welche Einbauart die Datei gedacht ist.</summary>
    public InstallationKind Kind { get; init; }

    /// <summary>Architektur, wie sie in update.json steht (x64, amd64, x86_64, arm64 …).</summary>
    public string Arch { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Größe in Byte, 0 wenn nicht angegeben.</summary>
    public long Size { get; init; }

    /// <summary>Dateiname aus der Adresse – Grundlage für den Namen im Zwischenspeicher.</summary>
    public string FileName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Url))
            {
                return "pixel-backup-update";
            }

            var withoutQuery = Url.Split('?')[0].TrimEnd('/');
            var name = withoutQuery[(withoutQuery.LastIndexOf('/') + 1)..];
            return string.IsNullOrWhiteSpace(name) ? "pixel-backup-update" : name;
        }
    }

    public bool IsUsable => Kind != InstallationKind.Unknown && !string.IsNullOrWhiteSpace(Url);
}

/// <summary>Inhalt von update.json.</summary>
public sealed class UpdateManifest
{
    public string Version { get; init; } = string.Empty;

    /// <summary>Veröffentlichungsdatum als Text (JJJJ-MM-TT).</summary>
    public string Released { get; init; } = string.Empty;

    /// <summary>Adresse der Veröffentlichungsseite mit den Änderungen.</summary>
    public string Notes { get; init; } = string.Empty;

    public IReadOnlyList<UpdatePackage> Packages { get; init; } = Array.Empty<UpdatePackage>();

    public bool Success { get; init; }

    public string? Error { get; init; }

    public static UpdateManifest Failed(string error) => new() { Error = error };
}

/// <summary>Ergebnis einer Abfrage: eigene Fassung, angebotene Fassung, passende Datei.</summary>
public sealed class UpdateCheckResult
{
    public required string LocalVersion { get; init; }

    public string? OnlineVersion { get; init; }

    public string? Released { get; init; }

    public string? Notes { get; init; }

    /// <summary>Die zur eigenen Einbauart passende Datei – null, wenn es keine gibt.</summary>
    public UpdatePackage? Package { get; init; }

    public InstallationKind Kind { get; init; }

    public bool UpdateAvailable { get; init; }

    public string? Error { get; init; }

    /// <summary>Gibt es ein Update und lässt es sich auch einspielen?</summary>
    public bool CanInstall => UpdateAvailable && Package is not null;

    public string Describe()
    {
        if (Error is not null)
        {
            return Loc.Tr($"Prüfung nicht möglich: {Error}", $"Check failed: {Error}");
        }

        if (!UpdateAvailable)
        {
            return Loc.Tr(
                $"Fassung {LocalVersion} ist aktuell.",
                $"Version {LocalVersion} is up to date.");
        }

        return Package is null
            ? Loc.Tr(
                $"Fassung {OnlineVersion} ist verfügbar, für diese Einbauart gibt es aber keine Datei.",
                $"Version {OnlineVersion} is available, but there is no file for this installation kind.")
            : Loc.Tr(
                $"Fassung {OnlineVersion} ist verfügbar (installiert: {LocalVersion}).",
                $"Version {OnlineVersion} is available (installed: {LocalVersion}).");
    }
}
