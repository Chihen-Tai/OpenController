using XboxControllerCenter.Processing;

namespace XboxControllerCenter.Tests;

public class AxisMathTests
{
    [Theory]
    [InlineData((short)0, 0.0)]
    [InlineData(short.MaxValue, 1.0)]
    [InlineData(short.MinValue, -1.0)]
    [InlineData((short)16384, 16384 / 32767.0)]
    [InlineData((short)-16384, -0.5)]
    public void NormalizeAxis_MapsExtremesExactly(short raw, double expected)
    {
        Assert.Equal(expected, AxisMath.NormalizeAxis(raw), 12);
    }

    [Fact]
    public void NormalizeThenDenormalize_IsBitExactForEveryValue()
    {
        for (int v = short.MinValue; v <= short.MaxValue; v++)
            Assert.Equal((short)v, AxisMath.DenormalizeAxis(AxisMath.NormalizeAxis((short)v)));
    }

    [Fact]
    public void NormalizeAxis_ExampleFromSpec()
    {
        Assert.Equal(0.0377, AxisMath.NormalizeAxis(1234), 4);
    }

    [Theory]
    [InlineData(2.0, short.MaxValue)]
    [InlineData(-2.0, short.MinValue)]
    [InlineData(double.NaN, (short)0)]
    [InlineData(double.PositiveInfinity, short.MaxValue)]
    public void DenormalizeAxis_ClampsOutOfRange(double value, short expected)
    {
        Assert.Equal(expected, AxisMath.DenormalizeAxis(value));
    }

    [Fact]
    public void TriggerRoundTrip_IsExact()
    {
        for (int v = 0; v <= 255; v++)
            Assert.Equal((byte)v, AxisMath.DenormalizeTrigger(AxisMath.NormalizeTrigger((byte)v)));
    }

    [Theory]
    [InlineData(-1.0, (byte)0)]
    [InlineData(1.5, (byte)255)]
    [InlineData(double.NaN, (byte)0)]
    public void DenormalizeTrigger_ClampsTo0To255(double value, byte expected)
    {
        Assert.Equal(expected, AxisMath.DenormalizeTrigger(value));
    }

    [Theory]
    [InlineData(0, 0, 65535, short.MinValue)]
    [InlineData(65535, 0, 65535, short.MaxValue)]
    [InlineData(5, 10, 10, (short)0)] // degenerate range
    public void RangeToAxis_MapsDirectInputRange(int value, int min, int max, short expected)
    {
        Assert.Equal(expected, AxisMath.RangeToAxis(value, min, max));
    }
}
