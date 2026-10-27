namespace OpenController.Processing;

/// <summary>Per-trigger processing parameters, normalized to 0..1.</summary>
public sealed record TriggerSettings
{
    /// <summary>Input at or below which output is zero. 0..0.9.</summary>
    public double Deadzone { get; init; }

    /// <summary>Input at or above which output is full (outer deadzone). Deadzone + 0.05 .. 1.</summary>
    public double MaxInput { get; init; } = 1.0;

    /// <summary>Minimum output once the trigger leaves the deadzone. 0..0.9.</summary>
    public double AntiDeadzone { get; init; }

    /// <summary>Output multiplier. 0.1..4.</summary>
    public double Sensitivity { get; init; } = 1.0;

    public ResponseCurve Curve { get; init; } = ResponseCurve.Linear;

    /// <summary>Processed value at which the trigger counts as "pressed" for digital mappings. 0.01..1.</summary>
    public double ActivationThreshold { get; init; } = 0.5;

    public static TriggerSettings Identity { get; } = new();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        StickSettings.CheckRange(errors, nameof(Deadzone), Deadzone, 0, 0.9);
        StickSettings.CheckRange(errors, nameof(MaxInput), MaxInput, 0.05, 1.0);
        StickSettings.CheckRange(errors, nameof(AntiDeadzone), AntiDeadzone, 0, 0.9);
        StickSettings.CheckRange(errors, nameof(Sensitivity), Sensitivity, StickSettings.MinSensitivity, StickSettings.MaxSensitivity);
        StickSettings.CheckRange(errors, nameof(ActivationThreshold), ActivationThreshold, 0.01, 1.0);
        if (errors.Count == 0 && MaxInput - Deadzone < StickSettings.MinActiveRange)
            errors.Add($"MaxInput must exceed Deadzone by at least {StickSettings.MinActiveRange:0.##}.");
        if (Curve is null)
            errors.Add("Curve is missing.");
        else
            errors.AddRange(Curve.Validate());
        return errors;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Validate().Count == 0;
}

public static class TriggerProcessor
{
    /// <summary>Processes a raw 0..255 trigger value; returns normalized output in [0, 1].</summary>
    public static double Process(byte raw, TriggerSettings s) => Process(AxisMath.NormalizeTrigger(raw), s);

    public static double Process(double input, TriggerSettings s)
    {
        if (!AxisMath.IsFinite(input) || !(input > s.Deadzone))
            return 0.0;
        double t = Math.Clamp((input - s.Deadzone) / (s.MaxInput - s.Deadzone), 0.0, 1.0);
        t = s.Curve.Evaluate(t);
        t = s.AntiDeadzone + ((1.0 - s.AntiDeadzone) * t);
        t *= s.Sensitivity;
        return Math.Clamp(t, 0.0, 1.0);
    }

    /// <summary>Digital "pressed" state derived from the processed value.</summary>
    public static bool IsPressed(double processed, TriggerSettings s) => processed > 0 && processed >= s.ActivationThreshold;
}
