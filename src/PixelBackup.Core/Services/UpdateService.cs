using System.Security.Cryptography;
using System.Text.Json;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;

namespace PixelBackup.Core.Services;

/// <summary>
/// Versionscheck und Bezug der passenden Datei.
///
/// WARUM KEINE GITHUB-API: Ohne Anmeldung erlaubt api.github.com 60 Abrufe je
/// Stunde und IP-Adresse – in einem Netz mit vielen Rechnern ist das schnell
/// aufgebraucht, und ein Zugangsschlüssel hat in einem verteilten Programm
/// nichts zu suchen. Stattdessen wird eine gewöhnliche Datei geladen:
/// update.json aus der jüngsten Veröffentlichung, ausgeliefert über das
/// Auslieferungsnetz von GitHub.
///
/// WAS BEWUSST NICHT PASSIERT: Es wird nichts geladen und nichts ersetzt,
/// ohne dass jemand zugestimmt hat. Eingespielt wird ausschließlich eine
/// Datei, deren SHA-256 zu der Angabe in update.json passt.
/// </summary>
public sealed class UpdateService
{
    /// <summary>Die jüngste Veröffentlichung – diese Adresse zeigt immer auf den neuesten Stand.</summary>
    public const string DefaultManifestUrl =
        "https://github.com/Tinnitus97/Pixel-Backup/releases/latest/download/update.json";

    /// <summary>Rückfallweg, falls es noch keine Veröffentlichung mit Datei gibt.</summary>
    public const string FallbackManifestUrl =
        "https://raw.githubusercontent.com/Tinnitus97/Pixel-Backup/main/update.json";

    public const string ReleasesPageUrl = "https://github.com/Tinnitus97/Pixel-Backup/releases";

    private readonly ILogSink _log;
    private readonly HttpClient _http;

    public UpdateService(ILogSink? log = null, HttpClient? http = null)
    {
        _log = log ?? NullLogSink.Instance;
        _http = http ?? CreateClient();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"PixelBackup/{InstallationInfo.CurrentVersion}");

        // Kein zwischengespeicherter Stand – sonst meldet ein Rechner stundenlang
        // die alte Nummer.
        client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true
        };

        return client;
    }

    /// <summary>Ordner für heruntergeladene Fassungen.</summary>
    public static string WorkFolder => Path.Combine(Path.GetTempPath(), "PixelBackup-Update");

    // ------------------------------------------------------------------ Abfragen

    /// <summary>
    /// Holt update.json. Ohne eigene Adresse wird erst die jüngste
    /// Veröffentlichung versucht, danach die Datei im Quellstand.
    /// </summary>
    public async Task<UpdateManifest> FetchAsync(string? url = null, CancellationToken ct = default)
    {
        var addresses = string.IsNullOrWhiteSpace(url)
            ? new[] { DefaultManifestUrl, FallbackManifestUrl }
            : new[] { url! };

        UpdateManifest? last = null;

        foreach (var address in addresses)
        {
            try
            {
                var json = await _http.GetStringAsync(address, ct).ConfigureAwait(false);
                var manifest = Parse(json);

                if (manifest.Success)
                {
                    return manifest;
                }

                last = manifest;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = UpdateManifest.Failed(ex.Message);
            }
        }

        return last ?? UpdateManifest.Failed(Loc.Tr("Keine Adresse angegeben.", "No address given."));
    }

    /// <summary>Wertet update.json aus. Öffentlich, damit es sich prüfen lässt.</summary>
    public static UpdateManifest Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var packages = new List<UpdatePackage>();
            if (root.TryGetProperty("packages", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in list.EnumerateArray())
                {
                    var package = ReadPackage(element);
                    if (package.IsUsable)
                    {
                        packages.Add(package);
                    }
                }
            }

            var version = Text(root, "version");
            if (string.IsNullOrWhiteSpace(version))
            {
                return UpdateManifest.Failed(Loc.Tr(
                    "update.json enthält keine Fassungsnummer.",
                    "update.json contains no version number."));
            }

            return new UpdateManifest
            {
                Success = true,
                Version = version,
                Released = Text(root, "released"),
                Notes = Text(root, "notes"),
                Packages = packages
            };
        }
        catch (Exception ex)
        {
            return UpdateManifest.Failed(ex.Message);
        }
    }

    private static UpdatePackage ReadPackage(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new UpdatePackage();
        }

        return new UpdatePackage
        {
            Kind = ParseKind(Text(element, "kind")),
            Arch = Text(element, "arch"),
            Url = Text(element, "url"),
            Sha256 = Text(element, "sha256").ToLowerInvariant(),
            Size = long.TryParse(Text(element, "size"), out var size) ? size : 0
        };
    }

    /// <summary>Die Kennungen, die in update.json stehen.</summary>
    public static InstallationKind ParseKind(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "windows-exe" or "exe" or "windows" => InstallationKind.WindowsExe,
        "deb" => InstallationKind.Deb,
        "rpm" => InstallationKind.Rpm,
        "appimage" => InstallationKind.AppImage,
        "flatpak" => InstallationKind.Flatpak,
        "tarball" or "tar.gz" or "portable" => InstallationKind.Portable,
        _ => InstallationKind.Unknown
    };

    /// <summary>Umgekehrt: die Kennung für update.json.</summary>
    public static string KindKey(InstallationKind kind) => kind switch
    {
        InstallationKind.WindowsExe => "windows-exe",
        InstallationKind.Deb => "deb",
        InstallationKind.Rpm => "rpm",
        InstallationKind.AppImage => "appimage",
        InstallationKind.Flatpak => "flatpak",
        InstallationKind.Portable => "tarball",
        _ => "unknown"
    };

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.ToString(),
                _ => string.Empty
            }
            : string.Empty;

    // ---------------------------------------------------------------- Vergleichen

    /// <summary>true, wenn die angebotene Fassung höher ist als die eigene.</summary>
    public static bool IsNewer(string? local, string? online)
    {
        if (string.IsNullOrWhiteSpace(online))
        {
            return false;
        }

        if (Version.TryParse(Trim(local), out var here) && Version.TryParse(Trim(online), out var there))
        {
            return there > here;
        }

        return false;

        // "v0.9.0" und "0.9.0-beta" kommen vor; verglichen wird der Zahlenteil.
        static string Trim(string? value)
        {
            var text = (value ?? string.Empty).Trim().TrimStart('v', 'V');
            var dash = text.IndexOfAny(new[] { '-', '+', ' ' });
            return dash > 0 ? text[..dash] : text;
        }
    }

    /// <summary>
    /// Sucht die Datei, die zur Einbauart passt. Stimmt die Architektur
    /// überein, wird sie bevorzugt; sonst zählt die Einbauart allein.
    /// </summary>
    public static UpdatePackage? SelectPackage(UpdateManifest manifest, InstallationKind kind, string arch)
    {
        var matching = manifest.Packages.Where(p => p.Kind == kind).ToList();
        if (matching.Count == 0)
        {
            return null;
        }

        var exact = matching.FirstOrDefault(p =>
            string.Equals(p.Arch, arch, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            return exact;
        }

        // Ohne Architekturangabe gilt die Datei für alle.
        return matching.FirstOrDefault(p => string.IsNullOrWhiteSpace(p.Arch));
    }

    /// <summary>Prüft in einem Zug: abfragen, vergleichen, passende Datei suchen.</summary>
    public async Task<UpdateCheckResult> CheckAsync(string? manifestUrl = null, CancellationToken ct = default)
    {
        var local = InstallationInfo.CurrentVersion;
        var kind = InstallationInfo.Kind;

        var manifest = await FetchAsync(manifestUrl, ct).ConfigureAwait(false);

        if (!manifest.Success)
        {
            _log.Warn(Loc.Tr(
                $"Versionscheck nicht möglich: {manifest.Error}",
                $"Version check failed: {manifest.Error}"));

            return new UpdateCheckResult { LocalVersion = local, Kind = kind, Error = manifest.Error };
        }

        var newer = IsNewer(local, manifest.Version);
        var package = newer ? SelectPackage(manifest, kind, InstallationInfo.Arch) : null;

        _log.Info(newer
            ? Loc.Tr(
                $"Neue Fassung verfügbar: {manifest.Version} (installiert: {local}).",
                $"New version available: {manifest.Version} (installed: {local}).")
            : Loc.Tr(
                $"Pixel Backup ist aktuell ({local}).",
                $"Pixel Backup is up to date ({local})."));

        return new UpdateCheckResult
        {
            LocalVersion = local,
            OnlineVersion = manifest.Version,
            Released = manifest.Released,
            Notes = string.IsNullOrWhiteSpace(manifest.Notes) ? ReleasesPageUrl : manifest.Notes,
            Package = package,
            Kind = kind,
            UpdateAvailable = newer
        };
    }

    // -------------------------------------------------------------- Herunterladen

    /// <summary>Ergebnis eines Downloads samt Prüfsummenvergleich.</summary>
    public sealed record DownloadResult(bool Success, string File, string? Error);

    /// <summary>
    /// Lädt eine angebotene Datei und prüft ihre SHA-256-Summe. Stimmt sie
    /// nicht, wird die Datei gelöscht – eine halb übertragene oder
    /// ausgetauschte Datei darf nicht eingespielt werden.
    /// </summary>
    public async Task<DownloadResult> DownloadAsync(
        UpdatePackage package,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(WorkFolder);
        var target = Path.Combine(WorkFolder, package.FileName);

        try
        {
            using (var response = await _http
                       .GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, ct)
                       .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? package.Size;
                var phase = Loc.Tr("Wird geladen", "Downloading");

                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var file = File.Create(target);

                var buffer = new byte[81920];
                long done = 0;
                int read;

                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    progress?.Report(new DownloadProgress(done, total, phase));
                }
            }

            if (!string.IsNullOrWhiteSpace(package.Sha256))
            {
                progress?.Report(new DownloadProgress(1, 1, Loc.Tr("Prüfsumme wird geprüft", "Verifying checksum")));

                var actual = await Sha256FileAsync(target, ct).ConfigureAwait(false);
                if (!string.Equals(actual, package.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(target);
                    return new DownloadResult(false, target, Loc.Tr(
                        "Die heruntergeladene Datei ist beschädigt (Prüfsumme stimmt nicht).",
                        "The downloaded file is damaged (checksum mismatch)."));
                }
            }

            _log.Info(Loc.Tr($"Neue Fassung geladen: {target}", $"New version downloaded: {target}"));
            return new DownloadResult(true, target, null);
        }
        catch (OperationCanceledException)
        {
            TryDelete(target);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(target);
            return new DownloadResult(false, target, ex.Message);
        }
    }

    public static async Task<string> Sha256FileAsync(string file, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Räumt den Arbeitsordner auf.</summary>
    public static void CleanUp()
    {
        try
        {
            if (Directory.Exists(WorkFolder))
            {
                Directory.Delete(WorkFolder, recursive: true);
            }
        }
        catch (Exception)
        {
            // Beim nächsten Mal.
        }
    }
}
