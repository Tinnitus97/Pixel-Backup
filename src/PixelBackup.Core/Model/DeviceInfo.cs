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

    public string DisplayName
    {
        get
        {
            var name = $"{Manufacturer} {Model}".Trim();
            return name.Length == 0 ? Serial : name;
        }
    }

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
