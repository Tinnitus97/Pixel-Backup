using PixelBackup.Core.Adb;
using PixelBackup.Core.Util;

namespace PixelBackup.Core.Model;

/// <summary>Stammdaten eines angeschlossenen Gerätes – wird im Manifest mitgesichert.</summary>
public sealed class DeviceInfo
{
    public string Serial { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    public string AndroidVersion { get; set; } = string.Empty;

    public string SdkLevel { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string HardwareSerial { get; set; } = string.Empty;

    public string BuildId { get; set; } = string.Empty;

    public string SecurityPatch { get; set; } = string.Empty;

    public long StorageTotalBytes { get; set; }

    public long StorageUsedBytes { get; set; }

    public long StorageFreeBytes { get; set; }

    public int BatteryLevel { get; set; }

    public bool IsCharging { get; set; }

    /// <summary>Root-Zugriff über adb (ermöglicht die vollständige App-Daten-Sicherung).</summary>
    public RootMode RootAccess { get; set; } = RootMode.None;

    public bool HasRoot => RootAccess != RootMode.None;

    public string RootText => RootAccess switch
    {
        RootMode.AdbRoot => "ja (adbd läuft als root)",
        RootMode.Su => "ja (su verfügbar)",
        _ => "nein – App-Daten sind systembedingt nicht vollständig sicherbar"
    };

    public string DisplayName
    {
        get
        {
            var name = $"{Manufacturer} {Model}".Trim();
            return name.Length == 0 ? Serial : name;
        }
    }

    /// <summary>API-Ebene als Zahl (0, wenn unbekannt).</summary>
    public int SdkNumber => int.TryParse(SdkLevel, out var value) ? value : 0;

    public string AndroidText =>
        string.IsNullOrWhiteSpace(SdkLevel) ? AndroidVersion : $"Android {AndroidVersion} (API {SdkLevel})";

    public string StorageText => StorageTotalBytes <= 0
        ? "unbekannt"
        : $"{Humanize.Bytes(StorageUsedBytes)} von {Humanize.Bytes(StorageTotalBytes)} belegt · {Humanize.Bytes(StorageFreeBytes)} frei";

    /// <summary>Ordnername für die Sicherungen dieses Gerätes.</summary>
    public string FolderName
    {
        get
        {
            var model = string.IsNullOrWhiteSpace(Model) ? "Geraet" : Model.Replace(' ', '_');
            var serial = string.IsNullOrWhiteSpace(Serial) ? "unbekannt" : Serial.Replace(':', '-');
            return PathMapper.SanitizeSegment($"{model}_{serial}");
        }
    }
}
