using System.Diagnostics;
using Windows.Gaming.Input;
using OpenController.Battery;
using PowerBatteryStatus = Windows.System.Power.BatteryStatus;

namespace OpenController.Platform.Input.WindowsGaming;

/// <summary>
/// Windows.Gaming.Input battery reports. The percentage is computed from the remaining and full-charge
/// capacities the driver reports (both in mWh) — it is only produced when both values are present.
/// This source also provides the charging status, which XInput and GATT do not.
/// </summary>
public sealed class GamingInputBatteryMonitor : IDisposable
{
    private readonly Action<BatteryReading> _onReading;
    private readonly Action<string> _onDeviceGone;
    private readonly HashSet<string> _known = [];
    private bool _subscribed;

    public GamingInputBatteryMonitor(Action<BatteryReading> onReading, Action<string> onDeviceGone)
    {
        _onReading = onReading;
        _onDeviceGone = onDeviceGone;
    }

    public string? Error { get; private set; }

    public void Poll()
    {
        IReadOnlyList<Gamepad> pads;
        try
        {
            if (!_subscribed)
            {
                // Windows populates Gamepad.Gamepads lazily; subscribing starts enumeration.
                Gamepad.GamepadAdded += OnGamepadChanged;
                Gamepad.GamepadRemoved += OnGamepadChanged;
                _subscribed = true;
            }
            pads = Gamepad.Gamepads;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return;
        }

        var seen = new HashSet<string>();
        DateTimeOffset now = DateTimeOffset.Now;
        foreach (Gamepad pad in pads)
        {
            try
            {
                RawGameController? raw = RawGameController.FromGameController(pad);
                string id = raw?.NonRoamableId ?? pad.GetHashCode().ToString();
                Windows.Devices.Power.BatteryReport? report = pad.TryGetBatteryReport();
                if (report is null)
                    continue;

                int? remaining = report.RemainingCapacityInMilliwattHours;
                int? full = report.FullChargeCapacityInMilliwattHours;
                int? percent = remaining is { } r && full is { } f && f > 0 && r >= 0
                    ? (int)Math.Round(Math.Clamp(100.0 * r / f, 0, 100))
                    : null;
                ChargingState charging = report.Status switch
                {
                    PowerBatteryStatus.Charging => ChargingState.Charging,
                    PowerBatteryStatus.Discharging => ChargingState.Discharging,
                    PowerBatteryStatus.Idle => ChargingState.Idle,
                    PowerBatteryStatus.NotPresent => ChargingState.NotPresent,
                    _ => ChargingState.Unknown,
                };
                bool wired = report.Status == PowerBatteryStatus.NotPresent;
                if (percent is null && !wired)
                    continue;

                seen.Add(id);
                _known.Add(id);
                _onReading(new BatteryReading
                {
                    Source = BatterySourceKind.WindowsGamingInput,
                    SourceDeviceId = id,
                    SourceDeviceName = raw?.DisplayName,
                    VendorId = raw?.HardwareVendorId,
                    ProductId = raw?.HardwareProductId,
                    Percent = percent,
                    IsWired = wired,
                    // Shown in the UI so the derived percentage stays transparent (some drivers report coarse values).
                    Detail = $"{remaining?.ToString() ?? "?"}/{full?.ToString() ?? "?"} mWh · {report.Status} · {(pad.IsWireless ? "wireless" : "wired")}",
                    Charging = charging,
                    Timestamp = now,
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Gaming input battery read failed: {ex.Message}");
            }
        }

        foreach (string gone in _known.Where(k => !seen.Contains(k)).ToList())
        {
            _known.Remove(gone);
            _onDeviceGone(gone);
        }
    }

    private void OnGamepadChanged(object? sender, Gamepad e)
    {
        // Picked up on the next poll.
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            try
            {
                Gamepad.GamepadAdded -= OnGamepadChanged;
                Gamepad.GamepadRemoved -= OnGamepadChanged;
            }
            catch (Exception)
            {
            }
            _subscribed = false;
        }
    }
}
