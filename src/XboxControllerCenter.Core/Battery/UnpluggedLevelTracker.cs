namespace XboxControllerCenter.Battery;

/// <summary>The last percentage a controller reported while it was not charging.</summary>
public sealed record UnpluggedLevel(int Percent, DateTimeOffset Time, BatterySourceKind Source);

/// <summary>
/// Remembers each controller's last percentage from before it was plugged in. While charging, Xbox controllers
/// report an inflated level (observed: 100/1000 mWh on battery, 400/1000 a moment after the cable was connected),
/// so the UI shows the level from before charging next to it. Keyed by controller id, which survives the
/// Bluetooth → USB reconnect (same XInput slot). Not thread-safe: call from the UI thread only.
/// </summary>
public sealed class UnpluggedLevelTracker
{
    /// <summary>A level older than this says nothing useful about the battery any more.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    private readonly Dictionary<string, UnpluggedLevel> _levels = new(StringComparer.Ordinal);

    /// <summary>
    /// Records <paramref name="status"/> and returns the level from before charging when the controller is
    /// charging now and such a level is known; otherwise null.
    /// </summary>
    public UnpluggedLevel? Update(string controllerId, BatteryStatus status, DateTimeOffset now)
    {
        if (status is { Freshness: BatteryFreshness.Fresh, Selected: { Percent: { } percent, IsWired: false } reading } &&
            status.Charging is not ChargingState.Charging)
        {
            _levels[controllerId] = new UnpluggedLevel(percent, reading.Timestamp, reading.Source);
            return null;
        }

        if (status.Charging != ChargingState.Charging || !_levels.TryGetValue(controllerId, out UnpluggedLevel? level))
            return null;
        if (now - level.Time > MaxAge)
        {
            _levels.Remove(controllerId);
            return null;
        }
        return level;
    }
}
