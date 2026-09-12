using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;

namespace PixelBackup.Core.Services;

/// <summary>Ein Paket aus dem Android-SDK-Verzeichnis von Google.</summary>
public sealed class SdkPackage
{
    public required string Path { get; init; }

    public required string DisplayName { get; init; }

    public required Version Revision { get; init; }

    public required string DownloadUrl { get; init; }

    public long Size { get; init; }

    public string? Sha1 { get; init; }

    public string RevisionText => Revision.ToString();
}

/// <summary>Fortschritt eines Downloads.</summary>
public sealed record DownloadProgress(long BytesDone, long BytesTotal, string Phase)
{
    public double Percent => BytesTotal > 0 ? Math.Clamp(BytesDone * 100d / BytesTotal, 0, 100) : 0;
}

/// <summary>
/// Liest die offiziellen Paketlisten von Google (dieselben, die auch Android Studio
/// verwendet) und lädt daraus einzelne Pakete herunter – mit Prüfsummenkontrolle.
/// </summary>
public sealed class SdkRepositoryClient
{
    public const string BaseUrl = "https://dl.google.com/android/repository/";
    public const string ToolsManifest = BaseUrl + "repository2-3.xml";
    public const string AddonManifest = BaseUrl + "addon2-3.xml";

    private readonly ILogSink _log;
    private readonly HttpClient _http;

    public SdkRepositoryClient(ILogSink? log = null, HttpClient? http = null)
    {
        _log = log ?? NullLogSink.Instance;
        _http = http ?? CreateClient();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PixelBackup/1.0");
        return client;
    }

    /// <summary>Betriebssystemkennung, wie Google sie in den Paketlisten verwendet.</summary>
    public static string HostOs =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macosx" : "linux";

    /// <summary>Sucht ein Paket (z. B. "platform-tools") in einer Paketliste.</summary>
    public async Task<SdkPackage?> QueryPackageAsync(string manifestUrl, string packagePath, CancellationToken ct = default)
    {
        var xml = await _http.GetStringAsync(manifestUrl, ct).ConfigureAwait(false);
        return ParsePackage(xml, packagePath, HostOs);
    }

    /// <summary>Wertet eine Paketliste aus. Öffentlich, damit es sich testen lässt.</summary>
    public static SdkPackage? ParsePackage(string xml, string packagePath, string hostOs)
    {
        var document = XDocument.Parse(xml);

        var package = document.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "remotePackage" &&
                                 string.Equals((string?)e.Attribute("path"), packagePath, StringComparison.OrdinalIgnoreCase));

        if (package is null)
        {
            return null;
        }

        var revision = ReadRevision(package);
        var displayName = package.Elements().FirstOrDefault(e => e.Name.LocalName == "display-name")?.Value?.Trim();

        var archives = package.Descendants().Where(e => e.Name.LocalName == "archive").ToList();
        var archive = archives.FirstOrDefault(a => MatchesHost(a, hostOs))
                      ?? archives.FirstOrDefault(a => a.Elements().All(e => e.Name.LocalName != "host-os"))
                      ?? archives.FirstOrDefault();

        var complete = archive?.Elements().FirstOrDefault(e => e.Name.LocalName == "complete");
        var url = complete?.Elements().FirstOrDefault(e => e.Name.LocalName == "url")?.Value?.Trim();

        if (archive is null || complete is null || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var sizeText = complete.Elements().FirstOrDefault(e => e.Name.LocalName == "size")?.Value;
        long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);

        return new SdkPackage
        {
            Path = packagePath,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? packagePath : displayName!,
            Revision = revision,
            DownloadUrl = url!.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : BaseUrl + url,
            Size = size,
            Sha1 = complete.Elements().FirstOrDefault(e => e.Name.LocalName == "checksum")?.Value?.Trim()
        };
    }

    private static bool MatchesHost(XElement archive, string hostOs)
    {
        var value = archive.Elements().FirstOrDefault(e => e.Name.LocalName == "host-os")?.Value?.Trim();
        return string.Equals(value, hostOs, StringComparison.OrdinalIgnoreCase);
    }

    private static Version ReadRevision(XElement package)
    {
        var revision = package.Elements().FirstOrDefault(e => e.Name.LocalName == "revision");
        if (revision is null)
        {
            return new Version(0, 0, 0);
        }

        int Part(string name)
        {
            var value = revision.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
        }

        return new Version(Part("major"), Part("minor"), Part("micro"));
    }

    /// <summary>Lädt ein Paket herunter, prüft die Prüfsumme und entpackt es.</summary>
    public async Task<string> DownloadAndExtractAsync(
        SdkPackage package,
        string targetParentDirectory,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(targetParentDirectory);
        var temporary = Path.Combine(Path.GetTempPath(), $"pixelbackup-{Guid.NewGuid():n}.zip");

        try
        {
            _log.Info(Loc.Tr($"Lade {package.DisplayName} {package.RevisionText} …", $"Downloading {package.DisplayName} {package.RevisionText} …"));
            await DownloadAsync(package, temporary, progress, ct).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(package.Sha1))
            {
                progress?.Report(new DownloadProgress(package.Size, package.Size, Loc.Tr("Prüfsumme wird geprüft", "Verifying checksum")));
                var actual = await Sha1FileAsync(temporary, ct).ConfigureAwait(false);
                if (!string.Equals(actual, package.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(Loc.Tr(
                        "Die heruntergeladene Datei ist beschädigt (Prüfsumme stimmt nicht).",
                        "The downloaded file is damaged (checksum mismatch)."));
                }
            }

            progress?.Report(new DownloadProgress(package.Size, package.Size, Loc.Tr("Wird entpackt", "Extracting")));
            System.IO.Compression.ZipFile.ExtractToDirectory(temporary, targetParentDirectory, overwriteFiles: true);

            _log.Info(Loc.Tr($"{package.DisplayName} entpackt nach {targetParentDirectory}", $"{package.DisplayName} extracted to {targetParentDirectory}"));
            return targetParentDirectory;
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task DownloadAsync(
        SdkPackage package,
        string targetFile,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        using var response = await _http
            .GetAsync(package.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? package.Size;
        var phase = Loc.Tr("Wird geladen", "Downloading");

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long done = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            progress?.Report(new DownloadProgress(done, total, phase));
        }
    }

    private static async Task<string> Sha1FileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        using var sha1 = SHA1.Create();
        var hash = await sha1.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
