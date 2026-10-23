using XboxControllerCenter.Battery;
using XboxControllerCenter.Devices;
using XboxControllerCenter.Models;

namespace XboxControllerCenter.Tests;

public class DeviceAndBatteryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static ControllerInfo XInput(int slot, ushort? pid = null, ConnectionType conn = ConnectionType.Unknown) => new()
    {
        Id = $"xinput:{slot}",
        DisplayName = "Xbox Controller",
        Api = InputApi.XInput,
        XInputSlot = slot,
        VendorId = pid is null ? null : ControllerCatalog.MicrosoftVendorId,
        ProductId = pid,
        Connection = conn,
    };

    // ---------- Connection state ----------

    [Fact]
    public void Registry_ConnectDisconnectReconnect()
    {
        var reg = new ControllerRegistry();
        var e1 = reg.Update(InputApi.XInput, [XInput(0)], T0);
        Assert.Equal(ConnectionEventKind.Connected, Assert.Single(e1).Kind);

        Assert.Empty(reg.Update(InputApi.XInput, [XInput(0)], T0)); // no change

        var e2 = reg.Update(InputApi.XInput, [], T0);
        Assert.Equal(ConnectionEventKind.Disconnected, Assert.Single(e2).Kind);
        Assert.False(reg.IsConnected("xinput:0"));

        var e3 = reg.Update(InputApi.XInput, [XInput(0)], T0);
        Assert.Equal(ConnectionEventKind.Reconnected, Assert.Single(e3).Kind);
    }

    [Fact]
    public void Registry_InfoChangeIsReported()
    {
        var reg = new ControllerRegistry();
        reg.Update(InputApi.XInput, [XInput(1)], T0);
        var e = reg.Update(InputApi.XInput, [XInput(1, 0x0B13, ConnectionType.Bluetooth)], T0);
        Assert.Equal(ConnectionEventKind.InfoChanged, Assert.Single(e).Kind);
        Assert.Equal(ConnectionType.Bluetooth, reg.Get("xinput:1")!.Connection);
    }

    [Fact]
    public void Registry_ApisAreIndependent()
    {
        var reg = new ControllerRegistry();
        reg.Update(InputApi.XInput, [XInput(0)], T0);
        var di = new ControllerInfo { Id = "dinput:1", DisplayName = "Pad", Api = InputApi.DirectInput };
        reg.Update(InputApi.DirectInput, [di], T0);
        Assert.Empty(reg.Update(InputApi.DirectInput, [di], T0));
        Assert.Equal(2, reg.Connected.Count);
        reg.Update(InputApi.DirectInput, [], T0);
        Assert.True(reg.IsConnected("xinput:0"));
        Assert.Single(reg.Connected);
    }

    [Fact]
    public void Registry_RejectsWrongApi()
    {
        var reg = new ControllerRegistry();
        Assert.Throws<ArgumentException>(() => reg.Update(InputApi.DirectInput, [XInput(0)], T0));
    }

    [Fact]
    public void Registry_MarkDisconnected()
    {
        var reg = new ControllerRegistry();
        reg.Update(InputApi.XInput, [XInput(2)], T0);
        Assert.Equal(ConnectionEventKind.Disconnected, reg.MarkDisconnected("xinput:2", T0)!.Value.Kind);
        Assert.Null(reg.MarkDisconnected("xinput:2", T0));
    }

    // ---------- Device paths / dedup ----------

    [Theory]
    [InlineData(@"\\?\HID#VID_045E&PID_0B12&IG_00#8&1a2b&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", 0x045E, 0x0B12, true, ConnectionType.Usb)]
    [InlineData(@"\\?\HID#{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&02045e_PID&0b13_REV&0509_aabbccddeeff&IG_00#9&1&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", 0x045E, 0x0B13, true, ConnectionType.Bluetooth)]
    [InlineData(@"\\?\HID#VID_054C&PID_09CC&MI_03#7&abc&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", 0x054C, 0x09CC, false, ConnectionType.Usb)]
    public void DevicePath_Parsing(string path, int vid, int pid, bool isXInput, ConnectionType transport)
    {
        Assert.True(DevicePathParser.TryParseVidPid(path, out ushort v, out ushort p));
        Assert.Equal(vid, v);
        Assert.Equal(pid, p);
        Assert.Equal(isXInput, DevicePathParser.IsXInputPath(path));
        Assert.Equal(transport, DevicePathParser.GuessTransport(path));
    }

    [Fact]
    public void DevicePath_UnparseableReturnsFalse()
    {
        Assert.False(DevicePathParser.TryParseVidPid(@"\\?\ROOT#SYSTEM#0000", out _, out _));
        Assert.False(DevicePathParser.TryParseVidPid(null, out _, out _));
        Assert.Equal(ConnectionType.Unknown, DevicePathParser.GuessTransport(@"\\?\ROOT#SYSTEM"));
    }

    [Fact]
    public void DirectInputProductGuid_Decodes()
    {
        var guid = new Guid("0B12045E-0000-0000-0000-504944564944");
        Assert.True(DevicePathParser.TryDecodeDirectInputProductGuid(guid, out ushort v, out ushort p));
        Assert.Equal(0x045E, v);
        Assert.Equal(0x0B12, p);
        Assert.False(DevicePathParser.TryDecodeDirectInputProductGuid(Guid.NewGuid(), out _, out _));
    }

    [Fact]
    public void Dedup_HidesXInputControllersInDirectInput()
    {
        HidInterfaceInfo[] hids =
        [
            new(@"\\?\HID#VID_045E&PID_0B12&IG_00#x", 0x045E, 0x0B12, true),
            new(@"\\?\HID#VID_054C&PID_09CC#x", 0x054C, 0x09CC, false),
        ];
        var xinputSet = DeviceDeduplicator.XInputVidPids(hids);
        Assert.True(DeviceDeduplicator.IsXInputDuplicate(new Guid("0B12045E-0000-0000-0000-504944564944"), null, xinputSet));
        Assert.False(DeviceDeduplicator.IsXInputDuplicate(new Guid("09CC054C-0000-0000-0000-504944564944"), null, xinputSet));
        Assert.True(DeviceDeduplicator.IsXInputDuplicate(Guid.NewGuid(), @"\\?\HID#VID_1234&PID_5678&IG_01#x", xinputSet));
    }

    [Fact]
    public void Catalog_KnowsSeriesControllerTransports()
    {
        Assert.True(ControllerCatalog.TryLookup(0x045E, 0x0B13, out var fam, out var transport, out _));
        Assert.Equal(ControllerFamily.XboxSeries, fam);
        Assert.Equal(ConnectionType.Bluetooth, transport);
        Assert.True(ControllerCatalog.TryLookup(0x045E, 0x0B12, out _, out transport, out _));
        Assert.Equal(ConnectionType.Usb, transport);
        Assert.False(ControllerCatalog.TryLookup(0x1234, 0x0B12, out _, out _, out _));
    }

    // ---------- Battery selection ----------

    private static BatteryReading Reading(BatterySourceKind kind, int? percent = null, BatteryCoarseLevel coarse = BatteryCoarseLevel.Unknown,
        TimeSpan? age = null, ChargingState charging = ChargingState.Unknown, bool wired = false) => new()
        {
            Source = kind,
            SourceDeviceId = kind.ToString(),
            Percent = percent,
            CoarseLevel = coarse,
            Charging = charging,
            IsWired = wired,
            Timestamp = T0 - (age ?? TimeSpan.Zero),
        };

    private static readonly BatteryPolicy Policy = new();

    [Fact]
    public void Battery_PrefersGattOverOtherSources()
    {
        BatteryStatus s = BatterySelector.Select(
        [
            Reading(BatterySourceKind.XInputCoarse, coarse: BatteryCoarseLevel.Medium),
            Reading(BatterySourceKind.BleGatt, 78),
            Reading(BatterySourceKind.WindowsDeviceProperty, 80),
        ], T0, Policy);
        Assert.Equal(BatterySourceKind.BleGatt, s.Selected!.Source);
        Assert.Equal(78, s.Selected.Percent);
        Assert.Equal(BatteryFreshness.Fresh, s.Freshness);
    }

    [Fact]
    public void Battery_FallsBackWhenHigherPrioritySourceIsStale()
    {
        BatteryStatus s = BatterySelector.Select(
        [
            Reading(BatterySourceKind.BleGatt, 78, age: TimeSpan.FromHours(1)),
            Reading(BatterySourceKind.XInputCoarse, coarse: BatteryCoarseLevel.Low),
        ], T0, Policy);
        Assert.Equal(BatterySourceKind.XInputCoarse, s.Selected!.Source);
        Assert.Null(s.Selected.Percent); // coarse level is never turned into a number
        Assert.Equal(BatteryCoarseLevel.Low, s.Selected.CoarseLevel);
        Assert.True(s.IsLow);
    }

    [Fact]
    public void Battery_OnlyStaleReadingIsMarkedStale()
    {
        BatteryStatus s = BatterySelector.Select([Reading(BatterySourceKind.BleGatt, 55, age: TimeSpan.FromHours(1))], T0, Policy);
        Assert.Equal(BatteryFreshness.Stale, s.Freshness);
        Assert.Equal(TimeSpan.FromHours(1), s.Age);
    }

    [Fact]
    public void Battery_InvalidValuesAreIgnored()
    {
        BatteryStatus s = BatterySelector.Select(
        [
            Reading(BatterySourceKind.BleGatt, 255),
            Reading(BatterySourceKind.BleGatt, -1),
            Reading(BatterySourceKind.XInputCoarse),
        ], T0, Policy);
        Assert.Equal(BatteryFreshness.None, s.Freshness);
        Assert.Null(s.Selected);
    }

    [Fact]
    public void Battery_NoReadingsIsUnavailable()
    {
        Assert.Same(BatteryStatus.Unavailable, BatterySelector.Select([], T0, Policy));
    }

    [Fact]
    public void Battery_ChargingTakenFromAnotherFreshSource()
    {
        BatteryStatus s = BatterySelector.Select(
        [
            Reading(BatterySourceKind.BleGatt, 15),
            Reading(BatterySourceKind.WindowsGamingInput, 14, charging: ChargingState.Charging),
        ], T0, Policy);
        Assert.Equal(BatterySourceKind.BleGatt, s.Selected!.Source);
        Assert.Equal(ChargingState.Charging, s.Charging);
        Assert.Equal(BatterySourceKind.WindowsGamingInput, s.ChargingSource);
        Assert.False(s.IsLow); // charging suppresses the low warning
    }

    [Fact]
    public void Battery_LowThreshold()
    {
        Assert.True(BatterySelector.Select([Reading(BatterySourceKind.BleGatt, 20)], T0, Policy).IsLow);
        Assert.False(BatterySelector.Select([Reading(BatterySourceKind.BleGatt, 21)], T0, Policy).IsLow);
        Assert.False(BatterySelector.Select([Reading(BatterySourceKind.XInputCoarse, wired: true)], T0, Policy).IsLow);
    }

    [Fact]
    public void Battery_PolicyScalesWithPollInterval()
    {
        var p = BatteryPolicy.ForPollInterval(TimeSpan.FromSeconds(120), 15);
        Assert.Equal(TimeSpan.FromSeconds(360), p.MaxAge(BatterySourceKind.BleGatt));
        Assert.Equal(15, p.LowPercentThreshold);
    }

    // ---------- Battery association ----------

    [Fact]
    public void Associate_ExactControllerId()
    {
        var r = Reading(BatterySourceKind.XInputCoarse, coarse: BatteryCoarseLevel.Full) with { ControllerId = "xinput:0" };
        var map = BatteryAssociator.Associate([XInput(0), XInput(1)], [r], out var un);
        Assert.Equal(AssociationConfidence.Exact, Assert.Single(map["xinput:0"]).Confidence);
        Assert.Empty(un);
    }

    [Fact]
    public void Associate_ByProductId()
    {
        var ble = Reading(BatterySourceKind.BleGatt, 70) with { VendorId = 0x045E, ProductId = 0x0B13 };
        var map = BatteryAssociator.Associate([XInput(0, 0x0B13), XInput(1, 0x0B12)], [ble], out var un);
        Assert.Equal(AssociationConfidence.MatchedByProductId, Assert.Single(map["xinput:0"]).Confidence);
        Assert.Empty(map["xinput:1"]);
        Assert.Empty(un);
    }

    [Fact]
    public void Associate_SingleDeviceFallback()
    {
        var ble = Reading(BatterySourceKind.BleGatt, 70);
        var map = BatteryAssociator.Associate([XInput(0)], [ble], out _);
        Assert.Equal(AssociationConfidence.SingleDevice, Assert.Single(map["xinput:0"]).Confidence);
    }

    [Fact]
    public void Associate_AmbiguousIsLeftUnassociated()
    {
        var a = Reading(BatterySourceKind.BleGatt, 70) with { SourceDeviceId = "a" };
        var b = Reading(BatterySourceKind.BleGatt, 30) with { SourceDeviceId = "b" };
        var map = BatteryAssociator.Associate([XInput(0), XInput(1)], [a, b], out var un);
        Assert.All(map.Values, Assert.Empty);
        Assert.Equal(2, un.Count);
    }

    [Fact]
    public void Associate_GipGenericControllerAcceptsRealMicrosoftProductId()
    {
        // Observed hardware: XInput reports PID 02FF for a USB Xbox Series controller; WGI reports the real 0B12.
        var wgi = Reading(BatterySourceKind.WindowsGamingInput, 100) with { VendorId = 0x045E, ProductId = 0x0B12 };
        var map = BatteryAssociator.Associate([XInput(0, ControllerCatalog.GipGenericProductId)], [wgi], out var un);
        Assert.Equal(AssociationConfidence.SingleDevice, Assert.Single(map["xinput:0"]).Confidence);
        Assert.Empty(un);

        var other = Reading(BatterySourceKind.WindowsGamingInput, 100) with { VendorId = 0x054C, ProductId = 0x09CC };
        map = BatteryAssociator.Associate([XInput(0, ControllerCatalog.GipGenericProductId)], [other], out un);
        Assert.Empty(map["xinput:0"]);
        Assert.Single(un);
    }

    [Fact]
    public void Associate_BluetoothBatteryNotPairedWithUsbController()
    {
        var ble = Reading(BatterySourceKind.BleGatt, 70);
        var map = BatteryAssociator.Associate([XInput(0, 0x0B12, ConnectionType.Usb)], [ble], out var un);
        Assert.Empty(map["xinput:0"]);
        Assert.Single(un);
    }
}
