using System.Globalization;
using System.Text.RegularExpressions;
using PixelBackup.Core.Adb;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;

namespace PixelBackup.Core.Services;

/// <summary>
/// Prüft, installiert und aktualisiert die Android-Plattform-Tools (adb, fastboot).
/// Bezugsquelle ist dieselbe Paketliste, die auch Android Studio verwendet.
/// </summary>
public sealed class PlatformToolsInstaller
{
    public const string PackagePath = "platform-tools";

    // Achtung: "adb version" nennt zuerst die Protokollfassung ("Android Debug Bridge
    // version 1.0.41") und erst danach die Werkzeugfassung ("Version 35.0.2-12147458").
    // Deshalb zeilenweise und ohne IgnoreCase suchen – sonst wird 1.0.41 gelesen.
    private static readonly Regex VersionPattern =
        new(@"^Version\s+(\d+)\.(\d+)\.(\d+)", RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly ILogSink _log;
    private readonly SdkRepositoryClient _client;

    public PlatformToolsInstaller(ILogSink? log = null, SdkRepositoryClient? client = null)
    {
        _log = log ?? NullLogSink.Instance;
        _client = client ?? new SdkRepositoryClient(_log);
    }

    /// <summary>Ordner, in dem Pixel Backup seine eigene adb-Fassung ablegt.</summary>
    public static string ManagedParentDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PixelBackup");

    public static string ManagedDirectory => Path.Combine(ManagedParentDirectory, PackagePath);

    public static string ManagedAdbPath => Path.Combine(ManagedDirectory, AdbLocator.ExecutableName);

    /// <summary>Liegt diese adb-Datei in der von Pixel Backup verwalteten Installation?</summary>
    public static bool IsManaged(string adbPath) =>
        adbPath.StartsWith(ManagedDirectory, StringComparison.OrdinalIgnoreCase);

    /// <summary>Liest die Werkzeugversion aus der Ausgabe von <c>adb version</c>.</summary>
    public static Version? ParseVersion(string adbVersionOutput)
    {
        var match = VersionPattern.Match(adbVersionOutput);
        if (!match.Success)
        {
            return null;
        }

        return new Version(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
    }

    public async Task<Version?> ReadInstalledVersionAsync(string adbPath, CancellationToken ct = default)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(adbPath, new[] { "version" }, ct).ConfigureAwait(false);
            return ParseVersion(result.StandardOutput);
        }
        catch (Exception ex)
        {
            _log.Debug("adb version: " + ex.Message);
            return null;
        }
    }

    public Task<SdkPackage?> QueryLatestAsync(CancellationToken ct = default) =>
        _client.QueryPackageAsync(SdkRepositoryClient.ToolsManifest, PackagePath, ct);

    /// <summary>
    /// Prüft, ob adb vorhanden und aktuell ist. <paramref name="checkOnline"/> steuert,
    /// ob dafür die Paketliste von Google abgefragt wird.
    /// </summary>
    public async Task<(ComponentStatus Status, SdkPackage? Latest)> CheckAsync(
        string? configuredAdbPath,
        bool checkOnline = true,
        CancellationToken ct = default)
    {
        var status = new ComponentStatus { Name = Loc.Tr("Android-Plattform-Tools (adb)", "Android platform tools (adb)") };

        var adbPath = AdbLocator.Locate(configuredAdbPath);
        Version? installed = null;

        if (adbPath is not null)
        {
            status.Location = adbPath;
            installed = await ReadInstalledVersionAsync(adbPath, ct).ConfigureAwait(false);
            status.InstalledVersion = installed?.ToString() ?? Loc.Tr("unbekannt", "unknown");
        }

        SdkPackage? latest = null;
        if (checkOnline)
        {
            try
            {
                latest = await QueryLatestAsync(ct).ConfigureAwait(false);
                status.LatestVersion = latest?.RevisionText;
            }
            catch (Exception ex)
            {
                _log.Warn(Loc.Tr(
                    "Die Paketliste von Google ist nicht erreichbar: " + ex.Message,
                    "Google's package list is unreachable: " + ex.Message));
            }
        }

        if (adbPath is null)
        {
            status.State = ComponentState.Missing;
            status.Message = latest is null
                ? Loc.Tr(
                    "adb wurde nicht gefunden. Pixel Backup kann die Plattform-Tools automatisch herunterladen.",
                    "adb was not found. Pixel Backup can download the platform tools automatically.")
                : Loc.Tr(
                    $"adb wurde nicht gefunden. Fassung {latest.RevisionText} kann automatisch installiert werden.",
                    $"adb was not found. Release {latest.RevisionText} can be installed automatically.");
            return (status, latest);
        }

        if (latest is not null && installed is not null && installed < latest.Revision)
        {
            status.State = ComponentState.UpdateAvailable;
            status.Message = IsManaged(adbPath)
                ? Loc.Tr(
                    $"Installiert ist {installed}, verfügbar ist {latest.RevisionText}.",
                    $"Installed is {installed}, available is {latest.RevisionText}.")
                : Loc.Tr(
                    $"Installiert ist {installed} ({adbPath}), verfügbar ist {latest.RevisionText}. " +
                    "Pixel Backup installiert die neue Fassung in einen eigenen Ordner und verwendet künftig diese.",
                    $"Installed is {installed} ({adbPath}), available is {latest.RevisionText}. " +
                    "Pixel Backup installs the new release into its own folder and uses that from then on.");
            return (status, latest);
        }

        status.State = ComponentState.UpToDate;
        status.Message = installed is null
            ? Loc.Tr($"adb gefunden: {adbPath}", $"adb found: {adbPath}")
            : latest is null
                ? Loc.Tr(
                    $"adb {installed} ist eingerichtet (kein Abgleich mit Google möglich).",
                    $"adb {installed} is set up (no comparison with Google possible).")
                : Loc.Tr($"adb {installed} ist aktuell.", $"adb {installed} is up to date.");

        return (status, latest);
    }

    /// <summary>
    /// Lädt die Plattform-Tools und entpackt sie in den verwalteten Ordner.
    /// Liefert den Pfad zur neuen adb-Datei zurück.
    /// </summary>
    public async Task<string> InstallAsync(
        SdkPackage package,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(ManagedParentDirectory);
        await _client.DownloadAndExtractAsync(package, ManagedParentDirectory, progress, ct).ConfigureAwait(false);

        var adbPath = ManagedAdbPath;
        if (!File.Exists(adbPath))
        {
            throw new FileNotFoundException(Loc.Tr(
                "Nach dem Entpacken wurde keine adb-Datei gefunden.",
                "No adb executable was found after extracting."), adbPath);
        }

        MakeExecutable(ManagedDirectory);
        _log.Info(Loc.Tr($"Plattform-Tools eingerichtet: {adbPath}", $"Platform tools installed: {adbPath}"));
        return adbPath;
    }

    /// <summary>Setzt unter Linux und macOS die Ausführungsrechte der entpackten Werkzeuge.</summary>
    private static void MakeExecutable(string directory)
    {
        if (OperatingSystem.IsWindows() || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            try
            {
                var name = Path.GetFileName(file);
                if (name.Contains('.') && !name.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.SetUnixFileMode(
                    file,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch (Exception)
            {
                // Ohne Ausführungsrecht meldet sich adb später selbst.
            }
        }
    }
}
