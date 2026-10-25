using XboxControllerCenter.Models;

namespace XboxControllerCenter.Mapping;

/// <summary>
/// Detects a button combination held for a minimum time (e.g. L3 + R3 to toggle the in-game overlay).
/// Fires once per hold: at least one of the buttons must be released before it can fire again, so keeping
/// the buttons down never toggles repeatedly. Extra buttons pressed at the same time do not matter.
/// </summary>
public sealed class HoldChordDetector
{
    private readonly long _holdTicks;
    private long _heldSince = -1;
    private bool _fired;

    /// <param name="chord">Buttons that must all be down. Must not be <see cref="GamepadButtons.None"/>.</param>
    /// <param name="hold">How long the chord must be held.</param>
    /// <param name="ticksPerSecond">Timestamp resolution (e.g. <c>Stopwatch.Frequency</c>).</param>
    public HoldChordDetector(GamepadButtons chord, TimeSpan hold, long ticksPerSecond)
    {
        if (chord == GamepadButtons.None)
            throw new ArgumentException("A chord needs at least one button.", nameof(chord));
        ArgumentOutOfRangeException.ThrowIfNegative(hold.Ticks, nameof(hold));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerSecond);
        Chord = chord;
        _holdTicks = (long)(hold.TotalSeconds * ticksPerSecond);
    }

    public GamepadButtons Chord { get; }

    /// <summary>True while every chord button is down (whether or not it has fired yet).</summary>
    public bool IsHeld => _heldSince >= 0;

    /// <summary>Feeds the current button state. Returns true exactly once per hold, when the hold time is reached.</summary>
    public bool Update(long timestamp, GamepadButtons buttons)
    {
        if ((buttons & Chord) != Chord)
        {
            Reset();
            return false;
        }
        if (_heldSince < 0)
            _heldSince = timestamp;
        if (_fired || timestamp - _heldSince < _holdTicks)
            return false;
        _fired = true;
        return true;
    }

    /// <summary>Forgets the current hold (e.g. when the controller disconnects).</summary>
    public void Reset()
    {
        _heldSince = -1;
        _fired = false;
    }
}
