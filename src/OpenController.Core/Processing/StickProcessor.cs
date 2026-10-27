using OpenController.Models;

namespace OpenController.Processing;

/// <summary>
/// Applies <see cref="StickSettings"/> to one analog stick. Order of operations
/// (documented in docs/PROCESSING_PIPELINE.md):
/// <list type="number">
/// <item>Normalize raw axes to [-1, 1].</item>
/// <item>Subtract the calibrated center offset (if enabled).</item>
/// <item>Divide by the per-axis input range, then apply axis inversion.</item>
/// <item>Deadzone: map magnitude r from [inner, 1 - outer] to t in [0, 1]; r ≤ inner gives 0.</item>
/// <item>Response curve: t ← curve(t).</item>
/// <item>Anti-deadzone: t ← a + (1 - a)·t outside the deadzone.</item>
/// <item>Sensitivity: t ← t·s.</item>
/// <item>Clamp t to the output saturation and each axis to [-1, 1].</item>
/// <item>Denormalize to XInput units (done by the caller via <see cref="AxisMath.DenormalizeAxis"/>).</item>
/// </list>
/// </summary>
public static class StickProcessor
{
    /// <summary>Processes a raw XInput stick and returns the normalized output vector.</summary>
    public static StickVector Process(short rawX, short rawY, StickSettings settings) =>
        Process(new StickVector(AxisMath.NormalizeAxis(rawX), AxisMath.NormalizeAxis(rawY)), settings);

    /// <summary>Processes an already-normalized stick vector.</summary>
    public static StickVector Process(StickVector input, StickSettings settings)
    {
        if (!AxisMath.IsFinite(input.X) || !AxisMath.IsFinite(input.Y))
            return StickVector.Zero;
        if (settings.IsIdentity)
            return input;

        StickVector v = Precondition(input, settings);
        return settings.DeadzoneMode == DeadzoneMode.Axial ? ProcessAxial(v, settings) : ProcessRadial(v, settings);
    }

    /// <summary>Steps 2–3: offset correction, input range scaling and inversion.</summary>
    public static StickVector Precondition(StickVector input, StickSettings s)
    {
        double x = input.X, y = input.Y;
        if (s.OffsetCorrection)
        {
            x -= s.OffsetX;
            y -= s.OffsetY;
        }
        x /= s.RangeX;
        y /= s.RangeY;
        if (s.InvertX)
            x = -x;
        if (s.InvertY)
            y = -y;
        return new StickVector(x, y);
    }

    private static StickVector ProcessRadial(StickVector v, StickSettings s)
    {
        double r = v.Length;
        double m = MapMagnitude(r, s);
        if (m <= 0)
            return StickVector.Zero;

        // Direction is preserved: scale the unit vector by the new magnitude.
        double ux = v.X / r, uy = v.Y / r;
        return new StickVector(Math.Clamp(ux * m, -1.0, 1.0), Math.Clamp(uy * m, -1.0, 1.0));
    }

    private static StickVector ProcessAxial(StickVector v, StickSettings s)
    {
        double x = Math.Sign(v.X) * MapMagnitude(Math.Abs(v.X), s);
        double y = Math.Sign(v.Y) * MapMagnitude(Math.Abs(v.Y), s);
        return new StickVector(Math.Clamp(x, -1.0, 1.0), Math.Clamp(y, -1.0, 1.0));
    }

    /// <summary>
    /// Steps 4–8 for a single non-negative magnitude. Monotonic non-decreasing in <paramref name="r"/>
    /// for every valid <see cref="StickSettings"/>; result is within [0, OutputSaturation].
    /// </summary>
    public static double MapMagnitude(double r, StickSettings s)
    {
        if (!(r > s.InnerDeadzone))
            return 0.0;

        double activeSpan = (1.0 - s.OuterDeadzone) - s.InnerDeadzone;
        double t = Math.Clamp((r - s.InnerDeadzone) / activeSpan, 0.0, 1.0);
        t = s.Curve.Evaluate(t);
        t = s.AntiDeadzone + ((1.0 - s.AntiDeadzone) * t);
        t *= s.Sensitivity;
        return Math.Clamp(t, 0.0, s.OutputSaturation);
    }
}
