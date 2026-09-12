using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelBackup.Core.Services;

public enum AppTheme
{
    System,
    Light,
    Dark
}

public enum LanguageSetting
{
    /// <summary>Anzeigesprache des Systems übernehmen.</summary>
    System,

    German,

    English
}

/// <summary>Dauerhaft gespeicherte Programmeinstellungen.</summary>
public sealed class AppSettings
{
    public string? AdbPath { get; set; }

    public string BackupRoot { get; set; } = DefaultBackupRoot;

    public bool ComputeHashes { get; set; } = true;

    public bool IncrementalByDefault { get; set; } = true;

    public bool RemoveDeletedFiles { get; set; }

    public bool CreateArchive { get; set; }

    /// <summary>Anzahl der aufzubewahrenden Sätze pro Gerät; 0 bedeutet "alle behalten".</summary>
    public int KeepSetsPerDevice { get; set; }

    public bool AutoBackupOnConnect { get; set; }

    public bool WatchDevices { get; set; } = true;

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Sprache der Oberfläche; Standard ist die Anzeigesprache des Systems.</summary>
    public LanguageSetting Language { get; set; } = LanguageSetting.System;

    /// <summary>Beim Start prüfen, ob adb und Treiber vorhanden und aktuell sind.</summary>
    public bool CheckComponentsOnStart { get; set; } = true;

    /// <summary>Fehlende Plattform-Tools beim Start selbsttätig nachinstallieren.</summary>
    public bool AutoInstallAdb { get; set; } = true;

    public List<string> SelectedCategories { get; set; } = new();

    public static string DefaultBackupRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "PixelBackup");

    public AppSettings Clone() => new()
    {
        AdbPath = AdbPath,
        BackupRoot = BackupRoot,
        ComputeHashes = ComputeHashes,
        IncrementalByDefault = IncrementalByDefault,
        RemoveDeletedFiles = RemoveDeletedFiles,
        CreateArchive = CreateArchive,
        KeepSetsPerDevice = KeepSetsPerDevice,
        AutoBackupOnConnect = AutoBackupOnConnect,
        WatchDevices = WatchDevices,
        Theme = Theme,
        Language = Language,
        CheckComponentsOnStart = CheckComponentsOnStart,
        AutoInstallAdb = AutoInstallAdb,
        SelectedCategories = new List<string>(SelectedCategories)
    };
}

/// <summary>Lädt und speichert die Einstellungen im Anwendungsdatenordner des Benutzers.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public SettingsStore(string? directory = null)
    {
        Directory = directory ?? DefaultDirectory;
        FilePath = Path.Combine(Directory, "settings.json");
    }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PixelBackup");

    public string Directory { get; }

    public string FilePath { get; }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
    }
}
