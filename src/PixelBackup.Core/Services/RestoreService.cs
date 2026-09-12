using System.Diagnostics;
using PixelBackup.Core.Adb;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Model;
using PixelBackup.Core.Util;

namespace PixelBackup.Core.Services;

/// <summary>Spielt einen Sicherungssatz wieder auf ein Gerät zurück.</summary>
public sealed class RestoreService
{
    private readonly AdbClient _adb;
    private readonly ILogSink _log;

    public RestoreService(AdbClient adb, ILogSink? log = null)
    {
        _adb = adb;
        _log = log ?? NullLogSink.Instance;
    }

    public async Task<RestoreResult> RunAsync(
        string serial,
        RestoreOptions options,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new RestoreResult();

        var entries = options.Set.EntriesOf(options.CategoryIds).ToList();
        var fileEntries = entries.Where(e => e.Type == BackupEntryType.File).ToList();
        var apkEntries = entries.Where(e => e.Type == BackupEntryType.Apk).ToList();
        var legacyEntries = entries.Where(e => e.Type == BackupEntryType.LegacyAppData).ToList();
        var exportEntries = entries
            .Where(e => e.Type is BackupEntryType.ContentExport or BackupEntryType.SettingsExport)
            .ToList();

        if (exportEntries.Count > 0)
        {
            result.Warnings.Add(
                "Kontakte, Nachrichten und Systemeinstellungen wurden als Datenexport gesichert und lassen sich nicht automatisch zurückschreiben. " +
                "Die Dateien liegen im Ordner 'data' des Sicherungssatzes.");
        }

        var appGroups = apkEntries
            .GroupBy(e => e.PackageName ?? "unbekannt")
            .ToList();

        var itemsTotal = fileEntries.Count + (options.InstallApps ? appGroups.Count : 0) +
                         (options.RestoreLegacyAppData ? legacyEntries.Count : 0);
        var bytesTotal = fileEntries.Sum(e => Math.Max(0, e.Size));
        long bytesDone = 0;
        var itemsDone = 0;

        _log.Info($"Wiederherstellung startet: {options.Set.Name} → {serial}");

        try
        {
            // 1) Konflikte vorab ermitteln, damit nicht für jede Datei einzeln geprüft werden muss.
            var existing = new HashSet<string>(StringComparer.Ordinal);
            if (options.ConflictMode != ConflictMode.Overwrite && fileEntries.Count > 0)
            {
                progress?.Report(new OperationProgress
                {
                    Phase = "Vorhandene Dateien werden geprüft",
                    ItemsTotal = itemsTotal,
                    BytesTotal = bytesTotal
                });

                existing = await _adb.FilterExistingAsync(serial, fileEntries.Select(e => e.RemotePath), ct)
                    .ConfigureAwait(false);
            }

            var createdDirectories = new HashSet<string>(StringComparer.Ordinal);
            var scanPaths = new List<string>();

            foreach (var entry in fileEntries)
            {
                ct.ThrowIfCancellationRequested();

                var localPath = options.Set.LocalPathOf(entry);
                var target = entry.RemotePath;

                progress?.Report(new OperationProgress
                {
                    Phase = CategoryCatalog.DisplayNameOf(entry.CategoryId),
                    CurrentItem = PathMapper.RemoteFileName(target),
                    ItemsDone = itemsDone,
                    ItemsTotal = itemsTotal,
                    BytesDone = bytesDone,
                    BytesTotal = bytesTotal,
                    BytesPerSecond = stopwatch.Elapsed.TotalSeconds > 0.5 ? bytesDone / stopwatch.Elapsed.TotalSeconds : 0,
                    Elapsed = stopwatch.Elapsed
                });

                itemsDone++;

                if (!File.Exists(localPath))
                {
                    result.FilesFailed++;
                    result.Warnings.Add($"In der Sicherung fehlt: {entry.RelativePath}");
                    continue;
                }

                if (existing.Contains(target))
                {
                    if (options.ConflictMode == ConflictMode.Skip)
                    {
                        result.FilesSkipped++;
                        continue;
                    }

                    if (options.ConflictMode == ConflictMode.KeepBoth)
                    {
                        target = PathMapper.AppendSuffix(target, "_wiederhergestellt");
                    }
                }

                var directory = PathMapper.RemoteDirectory(target);
                if (createdDirectories.Add(directory))
                {
                    await _adb.MakeDirectoryAsync(serial, directory, ct).ConfigureAwait(false);
                }

                var push = await _adb.PushAsync(serial, localPath, target, ct).ConfigureAwait(false);
                if (push.Success)
                {
                    result.FilesRestored++;
                    result.BytesRestored += Math.Max(0, entry.Size);
                    if (options.TriggerMediaScan && IsMedia(target))
                    {
                        scanPaths.Add(target);
                    }
                }
                else
                {
                    result.FilesFailed++;
                    var message = $"{PathMapper.RemoteFileName(target)} konnte nicht übertragen werden: {push.ErrorSummary}";
                    result.Warnings.Add(message);
                    _log.Warn(message);
                }

                bytesDone += Math.Max(0, entry.Size);
            }

            // 2) Apps installieren
            if (options.InstallApps && appGroups.Count > 0)
            {
                foreach (var group in appGroups)
                {
                    ct.ThrowIfCancellationRequested();

                    progress?.Report(new OperationProgress
                    {
                        Phase = "Apps werden installiert",
                        CurrentItem = group.Key,
                        ItemsDone = itemsDone,
                        ItemsTotal = itemsTotal,
                        BytesDone = bytesDone,
                        BytesTotal = bytesTotal,
                        Elapsed = stopwatch.Elapsed
                    });

                    itemsDone++;

                    var apks = group
                        .Select(options.Set.LocalPathOf)
                        .Where(File.Exists)
                        .ToList();

                    if (apks.Count == 0)
                    {
                        result.AppsFailed++;
                        result.Warnings.Add($"Für {group.Key} fehlen die APK-Dateien in der Sicherung.");
                        continue;
                    }

                    var install = await _adb.InstallAsync(serial, apks, options.AllowDowngrade, ct).ConfigureAwait(false);
                    if (install.Success && !install.CombinedOutput.Contains("Failure", StringComparison.OrdinalIgnoreCase))
                    {
                        result.AppsInstalled++;
                        _log.Info($"Installiert: {group.Key}");
                    }
                    else
                    {
                        result.AppsFailed++;
                        var message = $"{group.Key} konnte nicht installiert werden: {install.ErrorSummary}";
                        result.Warnings.Add(message);
                        _log.Warn(message);
                    }
                }
            }

            // 3) Klassische App-Daten
            if (options.RestoreLegacyAppData)
            {
                foreach (var entry in legacyEntries)
                {
                    ct.ThrowIfCancellationRequested();
                    var localPath = options.Set.LocalPathOf(entry);
                    if (!File.Exists(localPath))
                    {
                        continue;
                    }

                    progress?.Report(new OperationProgress
                    {
                        Phase = "App-Daten werden zurückgespielt",
                        CurrentItem = "Bitte am Gerät bestätigen",
                        ItemsDone = itemsDone,
                        ItemsTotal = itemsTotal,
                        BytesDone = bytesDone,
                        BytesTotal = bytesTotal,
                        Elapsed = stopwatch.Elapsed
                    });

                    itemsDone++;
                    _log.Info("Bitte die Wiederherstellung am Gerät bestätigen (adb restore).");
                    var restore = await _adb.LegacyRestoreAsync(serial, localPath, ct).ConfigureAwait(false);
                    if (!restore.Success)
                    {
                        result.Warnings.Add("Die App-Daten konnten nicht zurückgespielt werden: " + restore.ErrorSummary);
                    }
                }
            }

            // 4) Medienscanner anstoßen, damit Fotos und Videos sofort in der Galerie erscheinen.
            if (scanPaths.Count > 0)
            {
                progress?.Report(new OperationProgress
                {
                    Phase = "Medien werden am Gerät eingelesen",
                    ItemsDone = itemsDone,
                    ItemsTotal = itemsTotal,
                    BytesDone = bytesDone,
                    BytesTotal = bytesTotal,
                    Elapsed = stopwatch.Elapsed
                });

                foreach (var directory in scanPaths.Select(PathMapper.RemoteDirectory).Distinct().Take(200))
                {
                    ct.ThrowIfCancellationRequested();
                    await _adb.ScanMediaAsync(serial, directory, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Canceled = true;
            _log.Warn("Die Wiederherstellung wurde abgebrochen.");
        }

        result.Duration = stopwatch.Elapsed;
        _log.Info("Wiederherstellung beendet: " + result.SummaryText);
        return result;
    }

    private static bool IsMedia(string remotePath)
    {
        var extension = PathMapper.RemoteExtension(remotePath);
        return extension is "jpg" or "jpeg" or "png" or "heic" or "heif" or "webp" or "gif" or "dng"
            or "mp4" or "mkv" or "mov" or "3gp" or "webm" or "mp3" or "m4a" or "flac" or "wav" or "ogg" or "opus";
    }
}
