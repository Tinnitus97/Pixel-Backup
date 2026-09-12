namespace PixelBackup.Core.Adb;

public enum AdbDeviceState
{
    Unknown,
    Device,
    Unauthorized,
    Offline,
    Recovery,
    Sideload,
    Bootloader
}

/// <summary>Ein von <c>adb devices -l</c> gemeldetes Gerät.</summary>
public sealed class AdbDevice
{
    public string Serial { get; init; } = string.Empty;

    public AdbDeviceState State { get; init; } = AdbDeviceState.Unknown;

    public string Model { get; init; } = string.Empty;

    public string Product { get; init; } = string.Empty;

    public string DeviceName { get; init; } = string.Empty;

    public string TransportId { get; init; } = string.Empty;

    public bool IsWireless => Serial.Contains(':') && Serial.Count(c => c == '.') >= 3;

    public bool IsReady => State == AdbDeviceState.Device;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Model) ? Serial : Model.Replace('_', ' ');

    public string StateText => State switch
    {
        AdbDeviceState.Device => "verbunden",
        AdbDeviceState.Unauthorized => "nicht autorisiert – bitte USB-Debugging am Gerät bestätigen",
        AdbDeviceState.Offline => "offline",
        AdbDeviceState.Recovery => "Recovery-Modus",
        AdbDeviceState.Sideload => "Sideload-Modus",
        AdbDeviceState.Bootloader => "Bootloader/Fastboot",
        _ => "unbekannt"
    };

    public override string ToString() => $"{DisplayName} ({Serial}) – {StateText}";
}

/// <summary>Eine Datei auf dem Gerät inklusive Größe und Änderungszeit.</summary>
public sealed record RemoteFile(string Path, long Size, long ModifiedUnix)
{
    public DateTimeOffset Modified => DateTimeOffset.FromUnixTimeSeconds(ModifiedUnix);
}

/// <summary>Ein auf dem Gerät installiertes Anwendungspaket.</summary>
public sealed class InstalledPackage
{
    public string PackageName { get; init; } = string.Empty;

    public string VersionCode { get; init; } = string.Empty;

    public bool IsSystemApp { get; init; }

    public List<string> ApkPaths { get; } = new();

    public override string ToString() => PackageName;
}
