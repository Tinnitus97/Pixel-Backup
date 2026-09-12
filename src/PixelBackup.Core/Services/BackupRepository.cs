using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;

namespace PixelBackup.Core.Services;

/// <summary>Verwaltet die Sicherungssätze im Sicherungsordner.</summary>
public sealed class BackupRepository
{
    private readonly ILogSink _log;

    public BackupRepository(string root, ILogSink? log = null)
    {
        Root = root;
        _log = log ?? NullLogSink.Instance;
    }

    public string Root { get; set; }

    /// <summary>Liest alle Sicherungssätze ein (Ordnerstruktur: Wurzel/Gerät/Zeitstempel).</summary>
    public IReadOnlyList<BackupSet> LoadSets()
    {
        var sets = new List<BackupSet>();
        if (string.IsNullOrWhiteSpace(Root) || !Directory.Exists(Root))
        {
            return sets;
        }

        foreach (var deviceDirectory in SafeEnumerateDirectories(Root))
        {
            TryAdd(sets, deviceDirectory);
            foreach (var setDirectory in SafeEnumerateDirectories(deviceDirectory))
            {
                TryAdd(sets, setDirectory);
            }
        }

        return sets
            .OrderByDescending(s => s.Manifest.UpdatedUtc)
            .ToList();
    }

    public BackupSet? LatestFor(string deviceSerial) =>
        LoadSets().FirstOrDefault(s =>
            string.Equals(s.Manifest.Device.Serial, deviceSerial, StringComparison.OrdinalIgnoreCase));

    public void Delete(BackupSet set)
    {
        if (Directory.Exists(set.Directory))
        {
            Directory.Delete(set.Directory, recursive: true);
            _log.Info(Loc.Tr($"Sicherungssatz gelöscht: {set.Directory}", $"Backup set deleted: {set.Directory}"));
        }

        var archive = set.Directory.TrimEnd(Path.DirectorySeparatorChar) + ".zip";
        foreach (var candidate in new[] { archive, archive + ArchiveService.EncryptedExtension })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    /// <summary>Löscht alte Sätze eines Gerätes, sodass nur die neuesten <paramref name="keep"/> bleiben.</summary>
    public int ApplyRetention(string deviceSerial, int keep)
    {
        if (keep <= 0)
        {
            return 0;
        }

        var sets = LoadSets()
            .Where(s => string.Equals(s.Manifest.Device.Serial, deviceSerial, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Manifest.CreatedUtc)
            .ToList();

        var removed = 0;
        foreach (var set in sets.Skip(keep))
        {
            try
            {
                Delete(set);
                removed++;
            }
            catch (IOException ex)
            {
                _log.Warn(Loc.Tr(
                    $"Alter Satz {set.Name} konnte nicht gelöscht werden: {ex.Message}",
                    $"Old set {set.Name} could not be deleted: {ex.Message}"));
            }
        }

        return removed;
    }

    /// <summary>Belegter Speicherplatz aller Sicherungen auf der Festplatte.</summary>
    public long CalculateDiskUsage()
    {
        if (!Directory.Exists(Root))
        {
            return 0;
        }

        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
        }

        return total;
    }

    private void TryAdd(List<BackupSet> sets, string directory)
    {
        var manifest = BackupManifest.TryLoad(directory);
        if (manifest is not null)
        {
            sets.Add(new BackupSet(directory, manifest));
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }
}
