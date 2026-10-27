using OpenController.Diagnostics;
using OpenController.Models;

namespace OpenController.Tests;

public class DiagnosticsTests
{
    private static List<StickVector> RestingSamples(double offsetX, double offsetY, double noise, int count, int seed = 1)
    {
        var rng = new Random(seed);
        var list = new List<StickVector>(count);
        for (int i = 0; i < count; i++)
        {
            // Box–Muller Gaussian noise.
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            double mag = noise * Math.Sqrt(-2.0 * Math.Log(u1));
            list.Add(new StickVector(offsetX + (mag * Math.Cos(2 * Math.PI * u2)), offsetY + (mag * Math.Sin(2 * Math.PI * u2))));
        }
        return list;
    }

    [Fact]
    public void Drift_CleanStick_IsGoodWithMinimumDeadzone()
    {
        DriftResult r = DriftAnalyzer.Analyze(RestingSamples(0, 0, 0.001, 1000));
        Assert.Equal(DriftVerdict.Good, r.Verdict);
        Assert.InRange(r.RecommendedDeadzone, 0.02, 0.03);
        Assert.InRange(r.RmsNoise, 0.0005, 0.002);
    }

    [Fact]
    public void Drift_OffsetStick_ReportsOffsetAndLargerDeadzone()
    {
        DriftResult r = DriftAnalyzer.Analyze(RestingSamples(0.12, -0.05, 0.003, 1000));
        Assert.NotEqual(DriftVerdict.Invalid, r.Verdict);
        Assert.Equal(0.12, r.MeanOffset.X, 2);
        Assert.Equal(-0.05, r.MeanOffset.Y, 2);
        Assert.Equal(0.13, r.MeanOffsetMagnitude, 2);
        Assert.True(r.MaxOffset >= r.MeanOffsetMagnitude);
        // Deadzone must cover every observed resting sample.
        Assert.True(r.RecommendedDeadzone > r.MaxOffset);
        // With offset correction the remaining spread is just noise → much smaller deadzone.
        Assert.True(r.RecommendedDeadzoneWithOffsetCorrection < r.RecommendedDeadzone);
        Assert.True(r.RecommendedDeadzoneWithOffsetCorrection < 0.05);
    }

    [Fact]
    public void Drift_RejectsMovementDuringMeasurement()
    {
        List<StickVector> samples = RestingSamples(0, 0, 0.001, 500);
        samples.AddRange(Enumerable.Range(0, 30).Select(i => new StickVector(0.8, 0)));
        DriftResult r = DriftAnalyzer.Analyze(samples);
        Assert.Equal(DriftVerdict.Invalid, r.Verdict);
        Assert.NotNull(r.RejectionReason);
    }

    [Fact]
    public void Drift_RejectsSmallButSustainedTouch()
    {
        // Stick nudged from 0 to 0.3 halfway through — under the movement radius but not "at rest".
        List<StickVector> samples = RestingSamples(0, 0, 0.001, 500);
        samples.AddRange(RestingSamples(0.3, 0, 0.001, 500, seed: 2));
        Assert.Equal(DriftVerdict.Invalid, DriftAnalyzer.Analyze(samples).Verdict);
    }

    [Fact]
    public void Drift_RejectsTooFewSamples()
    {
        Assert.Equal(DriftVerdict.Invalid, DriftAnalyzer.Analyze(RestingSamples(0, 0, 0.001, 10)).Verdict);
    }

    [Fact]
    public void Drift_RejectsNonFiniteSamples()
    {
        List<StickVector> samples = RestingSamples(0, 0, 0.001, 200);
        samples[50] = new StickVector(double.NaN, 0);
        Assert.Equal(DriftVerdict.Invalid, DriftAnalyzer.Analyze(samples).Verdict);
    }

    [Theory]
    [InlineData(0.05, DriftVerdict.Good)]
    [InlineData(0.12, DriftVerdict.Minor)]
    [InlineData(0.2, DriftVerdict.Noticeable)]
    [InlineData(0.3, DriftVerdict.Severe)]
    public void Drift_Classification(double deadzone, DriftVerdict expected)
    {
        Assert.Equal(expected, DriftAnalyzer.Classify(deadzone));
    }

    [Fact]
    public void Drift_RecommendationIsRoundedUpAndClamped()
    {
        var o = new DriftAnalysisOptions();
        double r = DriftAnalyzer.Recommend(0.1, 0.05, 0.001, o);
        Assert.Equal(0.125, r, 9); // 0.1*1.15+0.01 = 0.125
        Assert.Equal(o.MaxRecommended, DriftAnalyzer.Recommend(0.9, 0.9, 0, o), 9);
        Assert.Equal(o.MinRecommended, DriftAnalyzer.Recommend(0, 0, 0, o), 9);
    }

    [Fact]
    public void Circularity_PerfectCircle()
    {
        var samples = Enumerable.Range(0, 3600).Select(i =>
        {
            double a = i / 10.0 * Math.PI / 180.0;
            return new StickVector(Math.Cos(a), Math.Sin(a));
        });
        CircularityResult r = CircularityAnalyzer.Analyze(samples);
        Assert.True(r.IsComplete);
        Assert.Equal(1.0, r.AngularCoverage, 9);
        Assert.Equal(0.0, r.MeanAbsoluteError, 6);
        Assert.Equal(1.0, r.RoundnessRatio, 6);
    }

    [Fact]
    public void Circularity_SquareGateHasLargerDiagonals()
    {
        // Points on the square boundary |x|,|y| <= 1.
        var samples = Enumerable.Range(0, 3600).Select(i =>
        {
            double a = i / 10.0 * Math.PI / 180.0;
            double c = Math.Cos(a), s = Math.Sin(a);
            double k = 1.0 / Math.Max(Math.Abs(c), Math.Abs(s));
            return new StickVector(c * k, s * k);
        });
        CircularityResult r = CircularityAnalyzer.Analyze(samples);
        Assert.Equal(Math.Sqrt(2), r.MaxRadius, 2);
        Assert.Equal(1.0, r.MinRadius, 2);
        Assert.True(r.MeanAbsoluteError > 0.1);
    }

    [Fact]
    public void Circularity_PartialRotationReportsCoverage()
    {
        var samples = Enumerable.Range(0, 900).Select(i =>
        {
            double a = i / 10.0 * Math.PI / 180.0; // 0..90°
            return new StickVector(Math.Cos(a), Math.Sin(a));
        });
        CircularityResult r = CircularityAnalyzer.Analyze(samples);
        Assert.InRange(r.AngularCoverage, 0.24, 0.27);
        Assert.False(r.IsComplete);
    }

    [Fact]
    public void Circularity_IgnoresCenterSamples()
    {
        CircularityResult r = CircularityAnalyzer.Analyze([new StickVector(0.1, 0.1), new StickVector(0, 0)]);
        Assert.Equal(0, r.AngularCoverage);
        Assert.Equal(2, r.SampleCount);
    }

    [Fact]
    public void SampleRing_CopySinceReturnsOnlyNewItems()
    {
        var ring = new SampleRing<int>(4);
        for (int i = 0; i < 3; i++)
            ring.Add(i);
        var dest = new List<int>();
        long seq = ring.CopySince(0, dest);
        Assert.Equal([0, 1, 2], dest);
        for (int i = 3; i < 10; i++)
            ring.Add(i);
        dest.Clear();
        seq = ring.CopySince(seq, dest);
        Assert.Equal([6, 7, 8, 9], dest); // older items were overwritten
        Assert.Equal(10, seq);

        Span<int> latest = stackalloc int[2];
        Assert.Equal(2, ring.CopyLatest(latest, 2));
        Assert.Equal(8, latest[0]);
        Assert.Equal(9, latest[1]);
    }

    [Fact]
    public void RateMeter_CountsEventsInWindow()
    {
        var meter = new RateMeter(1000, TimeSpan.FromSeconds(1));
        for (int i = 0; i < 125; i++)
            meter.Record(i * 8); // 125 events within 1 s
        Assert.Equal(125, meter.RatePerSecond(999), 0);
        Assert.Equal(0, meter.RatePerSecond(5000), 0);
    }
}
