using System.Diagnostics;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using OpenController.Models;
using OpenController.Platform.Output;

namespace OpenController.Platform.VirtualController;

public enum VirtualDriverStatus
{
    Unknown,
    /// <summary>ViGEmBus driver is installed and reachable.</summary>
    Available,
    /// <summary>ViGEmBus is not installed. Diagnostics keep working; virtual output is unavailable.</summary>
    NotInstalled,
    /// <summary>An incompatible ViGEmBus version is installed.</summary>
    VersionMismatch,
    Error,
}

public enum VirtualControllerState
{
    Stopped,
    Starting,
    Running,
    Faulted,
}

/// <summary>
/// Optional virtual Xbox 360 controller through the ViGEmBus driver (Nefarius.ViGEm.Client, MIT).
/// <para>
/// Safety rules implemented here:
/// <list type="bullet">
/// <item>No driver is ever installed by this application. If ViGEmBus is missing, the feature is simply unavailable.</item>
/// <item>The virtual device's XInput slot is reported through <see cref="ExcludedXInputSlotMask"/> so the sampler
/// never reads it back as a physical controller (no feedback loop). Until the slot is known, every slot that was
/// not occupied before the virtual device was created is excluded.</item>
/// <item>Stop / Dispose send a neutral report, unplug the device and release the driver handle.</item>
/// </list>
/// </para>
/// </summary>
public sealed class VirtualControllerService : IVirtualGamepadSink, IDisposable
{
    private readonly Lock _lock = new();
    private ViGEmClient? _client;
    private IXbox360Controller? _pad;
    private GamepadState _lastSent;
    private bool _hasSent;
    private volatile int _excludedMask;
    private volatile VirtualControllerState _state = VirtualControllerState.Stopped;
    private CancellationTokenSource? _slotCts;

    /// <summary>Raised (on a driver thread) when a game sets rumble: (large motor, small motor) 0..255.</summary>
    public event Action<byte, byte>? RumbleRequested;

    /// <summary>Raised when <see cref="State"/> or <see cref="UserIndex"/> changes (any thread).</summary>
    public event Action? StateChanged;

    public VirtualControllerState State => _state;
    public string? LastError { get; private set; }

    /// <summary>XInput user index of the virtual device once Windows reported it.</summary>
    public int? UserIndex { get; private set; }

    public bool IsActive => _state == VirtualControllerState.Running;

    public int ExcludedXInputSlotMask => _excludedMask;

    /// <summary>Checks whether the ViGEmBus driver can be reached, without creating a device.</summary>
    public static VirtualDriverStatus ProbeDriver(out string? detail)
    {
        detail = null;
        try
        {
            using var client = new ViGEmClient();
            return VirtualDriverStatus.Available;
        }
        catch (VigemBusNotFoundException)
        {
            return VirtualDriverStatus.NotInstalled;
        }
        catch (VigemBusVersionMismatchException ex)
        {
            detail = ex.Message;
            return VirtualDriverStatus.VersionMismatch;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return VirtualDriverStatus.Error;
        }
    }

    /// <summary>Creates and plugs in the virtual controller.</summary>
    /// <param name="occupiedXInputSlotMask">Slots occupied by physical controllers right now.</param>
    public bool Start(int occupiedXInputSlotMask)
    {
        lock (_lock)
        {
            if (_pad is not null)
                return true;

            SetState(VirtualControllerState.Starting);
            LastError = null;
            // Until Windows tells us which slot the virtual device got, hide every currently free slot.
            _excludedMask = ~occupiedXInputSlotMask & 0xF;
            try
            {
                _client = new ViGEmClient();
                _pad = _client.CreateXbox360Controller();
                _pad.AutoSubmitReport = false;
                _pad.FeedbackReceived += OnFeedback;
                _pad.Connect();
                _hasSent = false;
                SubmitLocked(GamepadState.Neutral);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                ReleaseLocked();
                SetState(VirtualControllerState.Faulted);
                return false;
            }

            SetState(VirtualControllerState.Running);
            _slotCts = new CancellationTokenSource();
            _ = ResolveUserIndexAsync(_pad, _slotCts.Token);
            return true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_pad is null && _state == VirtualControllerState.Stopped)
                return;
            ReleaseLocked();
            SetState(VirtualControllerState.Stopped);
        }
    }

    public void Submit(in GamepadState state)
    {
        lock (_lock)
        {
            if (_pad is null || _state != VirtualControllerState.Running)
                return;
            SubmitLocked(state);
        }
    }

    private void SubmitLocked(GamepadState s)
    {
        if (_hasSent && s == _lastSent)
            return;
        try
        {
            _pad!.SetButtonsFull((ushort)(s.Buttons & GamepadButtons.XInputMask));
            _pad.SetSliderValue(Xbox360Slider.LeftTrigger, s.LeftTrigger);
            _pad.SetSliderValue(Xbox360Slider.RightTrigger, s.RightTrigger);
            _pad.SetAxisValue(Xbox360Axis.LeftThumbX, s.LeftX);
            _pad.SetAxisValue(Xbox360Axis.LeftThumbY, s.LeftY);
            _pad.SetAxisValue(Xbox360Axis.RightThumbX, s.RightX);
            _pad.SetAxisValue(Xbox360Axis.RightThumbY, s.RightY);
            _pad.SubmitReport();
            _lastSent = s;
            _hasSent = true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Trace.WriteLine($"Virtual controller report failed: {ex.Message}");
            ReleaseLocked();
            SetState(VirtualControllerState.Faulted);
        }
    }

    private async Task ResolveUserIndexAsync(IXbox360Controller pad, CancellationToken ct)
    {
        // The XInput slot is assigned asynchronously after plug-in; poll briefly.
        for (int attempt = 0; attempt < 40 && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                int index = pad.UserIndex;
                if (index is >= 0 and < 4)
                {
                    lock (_lock)
                    {
                        if (!ReferenceEquals(pad, _pad))
                            return;
                        UserIndex = index;
                        _excludedMask = 1 << index;
                    }
                    StateChanged?.Invoke();
                    return;
                }
            }
            catch (Exception)
            {
                // Not reported yet.
            }

            try
            {
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        LastError = "Windows did not report the virtual controller's XInput slot; newly appearing XInput slots stay hidden while it runs.";
        StateChanged?.Invoke();
    }

    private void OnFeedback(object? sender, Xbox360FeedbackReceivedEventArgs e) =>
        RumbleRequested?.Invoke(e.LargeMotor, e.SmallMotor);

    private void ReleaseLocked()
    {
        _slotCts?.Cancel();
        _slotCts?.Dispose();
        _slotCts = null;

        if (_pad is not null)
        {
            try
            {
                _pad.FeedbackReceived -= OnFeedback;
                _pad.ResetReport();
                _pad.SubmitReport();
            }
            catch (Exception)
            {
                // Best effort: the device is being removed anyway.
            }

            try
            {
                _pad.Disconnect();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Virtual controller disconnect failed: {ex.Message}");
            }
            _pad = null;
        }

        _client?.Dispose();
        _client = null;
        UserIndex = null;
        _excludedMask = 0;
        _hasSent = false;
    }

    private void SetState(VirtualControllerState state)
    {
        _state = state;
        StateChanged?.Invoke();
    }

    public void Dispose() => Stop();
}
