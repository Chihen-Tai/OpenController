using XboxControllerCenter.Models;
using XboxControllerCenter.Processing;

namespace XboxControllerCenter.Tests;

public class StickProcessorTests
{
    private const double Tol = 1e-9;

    [Fact]
    public void Identity_PassesValuesThroughUnchanged_IncludingSquareCorners()
    {
        var corner = new StickVector(1.0, 1.0);
        Assert.Equal(corner, StickProcessor.Process(corner, StickSettings.Identity));
        Assert.Equal(new StickVector(0.01, -0.02), StickProcessor.Process(new StickVector(0.01, -0.02), StickSettings.Identity));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.05, 0.0)]
    [InlineData(0.1, 0.0)]   // exactly on the boundary: r <= d gives zero
    [InlineData(0.0707, 0.0707)]
    public void RadialDeadzone_ZeroInsideDeadzone(double x, double y)
    {
        var s = new StickSettings { InnerDeadzone = 0.1 };
        StickVector v = StickProcessor.Process(new StickVector(x, y), s);
        if (Math.Sqrt((x * x) + (y * y)) <= 0.1)
            Assert.Equal(StickVector.Zero, v);
    }

    [Fact]
    public void RadialDeadzone_IsContinuousAtTheEdge()
    {
        var s = new StickSettings { InnerDeadzone = 0.2 };
        StickVector justOutside = StickProcessor.Process(new StickVector(0.2 + 1e-9, 0), s);
        Assert.True(justOutside.Length < 1e-6);
    }

    [Fact]
    public void RadialDeadzone_RescalesLinearlyToFullRange()
    {
        var s = new StickSettings { InnerDeadzone = 0.2 };
        // r = 0.6 → (0.6 - 0.2) / 0.8 = 0.5
        Assert.Equal(0.5, StickProcessor.Process(new StickVector(0.6, 0), s).X, 9);
        Assert.Equal(1.0, StickProcessor.Process(new StickVector(1.0, 0), s).X, 9);
    }

    [Fact]
    public void RadialDeadzone_PreservesDirection()
    {
        var s = new StickSettings { InnerDeadzone = 0.25, Curve = ResponseCurve.Power(2) };
        var input = new StickVector(0.3, 0.4);
        StickVector output = StickProcessor.Process(input, s);
        Assert.Equal(input.AngleDegrees, output.AngleDegrees, 6);
    }

    [Fact]
    public void OuterDeadzone_ReachesFullOutputEarly()
    {
        var s = new StickSettings { InnerDeadzone = 0.1, OuterDeadzone = 0.1 };
        Assert.Equal(1.0, StickProcessor.Process(new StickVector(0.9, 0), s).Length, 9);
        Assert.Equal(1.0, StickProcessor.Process(new StickVector(0.95, 0), s).Length, 9);
    }

    [Fact]
    public void Output_NeverExceedsUnitCircleOrSaturation()
    {
        var s = new StickSettings { InnerDeadzone = 0.05, Sensitivity = 3.0, OutputSaturation = 0.8 };
        var rng = new Random(42);
        for (int i = 0; i < 10_000; i++)
        {
            var v = new StickVector((rng.NextDouble() * 2) - 1, (rng.NextDouble() * 2) - 1);
            StickVector o = StickProcessor.Process(v, s);
            Assert.True(o.Length <= 0.8 + Tol, $"{v} -> {o}");
            Assert.InRange(o.X, -1, 1);
            Assert.InRange(o.Y, -1, 1);
        }
    }

    [Fact]
    public void AntiDeadzone_JumpsToMinimumOutsideDeadzone()
    {
        var s = new StickSettings { InnerDeadzone = 0.1, AntiDeadzone = 0.2 };
        Assert.Equal(0.0, StickProcessor.Process(new StickVector(0.1, 0), s).Length, 12);
        Assert.Equal(0.2, StickProcessor.Process(new StickVector(0.1 + 1e-9, 0), s).Length, 6);
        Assert.Equal(1.0, StickProcessor.Process(new StickVector(1.0, 0), s).Length, 9);
        // Halfway through the active range: 0.2 + 0.8 * 0.5 = 0.6
        Assert.Equal(0.6, StickProcessor.Process(new StickVector(0.55, 0), s).Length, 9);
    }

    [Fact]
    public void Sensitivity_ScalesAndClamps()
    {
        var s = new StickSettings { Sensitivity = 0.5 };
        Assert.Equal(0.25, StickProcessor.Process(new StickVector(0.5, 0), s).X, 9);
        var s2 = new StickSettings { Sensitivity = 2.0 };
        Assert.Equal(1.0, StickProcessor.Process(new StickVector(0.7, 0), s2).X, 9);
    }

    [Theory]
    [MemberData(nameof(ValidConfigurations))]
    public void MapMagnitude_IsMonotonicForValidSettings(StickSettings s)
    {
        Assert.Empty(s.Validate());
        double prev = 0;
        for (int i = 0; i <= 2000; i++)
        {
            double r = i / 2000.0 * 1.5;
            double m = StickProcessor.MapMagnitude(r, s);
            Assert.True(m >= prev - Tol, $"Not monotonic at r={r}: {m} < {prev}");
            prev = m;
        }
    }

    public static TheoryData<StickSettings> ValidConfigurations() => new()
    {
        new StickSettings(),
        new StickSettings { InnerDeadzone = 0.3, OuterDeadzone = 0.2, AntiDeadzone = 0.25, Sensitivity = 1.7, Curve = ResponseCurve.Power(2.5) },
        new StickSettings { InnerDeadzone = 0.1, Curve = ResponseCurve.SCurve(3) },
        new StickSettings { InnerDeadzone = 0.1, Curve = ResponseCurve.Custom(new(0, 0), new(0.5, 0.2), new(1, 1)) },
        new StickSettings { InnerDeadzone = 0.1, Sensitivity = 0.3, OutputSaturation = 0.5 },
    };

    [Fact]
    public void AxialDeadzone_TreatsAxesIndependently()
    {
        var s = new StickSettings { DeadzoneMode = DeadzoneMode.Axial, InnerDeadzone = 0.2 };
        StickVector o = StickProcessor.Process(new StickVector(0.15, 0.6), s);
        Assert.Equal(0.0, o.X, 12);
        Assert.Equal(0.5, o.Y, 9);
    }

    [Fact]
    public void Inversion_FlipsAxes()
    {
        var s = new StickSettings { InvertX = true, InvertY = true };
        StickVector o = StickProcessor.Process(new StickVector(0.5, -0.25), s);
        Assert.Equal(-0.5, o.X, 9);
        Assert.Equal(0.25, o.Y, 9);
    }

    [Fact]
    public void OffsetCorrection_CentersADriftingStick()
    {
        var s = new StickSettings { OffsetCorrection = true, OffsetX = 0.05, OffsetY = -0.03, InnerDeadzone = 0.02 };
        Assert.Equal(StickVector.Zero, StickProcessor.Process(new StickVector(0.05, -0.03), s));
    }

    [Fact]
    public void InputRange_ScalesShortAxisToFull()
    {
        var s = new StickSettings { RangeX = 0.8 };
        Assert.Equal(1.0, StickProcessor.Process(new StickVector(0.8, 0), s).X, 9);
    }

    [Fact]
    public void NonFiniteInput_ProducesNeutral()
    {
        var s = new StickSettings { InnerDeadzone = 0.1 };
        Assert.Equal(StickVector.Zero, StickProcessor.Process(new StickVector(double.NaN, 0.5), s));
    }

    [Fact]
    public void RawShortOverload_UsesNormalization()
    {
        var s = new StickSettings { InnerDeadzone = 0.1 };
        Assert.Equal(StickVector.Zero, StickProcessor.Process(1000, -1000, s));
        Assert.Equal(1.0, StickProcessor.Process(short.MaxValue, 0, s).X, 9);
    }

    [Theory]
    [InlineData(-0.1, 0, 0, 1)]
    [InlineData(0.95, 0, 0, 1)]
    [InlineData(0.5, 0.5, 0, 1)]   // no usable range left
    [InlineData(0.1, 0, 0.95, 1)]  // anti-deadzone too large
    [InlineData(0.1, 0, 0, 0.0)]   // sensitivity too small
    [InlineData(double.NaN, 0, 0, 1)]
    public void Validate_RejectsInvalidConfigurations(double inner, double outer, double anti, double sens)
    {
        var s = new StickSettings { InnerDeadzone = inner, OuterDeadzone = outer, AntiDeadzone = anti, Sensitivity = sens };
        Assert.NotEmpty(s.Validate());
    }
}
