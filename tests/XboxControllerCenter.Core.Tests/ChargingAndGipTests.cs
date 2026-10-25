using XboxControllerCenter.Battery;
using XboxControllerCenter.Devices;
using XboxControllerCenter.Models;

namespace XboxControllerCenter.Tests;

public class ChargingAndGipTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 3, 0, 0, TimeSpan.Zero);

    private static BatteryStatus Status(int percent, ChargingState charging, DateTimeOffset time,
        BatteryFreshness freshness = BatteryFreshness.Fresh) => new()
    {
        Selected = new BatteryReading
        {
            Source = BatterySourceKind.WindowsGamingInput,
            SourceDeviceId = "wgi",
            Percent = percent,
            Charging = charging,
            Timestamp = time,
        },
        Freshness = freshness,
        Charging = charging,
    };

    // ---------- Level from before charging ----------

    [Fact]
    public void Tracker_ReturnsLevelFromBeforeTheCableWhileCharging()
    {
        var tracker = new UnpluggedLevelTracker();
        Assert.Null(tracker.Update("xinput:0", Status(10, ChargingState.Discharging, T0), T0));

        UnpluggedLevel? before = tracker.Update("xinput:0", Status(40, ChargingState.Charging, T0.AddMinutes(2)), T0.AddMinutes(2));

        Assert.NotNull(before);
        Assert.Equal(10, before.Percent);
        Assert.Equal(T0, before.Time);
    }

    [Fact]
    public void Tracker_ReturnsNothingWhenNotCharging()
    {
        var tracker = new UnpluggedLevelTracker();
        tracker.Update("xinput:0", Status(10, ChargingState.Discharging, T0), T0);
        Assert.Null(tracker.Update("xinput:0", Status(9, ChargingState.Discharging, T0.AddMinutes(1)), T0.AddMinutes(1)));
    }

    [Fact]
    public void Tracker_IgnoresStaleAndChargingReadingsWhenRemembering()
    {
        var tracker = new UnpluggedLevelTracker();
        tracker.Update("xinput:0", Status(10, ChargingState.Discharging, T0), T0);
        tracker.Update("xinput:0", Status(80, ChargingState.Discharging, T0.AddMinutes(1), BatteryFreshness.Stale), T0.AddMinutes(1));
        tracker.Update("xinput:0", Status(40, ChargingState.Charging, T0.AddMinutes(2)), T0.AddMinutes(2));

        UnpluggedLevel? before = tracker.Update("xinput:0", Status(50, ChargingState.Charging, T0.AddMinutes(3)), T0.AddMinutes(3));

        Assert.Equal(10, before?.Percent);
    }

    [Fact]
    public void Tracker_ForgetsOldLevelsAndKeepsControllersApart()
    {
        var tracker = new UnpluggedLevelTracker();
        tracker.Update("xinput:0", Status(10, ChargingState.Discharging, T0), T0);

        Assert.Null(tracker.Update("xinput:1", Status(40, ChargingState.Charging, T0), T0));
        DateTimeOffset later = T0 + UnpluggedLevelTracker.MaxAge + TimeSpan.FromMinutes(1);
        Assert.Null(tracker.Update("xinput:0", Status(40, ChargingState.Charging, later), later));
    }

    // ---------- GIP controller on a USB cable ----------

    [Fact]
    public void Gip_ResolvesTheOnlyXboxControllerOnUsb()
    {
        // Observed while charging: USB\VID_045E&PID_0B12 (Xbox Series) plus the 02FF XInput interface child.
        Assert.True(ControllerCatalog.TryResolveGipOverUsb([0x0B12, 0x02FF], 1, out ControllerFamily family, out string name));
        Assert.Equal(ControllerFamily.XboxSeries, family);
        Assert.Equal("Xbox Series X|S Controller", name);
    }

    [Fact]
    public void Gip_StaysAmbiguousWithoutExactlyOneMatch()
    {
        // Xbox Wireless Adapter only: no controller on USB.
        Assert.False(ControllerCatalog.TryResolveGipOverUsb([0x02FE], 1, out _, out _));
        // Two GIP controllers: cannot tell which one is on the cable.
        Assert.False(ControllerCatalog.TryResolveGipOverUsb([0x0B12], 2, out _, out _));
        // Two different wired models.
        Assert.False(ControllerCatalog.TryResolveGipOverUsb([0x0B12, 0x0B00], 1, out _, out _));
        // Xbox 360 pads are not GIP devices.
        Assert.False(ControllerCatalog.TryResolveGipOverUsb([0x028E], 1, out _, out _));
    }

    [Fact]
    public void Gip_DuplicateInstancesOfTheSameModelCountOnce()
    {
        Assert.True(ControllerCatalog.TryResolveGipOverUsb([0x0B12, 0x0B12], 1, out _, out _));
    }
}
