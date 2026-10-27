using System.Collections.Concurrent;
using System.Diagnostics;
using OpenController.Battery;
using OpenController.Models;
using OpenController.Platform.Input.Bluetooth;
using OpenController.Platform.Input.WindowsGaming;
using OpenController.Platform.Input.XInput;

namespace OpenController.Platform.Input;

/// <summary>Battery state for one connected controller.</summary>
public sealed record ControllerBattery(string ControllerId, BatteryStatus Status, AssociationConfidence? Confidence);

/// <summary>Everything the UI needs to show battery information.</summary>
public sealed record BatteryOverview(
    IReadOnlyDictionary<string, ControllerBattery> ByController,
    IReadOnlyList<BatteryReading> Unassociated,
    IReadOnlyList<BatteryReading> AllReadings,
    IReadOnlyCollection<BleDeviceStatus> BleDevices,
    DateTimeOffset? LastPoll);

/// <summary>
/// Collects battery readings from every source on a background task (never on the UI thread),
/// with a configurable interval, per-operation timeouts and cancellation.
/// XInput coarse levels are polled every 5 s; the Bluetooth/Windows sources at <see cref="PollInterval"/>.
/// </summary>
public sealed class BatteryService : IDisposable
{
    private static readonly TimeSpan XInputInterval = TimeSpan.FromSeconds(5);

    private readonly InputService _input;
    private readonly ConcurrentDictionary<(BatterySourceKind, string), BatteryReading> _readings = new();
    private readonly BleBatteryMonitor _ble;
    private readonly WindowsBatteryPropertyMonitor _property;
    private readonly GamingInputBatteryMonitor _gamingInput;
    private readonly AutoResetEvent _refresh = new(false);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private DateTimeOffset? _lastPoll;
    private long _pollIntervalTicks = TimeSpan.FromSeconds(60).Ticks;
    private int _lowPercent = 20;

    public BatteryService(InputService input)
    {
        _input = input;
        _ble = new BleBatteryMonitor(Store, id => Remove(BatterySourceKind.BleGatt, id));
        _property = new WindowsBatteryPropertyMonitor(Store, id => Remove(BatterySourceKind.WindowsDeviceProperty, id));
        _gamingInput = new GamingInputBatteryMonitor(Store, id => Remove(BatterySourceKind.WindowsGamingInput, id));
    }

    /// <summary>Raised on a background thread after each poll.</summary>
    public event Action? Updated;

    public TimeSpan PollInterval
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _pollIntervalTicks));
        set => Interlocked.Exchange(ref _pollIntervalTicks, Math.Clamp(value.Ticks, TimeSpan.FromSeconds(10).Ticks, TimeSpan.FromMinutes(10).Ticks));
    }

    public int LowPercentThreshold
    {
        get => Volatile.Read(ref _lowPercent);
        set => Volatile.Write(ref _lowPercent, Math.Clamp(value, 5, 50));
    }

    public BatteryPolicy Policy => BatteryPolicy.ForPollInterval(PollInterval, LowPercentThreshold);

    public void Start()
    {
        if (_loop is not null)
            return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Requests an immediate poll of all sources.</summary>
    public void RefreshNow() => _refresh.Set();

    private async Task RunAsync(CancellationToken ct)
    {
        DateTimeOffset nextSlow = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            // Local, cheap sources: every cycle.
            PollXInput(now);
            await RunSource(() => { _gamingInput.Poll(); return Task.CompletedTask; }, "Windows.Gaming.Input");

            // Bluetooth sources: at the configured (slower) interval.
            if (now >= nextSlow)
            {
                nextSlow = now + PollInterval;
                await RunSource(() => _ble.PollAsync(ct), "BLE GATT");
                await RunSource(() => _property.PollAsync(ct), "Windows battery property");
                _lastPoll = DateTimeOffset.Now;
            }

            Updated?.Invoke();

            // Wait for the next XInput poll or an explicit refresh request.
            int signaled = await Task.Run(() => WaitHandle.WaitAny([_refresh, ct.WaitHandle], XInputInterval), CancellationToken.None);
            if (signaled == 0)
                nextSlow = DateTimeOffset.MinValue;
        }

        async Task RunSource(Func<Task> poll, string name)
        {
            try
            {
                await poll();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{name} battery poll failed: {ex.Message}");
            }
        }
    }

    private void PollXInput(DateTimeOffset now)
    {
        var connectedSlots = new HashSet<int>();
        foreach (ControllerSnapshot c in _input.Latest.Controllers)
        {
            if (c.Info.XInputSlot is not { } slot)
                continue;
            connectedSlots.Add(slot);
            BatteryReading? r = XInputReader.ReadBattery(slot, now);
            if (r is not null)
                Store(r);
        }

        for (int slot = 0; slot < XInputReader.SlotCount; slot++)
        {
            if (!connectedSlots.Contains(slot))
                Remove(BatterySourceKind.XInputCoarse, XInputReader.IdForSlot(slot));
        }
    }

    private void Store(BatteryReading reading) => _readings[(reading.Source, reading.SourceDeviceId)] = reading;

    private void Remove(BatterySourceKind kind, string id) => _readings.TryRemove((kind, id), out _);

    /// <summary>Computes the current per-controller battery view.</summary>
    public BatteryOverview GetOverview()
    {
        IReadOnlyList<ControllerInfo> controllers = _input.Latest.Controllers.Select(c => c.Info).ToList();
        List<BatteryReading> all = _readings.Values.ToList();
        Dictionary<string, List<AssociatedReading>> map = BatteryAssociator.Associate(controllers, all, out List<BatteryReading> unassociated);
        DateTimeOffset now = DateTimeOffset.Now;
        BatteryPolicy policy = Policy;
        var byController = new Dictionary<string, ControllerBattery>(StringComparer.Ordinal);
        foreach ((string id, List<AssociatedReading> readings) in map)
        {
            BatteryStatus status = BatterySelector.Select(readings.Select(r => r.Reading), now, policy);
            AssociationConfidence? confidence = status.Selected is null
                ? null
                : readings.First(r => ReferenceEquals(r.Reading, status.Selected)).Confidence;
            byController[id] = new ControllerBattery(id, status, confidence);
        }
        return new BatteryOverview(byController, unassociated, all, _ble.DeviceStatus, _lastPoll);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }
        _cts?.Dispose();
        _ble.Dispose();
        _gamingInput.Dispose();
        _refresh.Dispose();
    }
}
