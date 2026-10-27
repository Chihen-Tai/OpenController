namespace OpenController.Battery;

/// <summary>Where a battery value came from. Lower numeric value = higher priority.</summary>
public enum BatterySourceKind
{
    /// <summary>Bluetooth LE GATT Battery Service 0x180F / Battery Level 0x2A19 (percentage reported by the device).</summary>
    BleGatt = 1,
    /// <summary>Battery percentage Windows stores on the Bluetooth device node (DEVPKEY_Bluetooth_Battery).</summary>
    WindowsDeviceProperty = 2,
    /// <summary>Windows.Gaming.Input BatteryReport (remaining / full-charge capacity reported by the driver).</summary>
    WindowsGamingInput = 3,
    /// <summary>XInputGetBatteryInformation — only coarse levels (Empty/Low/Medium/Full).</summary>
    XInputCoarse = 4,
}

/// <summary>Coarse level as defined by XInput. Never converted to a percentage.</summary>
public enum BatteryCoarseLevel
{
    Unknown,
    Empty,
    Low,
    Medium,
    Full,
}

public enum ChargingState
{
    /// <summary>The source does not report charging information.</summary>
    Unknown,
    Charging,
    Discharging,
    Idle,
    /// <summary>No battery present (e.g. wired-only controller).</summary>
    NotPresent,
}

/// <summary>One observation from one battery source.</summary>
public sealed record BatteryReading
{
    public required BatterySourceKind Source { get; init; }
    /// <summary>Identifier of the device the reading came from (BLE device id, XInput slot id, ...).</summary>
    public required string SourceDeviceId { get; init; }
    public string? SourceDeviceName { get; init; }
    public ushort? VendorId { get; init; }
    public ushort? ProductId { get; init; }
    /// <summary>Controller id this reading belongs to, when known exactly (e.g. XInput slot).</summary>
    public string? ControllerId { get; init; }

    /// <summary>Device-reported percentage, or null when the source only gives a coarse level.</summary>
    public int? Percent { get; init; }
    public BatteryCoarseLevel CoarseLevel { get; init; } = BatteryCoarseLevel.Unknown;
    public ChargingState Charging { get; init; } = ChargingState.Unknown;
    /// <summary>True when the device reports it is powered by cable / has no battery (XInput BATTERY_TYPE_WIRED).</summary>
    public bool IsWired { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    /// <summary>Raw values behind the reading (e.g. "1000/1000 mWh"), shown so derived percentages stay transparent.</summary>
    public string? Detail { get; init; }

    /// <summary>A reading is usable if it carries a percentage in 0..100, a coarse level, or a wired flag.</summary>
    public bool IsValid =>
        Percent is null ? CoarseLevel != BatteryCoarseLevel.Unknown || IsWired : Percent is >= 0 and <= 100;
}
