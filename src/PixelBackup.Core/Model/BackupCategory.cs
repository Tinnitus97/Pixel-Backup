using PixelBackup.Core.Localization;
using PixelBackup.Core.Util;

namespace PixelBackup.Core.Model;

public enum BackupCategoryKind
{
    /// <summary>Dateien aus dem internen Speicher.</summary>
    Files,

    /// <summary>Installierte Apps (APK-Dateien).</summary>
    Apps,

    /// <summary>Klassische <c>adb backup</c>-Sicherung der App-Daten.</summary>
    AppData,

    /// <summary>Abfrage eines Content-Providers (Kontakte, SMS, Anrufliste).</summary>
    ContentProvider,

    /// <summary>Systemeinstellungen (<c>settings list</c>).</summary>
    Settings,

    /// <summary>Vollständige App-Daten aus <c>/data/data</c> – nur mit Root-Zugriff.</summary>
    RootAppData
}

/// <summary>Zusätzliche Importdatei, die aus einem Datenexport erzeugt wird.</summary>
public enum ImportFormat
{
    None,

    /// <summary>vCard (.vcf) – lässt sich auf jedem Android-Gerät importieren.</summary>
    VCard,

    /// <summary>XML im Format von "SMS Backup &amp; Restore".</summary>
    SmsXml,

    /// <summary>XML der Anrufliste im Format von "SMS Backup &amp; Restore".</summary>
    CallsXml,

    /// <summary>Kalenderdatei (.ics).</summary>
    Ics
}

/// <summary>Eine wählbare Sicherungsgruppe wie "Fotos", "Videos" oder "Apps".</summary>
public sealed class BackupCategory
{
    public required string Id { get; init; }

    public required string NameDe { get; init; }

    public required string NameEn { get; init; }

    public required string DescriptionDe { get; init; }

    public required string DescriptionEn { get; init; }

    /// <summary>Anzeigename in der aktuell eingestellten Sprache.</summary>
    public string DisplayName => Loc.Tr(NameDe, NameEn);

    public string Description => Loc.Tr(DescriptionDe, DescriptionEn);

    public string Icon { get; init; } = "📁";

    public BackupCategoryKind Kind { get; init; } = BackupCategoryKind.Files;

    /// <summary>Verzeichnisse auf dem Gerät, die durchsucht werden.</summary>
    public IReadOnlyList<string> RemoteDirectories { get; init; } = Array.Empty<string>();

    /// <summary>Dateiendungen ohne Punkt; leer bedeutet "alle Dateien".</summary>
    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();

    /// <summary>Sammelt alle Dateien ein, die keiner anderen Kategorie zugeordnet sind.</summary>
    public bool IsCatchAll { get; init; }

    public bool SelectedByDefault { get; init; } = true;

    /// <summary>Hinweistext, wenn die Kategorie Einschränkungen unterliegt.</summary>
    public string? CaveatDe { get; init; }

    public string? CaveatEn { get; init; }

    public string? Caveat => CaveatDe is null ? null : Loc.Tr(CaveatDe, CaveatEn ?? CaveatDe);

    /// <summary>Content-URIs bzw. Namensräume, die exportiert werden.</summary>
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();

    /// <summary>Format der zusätzlich erzeugten Importdatei für das neue Telefon.</summary>
    public ImportFormat Import { get; init; } = ImportFormat.None;

    /// <summary>Kategorie steht nur mit Root-Zugriff zur Verfügung.</summary>
    public bool RequiresRoot { get; init; }

    /// <summary>Prüft, ob eine Gerätedatei zu dieser Kategorie gehört.</summary>
    public bool Matches(string remotePath)
    {
        if (Kind != BackupCategoryKind.Files)
        {
            return false;
        }

        if (!RemoteDirectories.Any(dir => PathMapper.IsInsideRemoteDirectory(remotePath, dir)))
        {
            return false;
        }

        if (Extensions.Count == 0)
        {
            return true;
        }

        var extension = PathMapper.RemoteExtension(remotePath);
        return extension.Length > 0 && Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public override string ToString() => DisplayName;
}
