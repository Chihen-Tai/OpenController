using System.Text.Json.Serialization;

namespace OpenController.Processing;

public enum CurveType
{
    /// <summary>f(t) = t.</summary>
    Linear,
    /// <summary>f(t) = t^k. k &gt; 1 gives finer control near the center, k &lt; 1 a faster response.</summary>
    Power,
    /// <summary>f(t) = t^k / (t^k + (1-t)^k). k &gt; 1 is flat at both ends and steep in the middle.</summary>
    SCurve,
    /// <summary>Piecewise-linear through user-defined monotonic points.</summary>
    Custom,
}

public readonly record struct CurvePoint(double X, double Y);

/// <summary>
/// A monotonic response curve on [0, 1] → [0, 1] with f(0) = 0.
/// All valid configurations are non-decreasing, so stronger stick deflection never produces weaker output.
/// </summary>
public sealed record ResponseCurve
{
    public const double MinExponent = 0.2;
    public const double MaxExponent = 5.0;

    public CurveType Type { get; init; } = CurveType.Linear;
    /// <summary>Exponent k for <see cref="CurveType.Power"/> and <see cref="CurveType.SCurve"/>.</summary>
    public double Exponent { get; init; } = 1.0;
    /// <summary>Control points for <see cref="CurveType.Custom"/>.</summary>
    public IReadOnlyList<CurvePoint> Points { get; init; } = [];

    public static ResponseCurve Linear { get; } = new();

    public static ResponseCurve Power(double exponent) => new() { Type = CurveType.Power, Exponent = exponent };

    public static ResponseCurve SCurve(double exponent) => new() { Type = CurveType.SCurve, Exponent = exponent };

    public static ResponseCurve Custom(params CurvePoint[] points) => new() { Type = CurveType.Custom, Points = points };

    [JsonIgnore]
    public bool IsIdentity => Type == CurveType.Linear || (Type is CurveType.Power or CurveType.SCurve && Exponent == 1.0);

    /// <summary>Value equality including the control points (records compare lists by reference otherwise).</summary>
    public bool Equals(ResponseCurve? other) =>
        other is not null && Type == other.Type && Exponent.Equals(other.Exponent) &&
        (ReferenceEquals(Points, other.Points) || (Points ?? []).SequenceEqual(other.Points ?? []));

    public override int GetHashCode() => HashCode.Combine(Type, Exponent, Points?.Count ?? 0);

    /// <summary>Evaluates the curve. Input is clamped to [0, 1]; the result is in [0, 1].</summary>
    public double Evaluate(double t)
    {
        if (double.IsNaN(t))
            return 0;
        t = Math.Clamp(t, 0.0, 1.0);
        double result = Type switch
        {
            CurveType.Linear => t,
            CurveType.Power => Math.Pow(t, Exponent),
            CurveType.SCurve => EvaluateSCurve(t, Exponent),
            CurveType.Custom => EvaluatePiecewise(t, Points),
            _ => t,
        };
        return Math.Clamp(result, 0.0, 1.0);
    }

    private static double EvaluateSCurve(double t, double k)
    {
        if (t <= 0)
            return 0;
        if (t >= 1)
            return 1;
        double a = Math.Pow(t, k);
        double b = Math.Pow(1.0 - t, k);
        return a / (a + b);
    }

    private static double EvaluatePiecewise(double t, IReadOnlyList<CurvePoint> points)
    {
        if (points.Count < 2)
            return t;
        for (int i = 1; i < points.Count; i++)
        {
            CurvePoint p0 = points[i - 1];
            CurvePoint p1 = points[i];
            if (t <= p1.X)
            {
                double span = p1.X - p0.X;
                double u = span <= 0 ? 1.0 : (t - p0.X) / span;
                return p0.Y + (u * (p1.Y - p0.Y));
            }
        }
        return points[^1].Y;
    }

    /// <summary>Returns human-readable problems; empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        switch (Type)
        {
            case CurveType.Linear:
                break;
            case CurveType.Power:
            case CurveType.SCurve:
                if (!AxisMath.IsFinite(Exponent) || Exponent < MinExponent || Exponent > MaxExponent)
                    errors.Add($"Curve exponent must be between {MinExponent} and {MaxExponent}.");
                break;
            case CurveType.Custom:
                ValidatePoints(errors);
                break;
            default:
                errors.Add($"Unknown curve type '{Type}'.");
                break;
        }
        return errors;
    }

    private void ValidatePoints(List<string> errors)
    {
        if (Points.Count < 2)
        {
            errors.Add("A custom curve needs at least two points.");
            return;
        }
        if (Points.Any(p => !AxisMath.IsFinite(p.X) || !AxisMath.IsFinite(p.Y) || p.Y < 0 || p.Y > 1))
            errors.Add("Custom curve points must have Y between 0 and 1.");
        if (Points[0].X != 0 || Points[0].Y != 0)
            errors.Add("A custom curve must start at (0, 0).");
        if (Points[^1].X != 1)
            errors.Add("A custom curve must end at X = 1.");
        for (int i = 1; i < Points.Count; i++)
        {
            if (Points[i].X <= Points[i - 1].X)
            {
                errors.Add("Custom curve X values must be strictly increasing.");
                break;
            }
            if (Points[i].Y < Points[i - 1].Y)
            {
                errors.Add("Custom curve Y values must not decrease (the curve must be monotonic).");
                break;
            }
        }
    }
}
