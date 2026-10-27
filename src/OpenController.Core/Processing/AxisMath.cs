namespace OpenController.Processing;

/// <summary>
/// Conversions between XInput integer units and normalized values.
/// <para>
/// Sticks use an asymmetric scale so both extremes map exactly to ±1:
/// positive values divide by 32767 and negative values by 32768. The inverse conversion is
/// therefore bit-exact for every 16-bit input (normalize → denormalize returns the original value).
/// </para>
/// </summary>
public static class AxisMath
{
    public const double AxisPositiveMax = 32767.0;
    public const double AxisNegativeMax = 32768.0;
    public const double TriggerMax = 255.0;

    /// <summary>Maps -32768..32767 to exactly -1..1 (0 → 0).</summary>
    public static double NormalizeAxis(short raw) => raw >= 0 ? raw / AxisPositiveMax : raw / AxisNegativeMax;

    /// <summary>Maps a normalized value back to XInput units, clamping to [-1, 1]. NaN maps to 0.</summary>
    public static short DenormalizeAxis(double value)
    {
        if (double.IsNaN(value))
            return 0;
        value = Math.Clamp(value, -1.0, 1.0);
        double scaled = value >= 0 ? value * AxisPositiveMax : value * AxisNegativeMax;
        return (short)Math.Clamp(Math.Round(scaled, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);
    }

    /// <summary>Maps 0..255 to 0..1.</summary>
    public static double NormalizeTrigger(byte raw) => raw / TriggerMax;

    /// <summary>Maps 0..1 to 0..255, clamping. NaN maps to 0.</summary>
    public static byte DenormalizeTrigger(double value)
    {
        if (double.IsNaN(value))
            return 0;
        value = Math.Clamp(value, 0.0, 1.0);
        return (byte)Math.Clamp(Math.Round(value * TriggerMax, MidpointRounding.AwayFromZero), 0, 255);
    }

    /// <summary>Converts a DirectInput axis value in [min, max] to XInput stick units.</summary>
    public static short RangeToAxis(int value, int min, int max)
    {
        if (max <= min)
            return 0;
        double n = (2.0 * (value - (double)min) / (max - (double)min)) - 1.0;
        return DenormalizeAxis(n);
    }

    public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
