namespace XboxControllerCenter.Battery;

/// <summary>One recorded device-reported battery percentage.</summary>
/// <param name="Time">When the value was read.</param>
/// <param name="Percent">Device-reported percentage (0..100).</param>
/// <param name="Session">Identifier of one continuous connection while the app was running.</param>
public readonly record struct BatterySample(DateTimeOffset Time, int Percent, string Session);

public enum BatteryEstimateState
{
    /// <summary>A measured drain rate exists; <see cref="BatteryLifeEstimate.Remaining"/> is set.</summary>
    Estimated,
    /// <summary>Not enough observed discharge yet to measure a rate.</summary>
    NotEnoughData,
    /// <summary>Charging or on external power: no runtime estimate.</summary>
    Charging,
}

public sealed record BatteryLifeEstimate
{
    public BatteryEstimateState State { get; init; }
    /// <summary>Estimated time until 0 %, only when <see cref="State"/> is Estimated.</summary>
    public TimeSpan? Remaining { get; init; }
    /// <summary>Measured drain in percentage points per hour of connected use.</summary>
    public double? PercentPerHour { get; init; }
    /// <summary>Connected time over which the drain was measured.</summary>
    public TimeSpan Observed { get; init; }
    /// <summary>Percentage points of drop observed in that time.</summary>
    public int ObservedDrop { get; init; }
    /// <summary>All recorded connected time since the last charge, including time with no level change.</summary>
    public TimeSpan Tracked { get; init; }
}

public sealed record BatteryEstimatorOptions
{
    /// <summary>Minimum measured drop before an estimate is shown.</summary>
    public int MinDropPercent { get; init; } = 3;
    /// <summary>Minimum measured connected time before an estimate is shown.</summary>
    public TimeSpan MinObserved { get; init; } = TimeSpan.FromMinutes(20);
    /// <summary>A gap longer than this between samples splits a session (controller asleep or off).</summary>
    public TimeSpan MaxGap { get; init; } = TimeSpan.FromMinutes(10);
    /// <summary>A rise of at least this many points means the battery was charged or replaced.</summary>
    public int ResetRisePercent { get; init; } = 2;
}

/// <summary>
/// Estimates remaining runtime from the controller's own percentage readings. No source reports a discharge
/// rate (Windows.Gaming.Input returns none; GATT only reports a percentage), so the rate is *measured*:
/// <list type="bullet">
/// <item>Only time while the controller was connected and the app was recording counts; samples are split into
/// segments at session changes and at gaps longer than <see cref="BatteryEstimatorOptions.MaxGap"/>.</item>
/// <item>Percentages are quantized, so each segment is measured between the first and the last moment the value
/// <em>changed</em> (step edges), not between arbitrary samples.</item>
/// <item>History before the last rise (charge / new batteries) is ignored.</item>
/// </list>
/// Rate = Σ drop / Σ duration over all segments; remaining = current % ÷ rate.
/// </summary>
public static class BatteryLifeEstimator
{
    public static BatteryLifeEstimate Estimate(IReadOnlyList<BatterySample> history, int currentPercent, ChargingState charging,
        BatteryEstimatorOptions? options = null)
    {
        options ??= new BatteryEstimatorOptions();
        if (charging == ChargingState.Charging || charging == ChargingState.NotPresent)
            return new BatteryLifeEstimate { State = BatteryEstimateState.Charging };

        var samples = history.Where(s => s.Percent is >= 0 and <= 100).OrderBy(s => s.Time).ToList();

        // Ignore everything before the last charge / battery swap.
        int start = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i].Percent - samples[i - 1].Percent >= options.ResetRisePercent)
                start = i;
        }
        samples = samples.Skip(start).ToList();

        int drop = 0;
        TimeSpan observed = TimeSpan.Zero;
        TimeSpan tracked = TimeSpan.Zero;
        foreach (List<BatterySample> segment in Segments(samples, options.MaxGap))
        {
            tracked += segment[^1].Time - segment[0].Time;
            // Step edges: the first sample at each new (lower) value.
            var edges = new List<BatterySample>();
            for (int i = 1; i < segment.Count; i++)
            {
                if (segment[i].Percent < segment[i - 1].Percent)
                    edges.Add(segment[i]);
            }
            if (edges.Count < 2)
                continue;
            int segmentDrop = edges[0].Percent - edges[^1].Percent;
            TimeSpan segmentTime = edges[^1].Time - edges[0].Time;
            if (segmentDrop <= 0 || segmentTime <= TimeSpan.Zero)
                continue;
            drop += segmentDrop;
            observed += segmentTime;
        }

        if (drop < options.MinDropPercent || observed < options.MinObserved)
            return new BatteryLifeEstimate { State = BatteryEstimateState.NotEnoughData, Observed = observed, ObservedDrop = drop, Tracked = tracked };

        double perHour = drop / observed.TotalHours;
        return new BatteryLifeEstimate
        {
            State = BatteryEstimateState.Estimated,
            PercentPerHour = perHour,
            Remaining = TimeSpan.FromHours(Math.Max(0, currentPercent) / perHour),
            Observed = observed,
            ObservedDrop = drop,
            Tracked = tracked,
        };
    }

    private static IEnumerable<List<BatterySample>> Segments(List<BatterySample> samples, TimeSpan maxGap)
    {
        var current = new List<BatterySample>();
        foreach (BatterySample s in samples)
        {
            if (current.Count > 0 && (s.Session != current[^1].Session || s.Time - current[^1].Time > maxGap))
            {
                yield return current;
                current = [];
            }
            current.Add(s);
        }
        if (current.Count > 0)
            yield return current;
    }
}

/// <summary>
/// Persistent per-controller record of device-reported percentages (only real percentages are stored; coarse XInput
/// levels never are). Samples are stored when the value changes or every <see cref="Heartbeat"/>, and pruned by age
/// and count so the file stays small.
/// </summary>
public sealed class BatteryHistory
{
    public static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);
    public const int MaxSamplesPerDevice = 4000;

    public Dictionary<string, List<BatterySample>> Devices { get; init; } = new(StringComparer.Ordinal);

    public IReadOnlyList<BatterySample> For(string deviceKey) =>
        Devices.TryGetValue(deviceKey, out List<BatterySample>? list) ? list : [];

    /// <summary>Records a sample; returns true when something was stored (caller may persist).</summary>
    public bool Record(string deviceKey, BatterySample sample)
    {
        if (sample.Percent is < 0 or > 100)
            return false;
        if (!Devices.TryGetValue(deviceKey, out List<BatterySample>? list))
            Devices[deviceKey] = list = [];

        if (list.Count > 0)
        {
            BatterySample last = list[^1];
            bool changed = last.Percent != sample.Percent || last.Session != sample.Session;
            if (!changed && sample.Time - last.Time < Heartbeat)
                return false;
        }

        list.Add(sample);
        DateTimeOffset cutoff = sample.Time - MaxAge;
        list.RemoveAll(s => s.Time < cutoff);
        if (list.Count > MaxSamplesPerDevice)
            list.RemoveRange(0, list.Count - MaxSamplesPerDevice);
        return true;
    }
}
