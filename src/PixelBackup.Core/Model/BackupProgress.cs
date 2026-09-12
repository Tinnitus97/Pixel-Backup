using PixelBackup.Core.Util;

namespace PixelBackup.Core.Model;

/// <summary>Fortschrittsmeldung eines laufenden Sicherungs- oder Wiederherstellungsvorgangs.</summary>
public sealed class OperationProgress
{
    public string Phase { get; init; } = string.Empty;

    public string CurrentItem { get; init; } = string.Empty;

    public int ItemsDone { get; init; }

    public int ItemsTotal { get; init; }

    public long BytesDone { get; init; }

    public long BytesTotal { get; init; }

    public double BytesPerSecond { get; init; }

    public TimeSpan Elapsed { get; init; }

    public double Percent => BytesTotal > 0
        ? Math.Clamp(BytesDone * 100d / BytesTotal, 0, 100)
        : ItemsTotal > 0
            ? Math.Clamp(ItemsDone * 100d / ItemsTotal, 0, 100)
            : 0;

    public TimeSpan? Eta
    {
        get
        {
            if (BytesPerSecond <= 0 || BytesTotal <= 0 || BytesDone >= BytesTotal)
            {
                return null;
            }

            var remaining = (BytesTotal - BytesDone) / BytesPerSecond;
            return remaining > TimeSpan.MaxValue.TotalSeconds
                ? null
                : TimeSpan.FromSeconds(remaining);
        }
    }

    public string EtaText => Eta is { } eta ? "noch ca. " + Humanize.Duration(eta) : "–";

    public string SpeedText => Humanize.Speed(BytesPerSecond);

    public string CountText => $"{ItemsDone:N0} / {ItemsTotal:N0}";

    public string BytesText => $"{Humanize.Bytes(BytesDone)} / {Humanize.Bytes(BytesTotal)}";
}

/// <summary>Ergebnis eines Sicherungslaufes.</summary>
public sealed class BackupResult
{
    public required string SetDirectory { get; init; }

    public required BackupManifest Manifest { get; init; }

    public int FilesCopied { get; set; }

    public int FilesSkipped { get; set; }

    public int FilesFailed { get; set; }

    public long BytesCopied { get; set; }

    public bool Canceled { get; set; }

    public TimeSpan Duration { get; set; }

    public List<string> Warnings { get; } = new();

    public string? ArchivePath { get; set; }

    public string SummaryText
    {
        get
        {
            var text = $"{Humanize.Count(FilesCopied, "Element", "Elemente")} gesichert ({Humanize.Bytes(BytesCopied)})";
            if (FilesSkipped > 0)
            {
                text += $", {FilesSkipped} unverändert";
            }

            if (FilesFailed > 0)
            {
                text += $", {FilesFailed} fehlgeschlagen";
            }

            return text + $" in {Humanize.Duration(Duration)}";
        }
    }
}

/// <summary>Ergebnis einer Wiederherstellung.</summary>
public sealed class RestoreResult
{
    public int FilesRestored { get; set; }

    public int FilesSkipped { get; set; }

    public int FilesFailed { get; set; }

    public int AppsInstalled { get; set; }

    public int AppsFailed { get; set; }

    /// <summary>Anzahl der zurückgespielten App-Daten-Archive (Root).</summary>
    public int AppDataRestored { get; set; }

    /// <summary>Anzahl der auf dem Gerät abgelegten Importdateien (vCard, ICS, XML).</summary>
    public int ImportFilesPlaced { get; set; }

    public long BytesRestored { get; set; }

    public bool Canceled { get; set; }

    public TimeSpan Duration { get; set; }

    public List<string> Warnings { get; } = new();

    public string SummaryText
    {
        get
        {
            var text = $"{Humanize.Count(FilesRestored, "Datei", "Dateien")} zurückgespielt ({Humanize.Bytes(BytesRestored)})";
            if (AppsInstalled > 0)
            {
                text += $", {Humanize.Count(AppsInstalled, "App", "Apps")} installiert";
            }

            if (AppDataRestored > 0)
            {
                text += $", App-Daten für {AppDataRestored} Apps";
            }

            if (ImportFilesPlaced > 0)
            {
                text += $", {Humanize.Count(ImportFilesPlaced, "Importdatei", "Importdateien")} abgelegt";
            }

            if (FilesSkipped > 0)
            {
                text += $", {FilesSkipped} übersprungen";
            }

            if (FilesFailed + AppsFailed > 0)
            {
                text += $", {FilesFailed + AppsFailed} fehlgeschlagen";
            }

            return text + $" in {Humanize.Duration(Duration)}";
        }
    }
}

/// <summary>Ergebnis einer Überprüfung eines Sicherungssatzes.</summary>
public sealed class VerificationResult
{
    public int Checked { get; set; }

    public int Ok { get; set; }

    public List<string> Missing { get; } = new();

    public List<string> SizeMismatch { get; } = new();

    public List<string> HashMismatch { get; } = new();

    public int WithoutHash { get; set; }

    public bool IsHealthy => Missing.Count == 0 && SizeMismatch.Count == 0 && HashMismatch.Count == 0;

    public string SummaryText => IsHealthy
        ? $"Alles in Ordnung – {Ok:N0} von {Checked:N0} Elementen geprüft." +
          (WithoutHash > 0 ? $" ({WithoutHash:N0} ohne Prüfsumme, nur Größe verglichen.)" : string.Empty)
        : $"Probleme gefunden: {Missing.Count} fehlend, {SizeMismatch.Count} mit abweichender Größe, {HashMismatch.Count} mit abweichender Prüfsumme.";
}
