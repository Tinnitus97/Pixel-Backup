using System.Globalization;
using System.Text;
using System.Text.Json;
using PixelBackup.Core.Adb;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;
using PixelBackup.Core.Services;
using PixelBackup.Core.Util;

namespace PixelBackup.App.Cli;

/// <summary>
/// Pixel Backup auf der Kommandozeile. Dieselben Dienste wie in der
/// Oberfläche, nur ohne Fenster – für Skripte, Aufgabenplanung, cron und
/// Rechner ohne grafische Sitzung.
///
/// Rückgabewerte: 0 in Ordnung, 1 Fehler, 2 falsche Aufrufform,
/// 3 kein Gerät, 4 adb fehlt, 130 abgebrochen.
/// </summary>
public sealed class CliRunner
{
    public const int ExitOk = 0;
    public const int ExitError = 1;
    public const int ExitUsage = 2;
    public const int ExitNoDevice = 3;
    public const int ExitNoAdb = 4;
    public const int ExitCanceled = 130;

    private readonly CliOptions _options;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly ILogSink _log;
    private readonly TextWriter _out;
    private readonly TextWriter _error;

    public CliRunner(CliOptions options, TextWriter? output = null, TextWriter? error = null)
    {
        _options = options;
        _out = output ?? Console.Out;
        _error = error ?? Console.Error;

        _store = new SettingsStore();
        _settings = _store.Load();

        // Auf der Kommandozeile schreibt das Protokoll in dieselbe Datei wie die
        // Oberfläche; Meldungen erscheinen zusätzlich auf dem Bildschirm.
        var file = new FileLogSink(Path.Combine(_store.Directory, "logs"));
        _log = _options.Quiet
            ? file
            : new CompositeLogSink(file, new DelegateLogSink(entry =>
            {
                if (entry.Level >= LogLevel.Warning)
                {
                    _error.WriteLine($"{entry.Level.ToString().ToUpperInvariant()}: {entry.Message}");
                }
            }));
    }

    /// <summary>Führt den Befehl aus.</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        ApplyLanguage();

        if (!_options.IsValid)
        {
            _error.WriteLine(_options.Error);
            _error.WriteLine(CliHelp.Usage);
            return ExitUsage;
        }

        try
        {
            return _options.Command switch
            {
                CliCommand.Help => Print(CliHelp.Text),
                CliCommand.Version => PrintVersion(),
                CliCommand.Manual => Print(CliHelp.Manual),
                CliCommand.Categories => PrintCategories(),
                CliCommand.List => ListSets(),
                CliCommand.Verify => await VerifyAsync(ct).ConfigureAwait(false),
                CliCommand.Components => await ComponentsAsync(ct).ConfigureAwait(false),
                CliCommand.Update => await UpdateAsync(ct).ConfigureAwait(false),
                CliCommand.Devices => await DevicesAsync(ct).ConfigureAwait(false),
                CliCommand.Info => await InfoAsync(ct).ConfigureAwait(false),
                CliCommand.Backup => await BackupAsync(ct).ConfigureAwait(false),
                CliCommand.Restore => await RestoreAsync(ct).ConfigureAwait(false),
                _ => Print(CliHelp.Text)
            };
        }
        catch (OperationCanceledException)
        {
            _error.WriteLine(Loc.Tr("Abgebrochen.", "Cancelled."));
            return ExitCanceled;
        }
        catch (Exception ex)
        {
            _error.WriteLine(Loc.Tr($"Fehler: {ex.Message}", $"Error: {ex.Message}"));
            _log.Error(Loc.Tr("Befehl fehlgeschlagen", "Command failed"), ex);
            return ExitError;
        }
    }

    // --------------------------------------------------------------- Sprache

    private void ApplyLanguage()
    {
        Localizer.I.Lang = _options.Language switch
        {
            "de" => AppLanguage.De,
            "en" => AppLanguage.En,
            _ => _settings.Language switch
            {
                LanguageSetting.German => AppLanguage.De,
                LanguageSetting.English => AppLanguage.En,
                _ => Localizer.DetectFromSystem()
            }
        };

        Localizer.I.ApplyCulture();
    }

    // --------------------------------------------------------------- Ausgabe

    private int Print(string text)
    {
        _out.WriteLine(text);
        return ExitOk;
    }

    private void Line(string text)
    {
        if (!_options.Quiet)
        {
            _out.WriteLine(text);
        }
    }

    private int Json(object value)
    {
        _out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
        return ExitOk;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private int PrintVersion()
    {
        if (_options.Json)
        {
            return Json(new
            {
                version = InstallationInfo.CurrentVersion,
                installation = UpdateService.KindKey(InstallationInfo.Kind),
                arch = InstallationInfo.Arch
            });
        }

        _out.WriteLine($"Pixel Backup {InstallationInfo.CurrentVersion}");
        _out.WriteLine(InstallationInfo.Describe());
        return ExitOk;
    }

    // ------------------------------------------------------------- Kategorien

    private int PrintCategories()
    {
        if (_options.Json)
        {
            return Json(CategoryCatalog.All.Select(c => new
            {
                id = c.Id,
                name = c.DisplayName,
                recommended = c.SelectedByDefault,
                description = c.Description
            }));
        }

        _out.WriteLine(Loc.Tr("Gruppen (Kennung – Name):", "Groups (id – name):"));
        foreach (var category in CategoryCatalog.All)
        {
            var mark = category.SelectedByDefault ? "*" : " ";
            _out.WriteLine($" {mark} {category.Id,-16} {category.DisplayName}");
        }

        _out.WriteLine();
        _out.WriteLine(Loc.Tr(
            "* = mit --all oder ohne Angabe von --categories vorausgewählt",
            "* = preselected with --all or without --categories"));
        return ExitOk;
    }

    // ------------------------------------------------------------ Sicherungen

    private BackupRepository Repository() => new(_options.Output ?? _settings.BackupRoot, _log);

    private int ListSets()
    {
        var repository = Repository();
        var sets = repository.LoadSets();

        if (_options.Json)
        {
            return Json(sets.Select(s => new
            {
                name = s.Name,
                directory = s.Directory,
                device = s.DeviceName,
                created = s.Created,
                updated = s.Updated,
                bytes = s.TotalBytes,
                files = s.Manifest.FileCount,
                categories = s.Manifest.Categories
            }));
        }

        if (sets.Count == 0)
        {
            _out.WriteLine(Loc.Tr(
                $"Keine Sicherung in {repository.Root}.",
                $"No backup in {repository.Root}."));
            return ExitOk;
        }

        _out.WriteLine(Loc.Tr($"Sicherungen in {repository.Root}:", $"Backups in {repository.Root}:"));
        foreach (var set in sets)
        {
            _out.WriteLine($"  {set.Name,-34} {set.DeviceName,-22} {set.UpdatedText,-17} {set.SizeText,10}");
        }

        return ExitOk;
    }

    /// <summary>Sucht den Satz für --set; ohne Angabe den jüngsten.</summary>
    private BackupSet? FindSet(BackupRepository repository)
    {
        var sets = repository.LoadSets();

        if (string.IsNullOrWhiteSpace(_options.Set))
        {
            return sets.Count > 0 ? sets[0] : null;
        }

        var wanted = _options.Set!;

        // Ein vollständiger Pfad darf auch außerhalb der Ablage liegen.
        if (Directory.Exists(wanted))
        {
            var manifest = BackupManifest.TryLoad(wanted);
            return manifest is null ? null : new BackupSet(wanted, manifest);
        }

        return sets.FirstOrDefault(s =>
            string.Equals(s.Name, wanted, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<int> VerifyAsync(CancellationToken ct)
    {
        var repository = Repository();
        var set = FindSet(repository);

        if (set is null)
        {
            _error.WriteLine(Loc.Tr("Kein Sicherungssatz gefunden.", "No backup set found."));
            return ExitError;
        }

        Line(Loc.Tr($"Prüfe {set.Name} …", $"Verifying {set.Name} …"));

        var service = new VerificationService(_log);
        var result = await service.VerifyAsync(set, ProgressReporter(), ct).ConfigureAwait(false);

        if (_options.Json)
        {
            return Json(new
            {
                set = set.Name,
                healthy = result.IsHealthy,
                @checked = result.Checked,
                ok = result.Ok,
                missing = result.Missing,
                sizeMismatch = result.SizeMismatch,
                hashMismatch = result.HashMismatch,
                withoutHash = result.WithoutHash
            });
        }

        _out.WriteLine(result.SummaryText);
        foreach (var missing in result.Missing.Take(20))
        {
            _out.WriteLine(Loc.Tr($"  fehlt: {missing}", $"  missing: {missing}"));
        }

        return result.IsHealthy ? ExitOk : ExitError;
    }

    // ---------------------------------------------------------------- Geräte

    /// <summary>Startet adb; null bedeutet: adb fehlt.</summary>
    private async Task<AdbClient?> ConnectAsync(CancellationToken ct)
    {
        var path = AdbLocator.Locate(_settings.AdbPath);
        if (path is null)
        {
            _error.WriteLine(Loc.Tr(
                "adb wurde nicht gefunden. Einrichten mit: pixel-backup components --install",
                "adb was not found. Install it with: pixel-backup components --install"));
            return null;
        }

        var adb = new AdbClient(path, _log);
        await adb.StartServerAsync(ct).ConfigureAwait(false);
        return adb;
    }

    /// <summary>Wählt das Gerät: --device, sonst das einzige verbundene.</summary>
    private async Task<(AdbClient Adb, string Serial)?> RequireDeviceAsync(CancellationToken ct)
    {
        var adb = await ConnectAsync(ct).ConfigureAwait(false);
        if (adb is null)
        {
            return null;
        }

        var devices = await adb.ListDevicesAsync(ct).ConfigureAwait(false);
        var usable = devices.Where(d => d.State == AdbDeviceState.Device).ToList();

        if (!string.IsNullOrWhiteSpace(_options.Device))
        {
            var match = usable.FirstOrDefault(d =>
                string.Equals(d.Serial, _options.Device, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                _error.WriteLine(Loc.Tr(
                    $"Gerät {_options.Device} ist nicht verbunden oder nicht freigegeben.",
                    $"Device {_options.Device} is not connected or not authorised."));
                return null;
            }

            return (adb, match.Serial);
        }

        switch (usable.Count)
        {
            case 0:
                var blocked = devices.FirstOrDefault(d => d.State != AdbDeviceState.Device);
                _error.WriteLine(blocked is null
                    ? Loc.Tr("Kein Gerät verbunden.", "No device connected.")
                    : Loc.Tr($"Gerät {blocked.Serial}: {blocked.StateText}", $"Device {blocked.Serial}: {blocked.StateText}"));
                return null;

            case 1:
                return (adb, usable[0].Serial);

            default:
                _error.WriteLine(Loc.Tr(
                    "Mehrere Geräte verbunden – bitte mit --device auswählen:",
                    "Several devices connected – please choose with --device:"));
                foreach (var device in usable)
                {
                    _error.WriteLine($"  {device.Serial}  {device.DisplayName}");
                }

                return null;
        }
    }

    private async Task<int> DevicesAsync(CancellationToken ct)
    {
        var adb = await ConnectAsync(ct).ConfigureAwait(false);
        if (adb is null)
        {
            return ExitNoAdb;
        }

        var devices = await adb.ListDevicesAsync(ct).ConfigureAwait(false);

        if (_options.Json)
        {
            return Json(devices.Select(d => new
            {
                serial = d.Serial,
                state = d.State.ToString().ToLowerInvariant(),
                model = d.Model,
                name = d.DisplayName
            }));
        }

        if (devices.Count == 0)
        {
            _out.WriteLine(Loc.Tr("Kein Gerät verbunden.", "No device connected."));
            return ExitNoDevice;
        }

        foreach (var device in devices)
        {
            _out.WriteLine($"{device.Serial,-24} {device.StateText,-22} {device.DisplayName}");
        }

        return ExitOk;
    }

    private async Task<int> InfoAsync(CancellationToken ct)
    {
        var selected = await RequireDeviceAsync(ct).ConfigureAwait(false);
        if (selected is null)
        {
            return ExitNoDevice;
        }

        var (adb, serial) = selected.Value;
        var info = await adb.GetDeviceInfoAsync(serial, ct).ConfigureAwait(false);

        if (_options.Json)
        {
            return Json(new
            {
                serial = info.Serial,
                manufacturer = info.Manufacturer,
                model = info.Model,
                android = info.AndroidVersion,
                sdk = info.SdkNumber,
                battery = info.BatteryLevel,
                storageFree = info.StorageFreeBytes,
                storageTotal = info.StorageTotalBytes,
                root = info.RootAccess.ToString().ToLowerInvariant()
            });
        }

        _out.WriteLine(info.DisplayName);
        _out.WriteLine($"  {Loc.Tr("Seriennummer", "Serial")}: {info.Serial}");
        _out.WriteLine($"  Android: {info.AndroidText}");
        _out.WriteLine($"  {Loc.Tr("Akku", "Battery")}: {info.BatteryText}");
        _out.WriteLine($"  {Loc.Tr("Speicher", "Storage")}: {info.StorageText}");
        _out.WriteLine($"  Root: {info.RootText}");
        return ExitOk;
    }

    // -------------------------------------------------------------- Sichern

    private IProgress<OperationProgress>? ProgressReporter()
    {
        if (_options.Quiet || _options.Json)
        {
            return null;
        }

        var lastPercent = -1;

        return new Progress<OperationProgress>(p =>
        {
            var percent = (int)p.Percent;
            if (percent == lastPercent)
            {
                return;
            }

            lastPercent = percent;
            _out.WriteLine($"[{percent,3} %] {p.CurrentItem}");
        });
    }

    private IReadOnlyList<BackupCategory> ChosenCategories()
    {
        if (_options.AllCategories)
        {
            return CategoryCatalog.All.ToList();
        }

        if (_options.Categories.Count > 0)
        {
            var wanted = new HashSet<string>(_options.Categories, StringComparer.OrdinalIgnoreCase);
            return CategoryCatalog.All.Where(c => wanted.Contains(c.Id)).ToList();
        }

        return CategoryCatalog.All.Where(c => c.SelectedByDefault).ToList();
    }

    private async Task<int> BackupAsync(CancellationToken ct)
    {
        var unknown = _options.Categories
            .Where(id => CategoryCatalog.All.All(c => !string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (unknown.Count > 0)
        {
            _error.WriteLine(Loc.Tr(
                $"Unbekannte Gruppe(n): {string.Join(", ", unknown)}. Bekannte zeigt: pixel-backup categories",
                $"Unknown group(s): {string.Join(", ", unknown)}. See: pixel-backup categories"));
            return ExitUsage;
        }

        var selected = await RequireDeviceAsync(ct).ConfigureAwait(false);
        if (selected is null)
        {
            return ExitNoDevice;
        }

        var (adb, serial) = selected.Value;
        var categories = ChosenCategories();
        var info = await adb.GetDeviceInfoAsync(serial, ct).ConfigureAwait(false);

        var repository = Repository();
        var previous = _options.Incremental ? repository.LatestFor(serial) : null;

        Line(Loc.Tr(
            $"Gerät {info.DisplayName} – {categories.Count} Gruppe(n) werden geprüft …",
            $"Device {info.DisplayName} – checking {categories.Count} group(s) …"));

        var service = new BackupService(adb, _log);
        var status = _options.Quiet || _options.Json
            ? null
            : new Progress<string>(text => _out.WriteLine(text));

        var plan = await service
            .CreatePlanAsync(serial, info, categories, previous, status, ct)
            .ConfigureAwait(false);

        var plannedBytes = plan.Items.Sum(i => Math.Max(0, i.Size));

        if (_options.DryRun)
        {
            if (_options.Json)
            {
                return Json(new
                {
                    device = info.DisplayName,
                    items = plan.Items.Count,
                    bytes = plannedBytes,
                    categories = categories.Select(c => c.Id),
                    warnings = plan.Warnings
                });
            }

            _out.WriteLine(Loc.Tr(
                $"Geplant: {plan.Items.Count} Einträge, {Humanize.Bytes(plannedBytes)}.",
                $"Planned: {plan.Items.Count} entries, {Humanize.Bytes(plannedBytes)}."));

            foreach (var warning in plan.Warnings)
            {
                _out.WriteLine("  ! " + warning);
            }

            return ExitOk;
        }

        if (plan.Items.Count == 0)
        {
            _out.WriteLine(Loc.Tr("Es gibt nichts zu sichern.", "There is nothing to back up."));
            return ExitOk;
        }

        if (_options.Archive && string.IsNullOrEmpty(_options.Password) && !_options.AssumeYes)
        {
            _error.WriteLine(Loc.Tr(
                "--archive ohne --password legt ein unverschlüsseltes Archiv an. Mit --yes bestätigen.",
                "--archive without --password creates an unencrypted archive. Confirm with --yes."));
            return ExitUsage;
        }

        var options = new BackupOptions
        {
            BackupRoot = repository.Root,
            Incremental = _options.Incremental && previous is not null,
            TargetSet = previous,
            ComputeHashes = !_options.NoHashes,
            CreateArchive = _options.Archive,
            ArchivePassword = string.IsNullOrEmpty(_options.Password) ? null : _options.Password,
            RemoveDeletedFiles = false,
            Notes = Loc.Tr("Über die Kommandozeile erstellt.", "Created from the command line.")
        };

        var result = await service
            .RunAsync(serial, plan, options, ProgressReporter(), ct)
            .ConfigureAwait(false);

        if (_options.Json)
        {
            Json(new
            {
                set = result.SetDirectory,
                copied = result.FilesCopied,
                skipped = result.FilesSkipped,
                failed = result.FilesFailed,
                bytes = result.BytesCopied,
                canceled = result.Canceled,
                seconds = (int)result.Duration.TotalSeconds,
                archive = result.ArchivePath,
                warnings = result.Warnings
            });
        }
        else
        {
            _out.WriteLine(result.SummaryText);
            _out.WriteLine(Loc.Tr($"Satz: {result.SetDirectory}", $"Set: {result.SetDirectory}"));
        }

        if (result.Canceled)
        {
            return ExitCanceled;
        }

        return result.FilesFailed > 0 ? ExitError : ExitOk;
    }

    // ------------------------------------------------------ Wiederherstellen

    private async Task<int> RestoreAsync(CancellationToken ct)
    {
        var repository = Repository();
        var set = FindSet(repository);

        if (set is null)
        {
            _error.WriteLine(Loc.Tr(
                $"Kein Sicherungssatz gefunden (Ordner: {repository.Root}).",
                $"No backup set found (folder: {repository.Root})."));
            return ExitError;
        }

        var categoryIds = _options.AllCategories || _options.Categories.Count == 0
            ? set.Manifest.Categories.ToList()
            : _options.Categories.ToList();

        if (_options.DryRun)
        {
            var entries = set.EntriesOf(categoryIds).ToList();
            if (_options.Json)
            {
                return Json(new
                {
                    set = set.Name,
                    directory = set.Directory,
                    entries = entries.Count,
                    bytes = entries.Sum(e => Math.Max(0, e.Size)),
                    categories = categoryIds
                });
            }

            _out.WriteLine(Loc.Tr(
                $"Würde {entries.Count} Einträge aus „{set.Name}“ zurückspielen ({Humanize.Bytes(entries.Sum(e => Math.Max(0, e.Size)))}).",
                $"Would restore {entries.Count} entries from \"{set.Name}\" ({Humanize.Bytes(entries.Sum(e => Math.Max(0, e.Size)))})."));
            return ExitOk;
        }

        var selected = await RequireDeviceAsync(ct).ConfigureAwait(false);
        if (selected is null)
        {
            return ExitNoDevice;
        }

        var (adb, serial) = selected.Value;

        if (!_options.AssumeYes)
        {
            _error.WriteLine(Loc.Tr(
                "Das Zurückspielen verändert Daten auf dem Gerät. Bitte mit --yes bestätigen (oder --dry-run verwenden).",
                "Restoring changes data on the device. Please confirm with --yes (or use --dry-run)."));
            return ExitUsage;
        }

        Line(Loc.Tr($"Spiele „{set.Name}“ zurück …", $"Restoring \"{set.Name}\" …"));

        var options = new RestoreOptions
        {
            Set = set,
            CategoryIds = categoryIds,
            ConflictMode = _options.Conflict switch
            {
                "overwrite" => ConflictMode.Overwrite,
                "both" => ConflictMode.KeepBoth,
                _ => ConflictMode.Skip
            }
        };

        var service = new RestoreService(adb, _log);
        var result = await service
            .RunAsync(serial, options, ProgressReporter(), ct)
            .ConfigureAwait(false);

        if (_options.Json)
        {
            Json(new
            {
                set = set.Name,
                restored = result.FilesRestored,
                skipped = result.FilesSkipped,
                failed = result.FilesFailed,
                appsInstalled = result.AppsInstalled,
                bytes = result.BytesRestored,
                canceled = result.Canceled,
                warnings = result.Warnings
            });
        }
        else
        {
            _out.WriteLine(result.SummaryText);
            foreach (var warning in result.Warnings)
            {
                _out.WriteLine("  ! " + warning);
            }
        }

        if (result.Canceled)
        {
            return ExitCanceled;
        }

        return result.FilesFailed > 0 || result.AppsFailed > 0 ? ExitError : ExitOk;
    }

    // ----------------------------------------------------------- Komponenten

    private async Task<int> ComponentsAsync(CancellationToken ct)
    {
        var installer = new PlatformToolsInstaller(_log);
        var (adbStatus, package) = await installer
            .CheckAsync(_settings.AdbPath, checkOnline: true, ct: ct)
            .ConfigureAwait(false);

        ComponentStatus access;
        if (OperatingSystem.IsLinux())
        {
            access = await new LinuxDeviceAccessService(_log).CheckAsync(false, ct).ConfigureAwait(false);
        }
        else if (UsbDriverService.IsWindows)
        {
            (access, _) = await new UsbDriverService(_log).CheckAsync(checkOnline: true, ct: ct).ConfigureAwait(false);
        }
        else
        {
            access = new ComponentStatus
            {
                Name = Loc.Tr("Gerätezugriff", "Device access"),
                State = ComponentState.NotRequired
            };
        }

        // --install richtet fehlende Plattform-Tools gleich ein.
        if (_options.Install && !adbStatus.IsHealthy)
        {
            if (package is null)
            {
                _error.WriteLine(Loc.Tr(
                    "Die Paketliste von Google ist nicht erreichbar.",
                    "Google's package list is unreachable."));
                return ExitError;
            }

            Line(Loc.Tr("Plattform-Tools werden eingerichtet …", "Installing platform tools …"));
            var path = await installer.InstallAsync(package, null, ct).ConfigureAwait(false);

            _settings.AdbPath = path;
            _store.Save(_settings);

            (adbStatus, _) = await installer.CheckAsync(path, checkOnline: false, ct: ct).ConfigureAwait(false);
        }

        if (_options.Json)
        {
            return Json(new
            {
                adb = new { state = adbStatus.State.ToString().ToLowerInvariant(), version = adbStatus.InstalledVersion, location = adbStatus.Location },
                deviceAccess = new { name = access.Name, state = access.State.ToString().ToLowerInvariant() },
                system = OperatingSystem.IsLinux() ? LinuxEnvironment.Describe() : Environment.OSVersion.VersionString
            });
        }

        _out.WriteLine($"{adbStatus.Name}: {adbStatus.StateText}");
        _out.WriteLine($"  {adbStatus.Message}");
        _out.WriteLine($"{access.Name}: {access.StateText}");
        _out.WriteLine($"  {access.Message}");

        if (OperatingSystem.IsLinux())
        {
            _out.WriteLine($"System: {LinuxEnvironment.Describe()}");
        }

        return adbStatus.IsHealthy ? ExitOk : ExitNoAdb;
    }

    // ------------------------------------------------------------- Update

    private async Task<int> UpdateAsync(CancellationToken ct)
    {
        var updater = new UpdateService(_log);
        var check = await updater.CheckAsync(_settings.UpdateManifestUrl, ct).ConfigureAwait(false);

        if (_options.Json)
        {
            Json(new
            {
                local = check.LocalVersion,
                online = check.OnlineVersion,
                available = check.UpdateAvailable,
                installation = UpdateService.KindKey(check.Kind),
                file = check.Package?.Url,
                error = check.Error
            });
        }
        else
        {
            _out.WriteLine(check.Describe());
        }

        if (check.Error is not null)
        {
            return ExitError;
        }

        if (!_options.Install || !check.UpdateAvailable)
        {
            return ExitOk;
        }

        if (check.Package is null)
        {
            _error.WriteLine(Loc.Tr(
                $"Für diese Einbauart gibt es keine Datei: {UpdateService.ReleasesPageUrl}",
                $"There is no file for this installation kind: {UpdateService.ReleasesPageUrl}"));
            return ExitError;
        }

        if (!_options.AssumeYes)
        {
            _error.WriteLine(Loc.Tr(
                "Zum Einspielen bitte --yes angeben.",
                "Please add --yes to install."));
            return ExitUsage;
        }

        Line(Loc.Tr($"Lade Fassung {check.OnlineVersion} …", $"Downloading version {check.OnlineVersion} …"));

        var progress = _options.Quiet ? null : new Progress<DownloadProgress>(p =>
        {
            if (p.BytesTotal > 0 && (int)p.Percent % 10 == 0)
            {
                _out.WriteLine($"[{p.Percent,3:F0} %] {p.Phase}");
            }
        });

        var download = await updater.DownloadAsync(check.Package, progress, ct).ConfigureAwait(false);
        if (!download.Success)
        {
            _error.WriteLine(download.Error ?? Loc.Tr("Der Download ist fehlgeschlagen.", "The download failed."));
            return ExitError;
        }

        var applied = await new UpdateInstaller(_log)
            .ApplyAsync(check.Package, download.File, ct)
            .ConfigureAwait(false);

        _out.WriteLine(applied.Message);
        return applied.Success ? ExitOk : ExitError;
    }
}
