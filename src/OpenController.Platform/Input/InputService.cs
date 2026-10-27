using System.Collections.Concurrent;
using System.Diagnostics;
using OpenController.Devices;
using OpenController.Diagnostics;
using OpenController.Mapping;
using OpenController.Models;
using OpenController.Platform.Input.DirectInput;
using OpenController.Platform.Input.XInput;
using OpenController.Platform.Native;
using OpenController.Platform.Output;
using OpenController.Processing;
using OpenController.Profiles;
using Vortice.DirectInput;

namespace OpenController.Platform.Input;

/// <summary>
/// Owns the high-frequency sampling thread. Each tick it reads every connected controller, runs the
/// processing pipeline and remapper with the active profile, routes the output of the selected source
/// controller to the virtual gamepad / keyboard, records history and publishes an immutable
/// <see cref="InputSnapshot"/>. The UI reads snapshots at its own (lower) frame rate.
/// <para>Threading: all device handles live on the sampling thread. Other threads only swap immutable
/// references (profile, settings) or read published snapshots and history rings.</para>
/// </summary>
public sealed class InputService : IDisposable
{
    private const int HistorySeconds = 30;
    private static readonly TimeSpan DisconnectedProbeInterval = TimeSpan.FromSeconds(1);
    private const int DirectInputFailureLimit = 50;

    private readonly ControllerRegistry _registry = new();
    private readonly ConcurrentDictionary<string, SampleRing<InputSample>> _history = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ControllerRuntime> _runtime = new(StringComparer.Ordinal);
    private readonly XInputSlotState[] _slots = Enumerable.Range(0, XInputReader.SlotCount).Select(_ => new XInputSlotState()).ToArray();
    private readonly Dictionary<Guid, DirectInputDevice> _diDevices = [];
    private readonly HashSet<Guid> _diFailedOpen = [];
    private readonly DirectInputEnumerator _enumerator = new();
    private readonly Lock _outputLock = new();

    private Thread? _thread;
    private volatile bool _stopRequested;
    private volatile int _samplingRateHz = 250;
    private nint _windowHandle;

    private Profile _profile = ProfilePresets.Default();
    private ProcessingSettings _processing = ProfilePresets.Default().ToProcessingSettings();
    private InputSnapshot _latest = InputSnapshot.Empty;
    private string? _outputSourceId;
    private bool _outputSourceAuto = true;
    private IVirtualGamepadSink? _virtualSink;
    private KeyboardOutput? _keyboard;
    private DeviceScanResult? _appliedScan;
    private IDirectInput8? _directInput;
    private string? _directInputError;
    private bool _outputWasLive;

    /// <summary>Raised on the sampling thread whenever controllers connect, disconnect or change.</summary>
    public event Action<IReadOnlyList<ConnectionEvent>>? ConnectionChanged;

    /// <summary>Raised on the sampling thread when a controller's input stalled while moving (gap in ms).</summary>
    public event Action<ControllerInfo, double, DateTimeOffset>? StallDetected;

    public InputSnapshot Latest => Volatile.Read(ref _latest);

    public int SamplingRateHz
    {
        get => _samplingRateHz;
        set => _samplingRateHz = Math.Clamp(value, 60, 1000);
    }

    public Profile ActiveProfile => Volatile.Read(ref _profile);

    /// <summary>Atomically switches the profile used by the sampling thread. Invalid profiles are rejected.</summary>
    public void SetProfile(Profile profile)
    {
        IReadOnlyList<string> errors = profile.Validate();
        if (errors.Count > 0)
            throw new ArgumentException("Invalid profile: " + string.Join(" ", errors), nameof(profile));
        Volatile.Write(ref _processing, profile.ToProcessingSettings());
        Volatile.Write(ref _profile, profile);
    }

    /// <summary>
    /// Selects which controller drives the virtual gamepad and keyboard output. Null with
    /// <paramref name="automatic"/> = true picks the first connected physical controller.
    /// </summary>
    public void SetOutputSource(string? controllerId, bool automatic)
    {
        lock (_outputLock)
        {
            _outputSourceAuto = automatic;
            _outputSourceId = automatic ? null : controllerId;
        }
    }

    public void AttachVirtualSink(IVirtualGamepadSink? sink) => Volatile.Write(ref _virtualSink, sink);

    public void AttachKeyboard(KeyboardOutput? keyboard) => Volatile.Write(ref _keyboard, keyboard);

    /// <summary>History ring for a controller id (null if the controller was never seen).</summary>
    public SampleRing<InputSample>? GetHistory(string controllerId) => _history.GetValueOrDefault(controllerId);

    /// <summary>Requests an immediate device rescan (e.g. after WM_DEVICECHANGE).</summary>
    public void RequestRescan()
    {
        _enumerator.RequestRescan();
        foreach (XInputSlotState s in _slots)
            s.NextProbe = 0;
    }

    /// <summary>Forwards rumble to a physical XInput controller (any thread).</summary>
    public void SetRumble(string controllerId, ushort leftMotor, ushort rightMotor)
    {
        ControllerSnapshot? c = Latest.Find(controllerId);
        if (c?.Info.XInputSlot is { } slot)
            XInputReader.SetVibration(slot, leftMotor, rightMotor);
    }

    /// <param name="windowHandle">Top-level window for DirectInput's cooperative level.</param>
    public void Start(nint windowHandle)
    {
        if (_thread is not null)
            return;
        _windowHandle = windowHandle;
        _stopRequested = false;
        _enumerator.Start();
        _thread = new Thread(Run) { IsBackground = true, Name = "XCC input sampler", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Stop()
    {
        _stopRequested = true;
        if (_thread is { IsAlive: true })
            _thread.Join(TimeSpan.FromSeconds(3));
        _thread = null;
    }

    public void Dispose()
    {
        Stop();
        _enumerator.Dispose();
    }

    // ------------------------------------------------------------------ sampling thread

    private void Run()
    {
        using var timer = new HighResolutionTimer();
        var loopRate = new RateMeter(Stopwatch.Frequency, TimeSpan.FromSeconds(1));
        try
        {
            _directInput = DInput.DirectInput8Create();
        }
        catch (Exception ex)
        {
            _directInputError = $"DirectInput unavailable: {ex.Message}";
        }

        long next = Stopwatch.GetTimestamp();
        try
        {
            while (!_stopRequested)
            {
                long period = Stopwatch.Frequency / _samplingRateHz;
                long now = Stopwatch.GetTimestamp();
                loopRate.Record(now);
                try
                {
                    Tick(now, loopRate.RatePerSecond(now), timer.IsHighResolution);
                }
                catch (Exception ex)
                {
                    // Never let one bad read kill the sampler; surface it in the snapshot instead.
                    Trace.WriteLine($"Input tick failed: {ex}");
                }

                next += period;
                long after = Stopwatch.GetTimestamp();
                if (next < after - (period * 4))
                    next = after + period; // fell far behind (e.g. system sleep); resynchronize
                timer.WaitUntil(next);
            }
        }
        finally
        {
            ShutdownOutputs();
            foreach (DirectInputDevice d in _diDevices.Values)
                d.Dispose();
            _diDevices.Clear();
            _directInput?.Dispose();
            _directInput = null;
        }
    }

    private void Tick(long now, double loopRateHz, bool highResTimer)
    {
        var events = new List<ConnectionEvent>();
        DateTimeOffset wallNow = DateTimeOffset.Now;
        IVirtualGamepadSink? sink = Volatile.Read(ref _virtualSink);
        int excludedMask = sink?.ExcludedXInputSlotMask ?? 0;

        DeviceScanResult? scan = _enumerator.Latest;
        bool scanChanged = !ReferenceEquals(scan, _appliedScan);
        _appliedScan = scan;
        IReadOnlyList<HidInterfaceInfo> hids = scan?.HidInterfaces ?? [];
        IReadOnlyList<ushort> usbIds = scan?.UsbMicrosoftProductIds ?? [];

        PollXInput(now, hids, usbIds, scanChanged, excludedMask, wallNow, events);
        PollDirectInput(scan, scanChanged, wallNow, events);

        // ---- processing for every connected controller
        Profile profile = Volatile.Read(ref _profile);
        ProcessingSettings processing = Volatile.Read(ref _processing);
        var snapshots = new List<ControllerSnapshot>(_runtime.Count);
        List<(ControllerInfo Info, double Ms)>? stalls = null;
        foreach (ControllerRuntime rt in _runtime.Values)
        {
            if (!rt.HasState)
                continue;
            ProcessedState processed = InputProcessor.Process(rt.Raw, processing);
            OutputFrame output = ButtonRemapper.Apply(processed, profile.ButtonMap);
            if (rt.PacketChanged)
            {
                rt.ChangeRate.Record(now);
                if (rt.Link.OnUpdate(now, rt.Raw) is { } stallMs)
                    (stalls ??= []).Add((rt.Info, stallMs));
            }
            rt.History.Add(new InputSample(now, rt.Raw, processed.LeftStick, processed.RightStick,
                (float)processed.LeftTrigger, (float)processed.RightTrigger));
            snapshots.Add(new ControllerSnapshot(rt.Info, rt.Raw, processed, output, rt.Packet,
                rt.ChangeRate.RatePerSecond(now), rt.DirectInputRaw, now, rt.Link.Stats));
        }

        snapshots.Sort((a, b) => string.CompareOrdinal(a.Info.Id, b.Info.Id));
        string? sourceId = RouteOutput(snapshots, sink);

        Volatile.Write(ref _latest, new InputSnapshot
        {
            Timestamp = now,
            Controllers = snapshots,
            SamplerRateHz = loopRateHz,
            TargetRateHz = _samplingRateHz,
            HighResolutionTimer = highResTimer,
            XInputAvailable = XInputReader.IsAvailable,
            GuideButtonSupported = XInputReader.SupportsGuideButton,
            DirectInputAvailable = _directInput is not null,
            DirectInputError = _directInputError ?? scan?.DirectInputError,
            ExcludedXInputSlotMask = excludedMask,
            OutputSourceId = sourceId,
        });

        if (events.Count > 0)
        {
            try
            {
                ConnectionChanged?.Invoke(events);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ConnectionChanged handler failed: {ex}");
            }
        }

        foreach ((ControllerInfo info, double ms) in stalls ?? [])
        {
            try
            {
                StallDetected?.Invoke(info, ms, wallNow);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"StallDetected handler failed: {ex}");
            }
        }
    }

    private void PollXInput(long now, IReadOnlyList<HidInterfaceInfo> hids, IReadOnlyList<ushort> usbIds, bool scanChanged, int excludedMask,
        DateTimeOffset wallNow, List<ConnectionEvent> events)
    {
        if (!XInputReader.IsAvailable)
            return;

        bool membershipChanged = false;
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            XInputSlotState st = _slots[slot];
            bool excluded = (excludedMask & (1 << slot)) != 0;

            // Disconnected slots are probed only once per second: XInputGetState on an empty slot is slow.
            if (!st.Connected && now < st.NextProbe)
                continue;

            GamepadState state = default;
            uint packet = 0;
            bool ok = !excluded && XInputReader.TryRead(slot, out state, out packet);
            string id = XInputReader.IdForSlot(slot);
            if (!ok)
            {
                if (st.Connected)
                {
                    st.Connected = false;
                    membershipChanged = true;
                    _runtime.Remove(id);
                }
                st.ScanWaitUntil = 0;
                st.NextProbe = now + (long)(DisconnectedProbeInterval.TotalSeconds * Stopwatch.Frequency);
                continue;
            }

            if (!st.Connected || (scanChanged && st.Info?.Connection == ConnectionType.Unknown))
            {
                ControllerInfo info = XInputReader.DescribeSlot(slot, hids, usbIds);
                if (!st.Connected && info.ConnectionEvidence == "GipAmbiguous" && !scanChanged)
                {
                    // The current scan predates the controller; a fresh one may find its USB device node. Wait for it
                    // briefly so the connection is reported (and logged) with the right model and transport.
                    if (st.ScanWaitUntil == 0)
                    {
                        st.ScanWaitUntil = now + Stopwatch.Frequency;
                        _enumerator.RequestRescan();
                    }
                    if (now < st.ScanWaitUntil)
                        continue;
                }
                if (!st.Connected)
                {
                    st.ScanWaitUntil = 0;
                    st.Connected = true;
                    membershipChanged = true;
                    st.Info = info;
                    _runtime[id] = new ControllerRuntime(info, GetOrResetHistory(id, reset: true));
                }
                else if (info != st.Info)
                {
                    st.Info = info;
                    _runtime[id].Info = info;
                    membershipChanged = true;
                }
            }

            ControllerRuntime rt = _runtime[id];
            rt.PacketChanged = !rt.HasState || packet != rt.Packet;
            rt.Raw = state;
            rt.Packet = packet;
            rt.HasState = true;
        }

        if (membershipChanged)
        {
            IEnumerable<ControllerInfo> present = _slots.Where(s => s.Connected && s.Info is not null).Select(s => s.Info!);
            events.AddRange(_registry.Update(InputApi.XInput, present, wallNow));
        }
    }

    private void PollDirectInput(DeviceScanResult? scan, bool scanChanged, DateTimeOffset wallNow, List<ConnectionEvent> events)
    {
        if (_directInput is null)
            return;

        bool membershipChanged = false;
        if (scanChanged && scan is not null)
        {
            var attached = scan.DirectInputDevices.ToDictionary(d => d.InstanceGuid);
            foreach (Guid gone in _diDevices.Keys.Where(g => !attached.ContainsKey(g)).ToList())
            {
                CloseDirectInput(gone);
                membershipChanged = true;
            }
            _diFailedOpen.IntersectWith(attached.Keys);
            foreach (DirectInputDeviceDescriptor d in attached.Values)
            {
                if (_diDevices.ContainsKey(d.InstanceGuid) || _diFailedOpen.Contains(d.InstanceGuid))
                    continue;
                try
                {
                    var dev = new DirectInputDevice(_directInput, d, _windowHandle);
                    // A device whose interface path says it is XInput-capable belongs to XInput.
                    if (DevicePathParser.IsXInputPath(dev.InterfacePath))
                    {
                        dev.Dispose();
                        _diFailedOpen.Add(d.InstanceGuid);
                        continue;
                    }
                    _diDevices[d.InstanceGuid] = dev;
                    _runtime[dev.Info.Id] = new ControllerRuntime(dev.Info, GetOrResetHistory(dev.Info.Id, reset: true));
                    membershipChanged = true;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Opening DirectInput device {d.ProductName} failed: {ex.Message}");
                    _diFailedOpen.Add(d.InstanceGuid);
                }
            }
        }

        List<Guid>? lost = null;
        foreach ((Guid guid, DirectInputDevice dev) in _diDevices)
        {
            if (dev.TryRead(out GamepadState state, out DirectInputRawState raw))
            {
                ControllerRuntime rt = _runtime[dev.Info.Id];
                rt.PacketChanged = !rt.HasState || state != rt.Raw;
                rt.Raw = state;
                rt.DirectInputRaw = raw;
                rt.HasState = true;
            }
            else if (dev.ConsecutiveFailures > DirectInputFailureLimit)
            {
                (lost ??= []).Add(guid);
            }
        }

        // Unplugged or lost; the next scan re-adds a device that is still attached.
        foreach (Guid guid in lost ?? [])
        {
            CloseDirectInput(guid);
            membershipChanged = true;
        }

        if (membershipChanged)
            events.AddRange(_registry.Update(InputApi.DirectInput, _diDevices.Values.Select(d => d.Info), wallNow));
    }

    private void CloseDirectInput(Guid guid)
    {
        if (_diDevices.Remove(guid, out DirectInputDevice? dev))
        {
            _runtime.Remove(dev.Info.Id);
            dev.Dispose();
        }
    }

    private SampleRing<InputSample> GetOrResetHistory(string id, bool reset)
    {
        SampleRing<InputSample> ring = _history.GetOrAdd(id, _ => new SampleRing<InputSample>(HistorySeconds * 1000));
        if (reset)
            ring.Clear();
        return ring;
    }

    /// <summary>Sends the selected controller's output to the sinks; neutral when it is gone.</summary>
    private string? RouteOutput(List<ControllerSnapshot> snapshots, IVirtualGamepadSink? sink)
    {
        string? requested;
        bool auto;
        lock (_outputLock)
        {
            requested = _outputSourceId;
            auto = _outputSourceAuto;
        }

        ControllerSnapshot? source = auto
            ? snapshots.FirstOrDefault(s => s.Info.Api == InputApi.XInput) ?? snapshots.FirstOrDefault()
            : snapshots.FirstOrDefault(s => s.Info.Id == requested);
        KeyboardOutput? keyboard = Volatile.Read(ref _keyboard);

        if (source is null)
        {
            if (_outputWasLive)
            {
                // Source disconnected: return everything to neutral immediately.
                sink?.Submit(GamepadState.Neutral);
                keyboard?.ReleaseAll();
                _outputWasLive = false;
            }
            return auto ? null : requested;
        }

        if (sink is { IsActive: true })
        {
            GamepadState g = source.Output.Gamepad;
            sink.Submit(g with { Buttons = g.Buttons & GamepadButtons.XInputMask });
        }
        keyboard?.Update(source.Output.Keys);
        _outputWasLive = true;
        return source.Info.Id;
    }

    private void ShutdownOutputs()
    {
        try
        {
            Volatile.Read(ref _virtualSink)?.Submit(GamepadState.Neutral);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Neutral report on shutdown failed: {ex.Message}");
        }
        Volatile.Read(ref _keyboard)?.ReleaseAll();
    }

    private sealed class XInputSlotState
    {
        public bool Connected;
        public long NextProbe;
        public ControllerInfo? Info;
        /// <summary>Non-zero while a new GIP controller waits for a device scan that can identify it.</summary>
        public long ScanWaitUntil;
    }

    private sealed class ControllerRuntime(ControllerInfo info, SampleRing<InputSample> history)
    {
        public ControllerInfo Info = info;
        public readonly SampleRing<InputSample> History = history;
        public readonly RateMeter ChangeRate = new(Stopwatch.Frequency, TimeSpan.FromSeconds(1));
        public readonly LinkQualityMonitor Link = new(Stopwatch.Frequency);
        public GamepadState Raw;
        public uint Packet;
        public bool HasState;
        public bool PacketChanged;
        public DirectInputRawState? DirectInputRaw;
    }
}
