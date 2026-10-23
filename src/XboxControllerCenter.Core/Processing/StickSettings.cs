using System.Text.Json.Serialization;

namespace XboxControllerCenter.Processing;

public enum DeadzoneMode
{
    /// <summary>Deadzone on the vector magnitude; preserves direction. Recommended.</summary>
    Radial,
    /// <summary>Independent deadzone on each axis; snaps toward the axes near the center.</summary>
    Axial,
}

/// <summary>
/// Per-stick processing parameters. All values are normalized (1.0 = full deflection).
/// See docs/PROCESSING_PIPELINE.md for the exact order of operations.
/// </summary>
public sealed record StickSettings
{
    public DeadzoneMode DeadzoneMode { get; init; } = DeadzoneMode.Radial;

    /// <summary>Radius (or per-axis distance) at or below which output is zero. 0..0.9.</summary>
    public double InnerDeadzone { get; init; }

    /// <summary>Fraction of the range near the edge that already counts as full deflection. 0..0.5.</summary>
    public double OuterDeadzone { get; init; }

    /// <summary>
    /// Minimum output magnitude once the stick leaves the inner deadzone. 0..0.9.
    /// Used to cancel a game's own deadzone; it deliberately creates a jump from 0 to this value.
    /// </summary>
    public double AntiDeadzone { get; init; }

    /// <summary>Output multiplier applied after the curve. 0.1..4.</summary>
    public double Sensitivity { get; init; } = 1.0;

    public ResponseCurve Curve { get; init; } = ResponseCurve.Linear;

    public bool InvertX { get; init; }
    public bool InvertY { get; init; }

    /// <summary>Raw normalized X value treated as full deflection (input range calibration). 0.5..1.</summary>
    public double RangeX { get; init; } = 1.0;

    /// <summary>Raw normalized Y value treated as full deflection (input range calibration). 0.5..1.</summary>
    public double RangeY { get; init; } = 1.0;

    /// <summary>Maximum output magnitude (output saturation). 0.05..1.</summary>
    public double OutputSaturation { get; init; } = 1.0;

    /// <summary>When true, <see cref="OffsetX"/>/<see cref="OffsetY"/> are subtracted from the raw position.</summary>
    public bool OffsetCorrection { get; init; }
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }

    public const double MaxInnerDeadzone = 0.9;
    public const double MaxOuterDeadzone = 0.5;
    public const double MinActiveRange = 0.05;
    public const double MaxAntiDeadzone = 0.9;
    public const double MinSensitivity = 0.1;
    public const double MaxSensitivity = 4.0;
    public const double MinRange = 0.5;
    public const double MinSaturation = 0.05;
    public const double MaxOffset = 0.5;

    /// <summary>No processing at all; the pipeline passes values through bit-exactly.</summary>
    public static StickSettings Identity { get; } = new();

    /// <summary>
    /// True when every parameter is neutral. The pipeline then skips the circular clamp, so raw values
    /// (including square-gate corners beyond radius 1) are forwarded unchanged.
    /// </summary>
    [JsonIgnore]
    public bool IsIdentity =>
        InnerDeadzone == 0 && OuterDeadzone == 0 && AntiDeadzone == 0 && Sensitivity == 1.0 &&
        Curve.IsIdentity && !InvertX && !InvertY && RangeX == 1.0 && RangeY == 1.0 &&
        OutputSaturation == 1.0 && (!OffsetCorrection || (OffsetX == 0 && OffsetY == 0));

    /// <summary>Returns human-readable problems; empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        CheckRange(errors, nameof(InnerDeadzone), InnerDeadzone, 0, MaxInnerDeadzone);
        CheckRange(errors, nameof(OuterDeadzone), OuterDeadzone, 0, MaxOuterDeadzone);
        CheckRange(errors, nameof(AntiDeadzone), AntiDeadzone, 0, MaxAntiDeadzone);
        CheckRange(errors, nameof(Sensitivity), Sensitivity, MinSensitivity, MaxSensitivity);
        CheckRange(errors, nameof(RangeX), RangeX, MinRange, 1.0);
        CheckRange(errors, nameof(RangeY), RangeY, MinRange, 1.0);
        CheckRange(errors, nameof(OutputSaturation), OutputSaturation, MinSaturation, 1.0);
        CheckRange(errors, nameof(OffsetX), OffsetX, -MaxOffset, MaxOffset);
        CheckRange(errors, nameof(OffsetY), OffsetY, -MaxOffset, MaxOffset);
        if (errors.Count == 0 && (1.0 - OuterDeadzone) - InnerDeadzone < MinActiveRange)
            errors.Add($"Inner deadzone plus outer deadzone leave less than {MinActiveRange:0.##} of usable range.");
        if (!Enum.IsDefined(DeadzoneMode))
            errors.Add($"Unknown deadzone mode '{DeadzoneMode}'.");
        if (Curve is null)
            errors.Add("Curve is missing.");
        else
            errors.AddRange(Curve.Validate());
        return errors;
    }

    [JsonIgnore]
    public bool IsValid => Validate().Count == 0;

    internal static void CheckRange(List<string> errors, string name, double value, double min, double max)
    {
        if (!AxisMath.IsFinite(value) || value < min || value > max)
            errors.Add($"{name} must be between {min:0.###} and {max:0.###} (was {value:0.###}).");
    }
}
