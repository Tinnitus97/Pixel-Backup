using PixelBackup.Core.Adb;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Util;

namespace PixelBackup.Core.Model;

public enum PlanAction
{
    /// <summary>Element wird vom Gerät geholt.</summary>
    Copy,

    /// <summary>Element ist unverändert und bereits gesichert.</summary>
    Skip
}

/// <summary>Ein konkret eingeplantes Element eines Sicherungslaufes.</summary>
public sealed class PlannedItem
{
    public required string CategoryId { get; init; }

    public BackupEntryType Type { get; init; } = BackupEntryType.File;

    public string RemotePath { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public long Size { get; set; }

    public long ModifiedUnix { get; init; }

    public string? PackageName { get; init; }

    public string? VersionCode { get; init; }

    /// <summary>Zusatzangabe, etwa der Namensraum der Systemeinstellungen.</summary>
    public string? Argument { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public PlanAction Action { get; set; } = PlanAction.Copy;

    public override string ToString() => DisplayName.Length > 0 ? DisplayName : RemotePath;
}

/// <summary>Ergebnis der Analyse: was würde gesichert und wie viel ist das.</summary>
public sealed class BackupPlan
{
    public DeviceInfo Device { get; init; } = new();

    /// <summary>Root-Zugriff, der beim Planen festgestellt wurde.</summary>
    public RootMode RootAccess { get; set; } = RootMode.None;

    public List<PlannedItem> Items { get; } = new();

    public List<string> Warnings { get; } = new();

    /// <summary>Alle auf dem Gerät gefundenen Dateipfade (für das Aufräumen gelöschter Dateien).</summary>
    public HashSet<string> SeenRemotePaths { get; } = new(StringComparer.Ordinal);

    public IEnumerable<PlannedItem> ItemsToCopy => Items.Where(i => i.Action == PlanAction.Copy);

    public int CopyCount => Items.Count(i => i.Action == PlanAction.Copy);

    public int SkipCount => Items.Count(i => i.Action == PlanAction.Skip);

    public long BytesToCopy => Items.Where(i => i.Action == PlanAction.Copy).Sum(i => Math.Max(0, i.Size));

    public long TotalBytes => Items.Sum(i => Math.Max(0, i.Size));

    public IReadOnlyList<CategorySummary> Summaries => Items
        .GroupBy(i => i.CategoryId)
        .Select(g => new CategorySummary(
            g.Key,
            g.Count(),
            g.Count(i => i.Action == PlanAction.Copy),
            g.Sum(i => Math.Max(0, i.Size))))
        .OrderBy(s => CategoryCatalog.IndexOf(s.CategoryId))
        .ToList();

    public string SummaryText =>
        $"{Humanize.Items(CopyCount)} · {Humanize.Bytes(BytesToCopy)}" +
        (SkipCount > 0 ? Loc.Tr($" (unverändert: {SkipCount})", $" ({SkipCount} unchanged)") : string.Empty);
}

public sealed record CategorySummary(string CategoryId, int ItemCount, int CopyCount, long Bytes)
{
    public string DisplayName => CategoryCatalog.DisplayNameOf(CategoryId);

    public string SizeText => Humanize.Bytes(Bytes);
}
