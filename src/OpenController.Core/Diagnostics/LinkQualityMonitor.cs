using OpenController.Models;
using OpenController.Processing;

namespace OpenController.Diagnostics;

public sealed record LinkQualityOptions
{
    /// <summary>Largest axis change (normalized, sticks -1..1, triggers 0..1) between two updates that counts as movement.</summary>
    public double MotionThreshold { get; init; } = 0.03;
    /// <summary>The update before a gap must have arrived within this time: the input was streaming.</summary>
    public double StreamIntervalMs { get; init; } = 40;
    /// <summary>A gap at least this long during movement is a suspected stall.</summary>
    public double StallMs { get; init; } = 100;
    /// <summary>The input must jump at least this far across the gap.</summary>
    public double StallJump { get; init; } = 0.15;
    /// <summary>Speed (normalized units per second) the input had right before the gap.</summary>
    public double StallMinSpeed { get; init; } = 2.0;
    /// <summary>Minimum cosine between the movement before the gap and the jump across it.</summary>
    public double StallMinDirection { get; init; } = 0.7;
    /// <summary>The jump must cover at least this share of the distance the movement would have covered in the gap.</summary>
    public double StallMinContinuation { get; init; } = 0.25;
    /// <summary>Update intervals during movement are kept for this long for the statistics.</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>Immutable statistics published by <see cref="LinkQualityMonitor"/>.</summary>
public sealed record LinkQualityStats
{
    public static LinkQualityStats Empty { get; } = new();

    /// <summary>Update intervals measured while the input was moving, within the window.</summary>
    public int MotionUpdates { get; init; }
    public double? MedianMs { get; init; }
    public double? P95Ms { get; init; }
    public double? MaxMs { get; init; }
    /// <summary>Suspected stalls since the controller connected.</summary>
    public int Stalls { get; init; }
    public double? LongestStallMs { get; init; }
    public double? LastStallMs { get; init; }
    /// <summary>Timestamp (monitor ticks) of the last stall.</summary>
    public long? LastStallTimestamp { get; init; }
}

/// <summary>
/// Measures how smoothly a controller's input arrives, from the state changes alone (Windows exposes no signal
/// strength for a connected Bluetooth controller). Two measurements:
/// <list type="bullet">
/// <item>Intervals between updates while the input is moving (an idle controller sends no changes, so idle time
/// is never counted). Their median / 95th percentile / maximum show the effective report spacing.</item>
/// <item>Suspected stalls: during fast, continuous movement the updates stop for at least
/// <see cref="LinkQualityOptions.StallMs"/> and then the input has jumped a matching distance in the direction it was
/// moving. A person stopping and moving again decelerates first, and a released stick snapping back is followed by a
/// slow start, so those patterns are not counted.</item>
/// </list>
/// Resolution is limited by the sampling rate. Not thread-safe for writers: call <see cref="OnUpdate"/> from the
/// sampling thread; <see cref="Stats"/> may be read from any thread.
/// </summary>
public sealed class LinkQualityMonitor
{
    private const int Axes = 6;
    private readonly long _ticksPerSecond;
    private readonly LinkQualityOptions _options;
    private readonly Queue<(long Time, double Ms)> _intervals = new();
    private readonly double[] _prev = new double[Axes];
    private readonly double[] _prevVelocity = new double[Axes];
    private bool _hasPrev;
    private long _prevTime;
    private double _prevIntervalMs = double.MaxValue;
    private bool _prevMoving;
    private int _stalls;
    private double? _longestStall, _lastStall;
    private long? _lastStallTime;
    private long? _lastPublish;
    private LinkQualityStats _stats = LinkQualityStats.Empty;

    public LinkQualityMonitor(long ticksPerSecond, LinkQualityOptions? options = null)
    {
        _ticksPerSecond = ticksPerSecond;
        _options = options ?? new LinkQualityOptions();
    }

    public LinkQualityStats Stats => Volatile.Read(ref _stats);

    /// <summary>Feeds one reported state change. Returns the gap length in ms when it was a suspected stall.</summary>
    public double? OnUpdate(long timestamp, GamepadState state)
    {
        Span<double> cur = stackalloc double[Axes];
        cur[0] = AxisMath.NormalizeAxis(state.LeftX);
        cur[1] = AxisMath.NormalizeAxis(state.LeftY);
        cur[2] = AxisMath.NormalizeAxis(state.RightX);
        cur[3] = AxisMath.NormalizeAxis(state.RightY);
        cur[4] = AxisMath.NormalizeTrigger(state.LeftTrigger);
        cur[5] = AxisMath.NormalizeTrigger(state.RightTrigger);

        if (!_hasPrev)
        {
            cur.CopyTo(_prev);
            _prevTime = timestamp;
            _hasPrev = true;
            return null;
        }

        double intervalMs = (timestamp - _prevTime) * 1000.0 / _ticksPerSecond;
        if (intervalMs <= 0)
            return null;

        Span<double> jump = stackalloc double[Axes];
        double maxDelta = 0;
        for (int i = 0; i < Axes; i++)
        {
            jump[i] = cur[i] - _prev[i];
            maxDelta = Math.Max(maxDelta, Math.Abs(jump[i]));
        }
        bool moving = maxDelta >= _options.MotionThreshold;

        double? stall = null;
        if (moving && _prevMoving && _prevIntervalMs <= _options.StreamIntervalMs)
        {
            _intervals.Enqueue((timestamp, intervalMs));
            if (intervalMs >= _options.StallMs && IsContinuation(jump, intervalMs / 1000.0))
            {
                stall = intervalMs;
                _stalls++;
                _lastStall = intervalMs;
                _lastStallTime = timestamp;
                _longestStall = Math.Max(_longestStall ?? 0, intervalMs);
            }
        }

        for (int i = 0; i < Axes; i++)
        {
            _prevVelocity[i] = jump[i] / (intervalMs / 1000.0);
            _prev[i] = cur[i];
        }
        _prevTime = timestamp;
        _prevIntervalMs = intervalMs;
        _prevMoving = moving;

        if (stall is not null || _lastPublish is not { } last || timestamp - last >= _ticksPerSecond / 4)
            Publish(timestamp);
        return stall;
    }

    /// <summary>The jump across the gap continues the fast movement that was in progress before it.</summary>
    private bool IsContinuation(ReadOnlySpan<double> jump, double gapSeconds)
    {
        double dot = 0, jumpLen = 0, speed = 0;
        for (int i = 0; i < Axes; i++)
        {
            dot += jump[i] * _prevVelocity[i];
            jumpLen += jump[i] * jump[i];
            speed += _prevVelocity[i] * _prevVelocity[i];
        }
        jumpLen = Math.Sqrt(jumpLen);
        speed = Math.Sqrt(speed);
        if (jumpLen < _options.StallJump || speed < _options.StallMinSpeed)
            return false;
        // A stick spans at most 2 units, so a long gap cannot be expected to cover more than that.
        double expected = Math.Min(speed * gapSeconds, 2.0);
        if (jumpLen < _options.StallMinContinuation * expected)
            return false;
        return dot / (jumpLen * speed) >= _options.StallMinDirection;
    }

    private void Publish(long now)
    {
        _lastPublish = now;
        long window = (long)(_options.Window.TotalSeconds * _ticksPerSecond);
        while (_intervals.Count > 0 && now - _intervals.Peek().Time > window)
            _intervals.Dequeue();

        double[] sorted = _intervals.Select(x => x.Ms).ToArray();
        Array.Sort(sorted);
        Volatile.Write(ref _stats, new LinkQualityStats
        {
            MotionUpdates = sorted.Length,
            MedianMs = Percentile(sorted, 0.5),
            P95Ms = Percentile(sorted, 0.95),
            MaxMs = sorted.Length > 0 ? sorted[^1] : null,
            Stalls = _stalls,
            LongestStallMs = _longestStall,
            LastStallMs = _lastStall,
            LastStallTimestamp = _lastStallTime,
        });
    }

    private static double? Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0)
            return null;
        int index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
