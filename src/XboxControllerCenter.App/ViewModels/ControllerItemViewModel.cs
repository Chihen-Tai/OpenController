using XboxControllerCenter.App.Localization;
using XboxControllerCenter.Battery;
using XboxControllerCenter.Models;
using XboxControllerCenter.Platform.Input;

namespace XboxControllerCenter.App.ViewModels;

/// <summary>One connected controller in the list (static info + battery).</summary>
public sealed class ControllerItemViewModel : ObservableObject
{
    private ControllerInfo _info;
    private string _batteryValue = "—";
    private string _batterySource = "";
    private string _batteryFreshness = "";
    private string _charging = "";
    private bool _isLow;
    private bool _isStale;
    private double _batteryPercent = -1;
    private bool _isOutputSource;
    private int _coarseSegments = -1;
    private bool _isCharging;
    private string _batteryDetail = "";

    public ControllerItemViewModel(ControllerInfo info)
    {
        _info = info;
    }

    public string Id => _info.Id;
    public ControllerInfo Info => _info;
    public string Name => _info.DisplayName;
    public string Api => Fmt.Api(_info);
    public string Connection => Fmt.Connection(_info);
    public string Slot => Fmt.Slot(_info);
    public string Ids => Fmt.Ids(_info);
    public string Capabilities => Fmt.Capabilities(_info);
    public string Title => _info.XInputSlot is { } s ? $"{Name} — {Loc.T("Player", s + 1)}" : Name;

    /// <summary>Short transport label for status chips ("Bluetooth", "USB", ...).</summary>
    public string TransportShort => _info.Connection switch
    {
        ConnectionType.Usb => Loc.T("Conn.Usb"),
        ConnectionType.Bluetooth => Loc.T("Conn.Bluetooth"),
        ConnectionType.XboxWireless => Loc.T("Conn.XboxWireless"),
        _ => Loc.T("Conn.UnknownShort"),
    };

    /// <summary>Resource key of the Fluent icon for the transport.</summary>
    public string TransportIconKey => _info.Connection switch
    {
        ConnectionType.Bluetooth => "Icon.Bluetooth",
        ConnectionType.Usb => "Icon.UsbStick",
        _ => "Icon.PlugConnected",
    };

    public string BatteryValue { get => _batteryValue; private set => Set(ref _batteryValue, value); }
    public string BatterySource { get => _batterySource; private set => Set(ref _batterySource, value); }
    public string BatteryFreshness { get => _batteryFreshness; private set => Set(ref _batteryFreshness, value); }
    public string ChargingText { get => _charging; private set => Set(ref _charging, value); }
    public bool IsLowBattery { get => _isLow; private set => Set(ref _isLow, value); }
    public bool IsStale { get => _isStale; private set => Set(ref _isStale, value); }
    /// <summary>0..100 when a real percentage exists, otherwise -1 (no bar is drawn).</summary>
    public double BatteryPercent { get => _batteryPercent; private set => Set(ref _batteryPercent, value); }
    public bool HasPercent => _batteryPercent >= 0;
    public bool IsOutputSource { get => _isOutputSource; set => Set(ref _isOutputSource, value); }
    /// <summary>0..3 segments for XInput coarse levels, -1 otherwise (never derived from a percentage).</summary>
    public int CoarseSegments { get => _coarseSegments; private set => Set(ref _coarseSegments, value); }
    public bool IsCharging { get => _isCharging; private set => Set(ref _isCharging, value); }
    /// <summary>Raw values behind the reading plus how it was matched to this controller.</summary>
    public string BatteryDetail { get => _batteryDetail; private set => Set(ref _batteryDetail, value); }

    public void UpdateInfo(ControllerInfo info)
    {
        if (info == _info)
            return;
        _info = info;
        RaiseAll();
    }

    public void UpdateBattery(ControllerBattery? battery)
    {
        BatteryStatus status = battery?.Status ?? BatteryStatus.Unavailable;
        BatteryReading? r = status.Selected;
        BatteryValue = Fmt.BatteryValue(r);
        BatterySource = r is null ? Loc.T("Battery.NoSource") : Fmt.SourceName(r.Source);
        BatteryDetail = r is null ? "" : string.Join("  ·  ", new[] { r.Detail, Fmt.Confidence(battery?.Confidence) }.Where(s => !string.IsNullOrEmpty(s)));
        CoarseSegments = r is { Percent: null, IsWired: false } ? r.CoarseLevel switch
        {
            BatteryCoarseLevel.Empty => 0,
            BatteryCoarseLevel.Low => 1,
            BatteryCoarseLevel.Medium => 2,
            BatteryCoarseLevel.Full => 3,
            _ => -1,
        } : -1;
        IsCharging = status.Charging == ChargingState.Charging;
        BatteryFreshness = Fmt.Freshness(status);
        ChargingText = Fmt.Charging(status);
        IsLowBattery = status.IsLow;
        IsStale = status.Freshness == XboxControllerCenter.Battery.BatteryFreshness.Stale;
        BatteryPercent = r?.Percent ?? -1;
        OnPropertyChanged(nameof(HasPercent));
    }

    public void Relocalize() => RaiseAll();
}
