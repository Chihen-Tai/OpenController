namespace XboxControllerCenter.Diagnostics;

/// <summary>
/// Counts events in a sliding time window. Used for two honest measurements:
/// <list type="bullet">
/// <item>the sampler's actual loop rate, and</item>
/// <item>the rate at which the device reported a *changed* state (XInput packet number changes).
/// This is a lower bound of the device report rate and can never exceed the sampling rate,
/// so it is not the controller's true polling rate.</item>
/// </list>
/// </summary>
public sealed class RateMeter
{
    private readonly Queue<long> _timestamps = new();
    private readonly long _windowTicks;
    private readonly long _ticksPerSecond;

    /// <param name="ticksPerSecond">Timestamp resolution, e.g. Stopwatch.Frequency.</param>
    /// <param name="window">Averaging window.</param>
    public RateMeter(long ticksPerSecond, TimeSpan window)
    {
        _ticksPerSecond = ticksPerSecond;
        _windowTicks = (long)(window.TotalSeconds * ticksPerSecond);
    }

    public void Record(long timestamp)
    {
        _timestamps.Enqueue(timestamp);
        Trim(timestamp);
    }

    /// <summary>Events per second over the window ending at <paramref name="now"/>.</summary>
    public double RatePerSecond(long now)
    {
        Trim(now);
        return _timestamps.Count * (double)_ticksPerSecond / _windowTicks;
    }

    public void Reset() => _timestamps.Clear();

    private void Trim(long now)
    {
        while (_timestamps.Count > 0 && now - _timestamps.Peek() > _windowTicks)
            _timestamps.Dequeue();
    }
}
