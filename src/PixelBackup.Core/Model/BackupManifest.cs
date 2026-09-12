using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelBackup.Core.Model;

public enum BackupEntryType
{
    File,
    Apk,
    LegacyAppData,
    ContentExport,
    SettingsExport
}

public enum BackupMode
{
    Full,
    Incremental
}

/// <summary>Ein gesichertes Element innerhalb eines Sicherungssatzes.</summary>
public sealed class BackupEntry
{
    public string CategoryId { get; set; } = string.Empty;

    public BackupEntryType Type { get; set; } = BackupEntryType.File;

    /// <summary>Ursprungspfad auf dem Gerät (bei Exporten die Quelle, z. B. der Content-URI).</summary>
    public string RemotePath { get; set; } = string.Empty;

    /// <summary>Pfad innerhalb des Sicherungsordners, immer mit '/' getrennt.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public long Size { get; set; }

    public long ModifiedUnix { get; set; }

    public string? Sha256 { get; set; }

    public string? PackageName { get; set; }

    public string? VersionCode { get; set; }

    public DateTimeOffset CapturedUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string Key => $"{Type}|{RemotePath}|{RelativePath}";

    [JsonIgnore]
    public DateTimeOffset Modified => ModifiedUnix > 0
        ? DateTimeOffset.FromUnixTimeSeconds(ModifiedUnix)
        : CapturedUtc;
}

/// <summary>Ein einzelner Sicherungslauf innerhalb eines Satzes.</summary>
public sealed class BackupRun
{
    public DateTimeOffset StartedUtc { get; set; }

    public DateTimeOffset FinishedUtc { get; set; }

    public BackupMode Mode { get; set; }

    public int FilesCopied { get; set; }

    public int FilesSkipped { get; set; }

    public int FilesFailed { get; set; }

    public long BytesCopied { get; set; }

    public bool Canceled { get; set; }

    public List<string> Categories { get; set; } = new();

    [JsonIgnore]
    public TimeSpan Duration => FinishedUtc - StartedUtc;
}

/// <summary>Beschreibt einen vollständigen Sicherungssatz auf der Festplatte.</summary>
public sealed class BackupManifest
{
    public const string FileName = "manifest.json";

    public int SchemaVersion { get; set; } = 1;

    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public string AppVersion { get; set; } = "1.0.0";

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DeviceInfo Device { get; set; } = new();

    public List<string> Categories { get; set; } = new();

    public List<BackupEntry> Entries { get; set; } = new();

    public List<BackupRun> Runs { get; set; } = new();

    public string? Notes { get; set; }

    public bool HasArchive { get; set; }

    public bool ArchiveEncrypted { get; set; }

    [JsonIgnore]
    public long TotalBytes => Entries.Sum(e => Math.Max(0, e.Size));

    [JsonIgnore]
    public int FileCount => Entries.Count;

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static BackupManifest? FromJson(string json) =>
        JsonSerializer.Deserialize<BackupManifest>(json, SerializerOptions);

    public void Save(string setDirectory)
    {
        Directory.CreateDirectory(setDirectory);
        var path = Path.Combine(setDirectory, FileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, ToJson());
        File.Move(temporary, path, overwrite: true);
    }

    public static BackupManifest? TryLoad(string setDirectory)
    {
        var path = Path.Combine(setDirectory, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return FromJson(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
