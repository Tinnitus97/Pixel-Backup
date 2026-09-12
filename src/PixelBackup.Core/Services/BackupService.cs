using System.Diagnostics;
using System.Globalization;
using System.Text;
using PixelBackup.Core.Adb;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Model;
using PixelBackup.Core.Util;

namespace PixelBackup.Core.Services;

/// <summary>Erstellt Sicherungssätze: Analyse (Plan) und Durchführung.</summary>
public sealed class BackupService
{
    private readonly AdbClient _adb;
    private readonly ILogSink _log;

    public BackupService(AdbClient adb, ILogSink? log = null)
    {
        _adb = adb;
        _log = log ?? NullLogSink.Instance;
    }

    // ---------------------------------------------------------------- Analyse

    /// <summary>
    /// Ermittelt, welche Elemente gesichert werden müssten. Bei einem inkrementellen Lauf
    /// werden unveränderte Dateien als "übersprungen" markiert.
    /// </summary>
    public async Task<BackupPlan> CreatePlanAsync(
        string serial,
        DeviceInfo device,
        IReadOnlyList<BackupCategory> categories,
        BackupSet? previousSet,
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        var plan = new BackupPlan { Device = device };
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var directoryCache = new Dictionary<string, IReadOnlyList<RemoteFile>>(StringComparer.OrdinalIgnoreCase);
        var fileCategories = CategoryCatalog.FileCategories.ToList();

        foreach (var category in categories.OrderBy(CategoryCatalog.IndexOf))
        {
            ct.ThrowIfCancellationRequested();
            status?.Report($"Analysiere {category.DisplayName} …");
            _log.Info($"Analysiere Kategorie '{category.DisplayName}'.");

            switch (category.Kind)
            {
                case BackupCategoryKind.Files:
                    await PlanFilesAsync(serial, plan, category, fileCategories, claimed, directoryCache, ct)
                        .ConfigureAwait(false);
                    break;

                case BackupCategoryKind.Apps:
                    await PlanAppsAsync(serial, plan, category, status, ct).ConfigureAwait(false);
                    break;

                case BackupCategoryKind.RootAppData:
                    await PlanRootAppDataAsync(serial, plan, category, status, ct).ConfigureAwait(false);
                    break;

                case BackupCategoryKind.AppData:
                    plan.Items.Add(new PlannedItem
                    {
                        CategoryId = category.Id,
                        Type = BackupEntryType.LegacyAppData,
                        RemotePath = "adb-backup",
                        RelativePath = $"{PathMapper.AppDataFolder}/app-daten.ab",
                        DisplayName = "App-Daten (adb backup)",
                        Size = 0
                    });
                    plan.Warnings.Add(
                        "Die klassische App-Daten-Sicherung muss am Gerät bestätigt werden und wird ab Android 12 von den meisten Apps abgelehnt.");
                    break;

                case BackupCategoryKind.ContentProvider:
                    foreach (var uri in category.Sources)
                    {
                        plan.Items.Add(new PlannedItem
                        {
                            CategoryId = category.Id,
                            Type = BackupEntryType.ContentExport,
                            RemotePath = uri,
                            Argument = uri,
                            RelativePath = $"{PathMapper.DataFolder}/{category.Id}-{SafeSourceName(uri)}.txt",
                            DisplayName = $"{category.DisplayName} ({uri})",
                            Size = 0
                        });
                    }

                    break;

                case BackupCategoryKind.Settings:
                    foreach (var nameSpace in category.Sources)
                    {
                        plan.Items.Add(new PlannedItem
                        {
                            CategoryId = category.Id,
                            Type = BackupEntryType.SettingsExport,
                            RemotePath = $"settings/{nameSpace}",
                            Argument = nameSpace,
                            RelativePath = $"{PathMapper.DataFolder}/einstellungen-{nameSpace}.txt",
                            DisplayName = $"Einstellungen: {nameSpace}",
                            Size = 0
                        });
                    }

                    break;
            }
        }

        if (previousSet is not null)
        {
            MarkUnchangedItems(plan, previousSet);
        }

        _log.Info($"Analyse abgeschlossen: {plan.CopyCount} zu sichernde Elemente, {Humanize.Bytes(plan.BytesToCopy)}.");
        return plan;
    }

    private async Task PlanFilesAsync(
        string serial,
        BackupPlan plan,
        BackupCategory category,
        IReadOnlyList<BackupCategory> fileCategories,
        HashSet<string> claimed,
        Dictionary<string, IReadOnlyList<RemoteFile>> directoryCache,
        CancellationToken ct)
    {
        foreach (var directory in category.RemoteDirectories)
        {
            ct.ThrowIfCancellationRequested();
            var cacheKey = directory + (category.IsCatchAll ? "|prune" : string.Empty);
            if (!directoryCache.TryGetValue(cacheKey, out var files))
            {
                files = await _adb.ListFilesAsync(serial, directory, category.IsCatchAll, ct).ConfigureAwait(false);
                directoryCache[cacheKey] = files;
                _log.Debug($"{directory}: {files.Count} Dateien gefunden.");
            }

            foreach (var file in files)
            {
                plan.SeenRemotePaths.Add(file.Path);

                if (category.IsCatchAll)
                {
                    if (fileCategories.Any(c => c.Matches(file.Path)))
                    {
                        continue;
                    }
                }
                else if (!category.Matches(file.Path))
                {
                    continue;
                }

                if (!claimed.Add(file.Path))
                {
                    continue;
                }

                plan.Items.Add(new PlannedItem
                {
                    CategoryId = category.Id,
                    Type = BackupEntryType.File,
                    RemotePath = file.Path,
                    RelativePath = PathMapper.ToRelativeBackupPath(file.Path),
                    Size = file.Size,
                    ModifiedUnix = file.ModifiedUnix,
                    DisplayName = PathMapper.RemoteFileName(file.Path)
                });
            }
        }
    }

    private async Task PlanAppsAsync(
        string serial,
        BackupPlan plan,
        BackupCategory category,
        IProgress<string>? status,
        CancellationToken ct)
    {
        var packages = await _adb.ListPackagesAsync(serial, includeSystemApps: false, ct).ConfigureAwait(false);
        status?.Report($"{packages.Count} Apps gefunden – ermittle Installationsdateien …");

        var paths = await _adb.ResolveApkPathsAsync(serial, packages.Select(p => p.PackageName), ct)
            .ConfigureAwait(false);

        var allApks = paths.Values.SelectMany(v => v).ToList();
        var sizes = (await _adb.StatAsync(serial, allApks, ct).ConfigureAwait(false))
            .ToDictionary(f => f.Path, f => f, StringComparer.Ordinal);

        foreach (var package in packages)
        {
            ct.ThrowIfCancellationRequested();
            if (!paths.TryGetValue(package.PackageName, out var apks) || apks.Count == 0)
            {
                plan.Warnings.Add($"Für {package.PackageName} wurde keine Installationsdatei gefunden.");
                continue;
            }

            foreach (var apk in apks)
            {
                sizes.TryGetValue(apk, out var stat);
                plan.SeenRemotePaths.Add(apk);
                plan.Items.Add(new PlannedItem
                {
                    CategoryId = category.Id,
                    Type = BackupEntryType.Apk,
                    RemotePath = apk,
                    RelativePath = $"{PathMapper.AppsFolder}/{PathMapper.SanitizeSegment(package.PackageName)}/{PathMapper.SanitizeSegment(PathMapper.RemoteFileName(apk))}",
                    Size = stat?.Size ?? -1,
                    ModifiedUnix = stat?.ModifiedUnix ?? 0,
                    PackageName = package.PackageName,
                    VersionCode = package.VersionCode,
                    DisplayName = package.PackageName
                });
            }
        }
    }

    private async Task PlanRootAppDataAsync(
        string serial,
        BackupPlan plan,
        BackupCategory category,
        IProgress<string>? status,
        CancellationToken ct)
    {
        var mode = await _adb.DetectRootAsync(serial, ct).ConfigureAwait(false);
        plan.RootAccess = mode;

        if (mode == RootMode.None)
        {
            var warning = "Ohne Root-Zugriff lassen sich die vollständigen App-Daten nicht sichern – " +
                          "diese Gruppe wird übersprungen. Gesichert werden stattdessen die Apps selbst (APK).";
            plan.Warnings.Add(warning);
            _log.Warn(warning);
            return;
        }

        status?.Report("Root erkannt – ermittle Größe der App-Daten …");
        var packages = await _adb.ListPackagesAsync(serial, includeSystemApps: false, ct).ConfigureAwait(false);
        var sizes = await _adb
            .GetAppDataSizesAsync(serial, packages.Select(p => p.PackageName), mode, ct)
            .ConfigureAwait(false);

        foreach (var package in packages)
        {
            ct.ThrowIfCancellationRequested();
            var remotePath = "/data/data/" + package.PackageName;
            plan.SeenRemotePaths.Add(remotePath);
            plan.Items.Add(new PlannedItem
            {
                CategoryId = category.Id,
                Type = BackupEntryType.RootAppData,
                RemotePath = remotePath,
                RelativePath = $"{PathMapper.RootAppDataFolder}/{PathMapper.SanitizeSegment(package.PackageName)}.tar",
                Size = sizes.TryGetValue(package.PackageName, out var size) ? size : -1,
                PackageName = package.PackageName,
                VersionCode = package.VersionCode,
                DisplayName = package.PackageName
            });
        }
    }

    private static void MarkUnchangedItems(BackupPlan plan, BackupSet previousSet)
    {
        var existing = new Dictionary<string, BackupEntry>(StringComparer.Ordinal);
        foreach (var entry in previousSet.Manifest.Entries)
        {
            existing[EntryKey(entry.Type, entry.RemotePath)] = entry;
        }

        foreach (var item in plan.Items)
        {
            if (item.Type is not (BackupEntryType.File or BackupEntryType.Apk))
            {
                continue;
            }

            if (!existing.TryGetValue(EntryKey(item.Type, item.RemotePath), out var entry))
            {
                continue;
            }

            if (item.Size < 0 || entry.Size != item.Size || entry.ModifiedUnix != item.ModifiedUnix)
            {
                continue;
            }

            if (File.Exists(previousSet.LocalPathOf(entry)))
            {
                item.Action = PlanAction.Skip;
            }
        }
    }

    private static string EntryKey(BackupEntryType type, string remotePath) => $"{type}|{remotePath}";

    private static string SafeSourceName(string uri)
    {
        var index = uri.IndexOf("://", StringComparison.Ordinal);
        var name = index >= 0 ? uri[(index + 3)..] : uri;
        return PathMapper.SanitizeSegment(name.Replace('/', '-').Trim('-'));
    }

    // ----------------------------------------------------------- Durchführung

    public async Task<BackupResult> RunAsync(
        string serial,
        BackupPlan plan,
        BackupOptions options,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        var setDirectory = options.Incremental && options.TargetSet is not null
            ? options.TargetSet.Directory
            : CreateSetDirectory(options.BackupRoot, plan.Device);

        Directory.CreateDirectory(setDirectory);

        var manifest = options.Incremental && options.TargetSet is not null
            ? options.TargetSet.Manifest
            : new BackupManifest { Device = plan.Device, Notes = options.Notes };

        manifest.Device = plan.Device;
        foreach (var categoryId in plan.Items.Select(i => i.CategoryId).Distinct())
        {
            if (!manifest.Categories.Contains(categoryId))
            {
                manifest.Categories.Add(categoryId);
            }
        }

        var entries = manifest.Entries.ToDictionary(e => EntryKey(e.Type, e.RemotePath), e => e, StringComparer.Ordinal);
        var result = new BackupResult { SetDirectory = setDirectory, Manifest = manifest };
        result.Warnings.AddRange(plan.Warnings);

        var itemsToCopy = plan.ItemsToCopy.ToList();
        var itemsTotal = itemsToCopy.Count;
        var bytesTotal = plan.BytesToCopy;
        long bytesDone = 0;
        var itemsDone = 0;

        result.FilesSkipped = plan.SkipCount;

        _log.Info($"Sicherung startet: {setDirectory}");
        progress?.Report(new OperationProgress
        {
            Phase = "Sicherung wird vorbereitet",
            ItemsTotal = itemsTotal,
            BytesTotal = bytesTotal
        });

        void Register(BackupEntry? entry)
        {
            if (entry is null)
            {
                result.FilesFailed++;
                return;
            }

            entries[EntryKey(entry.Type, entry.RemotePath)] = entry;
            result.FilesCopied++;
            result.BytesCopied += Math.Max(0, entry.Size);
        }

        try
        {
            foreach (var batch in BuildBatches(itemsToCopy))
            {
                ct.ThrowIfCancellationRequested();

                var batchBase = bytesDone;
                var batchBytes = batch.Sum(i => Math.Max(0, i.Size));
                var first = batch[0];

                void ReportBatch(int percent) => progress?.Report(new OperationProgress
                {
                    Phase = CategoryCatalog.DisplayNameOf(first.CategoryId),
                    CurrentItem = batch.Count == 1
                        ? first.DisplayName
                        : $"{first.DisplayName} (+{batch.Count - 1} weitere)",
                    ItemsDone = itemsDone,
                    ItemsTotal = itemsTotal,
                    BytesDone = batchBase + batchBytes * percent / 100,
                    BytesTotal = bytesTotal,
                    BytesPerSecond = Speed(batchBase, stopwatch.Elapsed),
                    Elapsed = stopwatch.Elapsed
                });

                ReportBatch(0);

                try
                {
                    if (batch.Count == 1)
                    {
                        Register(await CaptureAsync(serial, setDirectory, first, options, plan.RootAccess, ct)
                            .ConfigureAwait(false));
                    }
                    else
                    {
                        foreach (var entry in await CaptureBatchAsync(serial, setDirectory, batch, options, ct)
                                     .ConfigureAwait(false))
                        {
                            Register(entry);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.FilesFailed += batch.Count;
                    var message = $"{first.DisplayName}: {ex.Message}";
                    result.Warnings.Add(message);
                    _log.Warn(message);
                }

                itemsDone += batch.Count;
                bytesDone = batchBase + batch.Sum(i => Math.Max(0, i.Size));
                ReportBatch(100);
            }
        }
        catch (OperationCanceledException)
        {
            result.Canceled = true;
            _log.Warn("Die Sicherung wurde abgebrochen – die bereits kopierten Daten bleiben erhalten.");
        }

        if (!result.Canceled && options.Incremental && options.RemoveDeletedFiles)
        {
            RemoveVanishedEntries(setDirectory, plan, entries, result);
        }

        if (!result.Canceled)
        {
            try
            {
                var importFiles = await ImportFileBuilder
                    .BuildAsync(setDirectory, entries.Values.ToList(), _log, ct)
                    .ConfigureAwait(false);

                foreach (var importFile in importFiles)
                {
                    entries[EntryKey(importFile.Type, importFile.RemotePath)] = importFile;
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add("Importdateien konnten nicht erzeugt werden: " + ex.Message);
                _log.Warn("Importdateien konnten nicht erzeugt werden: " + ex.Message);
            }
        }

        manifest.Entries = entries.Values.OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        manifest.UpdatedUtc = DateTimeOffset.UtcNow;
        manifest.Runs.Add(new BackupRun
        {
            StartedUtc = startedUtc,
            FinishedUtc = DateTimeOffset.UtcNow,
            Mode = options.Incremental ? BackupMode.Incremental : BackupMode.Full,
            FilesCopied = result.FilesCopied,
            FilesSkipped = result.FilesSkipped,
            FilesFailed = result.FilesFailed,
            BytesCopied = result.BytesCopied,
            Canceled = result.Canceled,
            Categories = plan.Items.Select(i => i.CategoryId).Distinct().ToList()
        });

        result.Duration = stopwatch.Elapsed;
        manifest.Save(setDirectory);
        WriteReport(setDirectory, manifest, result);
        WriteMigrationGuide(setDirectory, manifest);

        if (options.CreateArchive && !result.Canceled)
        {
            progress?.Report(new OperationProgress
            {
                Phase = "Archiv wird erstellt",
                ItemsDone = itemsTotal,
                ItemsTotal = itemsTotal,
                BytesDone = bytesTotal,
                BytesTotal = bytesTotal,
                Elapsed = stopwatch.Elapsed
            });

            try
            {
                var archive = await ArchiveService
                    .CreateArchiveAsync(setDirectory, options.ArchivePassword, ct)
                    .ConfigureAwait(false);
                result.ArchivePath = archive;
                manifest.HasArchive = true;
                manifest.ArchiveEncrypted = !string.IsNullOrEmpty(options.ArchivePassword);
                manifest.Save(setDirectory);
                _log.Info($"Archiv erstellt: {archive}");
            }
            catch (Exception ex)
            {
                result.Warnings.Add("Das Archiv konnte nicht erstellt werden: " + ex.Message);
                _log.Error("Archiv konnte nicht erstellt werden", ex);
            }
        }

        _log.Info("Sicherung beendet: " + result.SummaryText);
        return result;
    }

    /// <summary>
    /// Fasst aufeinanderfolgende Dateien desselben Geräteordners zu Stapeln zusammen.
    /// Ein einziger adb-Aufruf für 40 Dateien ist deutlich schneller als 40 Aufrufe.
    /// </summary>
    public static IEnumerable<List<PlannedItem>> BuildBatches(
        IReadOnlyList<PlannedItem> items,
        int maxBatchSize = 40,
        int maxArgumentLength = 6000)
    {
        var batch = new List<PlannedItem>();
        string? directory = null;
        var length = 0;

        foreach (var item in items)
        {
            if (!CanBatch(item))
            {
                if (batch.Count > 0)
                {
                    yield return batch;
                    batch = new List<PlannedItem>();
                    directory = null;
                    length = 0;
                }

                yield return new List<PlannedItem> { item };
                continue;
            }

            var itemDirectory = PathMapper.RemoteDirectory(item.RemotePath);
            if (batch.Count > 0 &&
                (!string.Equals(itemDirectory, directory, StringComparison.Ordinal) ||
                 batch.Count >= maxBatchSize ||
                 length + item.RemotePath.Length > maxArgumentLength))
            {
                yield return batch;
                batch = new List<PlannedItem>();
                length = 0;
            }

            directory = itemDirectory;
            batch.Add(item);
            length += item.RemotePath.Length + 3;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    private static bool CanBatch(PlannedItem item)
    {
        if (item.Type != BackupEntryType.File)
        {
            return false;
        }

        // Beim Stapelabruf legt adb die Dateien unter ihrem Originalnamen im Zielordner ab.
        // Namen, die auf der Festplatte angepasst werden müssten, werden einzeln geholt.
        var name = PathMapper.RemoteFileName(item.RemotePath);
        return name.Length > 0 && PathMapper.SanitizeSegment(name) == name;
    }

    private async Task<List<BackupEntry?>> CaptureBatchAsync(
        string serial,
        string setDirectory,
        List<PlannedItem> batch,
        BackupOptions options,
        CancellationToken ct)
    {
        var results = new List<BackupEntry?>(batch.Count);
        var localDirectory = Path.GetDirectoryName(PathMapper.ToLocalPath(setDirectory, batch[0].RelativePath));
        if (string.IsNullOrEmpty(localDirectory))
        {
            throw new InvalidOperationException("Ungültiges Sicherungsverzeichnis.");
        }

        var pull = await _adb
            .PullManyAsync(serial, batch.Select(i => i.RemotePath).ToList(), localDirectory, ct)
            .ConfigureAwait(false);

        foreach (var item in batch)
        {
            ct.ThrowIfCancellationRequested();
            var localPath = PathMapper.ToLocalPath(setDirectory, item.RelativePath);

            if (!File.Exists(localPath))
            {
                _log.Warn($"Konnte {item.RemotePath} nicht kopieren: {pull.ErrorSummary}");
                results.Add(null);
                continue;
            }

            var info = new FileInfo(localPath);
            if (item.Size < 0)
            {
                item.Size = info.Length;
            }

            results.Add(new BackupEntry
            {
                CategoryId = item.CategoryId,
                Type = item.Type,
                RemotePath = item.RemotePath,
                RelativePath = item.RelativePath,
                Size = info.Length,
                ModifiedUnix = item.ModifiedUnix > 0
                    ? item.ModifiedUnix
                    : new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
                Sha256 = options.ComputeHashes
                    ? await Hashing.Sha256FileAsync(localPath, ct).ConfigureAwait(false)
                    : null
            });
        }

        return results;
    }

    private async Task<BackupEntry?> CaptureAsync(
        string serial,
        string setDirectory,
        PlannedItem item,
        BackupOptions options,
        RootMode rootMode,
        CancellationToken ct)
    {
        var localPath = PathMapper.ToLocalPath(setDirectory, item.RelativePath);

        switch (item.Type)
        {
            case BackupEntryType.File:
            case BackupEntryType.Apk:
            {
                var pull = await _adb.PullAsync(serial, item.RemotePath, localPath, ct).ConfigureAwait(false);
                if (!File.Exists(localPath))
                {
                    _log.Warn($"Konnte {item.RemotePath} nicht kopieren: {pull.ErrorSummary}");
                    return null;
                }

                var info = new FileInfo(localPath);
                if (item.Size < 0)
                {
                    item.Size = info.Length;
                }

                return new BackupEntry
                {
                    CategoryId = item.CategoryId,
                    Type = item.Type,
                    RemotePath = item.RemotePath,
                    RelativePath = item.RelativePath,
                    Size = info.Length,
                    ModifiedUnix = item.ModifiedUnix > 0
                        ? item.ModifiedUnix
                        : new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
                    PackageName = item.PackageName,
                    VersionCode = item.VersionCode,
                    Sha256 = options.ComputeHashes ? await Hashing.Sha256FileAsync(localPath, ct).ConfigureAwait(false) : null
                };
            }

            case BackupEntryType.RootAppData:
            {
                if (rootMode == RootMode.None || item.PackageName is null)
                {
                    return null;
                }

                var command = AdbClient.WrapRoot(
                    $"tar -c -C /data/data {AdbClient.Quote(item.PackageName)} 2>/dev/null",
                    rootMode);

                var export = await _adb.ExecOutToFileAsync(serial, command, localPath, ct).ConfigureAwait(false);

                if (!File.Exists(localPath) || new FileInfo(localPath).Length < 1024)
                {
                    _log.Warn($"Keine App-Daten für {item.PackageName}: {export.ErrorSummary}");
                    TryDelete(localPath);
                    return null;
                }

                var archive = new FileInfo(localPath);
                item.Size = archive.Length;
                return new BackupEntry
                {
                    CategoryId = item.CategoryId,
                    Type = item.Type,
                    RemotePath = item.RemotePath,
                    RelativePath = item.RelativePath,
                    Size = archive.Length,
                    PackageName = item.PackageName,
                    VersionCode = item.VersionCode,
                    Sha256 = options.ComputeHashes ? await Hashing.Sha256FileAsync(localPath, ct).ConfigureAwait(false) : null
                };
            }

            case BackupEntryType.LegacyAppData:
            {
                _log.Info("Bitte die Sicherung am Gerät bestätigen (App-Daten über adb backup).");
                var backup = await _adb
                    .LegacyBackupAsync(serial, localPath, options.LegacyBackupWithApks, includeSharedStorage: false, ct)
                    .ConfigureAwait(false);

                if (!File.Exists(localPath) || new FileInfo(localPath).Length <= 512)
                {
                    _log.Warn("Es wurden keine App-Daten geliefert: " + backup.ErrorSummary);
                    return null;
                }

                var info = new FileInfo(localPath);
                item.Size = info.Length;
                return new BackupEntry
                {
                    CategoryId = item.CategoryId,
                    Type = item.Type,
                    RemotePath = item.RemotePath,
                    RelativePath = item.RelativePath,
                    Size = info.Length,
                    Sha256 = options.ComputeHashes ? await Hashing.Sha256FileAsync(localPath, ct).ConfigureAwait(false) : null
                };
            }

            case BackupEntryType.ContentExport:
            {
                var query = await _adb.QueryContentAsync(serial, item.Argument ?? item.RemotePath, ct).ConfigureAwait(false);
                var text = query.StandardOutput;
                if (text.Contains("Error while accessing provider", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Permission Denial", StringComparison.OrdinalIgnoreCase) ||
                    text.Trim().Length == 0)
                {
                    _log.Warn($"Export von {item.RemotePath} nicht möglich: {query.ErrorSummary}");
                    return null;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                await File.WriteAllTextAsync(localPath, text, Encoding.UTF8, ct).ConfigureAwait(false);

                var rows = ContentRowParser.Parse(text);
                if (rows.Count > 0)
                {
                    var csvPath = Path.ChangeExtension(localPath, ".csv");
                    await File.WriteAllTextAsync(csvPath, ContentRowParser.ToCsv(rows), Encoding.UTF8, ct)
                        .ConfigureAwait(false);
                    _log.Info($"{item.DisplayName}: {rows.Count} Datensätze exportiert.");
                }

                var info = new FileInfo(localPath);
                item.Size = info.Length;
                return new BackupEntry
                {
                    CategoryId = item.CategoryId,
                    Type = item.Type,
                    RemotePath = item.RemotePath,
                    RelativePath = item.RelativePath,
                    Size = info.Length,
                    Sha256 = options.ComputeHashes ? await Hashing.Sha256FileAsync(localPath, ct).ConfigureAwait(false) : null
                };
            }

            case BackupEntryType.SettingsExport:
            {
                var dump = await _adb.DumpSettingsAsync(serial, item.Argument ?? "system", ct).ConfigureAwait(false);
                if (dump.StandardOutput.Trim().Length == 0)
                {
                    _log.Warn($"Einstellungen '{item.Argument}' konnten nicht gelesen werden.");
                    return null;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                await File.WriteAllTextAsync(localPath, dump.StandardOutput, Encoding.UTF8, ct).ConfigureAwait(false);

                var info = new FileInfo(localPath);
                item.Size = info.Length;
                return new BackupEntry
                {
                    CategoryId = item.CategoryId,
                    Type = item.Type,
                    RemotePath = item.RemotePath,
                    RelativePath = item.RelativePath,
                    Size = info.Length,
                    Sha256 = options.ComputeHashes ? await Hashing.Sha256FileAsync(localPath, ct).ConfigureAwait(false) : null
                };
            }

            default:
                return null;
        }
    }

    private void RemoveVanishedEntries(
        string setDirectory,
        BackupPlan plan,
        Dictionary<string, BackupEntry> entries,
        BackupResult result)
    {
        var categories = plan.Items.Select(i => i.CategoryId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = 0;

        foreach (var key in entries.Keys.ToList())
        {
            var entry = entries[key];
            if (entry.Type is not (BackupEntryType.File or BackupEntryType.Apk))
            {
                continue;
            }

            if (!categories.Contains(entry.CategoryId) || plan.SeenRemotePaths.Contains(entry.RemotePath))
            {
                continue;
            }

            try
            {
                var local = PathMapper.ToLocalPath(setDirectory, entry.RelativePath);
                if (File.Exists(local))
                {
                    File.Delete(local);
                }

                entries.Remove(key);
                removed++;
            }
            catch (IOException ex)
            {
                result.Warnings.Add($"{entry.RelativePath} konnte nicht entfernt werden: {ex.Message}");
            }
        }

        if (removed > 0)
        {
            _log.Info($"{removed} auf dem Gerät gelöschte Dateien wurden auch aus der Sicherung entfernt.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private static double Speed(long bytesDone, TimeSpan elapsed) =>
        elapsed.TotalSeconds <= 0.5 ? 0 : bytesDone / elapsed.TotalSeconds;

    private static string CreateSetDirectory(string root, DeviceInfo device)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        var directory = Path.Combine(root, device.FolderName, timestamp);
        var suffix = 1;
        while (Directory.Exists(directory))
        {
            directory = Path.Combine(root, device.FolderName, $"{timestamp}_{suffix++}");
        }

        return directory;
    }

    /// <summary>
    /// Schreibt eine Schritt-für-Schritt-Anleitung für den Umzug auf ein neues Telefon
    /// direkt in den Sicherungssatz.
    /// </summary>
    private static void WriteMigrationGuide(string setDirectory, BackupManifest manifest)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Umzug auf ein neues Telefon");
        builder.AppendLine(new string('=', 40));
        builder.AppendLine();
        builder.AppendLine($"Quelle: {manifest.Device.DisplayName} ({manifest.Device.AndroidText})");
        builder.AppendLine($"Satz:   {Path.GetFileName(setDirectory.TrimEnd(Path.DirectorySeparatorChar))}");
        builder.AppendLine();
        builder.AppendLine("1. Neues Telefon einrichten, mit demselben Google-Konto anmelden.");
        builder.AppendLine("2. Entwickleroptionen und USB-Debugging am neuen Telefon aktivieren.");
        builder.AppendLine("3. In Pixel Backup auf 'Wiederherstellen' wechseln, diesen Satz wählen");
        builder.AppendLine("   und die gewünschten Gruppen zurückspielen.");
        builder.AppendLine();

        var hasImportFiles = manifest.Entries.Any(e => e.Type == BackupEntryType.ImportFile);
        if (hasImportFiles)
        {
            builder.AppendLine("Importdateien in diesem Satz (Ordner 'data'):");
            foreach (var entry in manifest.Entries.Where(e => e.Type == BackupEntryType.ImportFile))
            {
                builder.AppendLine("   - " + Path.GetFileName(entry.RelativePath));
            }

            builder.AppendLine();
            builder.AppendLine("   Beim Wiederherstellen legt Pixel Backup diese Dateien unter");
            builder.AppendLine("   /sdcard/PixelBackup-Import auf dem neuen Telefon ab. Danach:");
            builder.AppendLine("   - kontakte.vcf  ▸ Kontakte-App ▸ Einstellungen ▸ Importieren ▸ .vcf-Datei");
            builder.AppendLine("   - kalender.ics  ▸ Kalender-App bzw. calendar.google.com ▸ Importieren");
            builder.AppendLine("   - sms.xml / anrufliste.xml ▸ App 'SMS Backup & Restore' ▸ Wiederherstellen");
            builder.AppendLine();
        }

        if (manifest.Entries.Any(e => e.Type == BackupEntryType.RootAppData))
        {
            builder.AppendLine("App-Daten (Root): Die tar-Archive im Ordner 'appdata-root' lassen sich nur auf");
            builder.AppendLine("ein ebenfalls gerootetes Gerät zurückspielen. Reihenfolge: erst die App");
            builder.AppendLine("installieren, dann die Daten zurückspielen.");
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("Hinweis zu App-Daten: Ohne Root kann kein PC-Werkzeug die Daten installierter");
            builder.AppendLine("Apps auslesen – das verhindert Android seit Version 12 grundsätzlich.");
            builder.AppendLine("Für Spielstände, Chatverläufe und Einstellungen deshalb zusätzlich nutzen:");
            builder.AppendLine("   - die Android-Übertragung beim Ersteinrichten des neuen Telefons");
            builder.AppendLine("     (Kabel oder WLAN) – sie überträgt Apps samt Daten;");
            builder.AppendLine("   - die Sicherung der jeweiligen App (z. B. WhatsApp ▸ Google Drive).");
            builder.AppendLine();
        }

        builder.AppendLine("Die Dateien dieses Satzes liegen unverändert im Ordner 'files' und lassen sich");
        builder.AppendLine("auch ohne Pixel Backup weiterverwenden.");

        try
        {
            File.WriteAllText(Path.Combine(setDirectory, "umzug-anleitung.txt"), builder.ToString(), Encoding.UTF8);
        }
        catch (IOException)
        {
        }
    }

    private static void WriteReport(string setDirectory, BackupManifest manifest, BackupResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Pixel Backup – Sicherungsbericht");
        builder.AppendLine(new string('=', 40));
        builder.AppendLine($"Gerät:        {manifest.Device.DisplayName} ({manifest.Device.Serial})");
        builder.AppendLine($"Android:      {manifest.Device.AndroidText}");
        builder.AppendLine($"Erstellt:     {manifest.CreatedUtc.ToLocalTime():dd.MM.yyyy HH:mm}");
        builder.AppendLine($"Aktualisiert: {manifest.UpdatedUtc.ToLocalTime():dd.MM.yyyy HH:mm}");
        builder.AppendLine($"Kategorien:   {string.Join(", ", manifest.Categories.Select(CategoryCatalog.DisplayNameOf))}");
        builder.AppendLine($"Umfang:       {manifest.FileCount:N0} Elemente, {Humanize.Bytes(manifest.TotalBytes)}");
        builder.AppendLine($"Letzter Lauf: {result.SummaryText}");
        builder.AppendLine();

        foreach (var group in manifest.Entries.GroupBy(e => e.CategoryId))
        {
            builder.AppendLine(
                $"  {CategoryCatalog.DisplayNameOf(group.Key),-24} {group.Count(),8:N0} Elemente   {Humanize.Bytes(group.Sum(e => e.Size)),12}");
        }

        if (result.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Hinweise:");
            foreach (var warning in result.Warnings.Distinct().Take(200))
            {
                builder.AppendLine("  - " + warning);
            }
        }

        try
        {
            File.WriteAllText(Path.Combine(setDirectory, "bericht.txt"), builder.ToString(), Encoding.UTF8);
        }
        catch (IOException)
        {
            // Der Bericht ist nur eine Beigabe – Fehler hier dürfen die Sicherung nicht gefährden.
        }
    }
}
