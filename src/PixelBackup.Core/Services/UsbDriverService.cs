using System.Diagnostics;
using System.Text.Json;
using PixelBackup.Core.Adb;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;

namespace PixelBackup.Core.Services;

/// <summary>
/// Prüft unter Windows, ob der Google-USB-Treiber eingerichtet ist und ob
/// angeschlossene Android-Geräte sauber erkannt werden. Auf anderen Systemen
/// wird kein Treiber benötigt.
/// </summary>
public sealed class UsbDriverService
{
    public const string PackagePath = "extras;google;usb_driver";

    private readonly ILogSink _log;
    private readonly SdkRepositoryClient _client;

    public UsbDriverService(ILogSink? log = null, SdkRepositoryClient? client = null)
    {
        _log = log ?? NullLogSink.Instance;
        _client = client ?? new SdkRepositoryClient(_log);
    }

    [System.Runtime.Versioning.SupportedOSPlatformGuard("windows")]
    public static bool IsWindows => OperatingSystem.IsWindows();

    public static string ManagedDirectory => Path.Combine(
        PlatformToolsInstaller.ManagedParentDirectory,
        "usb_driver");

    public Task<SdkPackage?> QueryLatestAsync(CancellationToken ct = default) =>
        _client.QueryPackageAsync(SdkRepositoryClient.AddonManifest, PackagePath, ct);

    /// <summary>Ergebnis der Windows-Abfrage; öffentlich, damit es sich testen lässt.</summary>
    public sealed class WindowsDriverReport
    {
        public bool StoreHasGoogle { get; set; }

        public List<WindowsDevice> Devices { get; set; } = new();

        public List<WindowsDriver> Drivers { get; set; } = new();
    }

    public sealed class WindowsDevice
    {
        public string? Name { get; set; }

        public string? Status { get; set; }

        public int ConfigManagerErrorCode { get; set; }

        public bool HasProblem => ConfigManagerErrorCode != 0 ||
                                  !string.Equals(Status, "OK", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class WindowsDriver
    {
        public string? InfName { get; set; }

        public string? DriverVersion { get; set; }

        public string? DriverProviderName { get; set; }
    }

    private const string PowerShellScript = """
        $ErrorActionPreference = 'SilentlyContinue'
        $store = ''
        try { $store = (pnputil /enum-drivers | Out-String) } catch { }
        $devices = Get-CimInstance Win32_PnPEntity |
            Where-Object { $_.Name -match 'Android|ADB|Fastboot' } |
            Select-Object Name, Status, ConfigManagerErrorCode
        $drivers = Get-CimInstance Win32_PnPSignedDriver |
            Where-Object { $_.InfName -like 'android_winusb*' -or $_.DriverProviderName -eq 'Google, Inc.' } |
            Select-Object InfName, DriverVersion, DriverProviderName
        [pscustomobject]@{
            StoreHasGoogle = [bool]($store -match 'android_winusb\.inf')
            Devices = @($devices)
            Drivers = @($drivers)
        } | ConvertTo-Json -Depth 4 -Compress
        """;

    /// <summary>Fragt Windows nach Treibern und angeschlossenen Android-Geräten.</summary>
    public async Task<WindowsDriverReport?> QueryWindowsAsync(CancellationToken ct = default)
    {
        if (!IsWindows)
        {
            return null;
        }

        try
        {
            var result = await ProcessRunner.RunAsync(
                "powershell",
                new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", PowerShellScript },
                ct).ConfigureAwait(false);

            var json = result.StandardOutput.Trim();
            if (json.Length == 0)
            {
                return null;
            }

            return ParseReport(json);
        }
        catch (Exception ex)
        {
            _log.Debug("USB-Treiberabfrage: " + ex.Message);
            return null;
        }
    }

    /// <summary>Wertet die JSON-Ausgabe der Windows-Abfrage aus.</summary>
    public static WindowsDriverReport? ParseReport(string json)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };

            return JsonSerializer.Deserialize<WindowsDriverReport>(json, options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<(ComponentStatus Status, SdkPackage? Latest)> CheckAsync(
        bool checkOnline = true,
        CancellationToken ct = default)
    {
        var status = new ComponentStatus { Name = Loc.Tr("USB-Treiber (Google)", "USB driver (Google)") };

        if (!IsWindows)
        {
            status.State = ComponentState.NotRequired;
            status.Message = OperatingSystem.IsLinux()
                ? Loc.Tr(
                    "Unter Linux wird kein Treiber benötigt. Falls das Gerät nicht erscheint, fehlen meist die udev-Regeln (Paket android-sdk-platform-tools-common).",
                    "No driver is needed on Linux. If the device does not show up, the udev rules are usually missing (package android-sdk-platform-tools-common).")
                : Loc.Tr(
                    "Unter macOS wird kein Treiber benötigt.",
                    "No driver is needed on macOS.");
            return (status, null);
        }

        var report = await QueryWindowsAsync(ct).ConfigureAwait(false);

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
                _log.Debug("USB-Treiberpaket: " + ex.Message);
            }
        }

        var driver = report?.Drivers.FirstOrDefault();
        status.InstalledVersion = driver?.DriverVersion;
        status.Location = driver?.InfName;

        var problemDevices = report?.Devices.Where(d => d.HasProblem).ToList() ?? new List<WindowsDevice>();
        var driverPresent = (report?.StoreHasGoogle ?? false) || driver is not null;

        if (problemDevices.Count > 0)
        {
            status.State = ComponentState.Problem;
            var names = string.Join(", ", problemDevices.Select(d => d.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Take(3));
            status.Message = Loc.Tr(
                $"Windows meldet ein Problem mit: {names}. Der Google-USB-Treiber kann das beheben.",
                $"Windows reports a problem with: {names}. The Google USB driver can fix that.");
            return (status, latest);
        }

        if (!driverPresent)
        {
            status.State = ComponentState.Missing;
            status.Message = Loc.Tr(
                "Der Google-USB-Treiber ist nicht eingerichtet. Für Pixel- und Nexus-Geräte wird er benötigt; " +
                "viele andere Hersteller laufen auch ohne ihn.",
                "The Google USB driver is not installed. Pixel and Nexus devices need it; " +
                "many other manufacturers work without it.");
            return (status, latest);
        }

        status.State = ComponentState.UpToDate;
        status.Message = driver is null
            ? Loc.Tr(
                "Der Google-USB-Treiber liegt im Treiberspeicher von Windows.",
                "The Google USB driver is present in the Windows driver store.")
            : Loc.Tr(
                $"Treiber {driver.DriverVersion} von {driver.DriverProviderName} ist eingerichtet.",
                $"Driver {driver.DriverVersion} by {driver.DriverProviderName} is installed.");

        return (status, latest);
    }

    /// <summary>Lädt das Treiberpaket und entpackt es in den Programmordner.</summary>
    public async Task<string> DownloadAsync(
        SdkPackage package,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(PlatformToolsInstaller.ManagedParentDirectory);
        await _client
            .DownloadAndExtractAsync(package, PlatformToolsInstaller.ManagedParentDirectory, progress, ct)
            .ConfigureAwait(false);

        return ManagedDirectory;
    }

    /// <summary>
    /// Reicht den Treiber an Windows weiter. Das verlangt erhöhte Rechte, deshalb
    /// erscheint die Rückfrage der Benutzerkontensteuerung.
    /// </summary>
    public async Task<(bool Success, string Message)> InstallAsync(string driverDirectory, CancellationToken ct = default)
    {
        if (!IsWindows)
        {
            return (false, Loc.Tr("Nur unter Windows möglich.", "Only possible on Windows."));
        }

        var inf = Directory.Exists(driverDirectory)
            ? Directory.EnumerateFiles(driverDirectory, "android_winusb.inf", SearchOption.AllDirectories).FirstOrDefault()
            : null;

        if (inf is null)
        {
            return (false, Loc.Tr(
                "Im Treiberpaket wurde keine android_winusb.inf gefunden.",
                "No android_winusb.inf was found in the driver package."));
        }

        try
        {
            var startInfo = new ProcessStartInfo("pnputil")
            {
                Arguments = $"/add-driver \"{inf}\" /install",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (false, Loc.Tr("Der Installationsvorgang ließ sich nicht starten.", "The installation could not be started."));
            }

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                _log.Info(Loc.Tr("USB-Treiber eingerichtet.", "USB driver installed."));
                return (true, Loc.Tr(
                    "Der Treiber wurde eingerichtet. Gerät einmal ab- und wieder anstecken.",
                    "The driver was installed. Unplug the device once and plug it back in."));
            }

            return (false, Loc.Tr(
                $"pnputil endete mit Code {process.ExitCode}.",
                $"pnputil ended with code {process.ExitCode}."));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 1223: Der Benutzer hat die Rückfrage der Benutzerkontensteuerung abgelehnt.
            return (false, Loc.Tr(
                "Die Treiberinstallation wurde abgebrochen – sie benötigt Administratorrechte.",
                "The driver installation was cancelled – it needs administrator rights."));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
