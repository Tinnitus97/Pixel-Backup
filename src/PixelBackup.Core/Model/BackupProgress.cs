using PixelBackup.Core.Util;
using PixelBackup.Core.Localization;

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

    /// <summary>Übertragene Bytes des laufenden Schrittes (Datei bzw. Stapel).</summary>
    public long CurrentBytesDone { get; init; }

    /// <summary>Größe des laufenden Schrittes; 0, wenn sie sich nicht beziffern lässt.</summary>
    public long CurrentBytesTotal { get; init; }

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

    public string EtaText => Eta is { } eta
        ? Loc.Tr("noch ca. " + Humanize.Duration(eta), Humanize.Duration(eta) + " left")
        : "–";

    public string SpeedText => Humanize.Speed(BytesPerSecond);

    /// <summary>Fortschritt des laufenden Schrittes in Prozent.</summary>
    public double CurrentPercent => CurrentBytesTotal > 0
        ? Math.Clamp(CurrentBytesDone * 100d / CurrentBytesTotal, 0, 100)
        : 0;

    /// <summary>
    /// Lässt sich der laufende Schritt beziffern? Wenn nicht (Vorbereitung,
    /// Archivierung, Ausleseschritte ohne Größenangabe), zeigt die Oberfläche
    /// dafür einen laufenden Balken statt einer erfundenen Zahl.
    /// </summary>
    public bool HasCurrentProgress => CurrentBytesTotal > 0;

    public string CurrentBytesText => CurrentBytesTotal > 0
        ? $"{Humanize.Bytes(CurrentBytesDone)} / {Humanize.Bytes(CurrentBytesTotal)}"
        : "–";

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
            var text = Loc.Tr(
                $"{Humanize.Items(FilesCopied)} gesichert ({Humanize.Bytes(BytesCopied)})",
                $"{Humanize.Items(FilesCopied)} backed up ({Humanize.Bytes(BytesCopied)})");

            if (FilesSkipped > 0)
            {
                text += Loc.Tr($", {FilesSkipped} unverändert", $", {FilesSkipped} unchanged");
            }

            if (FilesFailed > 0)
            {
                text += Loc.Tr($", {FilesFailed} fehlgeschlagen", $", {FilesFailed} failed");
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
            var text = Loc.Tr(
                $"{Humanize.Files(FilesRestored)} zurückgespielt ({Humanize.Bytes(BytesRestored)})",
                $"{Humanize.Files(FilesRestored)} restored ({Humanize.Bytes(BytesRestored)})");

            if (AppsInstalled > 0)
            {
                text += Loc.Tr($", {Humanize.Apps(AppsInstalled)} installiert", $", {Humanize.Apps(AppsInstalled)} installed");
            }

            if (AppDataRestored > 0)
            {
                text += Loc.Tr($", App-Daten für {AppDataRestored} Apps", $", app data for {AppDataRestored} apps");
            }

            if (ImportFilesPlaced > 0)
            {
                text += Loc.Tr(
                    $", {Humanize.Count(ImportFilesPlaced, "Importdatei", "Importdateien")} abgelegt",
                    $", {Humanize.Count(ImportFilesPlaced, "import file", "import files")} placed");
            }

            if (FilesSkipped > 0)
            {
                text += Loc.Tr($", {FilesSkipped} übersprungen", $", {FilesSkipped} skipped");
            }

            if (FilesFailed + AppsFailed > 0)
            {
                text += Loc.Tr($", {FilesFailed + AppsFailed} fehlgeschlagen", $", {FilesFailed + AppsFailed} failed");
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
        ? Loc.Tr(
              $"Alles in Ordnung – {Ok:N0} von {Checked:N0} Elementen geprüft.",
              $"All good – {Ok:N0} of {Checked:N0} items verified.") +
          (WithoutHash > 0
              ? Loc.Tr(
                  $" ({WithoutHash:N0} ohne Prüfsumme, nur Größe verglichen.)",
                  $" ({WithoutHash:N0} without a checksum, size compared only.)")
              : string.Empty)
        : Loc.Tr(
            $"Probleme gefunden: {Missing.Count} fehlend, {SizeMismatch.Count} mit abweichender Größe, {HashMismatch.Count} mit abweichender Prüfsumme.",
            $"Problems found: {Missing.Count} missing, {SizeMismatch.Count} with a different size, {HashMismatch.Count} with a different checksum.");
}
