namespace XboxControllerCenter.Battery;

public enum BatteryFreshness
{
    /// <summary>No usable reading at all.</summary>
    None,
    Fresh,
    /// <summary>Only an old reading exists; it must be displayed as stale, never as current.</summary>
    Stale,
}

/// <summary>The value to show for one controller and why.</summary>
public sealed record BatteryStatus
{
    public static BatteryStatus Unavailable { get; } = new() { Freshness = BatteryFreshness.None };

    public BatteryReading? Selected { get; init; }
    public BatteryFreshness Freshness { get; init; }
    public TimeSpan? Age { get; init; }
    /// <summary>Charging state from the selected reading or from another fresh source that reports it.</summary>
    public ChargingState Charging { get; init; } = ChargingState.Unknown;
    public BatterySourceKind? ChargingSource { get; init; }
    public bool IsLow { get; init; }
}

public sealed record BatteryPolicy
{
    /// <summary>A reading older than this is stale.</summary>
    public TimeSpan MaxAgeGatt { get; init; } = TimeSpan.FromMinutes(3);
    public TimeSpan MaxAgeDeviceProperty { get; init; } = TimeSpan.FromMinutes(3);
    public TimeSpan MaxAgeGamingInput { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan MaxAgeXInput { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Percentage at or below which the battery is "low".</summary>
    public int LowPercentThreshold { get; init; } = 20;

    /// <summary>Freshness limits scaled to the user-configured polling interval.</summary>
    public static BatteryPolicy ForPollInterval(TimeSpan pollInterval, int lowPercentThreshold)
    {
        TimeSpan slow = TimeSpan.FromTicks(Math.Max(pollInterval.Ticks * 3, TimeSpan.FromSeconds(30).Ticks));
        return new BatteryPolicy
        {
            MaxAgeGatt = slow,
            MaxAgeDeviceProperty = slow,
            MaxAgeGamingInput = slow,
            LowPercentThreshold = lowPercentThreshold,
        };
    }

    public TimeSpan MaxAge(BatterySourceKind kind) => kind switch
    {
        BatterySourceKind.BleGatt => MaxAgeGatt,
        BatterySourceKind.WindowsDeviceProperty => MaxAgeDeviceProperty,
        BatterySourceKind.WindowsGamingInput => MaxAgeGamingInput,
        _ => MaxAgeXInput,
    };
}

/// <summary>
/// Chooses which battery reading to display. Priority:
/// BLE GATT percentage &gt; Windows device property percentage &gt; Windows.Gaming.Input &gt; XInput coarse level.
/// Invalid readings are ignored. Fresh readings always beat stale ones; if only stale readings exist,
/// the newest is returned with <see cref="BatteryFreshness.Stale"/> so the UI can label it.
/// </summary>
public static class BatterySelector
{
    public static BatteryStatus Select(IEnumerable<BatteryReading> readings, DateTimeOffset now, BatteryPolicy policy)
    {
        var valid = readings.Where(r => r.IsValid && r.Timestamp <= now + TimeSpan.FromSeconds(5)).ToList();
        if (valid.Count == 0)
            return BatteryStatus.Unavailable;

        var fresh = valid.Where(r => now - r.Timestamp <= policy.MaxAge(r.Source)).ToList();
        BatteryReading selected;
        BatteryFreshness freshness;
        if (fresh.Count > 0)
        {
            selected = fresh.OrderBy(r => (int)r.Source).ThenByDescending(r => r.Timestamp).First();
            freshness = BatteryFreshness.Fresh;
        }
        else
        {
            selected = valid.OrderByDescending(r => r.Timestamp).ThenBy(r => (int)r.Source).First();
            freshness = BatteryFreshness.Stale;
        }

        ChargingState charging = selected.Charging;
        BatterySourceKind? chargingSource = charging != ChargingState.Unknown ? selected.Source : null;
        if (charging == ChargingState.Unknown)
        {
            BatteryReading? withCharging = fresh.Where(r => r.Charging != ChargingState.Unknown)
                .OrderBy(r => (int)r.Source).FirstOrDefault();
            if (withCharging is not null)
            {
                charging = withCharging.Charging;
                chargingSource = withCharging.Source;
            }
        }

        return new BatteryStatus
        {
            Selected = selected,
            Freshness = freshness,
            Age = now - selected.Timestamp,
            Charging = charging,
            ChargingSource = chargingSource,
            IsLow = IsLow(selected, policy) && charging != ChargingState.Charging,
        };
    }

    public static bool IsLow(BatteryReading reading, BatteryPolicy policy) =>
        !reading.IsWired && (reading.Percent is { } p
            ? p <= policy.LowPercentThreshold
            : reading.CoarseLevel is BatteryCoarseLevel.Low or BatteryCoarseLevel.Empty);
}
