using System.Globalization;
using OpenController.Devices;
using OpenController.Models;
using OpenController.Profiles;

namespace OpenController.Tests;

public class MultiControllerTests
{
    private const int Centered = -1;

    private static int[] Axes(int x = 0, int y = 0, int z = 0, int rx = 0, int ry = 0, int rz = 0) =>
        [x, y, z, rx, ry, rz, 0, 0];

    private static bool[] Pressed(params int[] indices)
    {
        var b = new bool[32];
        foreach (int i in indices)
            b[i] = true;
        return b;
    }

    // ---------- Catalog ----------

    [Theory]
    [InlineData(0x054C, 0x05C4, ControllerFamily.DualShock4)]
    [InlineData(0x054C, 0x09CC, ControllerFamily.DualShock4)]
    [InlineData(0x054C, 0x0CE6, ControllerFamily.DualSense)]
    [InlineData(0x054C, 0x0DF2, ControllerFamily.DualSenseEdge)]
    [InlineData(0x057E, 0x2009, ControllerFamily.SwitchPro)]
    public void Catalog_KnowsSonyAndNintendo(int vid, int pid, ControllerFamily expected)
    {
        Assert.True(ControllerCatalog.TryLookup((ushort)vid, (ushort)pid, out ControllerFamily family, out ConnectionType transport, out string name));
        Assert.Equal(expected, family);
        Assert.Equal(ConnectionType.Unknown, transport); // same PID over USB and Bluetooth
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void Catalog_SonyProductIdUnderOtherVendor_IsUnknown()
    {
        Assert.False(ControllerCatalog.TryLookup(0x1234, 0x0CE6, out _, out _, out _));
    }

    // ---------- Layouts ----------

    [Fact]
    public void PlayStation_FaceButtonsMatchXboxPositions()
    {
        DirectInputLayout l = DirectInputLayout.PlayStation;
        Assert.Equal(GamepadButtons.X, l.ToGamepad(Axes(), Pressed(0), Centered).Buttons); // Square
        Assert.Equal(GamepadButtons.A, l.ToGamepad(Axes(), Pressed(1), Centered).Buttons); // Cross
        Assert.Equal(GamepadButtons.B, l.ToGamepad(Axes(), Pressed(2), Centered).Buttons); // Circle
        Assert.Equal(GamepadButtons.Y, l.ToGamepad(Axes(), Pressed(3), Centered).Buttons); // Triangle
    }

    [Fact]
    public void PlayStation_SystemButtonsAndSticks()
    {
        DirectInputLayout l = DirectInputLayout.PlayStation;
        GamepadState s = l.ToGamepad(Axes(), Pressed(8, 9, 10, 11, 12, 13), Centered);
        Assert.Equal(GamepadButtons.View | GamepadButtons.Menu | GamepadButtons.LeftThumb | GamepadButtons.RightThumb
            | GamepadButtons.Guide | GamepadButtons.Share, s.Buttons);

        // Digital L2/R2 bits do not become buttons; the analog Rx/Ry axes drive the triggers.
        s = l.ToGamepad(Axes(rx: 32767, ry: -32768, z: 32767, rz: -32768), Pressed(6, 7), Centered);
        Assert.Equal(GamepadButtons.None, s.Buttons);
        Assert.Equal(255, s.LeftTrigger);
        Assert.Equal(0, s.RightTrigger);
        Assert.Equal(32767, s.RightX);
        Assert.Equal(32767, s.RightY); // DirectInput up (negative) becomes XInput up (positive)
    }

    [Fact]
    public void SwitchPro_PositionalFaceButtonsAndDigitalTriggers()
    {
        DirectInputLayout l = DirectInputLayout.SwitchPro(hasRzAxis: false);
        Assert.Equal(GamepadButtons.A, l.ToGamepad(Axes(), Pressed(0), Centered).Buttons); // Nintendo B (bottom)
        Assert.Equal(GamepadButtons.B, l.ToGamepad(Axes(), Pressed(1), Centered).Buttons); // Nintendo A (right)
        Assert.Equal(GamepadButtons.Guide | GamepadButtons.Share, l.ToGamepad(Axes(), Pressed(12, 13), Centered).Buttons);

        GamepadState s = l.ToGamepad(Axes(rx: 32767), Pressed(6), Centered);
        Assert.Equal(255, s.LeftTrigger);
        Assert.Equal(0, s.RightTrigger);
        Assert.Equal(32767, s.RightX);
    }

    [Fact]
    public void Generic_KeepsPreviousDefaults()
    {
        DirectInputLayout zrz = DirectInputLayout.Generic(hasRzAxis: true);
        GamepadState s = zrz.ToGamepad(Axes(z: 1000, rz: 2000, rx: 32767), Pressed(0, 6, 7, 8), Centered);
        Assert.Equal(GamepadButtons.A | GamepadButtons.View | GamepadButtons.Menu | GamepadButtons.LeftThumb, s.Buttons);
        Assert.Equal(1000, s.RightX);
        Assert.Equal(255, s.LeftTrigger);

        DirectInputLayout rxry = DirectInputLayout.Generic(hasRzAxis: false);
        s = rxry.ToGamepad(Axes(rx: 1000), Pressed(6, 9, 11), 9000);
        Assert.Equal(GamepadButtons.Menu | GamepadButtons.RightThumb | GamepadButtons.DPadRight, s.Buttons);
        Assert.Equal(255, s.LeftTrigger);
        Assert.Equal(1000, s.RightX);
    }

    [Fact]
    public void Layout_ShortButtonArrayIsSafe()
    {
        GamepadState s = DirectInputLayout.PlayStation.ToGamepad(Axes(), [true, true], Centered);
        Assert.Equal(GamepadButtons.X | GamepadButtons.A, s.Buttons);
    }

    // ---------- Labels ----------

    [Fact]
    public void Labels_FollowControllerFamily()
    {
        Assert.Equal("A", GamepadControl.A.ShortLabel(ControllerFamily.XboxSeries));
        Assert.Equal("✕", GamepadControl.A.ShortLabel(ControllerFamily.DualSense));
        Assert.Equal("L2", GamepadControl.LeftTrigger.ShortLabel(ControllerFamily.DualShock4));
        Assert.Equal("Share", GamepadControl.View.ShortLabel(ControllerFamily.DualShock4));
        Assert.Equal("Create", GamepadControl.View.ShortLabel(ControllerFamily.DualSense));
        Assert.Equal("B", GamepadControl.A.ShortLabel(ControllerFamily.SwitchPro));
        Assert.Equal("ZR", GamepadControl.RightTrigger.ShortLabel(ControllerFamily.SwitchPro));
        Assert.Equal("LB", GamepadControl.LeftShoulder.ShortLabel(ControllerFamily.GenericDirectInput));
    }

    // ---------- Language default ----------

    [Theory]
    [InlineData("zh-TW", "zh-TW", "zh-TW")]
    [InlineData("en-US", "zh-TW", "zh-TW")] // English Windows, Taiwan regional format
    [InlineData("zh-HK", "en-US", "zh-TW")]
    [InlineData("en-US", "en-US", "en")]
    [InlineData("ja-JP", "ja-JP", "en")]
    public void DefaultLanguage_FollowsWindows(string ui, string format, string expected)
    {
        Assert.Equal(expected, AppSettings.DefaultLanguage(new CultureInfo(ui), new CultureInfo(format)));
    }

    [Fact]
    public void SettingsLoad_UsesDefaultsOnlyWithoutFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "xcc-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AppSettingsStore(Path.Combine(dir, "settings.json"));
            var defaults = new AppSettings { Language = "zh-TW" };
            Assert.Equal("zh-TW", store.Load(defaults).Settings.Language);

            store.Save(new AppSettings { Language = "en" });
            Assert.Equal("en", store.Load(defaults).Settings.Language); // a saved choice wins
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
