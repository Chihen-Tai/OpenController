using XboxControllerCenter.Models;

namespace XboxControllerCenter.Diagnostics;

/// <summary>Result of a stick-rotation test. Radii are normalized; the ideal edge is 1.0.</summary>
public sealed record CircularityResult
{
    public int SampleCount { get; init; }
    public int BinCount { get; init; }
    /// <summary>Maximum radius seen in each angular bin (index 0 starts at 0°, counter-clockwise). NaN = not visited.</summary>
    public required double[] BinMaxRadius { get; init; }
    /// <summary>Fraction of bins in which the stick reached at least <see cref="CircularityAnalyzer.EdgeThreshold"/>.</summary>
    public double AngularCoverage { get; init; }
    public double MaxRadius { get; init; }
    /// <summary>Smallest edge radius among covered bins.</summary>
    public double MinRadius { get; init; }
    public double MeanRadius { get; init; }
    /// <summary>Mean absolute deviation of the covered bins' edge radius from 1.0 ("average error").</summary>
    public double MeanAbsoluteError { get; init; }
    /// <summary>Root-mean-square deviation of the covered bins' edge radius from 1.0.</summary>
    public double RmsError { get; init; }
    /// <summary>MinRadius / MaxRadius (1.0 = perfectly round).</summary>
    public double RoundnessRatio { get; init; }

    public bool IsComplete => AngularCoverage >= CircularityAnalyzer.CompleteCoverage;
}

/// <summary>
/// Measures how round a stick's outer gate is: the user rotates the stick along its edge, samples are
/// binned by angle and the maximum radius per bin approximates the gate boundary.
/// </summary>
public static class CircularityAnalyzer
{
    public const int DefaultBins = 72; // 5° per bin
    /// <summary>A bin only counts as covered when the stick was pushed at least this far.</summary>
    public const double EdgeThreshold = 0.5;
    public const double CompleteCoverage = 0.95;

    public static CircularityResult Analyze(IEnumerable<StickVector> samples, int binCount = DefaultBins)
    {
        if (binCount < 4)
            throw new ArgumentOutOfRangeException(nameof(binCount));

        var bins = new double[binCount];
        Array.Fill(bins, double.NaN);
        int count = 0;
        foreach (StickVector s in samples)
        {
            if (!double.IsFinite(s.X) || !double.IsFinite(s.Y))
                continue;
            count++;
            double r = s.Length;
            if (r < EdgeThreshold)
                continue;
            int bin = (int)(s.AngleDegrees / 360.0 * binCount) % binCount;
            if (double.IsNaN(bins[bin]) || r > bins[bin])
                bins[bin] = r;
        }

        double[] covered = bins.Where(b => !double.IsNaN(b)).ToArray();
        if (covered.Length == 0)
        {
            return new CircularityResult { SampleCount = count, BinCount = binCount, BinMaxRadius = bins };
        }

        double max = covered.Max();
        double min = covered.Min();
        return new CircularityResult
        {
            SampleCount = count,
            BinCount = binCount,
            BinMaxRadius = bins,
            AngularCoverage = covered.Length / (double)binCount,
            MaxRadius = max,
            MinRadius = min,
            MeanRadius = covered.Average(),
            MeanAbsoluteError = covered.Average(r => Math.Abs(r - 1.0)),
            RmsError = Math.Sqrt(covered.Average(r => (r - 1.0) * (r - 1.0))),
            RoundnessRatio = max > 0 ? min / max : 0,
        };
    }
}
