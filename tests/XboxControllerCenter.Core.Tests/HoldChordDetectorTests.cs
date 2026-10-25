using XboxControllerCenter.Mapping;
using XboxControllerCenter.Models;

namespace XboxControllerCenter.Tests;

public class HoldChordDetectorTests
{
    private const long Hz = 1000; // timestamps in milliseconds
    private const GamepadButtons Chord = GamepadButtons.LeftThumb | GamepadButtons.RightThumb;

    private static HoldChordDetector Detector() => new(Chord, TimeSpan.FromMilliseconds(500), Hz);

    [Fact]
    public void FiresOnceAfterHoldTime()
    {
        HoldChordDetector d = Detector();
        Assert.False(d.Update(0, Chord));
        Assert.True(d.IsHeld);
        Assert.False(d.Update(499, Chord));
        Assert.True(d.Update(500, Chord));
        Assert.False(d.Update(600, Chord));
        Assert.False(d.Update(5000, Chord)); // still held: no repeat
    }

    [Fact]
    public void ReleaseRearms()
    {
        HoldChordDetector d = Detector();
        d.Update(0, Chord);
        Assert.True(d.Update(500, Chord));
        Assert.False(d.Update(600, GamepadButtons.LeftThumb)); // one button released
        Assert.False(d.IsHeld);
        Assert.False(d.Update(700, Chord));
        Assert.True(d.Update(1200, Chord));
    }

    [Fact]
    public void ShortPressesNeverFire()
    {
        HoldChordDetector d = Detector();
        for (long t = 0; t < 5000; t += 400)
        {
            Assert.False(d.Update(t, Chord));
            Assert.False(d.Update(t + 300, Chord));
            Assert.False(d.Update(t + 350, GamepadButtons.None));
        }
    }

    [Fact]
    public void SingleButtonDoesNotCount()
    {
        HoldChordDetector d = Detector();
        Assert.False(d.Update(0, GamepadButtons.LeftThumb));
        Assert.False(d.Update(1000, GamepadButtons.LeftThumb));
        Assert.False(d.Update(2000, GamepadButtons.RightThumb | GamepadButtons.A));
        Assert.False(d.IsHeld);
    }

    [Fact]
    public void ExtraButtonsAreIgnored()
    {
        HoldChordDetector d = Detector();
        d.Update(0, Chord | GamepadButtons.A);
        Assert.True(d.Update(500, Chord | GamepadButtons.LeftShoulder));
    }

    [Fact]
    public void ResetForgetsHold()
    {
        HoldChordDetector d = Detector();
        d.Update(0, Chord);
        d.Reset();
        Assert.False(d.Update(500, Chord)); // hold restarts at 500
        Assert.True(d.Update(1000, Chord));
    }

    [Fact]
    public void EmptyChordIsRejected() =>
        Assert.Throws<ArgumentException>(() => new HoldChordDetector(GamepadButtons.None, TimeSpan.FromSeconds(1), Hz));
}
