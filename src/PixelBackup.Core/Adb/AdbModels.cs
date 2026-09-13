using PixelBackup.Core.Localization;

namespace PixelBackup.Core.Adb;

/// <summary>Art des Root-Zugriffs, den adb auf dem Gerät hat.</summary>
public enum RootMode
{
    /// <summary>Kein Root – der Normalfall bei Seriengeräten.</summary>
    None,

    /// <summary>adbd läuft bereits als root (Entwickler-/userdebug-Abbild).</summary>
    AdbRoot,

    /// <summary>Root über den Befehl <c>su</c> (Magisk o. Ä.).</summary>
    Su
}

public enum AdbDeviceState
{
    Unknown,
    Device,
    Unauthorized,
    Offline,

    /// <summary>Gerät ist sichtbar, adb darf aber nicht zugreifen (fehlende udev-Regeln unter Linux).</summary>
    NoPermissions,

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
        AdbDeviceState.Device => Loc.Tr("verbunden", "connected"),
        AdbDeviceState.Unauthorized => Loc.Tr(
            "nicht autorisiert – bitte USB-Debugging am Gerät bestätigen",
            "unauthorised – please confirm USB debugging on the device"),
        AdbDeviceState.Offline => Loc.Tr("offline", "offline"),
        AdbDeviceState.NoPermissions => Loc.Tr(
            "kein Zugriff – Geräteregeln fehlen (siehe „Komponenten“)",
            "no access – device rules missing (see \"Components\")"),
        AdbDeviceState.Recovery => Loc.Tr("Recovery-Modus", "recovery mode"),
        AdbDeviceState.Sideload => Loc.Tr("Sideload-Modus", "sideload mode"),
        AdbDeviceState.Bootloader => Loc.Tr("Bootloader/Fastboot", "bootloader/fastboot"),
        _ => Loc.Tr("unbekannt", "unknown")
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
