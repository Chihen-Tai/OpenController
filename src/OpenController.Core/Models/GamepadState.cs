namespace OpenController.Models;

/// <summary>
/// A gamepad state in XInput units: sticks are signed 16-bit (-32768..32767, +Y = up),
/// triggers are 0..255. Used both for raw physical input and for the processed output
/// that is sent to a virtual controller.
/// </summary>
public readonly record struct GamepadState(
    GamepadButtons Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short LeftX,
    short LeftY,
    short RightX,
    short RightY)
{
    /// <summary>Centered sticks, released triggers, no buttons.</summary>
    public static GamepadState Neutral => default;

    public bool IsPressed(GamepadButtons button) => (Buttons & button) == button && button != GamepadButtons.None;
}

/// <summary>A 2-D stick vector in normalized units (nominally within the unit square, +Y = up).</summary>
public readonly record struct StickVector(double X, double Y)
{
    public static StickVector Zero => default;

    /// <summary>Euclidean magnitude (radius) of the vector.</summary>
    public double Length => Math.Sqrt(X * X + Y * Y);

    /// <summary>Angle in degrees, counter-clockwise from +X, in [0, 360).</summary>
    public double AngleDegrees
    {
        get
        {
            double a = Math.Atan2(Y, X) * 180.0 / Math.PI;
            return a < 0 ? a + 360.0 : a;
        }
    }

    public static StickVector operator -(StickVector a, StickVector b) => new(a.X - b.X, a.Y - b.Y);
    public static StickVector operator *(StickVector a, double k) => new(a.X * k, a.Y * k);
}
