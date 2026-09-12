using System.Globalization;
using PixelBackup.Core.Util;

namespace PixelBackup.Core.Model;

/// <summary>Ein Sicherungssatz: Ordner auf der Festplatte plus zugehöriges Manifest.</summary>
public sealed class BackupSet
{
    public BackupSet(string directory, BackupManifest manifest)
    {
        Directory = directory;
        Manifest = manifest;
    }

    public string Directory { get; }

    public BackupManifest Manifest { get; }

    public string Name => Path.GetFileName(Directory.TrimEnd(Path.DirectorySeparatorChar));

    public DateTimeOffset Created => Manifest.CreatedUtc.ToLocalTime();

    public DateTimeOffset Updated => Manifest.UpdatedUtc.ToLocalTime();

    public long TotalBytes => Manifest.TotalBytes;

    public string DeviceName => Manifest.Device.DisplayName;

    public string SizeText => Humanize.Bytes(TotalBytes);

    public string CreatedText => Created.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);

    public string UpdatedText => Updated.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);

    public string CategoriesText => Manifest.Categories.Count == 0
        ? "–"
        : string.Join(", ", Manifest.Categories.Select(CategoryCatalog.DisplayNameOf));

    public string Summary =>
        $"{Humanize.Count(Manifest.FileCount, "Element", "Elemente")} · {SizeText} · {CategoriesText}";

    /// <summary>Absoluter Pfad eines Eintrags innerhalb dieses Satzes.</summary>
    public string LocalPathOf(BackupEntry entry) => PathMapper.ToLocalPath(Directory, entry.RelativePath);

    public IEnumerable<BackupEntry> EntriesOf(IEnumerable<string> categoryIds)
    {
        var ids = new HashSet<string>(categoryIds, StringComparer.OrdinalIgnoreCase);
        return Manifest.Entries.Where(e => ids.Contains(e.CategoryId));
    }

    public override string ToString() => $"{Name} ({SizeText})";
}
