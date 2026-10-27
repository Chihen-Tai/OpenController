using OpenController.Models;

namespace OpenController.Diagnostics;

public enum DriftVerdict
{
    /// <summary>Measurement rejected (too few samples or the stick moved).</summary>
    Invalid,
    Good,
    Minor,
    Noticeable,
    Severe,
}

public sealed record DriftAnalysisOptions
{
    /// <summary>Minimum number of samples for a valid measurement.</summary>
    public int MinSamples { get; init; } = 100;

    /// <summary>Any sample farther than this from the center means the stick was not at rest.</summary>
    public double MovementRadius { get; init; } = 0.40;

    /// <summary>
    /// If the 99th-percentile distance from the median exceeds this value the stick was touched or
    /// moved during the test (resting jitter is normally far below this).
    /// </summary>
    public double MaxSpread { get; init; } = 0.08;

    /// <summary>Safety factor applied to the observed worst-case radius.</summary>
    public double SafetyFactor { get; init; } = 1.15;

    /// <summary>Additive margin on top of the safety factor.</summary>
    public double SafetyMargin { get; init; } = 0.01;

    /// <summary>Lower bound for a recommendation (very clean sticks still need some deadzone).</summary>
    public double MinRecommended { get; init; } = 0.02;

    /// <summary>Upper bound for a recommendation.</summary>
    public double MaxRecommended { get; init; } = 0.5;
}

/// <summary>Result of a resting-stick measurement. All values are normalized (1.0 = full deflection).</summary>
public sealed record DriftResult
{
    public DriftVerdict Verdict { get; init; }
    public string? RejectionReason { get; init; }
    public int SampleCount { get; init; }

    /// <summary>Mean resting position (the offset).</summary>
    public StickVector MeanOffset { get; init; }
    /// <summary>|mean| — distance of the average resting position from the true center.</summary>
    public double MeanOffsetMagnitude { get; init; }
    /// <summary>Largest observed distance from the true center.</summary>
    public double MaxOffset { get; init; }
    /// <summary>RMS distance of samples from their mean (sensor noise).</summary>
    public double RmsNoise { get; init; }
    /// <summary>Mean radial distance from the true center.</summary>
    public double MeanRadius { get; init; }
    /// <summary>Standard deviation of the radial distance from the true center.</summary>
    public double RadialStdDev { get; init; }
    /// <summary>99th-percentile radial distance from the true center.</summary>
    public double P99Radius { get; init; }

    /// <summary>Recommended inner deadzone without offset correction.</summary>
    public double RecommendedDeadzone { get; init; }
    /// <summary>Recommended inner deadzone when offset correction subtracts <see cref="MeanOffset"/> first.</summary>
    public double RecommendedDeadzoneWithOffsetCorrection { get; init; }
}

/// <summary>
/// Resting-stick (drift) analysis. Pure function of the samples, so it is fully unit-testable.
/// <para>
/// Recommendation: R = clamp(SafetyFactor · max(maxRadius, meanRadius + 4·σr) + SafetyMargin), rounded up
/// to 0.005. The same formula is applied to distances from the mean for the offset-corrected variant.
/// </para>
/// </summary>
public static class DriftAnalyzer
{
    public static DriftResult Analyze(IReadOnlyList<StickVector> samples, DriftAnalysisOptions? options = null)
    {
        options ??= new DriftAnalysisOptions();
        int n = samples.Count;
        if (n < options.MinSamples)
            return Reject(n, $"Not enough samples ({n} < {options.MinSamples}). Is the controller connected?");

        foreach (StickVector s in samples)
        {
            if (!double.IsFinite(s.X) || !double.IsFinite(s.Y))
                return Reject(n, "Invalid (non-finite) samples received.");
        }

        double maxRadius = 0;
        foreach (StickVector s in samples)
            maxRadius = Math.Max(maxRadius, s.Length);
        if (maxRadius > options.MovementRadius)
            return Reject(n, "The stick moved during the measurement. Release both sticks and try again.");

        // Spread around the median is robust against a slowly drifting average.
        double medX = Median(samples.Select(s => s.X));
        double medY = Median(samples.Select(s => s.Y));
        double p99Spread = Percentile(samples.Select(s => Math.Sqrt(Sq(s.X - medX) + Sq(s.Y - medY))), 0.99);
        if (p99Spread > options.MaxSpread)
            return Reject(n, "The stick was touched or moved during the measurement. Release both sticks and try again.");

        double meanX = samples.Average(s => s.X);
        double meanY = samples.Average(s => s.Y);
        var radii = samples.Select(s => s.Length).ToArray();
        double meanRadius = radii.Average();
        double radialStd = Math.Sqrt(radii.Average(r => Sq(r - meanRadius)));
        double rmsNoise = Math.Sqrt(samples.Average(s => Sq(s.X - meanX) + Sq(s.Y - meanY)));
        double p99Radius = Percentile(radii, 0.99);

        var centered = samples.Select(s => Math.Sqrt(Sq(s.X - meanX) + Sq(s.Y - meanY))).ToArray();
        double centeredMean = centered.Average();
        double centeredStd = Math.Sqrt(centered.Average(r => Sq(r - centeredMean)));

        double recommended = Recommend(maxRadius, meanRadius, radialStd, options);
        double recommendedCorrected = Recommend(centered.Max(), centeredMean, centeredStd, options);

        return new DriftResult
        {
            Verdict = Classify(recommended),
            SampleCount = n,
            MeanOffset = new StickVector(meanX, meanY),
            MeanOffsetMagnitude = Math.Sqrt(Sq(meanX) + Sq(meanY)),
            MaxOffset = maxRadius,
            RmsNoise = rmsNoise,
            MeanRadius = meanRadius,
            RadialStdDev = radialStd,
            P99Radius = p99Radius,
            RecommendedDeadzone = recommended,
            RecommendedDeadzoneWithOffsetCorrection = recommendedCorrected,
        };
    }

    internal static double Recommend(double maxRadius, double meanRadius, double radialStd, DriftAnalysisOptions o)
    {
        double worst = Math.Max(maxRadius, meanRadius + (4 * radialStd));
        double r = (worst * o.SafetyFactor) + o.SafetyMargin;
        r = Math.Ceiling(r / 0.005) * 0.005;
        return Math.Round(Math.Clamp(r, o.MinRecommended, o.MaxRecommended), 3);
    }

    /// <summary>Thresholds relative to the commonly used XInput default deadzone (≈0.24).</summary>
    public static DriftVerdict Classify(double recommendedDeadzone) => recommendedDeadzone switch
    {
        <= 0.08 => DriftVerdict.Good,
        <= 0.15 => DriftVerdict.Minor,
        <= 0.25 => DriftVerdict.Noticeable,
        _ => DriftVerdict.Severe,
    };

    private static DriftResult Reject(int n, string reason) =>
        new() { Verdict = DriftVerdict.Invalid, RejectionReason = reason, SampleCount = n };

    private static double Sq(double v) => v * v;

    internal static double Median(IEnumerable<double> values) => Percentile(values, 0.5);

    /// <summary>Linear-interpolated percentile, p in [0, 1].</summary>
    internal static double Percentile(IEnumerable<double> values, double p)
    {
        double[] sorted = values.ToArray();
        if (sorted.Length == 0)
            return 0;
        Array.Sort(sorted);
        double pos = p * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        return sorted[lo] + ((sorted[hi] - sorted[lo]) * (pos - lo));
    }
}
