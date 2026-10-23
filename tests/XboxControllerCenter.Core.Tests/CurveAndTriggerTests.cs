using XboxControllerCenter.Processing;

namespace XboxControllerCenter.Tests;

public class CurveAndTriggerTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.25)]
    [InlineData(1.0, 1.0)]
    public void PowerCurve_Squares(double t, double expected)
    {
        Assert.Equal(expected, ResponseCurve.Power(2).Evaluate(t), 9);
    }

    [Fact]
    public void SCurve_IsSymmetricAndPinned()
    {
        ResponseCurve c = ResponseCurve.SCurve(3);
        Assert.Equal(0.0, c.Evaluate(0), 12);
        Assert.Equal(0.5, c.Evaluate(0.5), 12);
        Assert.Equal(1.0, c.Evaluate(1), 12);
        Assert.Equal(1.0 - c.Evaluate(0.2), c.Evaluate(0.8), 9);
    }

    [Fact]
    public void CustomCurve_InterpolatesLinearly()
    {
        ResponseCurve c = ResponseCurve.Custom(new(0, 0), new(0.5, 0.2), new(1, 1));
        Assert.Equal(0.1, c.Evaluate(0.25), 9);
        Assert.Equal(0.6, c.Evaluate(0.75), 9);
    }

    [Fact]
    public void Curves_ClampInput()
    {
        Assert.Equal(1.0, ResponseCurve.Power(2).Evaluate(3), 12);
        Assert.Equal(0.0, ResponseCurve.Power(2).Evaluate(-1), 12);
        Assert.Equal(0.0, ResponseCurve.Linear.Evaluate(double.NaN), 12);
    }

    [Fact]
    public void AllCurveTypes_AreMonotonic()
    {
        ResponseCurve[] curves =
        [
            ResponseCurve.Linear, ResponseCurve.Power(0.2), ResponseCurve.Power(5), ResponseCurve.SCurve(0.3),
            ResponseCurve.SCurve(5), ResponseCurve.Custom(new(0, 0), new(0.3, 0.3), new(0.6, 0.3), new(1, 1)),
        ];
        foreach (ResponseCurve c in curves)
        {
            Assert.Empty(c.Validate());
            double prev = 0;
            for (int i = 0; i <= 1000; i++)
            {
                double v = c.Evaluate(i / 1000.0);
                Assert.True(v >= prev - 1e-12, $"{c.Type} decreased at {i}");
                prev = v;
            }
        }
    }

    [Fact]
    public void Validate_RejectsNonMonotonicCustomCurve()
    {
        Assert.NotEmpty(ResponseCurve.Custom(new(0, 0), new(0.5, 0.8), new(1, 0.5)).Validate());
        Assert.NotEmpty(ResponseCurve.Custom(new(0, 0), new(0.5, 0.5), new(0.5, 0.6), new(1, 1)).Validate());
        Assert.NotEmpty(ResponseCurve.Custom(new(0, 0.1), new(1, 1)).Validate());
        Assert.NotEmpty(ResponseCurve.Power(0).Validate());
        Assert.NotEmpty(ResponseCurve.Power(double.NaN).Validate());
    }

    [Fact]
    public void Trigger_IdentityIsExactForAllValues()
    {
        for (int v = 0; v <= 255; v++)
            Assert.Equal((byte)v, AxisMath.DenormalizeTrigger(TriggerProcessor.Process((byte)v, TriggerSettings.Identity)));
    }

    [Fact]
    public void Trigger_DeadzoneAndMaxInput()
    {
        var s = new TriggerSettings { Deadzone = 0.1, MaxInput = 0.9 };
        Assert.Equal(0.0, TriggerProcessor.Process(0.1, s), 12);
        Assert.Equal(0.5, TriggerProcessor.Process(0.5, s), 9);
        Assert.Equal(1.0, TriggerProcessor.Process(0.95, s), 12);
    }

    [Fact]
    public void Trigger_AntiDeadzoneAndSensitivity()
    {
        var s = new TriggerSettings { AntiDeadzone = 0.2 };
        Assert.Equal(0.0, TriggerProcessor.Process(0.0, s), 12);
        Assert.Equal(0.6, TriggerProcessor.Process(0.5, s), 9);
        var s2 = new TriggerSettings { Sensitivity = 2 };
        Assert.Equal(1.0, TriggerProcessor.Process(0.6, s2), 12);
    }

    [Fact]
    public void Trigger_OutputAlwaysWithinXInputRange()
    {
        var s = new TriggerSettings { Deadzone = 0.05, AntiDeadzone = 0.3, Sensitivity = 4, Curve = ResponseCurve.Power(0.3) };
        for (int v = 0; v <= 255; v++)
        {
            double o = TriggerProcessor.Process((byte)v, s);
            Assert.InRange(o, 0.0, 1.0);
            Assert.InRange(AxisMath.DenormalizeTrigger(o), (byte)0, (byte)255);
        }
    }

    [Fact]
    public void Trigger_ActivationThreshold()
    {
        var s = new TriggerSettings { ActivationThreshold = 0.3 };
        Assert.False(TriggerProcessor.IsPressed(0.29, s));
        Assert.True(TriggerProcessor.IsPressed(0.3, s));
        Assert.False(TriggerProcessor.IsPressed(0.0, s with { ActivationThreshold = 0.01 }));
    }

    [Fact]
    public void Trigger_ValidateRejectsBadRanges()
    {
        Assert.NotEmpty(new TriggerSettings { Deadzone = 0.5, MaxInput = 0.52 }.Validate());
        Assert.NotEmpty(new TriggerSettings { ActivationThreshold = 0 }.Validate());
        Assert.Empty(new TriggerSettings { Deadzone = 0.1, MaxInput = 0.9 }.Validate());
    }
}
