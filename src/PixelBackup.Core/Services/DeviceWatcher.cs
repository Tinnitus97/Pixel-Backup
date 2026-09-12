using PixelBackup.Core.Adb;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;

namespace PixelBackup.Core.Services;

/// <summary>Überwacht regelmäßig die Liste der angeschlossenen Geräte.</summary>
public sealed class DeviceWatcher : IDisposable
{
    private readonly ILogSink _log;
    private readonly TimeSpan _interval;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string _lastSignature = string.Empty;

    public DeviceWatcher(AdbClient adb, ILogSink? log = null, TimeSpan? interval = null)
    {
        Adb = adb;
        _log = log ?? NullLogSink.Instance;
        _interval = interval ?? TimeSpan.FromSeconds(3);
    }

    public AdbClient Adb { get; set; }

    /// <summary>Wird ausgelöst, sobald sich die Geräteliste verändert.</summary>
    public event Action<IReadOnlyList<AdbDevice>>? DevicesChanged;

    /// <summary>Wird ausgelöst, wenn ein neues, betriebsbereites Gerät hinzukommt.</summary>
    public event Action<AdbDevice>? DeviceConnected;

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var knownReady = new HashSet<string>(StringComparer.Ordinal);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var devices = await Adb.ListDevicesAsync(ct).ConfigureAwait(false);
                var signature = string.Join(";", devices.Select(d => $"{d.Serial}:{d.State}"));

                if (signature != _lastSignature)
                {
                    _lastSignature = signature;
                    DevicesChanged?.Invoke(devices);

                    foreach (var device in devices.Where(d => d.IsReady))
                    {
                        if (knownReady.Add(device.Serial))
                        {
                            _log.Info(Loc.Tr(
                                $"Gerät verbunden: {device.DisplayName} ({device.Serial})",
                                $"Device connected: {device.DisplayName} ({device.Serial})"));
                            DeviceConnected?.Invoke(device);
                        }
                    }

                    knownReady.RemoveWhere(serial => devices.All(d => d.Serial != serial || !d.IsReady));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Debug(Loc.Tr("Geräteüberwachung: ", "Device watcher: ") + ex.Message);
            }

            try
            {
                await Task.Delay(_interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _cts = null;
    }
}
