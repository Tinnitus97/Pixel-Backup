using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Model;

namespace PixelBackup.Core.Adb;

/// <summary>
/// Dünne, aufgabenorientierte Hülle um das Kommandozeilenwerkzeug <c>adb</c>.
/// Sämtliche Gerätekommunikation der Anwendung läuft über diese Klasse.
/// </summary>
public sealed class AdbClient
{
    private static readonly Regex PercentPattern = new(@"\[\s*(\d{1,3})%\]", RegexOptions.Compiled);

    private readonly ILogSink _log;

    public AdbClient(string adbPath, ILogSink? log = null)
    {
        AdbPath = adbPath;
        _log = log ?? NullLogSink.Instance;
    }

    public string AdbPath { get; }

    /// <summary>Maskiert einen Wert für die Verwendung in einer Android-Shell.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    public async Task<ProcessResult> ExecAsync(
        IReadOnlyList<string> arguments,
        CancellationToken ct = default,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null)
    {
        _log.Debug("adb " + string.Join(" ", arguments));
        var result = await ProcessRunner.RunAsync(AdbPath, arguments, ct, onOutputLine, onErrorLine).ConfigureAwait(false);
        if (!result.Success)
        {
            _log.Debug($"adb endete mit Code {result.ExitCode}: {result.ErrorSummary}");
        }

        return result;
    }

    private static List<string> ForDevice(string serial, params string[] rest)
    {
        var arguments = new List<string>(rest.Length + 2);
        if (!string.IsNullOrEmpty(serial))
        {
            arguments.Add("-s");
            arguments.Add(serial);
        }

        arguments.AddRange(rest);
        return arguments;
    }

    // ---------------------------------------------------------------- Server

    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var result = await ExecAsync(new[] { "version" }, ct).ConfigureAwait(false);
        return result.OutputLines.FirstOrDefault()?.Trim() ?? "unbekannt";
    }

    public Task<ProcessResult> StartServerAsync(CancellationToken ct = default) =>
        ExecAsync(new[] { "start-server" }, ct);

    public Task<ProcessResult> KillServerAsync(CancellationToken ct = default) =>
        ExecAsync(new[] { "kill-server" }, ct);

    // ---------------------------------------------------------------- Geräte

    public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken ct = default)
    {
        var result = await ExecAsync(new[] { "devices", "-l" }, ct).ConfigureAwait(false);
        var devices = new List<AdbDevice>();

        foreach (var line in result.OutputLines)
        {
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("*", StringComparison.Ordinal) ||
                line.StartsWith("adb server", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts.Skip(2))
            {
                var separator = part.IndexOf(':');
                if (separator > 0)
                {
                    properties[part[..separator]] = part[(separator + 1)..];
                }
            }

            devices.Add(new AdbDevice
            {
                Serial = parts[0],
                State = ParseState(parts[1]),
                Model = properties.GetValueOrDefault("model", string.Empty),
                Product = properties.GetValueOrDefault("product", string.Empty),
                DeviceName = properties.GetValueOrDefault("device", string.Empty),
                TransportId = properties.GetValueOrDefault("transport_id", string.Empty)
            });
        }

        return devices;
    }

    private static AdbDeviceState ParseState(string value) => value.ToLowerInvariant() switch
    {
        "device" => AdbDeviceState.Device,
        "unauthorized" => AdbDeviceState.Unauthorized,
        "offline" => AdbDeviceState.Offline,
        "recovery" => AdbDeviceState.Recovery,
        "sideload" => AdbDeviceState.Sideload,
        "bootloader" or "fastboot" => AdbDeviceState.Bootloader,
        _ => AdbDeviceState.Unknown
    };

    public Task<ProcessResult> ConnectAsync(string hostAndPort, CancellationToken ct = default) =>
        ExecAsync(new[] { "connect", hostAndPort }, ct);

    public Task<ProcessResult> DisconnectAsync(string hostAndPort, CancellationToken ct = default) =>
        ExecAsync(new[] { "disconnect", hostAndPort }, ct);

    public Task<ProcessResult> EnableTcpIpAsync(string serial, int port, CancellationToken ct = default) =>
        ExecAsync(ForDevice(serial, "tcpip", port.ToString(CultureInfo.InvariantCulture)), ct);

    public async Task<string?> GetWlanAddressAsync(string serial, CancellationToken ct = default)
    {
        var output = await ShellTextAsync(serial, "ip -f inet addr show wlan0", ct).ConfigureAwait(false);
        var match = Regex.Match(output, @"inet\s+(\d{1,3}(?:\.\d{1,3}){3})");
        return match.Success ? match.Groups[1].Value : null;
    }

    // ----------------------------------------------------------------- Shell

    public Task<ProcessResult> ShellAsync(string serial, string command, CancellationToken ct = default) =>
        ExecAsync(ForDevice(serial, "shell", command), ct);

    public async Task<string> ShellTextAsync(string serial, string command, CancellationToken ct = default)
    {
        var result = await ShellAsync(serial, command, ct).ConfigureAwait(false);
        return result.StandardOutput;
    }

    public async Task<DeviceInfo> GetDeviceInfoAsync(string serial, CancellationToken ct = default)
    {
        var properties = new[]
        {
            "ro.product.model",
            "ro.product.manufacturer",
            "ro.build.version.release",
            "ro.build.version.sdk",
            "ro.product.name",
            "ro.serialno",
            "ro.build.id",
            "ro.build.version.security_patch"
        };

        var command = string.Join("; ", properties.Select(p => $"getprop {p}"));
        var lines = (await ShellTextAsync(serial, command, ct).ConfigureAwait(false))
            .Split('\n')
            .Select(l => l.Trim())
            .ToArray();

        string At(int index) => index < lines.Length ? lines[index] : string.Empty;

        var info = new DeviceInfo
        {
            Serial = serial,
            Model = At(0),
            Manufacturer = At(1),
            AndroidVersion = At(2),
            SdkLevel = At(3),
            ProductName = At(4),
            HardwareSerial = At(5),
            BuildId = At(6),
            SecurityPatch = At(7)
        };

        await FillStorageAsync(serial, info, ct).ConfigureAwait(false);
        await FillBatteryAsync(serial, info, ct).ConfigureAwait(false);
        info.RootAccess = await DetectRootAsync(serial, ct).ConfigureAwait(false);
        return info;
    }

    private async Task FillStorageAsync(string serial, DeviceInfo info, CancellationToken ct)
    {
        var output = await ShellTextAsync(serial, "df -k /sdcard", ct).ConfigureAwait(false);
        foreach (var line in output.Split('\n').Skip(1))
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                continue;
            }

            if (long.TryParse(parts[1], out var total) &&
                long.TryParse(parts[2], out var used) &&
                long.TryParse(parts[3], out var available))
            {
                info.StorageTotalBytes = total * 1024L;
                info.StorageUsedBytes = used * 1024L;
                info.StorageFreeBytes = available * 1024L;
                return;
            }
        }
    }

    private async Task FillBatteryAsync(string serial, DeviceInfo info, CancellationToken ct)
    {
        var output = await ShellTextAsync(serial, "dumpsys battery", ct).ConfigureAwait(false);
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("level:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(trimmed[6..].Trim(), out var level))
            {
                info.BatteryLevel = level;
            }
            else if (trimmed.StartsWith("AC powered: true", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("USB powered: true", StringComparison.OrdinalIgnoreCase))
            {
                info.IsCharging = true;
            }
        }
    }

    // ------------------------------------------------------------- Dateisystem

    /// <summary>Listet alle Dateien unterhalb eines Verzeichnisses samt Größe und Zeitstempel.</summary>
    public async Task<IReadOnlyList<RemoteFile>> ListFilesAsync(
        string serial,
        string remoteDirectory,
        bool pruneAndroidFolder = false,
        CancellationToken ct = default)
    {
        var prune = pruneAndroidFolder ? "-name Android -prune -o " : string.Empty;
        var command = $"find {Quote(remoteDirectory)} {prune}-type f -exec stat -c '%s|%Y|%n' {{}} + 2>/dev/null";
        var result = await ShellAsync(serial, command, ct).ConfigureAwait(false);

        var files = ParseStatLines(result.StandardOutput);
        if (files.Count > 0)
        {
            return files;
        }

        // Rückfallebene für Geräte, deren Shell kein "stat" oder kein "find -exec … +" kennt:
        // nur die Pfade einsammeln, die Größe wird dann beim Kopieren ermittelt.
        var fallback = await ShellAsync(serial, $"find {Quote(remoteDirectory)} {prune}-type f 2>/dev/null", ct)
            .ConfigureAwait(false);
        var paths = fallback.OutputLines
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("/", StringComparison.Ordinal))
            .Select(line => new RemoteFile(line, -1, 0))
            .ToList();

        if (paths.Count > 0)
        {
            _log.Warn($"Für {remoteDirectory} konnten keine Dateigrößen ermittelt werden – die Fortschrittsanzeige ist dort ungenau.");
        }

        return paths;
    }

    public static List<RemoteFile> ParseStatLines(string output)
    {
        var files = new List<RemoteFile>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var first = line.IndexOf('|');
            if (first <= 0)
            {
                continue;
            }

            var second = line.IndexOf('|', first + 1);
            if (second < 0)
            {
                continue;
            }

            if (!long.TryParse(line[..first], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
            {
                continue;
            }

            var modifiedText = line[(first + 1)..second];
            var dot = modifiedText.IndexOf('.');
            if (dot > 0)
            {
                modifiedText = modifiedText[..dot];
            }

            long.TryParse(modifiedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var modified);

            var path = line[(second + 1)..].Trim();
            if (path.Length == 0)
            {
                continue;
            }

            files.Add(new RemoteFile(path, size, modified));
        }

        return files;
    }

    /// <summary>Ermittelt Größe und Zeitstempel einzelner Dateien (z. B. APK-Pfade).</summary>
    public async Task<IReadOnlyList<RemoteFile>> StatAsync(
        string serial,
        IEnumerable<string> remotePaths,
        CancellationToken ct = default)
    {
        var files = new List<RemoteFile>();
        foreach (var chunk in Chunk(remotePaths, 60))
        {
            ct.ThrowIfCancellationRequested();
            var command = "stat -c '%s|%Y|%n' " + string.Join(" ", chunk.Select(Quote)) + " 2>/dev/null";
            var result = await ShellAsync(serial, command, ct).ConfigureAwait(false);
            files.AddRange(ParseStatLines(result.StandardOutput));
        }

        return files;
    }

    /// <summary>Liefert jene Pfade zurück, die auf dem Gerät bereits existieren.</summary>
    public async Task<HashSet<string>> FilterExistingAsync(
        string serial,
        IEnumerable<string> remotePaths,
        CancellationToken ct = default)
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in Chunk(remotePaths, 80))
        {
            ct.ThrowIfCancellationRequested();
            var builder = new StringBuilder("for f in ");
            builder.Append(string.Join(" ", chunk.Select(Quote)));
            builder.Append("; do [ -e \"$f\" ] && echo \"$f\"; done");
            var result = await ShellAsync(serial, builder.ToString(), ct).ConfigureAwait(false);
            foreach (var line in result.OutputLines)
            {
                existing.Add(line.Trim());
            }
        }

        return existing;
    }

    public Task<ProcessResult> MakeDirectoryAsync(string serial, string remoteDirectory, CancellationToken ct = default) =>
        ShellAsync(serial, $"mkdir -p {Quote(remoteDirectory)}", ct);

    public async Task<bool> DirectoryExistsAsync(string serial, string remoteDirectory, CancellationToken ct = default)
    {
        var result = await ShellAsync(serial, $"[ -d {Quote(remoteDirectory)} ] && echo ja || echo nein", ct)
            .ConfigureAwait(false);
        return result.StandardOutput.Contains("ja", StringComparison.Ordinal);
    }

    /// <summary>Kopiert eine Datei vom Gerät auf den PC.</summary>
    public async Task<ProcessResult> PullAsync(
        string serial,
        string remotePath,
        string localPath,
        CancellationToken ct = default,
        Action<int>? onPercent = null)
    {
        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        void HandleLine(string line)
        {
            if (onPercent is null)
            {
                return;
            }

            var match = PercentPattern.Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
            {
                onPercent(Math.Clamp(percent, 0, 100));
            }
        }

        return await ExecAsync(ForDevice(serial, "pull", "-a", remotePath, localPath), ct, HandleLine, HandleLine)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Holt mehrere Dateien aus demselben Geräteordner in einem einzigen adb-Aufruf.
    /// Das ist bei vielen kleinen Dateien um ein Vielfaches schneller als Einzelaufrufe.
    /// </summary>
    public async Task<ProcessResult> PullManyAsync(
        string serial,
        IReadOnlyList<string> remotePaths,
        string localDirectory,
        CancellationToken ct = default,
        Action<int>? onPercent = null)
    {
        Directory.CreateDirectory(localDirectory);

        var arguments = ForDevice(serial, "pull", "-a");
        arguments.AddRange(remotePaths);
        arguments.Add(localDirectory);

        void HandleLine(string line)
        {
            if (onPercent is null)
            {
                return;
            }

            var match = PercentPattern.Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
            {
                onPercent(Math.Clamp(percent, 0, 100));
            }
        }

        return await ExecAsync(arguments, ct, HandleLine, HandleLine).ConfigureAwait(false);
    }

    /// <summary>Kopiert eine Datei vom PC auf das Gerät.</summary>
    public async Task<ProcessResult> PushAsync(
        string serial,
        string localPath,
        string remotePath,
        CancellationToken ct = default,
        Action<int>? onPercent = null)
    {
        void HandleLine(string line)
        {
            if (onPercent is null)
            {
                return;
            }

            var match = PercentPattern.Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
            {
                onPercent(Math.Clamp(percent, 0, 100));
            }
        }

        return await ExecAsync(ForDevice(serial, "push", localPath, remotePath), ct, HandleLine, HandleLine)
            .ConfigureAwait(false);
    }

    /// <summary>Fordert den Medienscanner auf, eine wiederhergestellte Datei zu indizieren.</summary>
    public Task<ProcessResult> ScanMediaAsync(string serial, string remotePath, CancellationToken ct = default) =>
        ShellAsync(
            serial,
            $"am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d file://{remotePath.Replace(" ", "%20")} >/dev/null 2>&1",
            ct);

    // ------------------------------------------------------------------- Root

    /// <summary>Prüft, ob und auf welchem Weg Root-Rechte zur Verfügung stehen.</summary>
    public async Task<RootMode> DetectRootAsync(string serial, CancellationToken ct = default)
    {
        var direct = await ShellTextAsync(serial, "id", ct).ConfigureAwait(false);
        if (direct.Contains("uid=0(", StringComparison.Ordinal))
        {
            return RootMode.AdbRoot;
        }

        var su = await ShellAsync(serial, "su -c id 2>/dev/null", ct).ConfigureAwait(false);
        if (su.StandardOutput.Contains("uid=0(", StringComparison.Ordinal))
        {
            return RootMode.Su;
        }

        return RootMode.None;
    }

    /// <summary>Verpackt ein Shell-Kommando so, dass es mit Root-Rechten läuft.</summary>
    public static string WrapRoot(string command, RootMode mode) => mode switch
    {
        RootMode.AdbRoot => command,
        RootMode.Su => "su -c " + Quote(command),
        _ => throw new AdbException("Für diesen Vorgang werden Root-Rechte benötigt.")
    };

    public Task<ProcessResult> RootShellAsync(string serial, string command, RootMode mode, CancellationToken ct = default) =>
        ShellAsync(serial, WrapRoot(command, mode), ct);

    /// <summary>
    /// Führt ein Kommando aus und schreibt dessen Ausgabe binärsicher in eine Datei
    /// (<c>adb exec-out</c>) – etwa einen tar-Strom der App-Daten.
    /// </summary>
    public async Task<ProcessResult> ExecOutToFileAsync(
        string serial,
        string command,
        string localPath,
        CancellationToken ct = default)
    {
        _log.Debug($"adb exec-out {command}");
        return await ProcessRunner
            .RunToFileAsync(AdbPath, ForDevice(serial, "exec-out", command), localPath, ct)
            .ConfigureAwait(false);
    }

    /// <summary>Liest die Linux-Benutzerkennung eines installierten Paketes.</summary>
    public async Task<string?> GetPackageUidAsync(string serial, string packageName, CancellationToken ct = default)
    {
        var output = await ShellTextAsync(serial, $"dumpsys package {Quote(packageName)} | grep -m 1 userId=", ct)
            .ConfigureAwait(false);

        var match = Regex.Match(output, @"userId=(\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Ermittelt die Größe der App-Datenordner (nur mit Root lesbar).</summary>
    public async Task<Dictionary<string, long>> GetAppDataSizesAsync(
        string serial,
        IEnumerable<string> packageNames,
        RootMode mode,
        CancellationToken ct = default)
    {
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var chunk in Chunk(packageNames, 30))
        {
            ct.ThrowIfCancellationRequested();
            var inner = "for p in " + string.Join(" ", chunk.Select(Quote)) +
                        "; do echo \"$p|$(du -sk /data/data/$p 2>/dev/null | cut -f1)\"; done";

            var result = await RootShellAsync(serial, inner, mode, ct).ConfigureAwait(false);
            foreach (var line in result.OutputLines)
            {
                var parts = line.Trim().Split('|');
                if (parts.Length == 2 && long.TryParse(parts[1].Trim(), out var kilobytes))
                {
                    sizes[parts[0]] = kilobytes * 1024L;
                }
            }
        }

        return sizes;
    }

    // ------------------------------------------------------------------- Apps

    public async Task<IReadOnlyList<InstalledPackage>> ListPackagesAsync(
        string serial,
        bool includeSystemApps = false,
        CancellationToken ct = default)
    {
        var filter = includeSystemApps ? string.Empty : "-3 ";
        var result = await ShellAsync(serial, $"pm list packages {filter}--show-versioncode", ct).ConfigureAwait(false);

        var packages = new List<InstalledPackage>();
        foreach (var line in result.OutputLines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("package:", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = trimmed[8..].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var versionCode = parts
                .FirstOrDefault(p => p.StartsWith("versionCode:", StringComparison.OrdinalIgnoreCase))
                ?.Split(':')
                .LastOrDefault() ?? string.Empty;

            packages.Add(new InstalledPackage
            {
                PackageName = parts[0],
                VersionCode = versionCode,
                IsSystemApp = includeSystemApps
            });
        }

        return packages;
    }

    /// <summary>Ermittelt die APK-Dateien (Basis und Splits) der angegebenen Pakete.</summary>
    public async Task<Dictionary<string, List<string>>> ResolveApkPathsAsync(
        string serial,
        IEnumerable<string> packageNames,
        CancellationToken ct = default)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var chunk in Chunk(packageNames, 40))
        {
            ct.ThrowIfCancellationRequested();
            var command = string.Join(
                "; ",
                chunk.Select(p => $"echo '#PKG#{p}'; pm path {Quote(p)} 2>/dev/null"));

            var result = await ShellAsync(serial, command, ct).ConfigureAwait(false);

            string? current = null;
            foreach (var line in result.OutputLines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#PKG#", StringComparison.Ordinal))
                {
                    current = trimmed[5..];
                    map[current] = new List<string>();
                }
                else if (current is not null && trimmed.StartsWith("package:", StringComparison.Ordinal))
                {
                    map[current].Add(trimmed[8..]);
                }
            }
        }

        return map;
    }

    public async Task<ProcessResult> InstallAsync(
        string serial,
        IReadOnlyList<string> localApkPaths,
        bool allowDowngrade = false,
        CancellationToken ct = default)
    {
        var arguments = ForDevice(serial, localApkPaths.Count > 1 ? "install-multiple" : "install");
        arguments.Add("-r");
        if (allowDowngrade)
        {
            arguments.Add("-d");
        }

        arguments.AddRange(localApkPaths);
        return await ExecAsync(arguments, ct).ConfigureAwait(false);
    }

    // --------------------------------------------------------- Daten-Exporte

    /// <summary>Fragt einen Content-Provider ab (Kontakte, SMS, Anrufliste …).</summary>
    public async Task<ProcessResult> QueryContentAsync(string serial, string uri, CancellationToken ct = default) =>
        await ShellAsync(serial, $"content query --uri {Quote(uri)} 2>&1", ct).ConfigureAwait(false);

    public async Task<ProcessResult> DumpSettingsAsync(string serial, string nameSpace, CancellationToken ct = default) =>
        await ShellAsync(serial, $"settings list {nameSpace} 2>&1", ct).ConfigureAwait(false);

    /// <summary>
    /// Klassische <c>adb backup</c>-Sicherung der App-Daten. Muss am Gerät bestätigt werden und
    /// wird ab Android 12 von den meisten Apps ignoriert.
    /// </summary>
    public async Task<ProcessResult> LegacyBackupAsync(
        string serial,
        string localFile,
        bool includeApks,
        bool includeSharedStorage,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(localFile);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var arguments = ForDevice(serial, "backup");
        arguments.Add(includeApks ? "-apk" : "-noapk");
        arguments.Add(includeSharedStorage ? "-shared" : "-noshared");
        arguments.Add("-all");
        arguments.Add("-f");
        arguments.Add(localFile);

        return await ExecAsync(arguments, ct).ConfigureAwait(false);
    }

    public async Task<ProcessResult> LegacyRestoreAsync(string serial, string localFile, CancellationToken ct = default) =>
        await ExecAsync(ForDevice(serial, "restore", localFile), ct).ConfigureAwait(false);

    // ----------------------------------------------------------------- Helfer

    private static IEnumerable<List<T>> Chunk<T>(IEnumerable<T> source, int size)
    {
        var buffer = new List<T>(size);
        foreach (var item in source)
        {
            buffer.Add(item);
            if (buffer.Count == size)
            {
                yield return buffer;
                buffer = new List<T>(size);
            }
        }

        if (buffer.Count > 0)
        {
            yield return buffer;
        }
    }
}
