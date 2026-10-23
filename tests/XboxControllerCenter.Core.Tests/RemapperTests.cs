using XboxControllerCenter.Mapping;
using XboxControllerCenter.Models;
using XboxControllerCenter.Processing;

namespace XboxControllerCenter.Tests;

public class RemapperTests
{
    private static ProcessedState State(GamepadButtons buttons = GamepadButtons.None, double lt = 0, double rt = 0,
        bool ltPressed = false, bool rtPressed = false) =>
        new(buttons, new StickVector(0.5, -0.5), StickVector.Zero, lt, rt, ltPressed, rtPressed);

    private static Dictionary<GamepadControl, MappingTarget> Map(params (GamepadControl, MappingTarget)[] entries) =>
        entries.ToDictionary(e => e.Item1, e => e.Item2);

    [Fact]
    public void IdentityMap_PassesEverythingThrough()
    {
        OutputFrame f = ButtonRemapper.Apply(State(GamepadButtons.A | GamepadButtons.DPadUp, lt: 0.5, rt: 1.0), ButtonRemapper.IdentityMap);
        Assert.Equal(GamepadButtons.A | GamepadButtons.DPadUp, f.Gamepad.Buttons);
        Assert.Equal(128, f.Gamepad.LeftTrigger);
        Assert.Equal(255, f.Gamepad.RightTrigger);
        Assert.Equal(16384, f.Gamepad.LeftX);
        Assert.Equal(-16384, f.Gamepad.LeftY);
        Assert.True(f.Keys.IsEmpty);
    }

    [Fact]
    public void EmptyMap_BehavesLikeIdentity()
    {
        OutputFrame f = ButtonRemapper.Apply(State(GamepadButtons.B), new Dictionary<GamepadControl, MappingTarget>());
        Assert.Equal(GamepadButtons.B, f.Gamepad.Buttons);
    }

    [Fact]
    public void SwapAAndB()
    {
        var map = Map((GamepadControl.A, MappingTarget.ToControl(GamepadControl.B)), (GamepadControl.B, MappingTarget.ToControl(GamepadControl.A)));
        Assert.Equal(GamepadButtons.B, ButtonRemapper.Apply(State(GamepadButtons.A), map).Gamepad.Buttons);
        Assert.Equal(GamepadButtons.A, ButtonRemapper.Apply(State(GamepadButtons.B), map).Gamepad.Buttons);
        Assert.Equal(GamepadButtons.A | GamepadButtons.B, ButtonRemapper.Apply(State(GamepadButtons.A | GamepadButtons.B), map).Gamepad.Buttons);
    }

    [Fact]
    public void SwapBumpers()
    {
        var map = Map((GamepadControl.LeftShoulder, MappingTarget.ToControl(GamepadControl.RightShoulder)),
            (GamepadControl.RightShoulder, MappingTarget.ToControl(GamepadControl.LeftShoulder)));
        Assert.Equal(GamepadButtons.RightShoulder, ButtonRemapper.Apply(State(GamepadButtons.LeftShoulder), map).Gamepad.Buttons);
    }

    [Fact]
    public void DisabledButton_ProducesNothing()
    {
        var map = Map((GamepadControl.LeftThumb, MappingTarget.Disabled));
        Assert.Equal(GamepadButtons.None, ButtonRemapper.Apply(State(GamepadButtons.LeftThumb), map).Gamepad.Buttons);
    }

    [Fact]
    public void L3ToA()
    {
        var map = Map((GamepadControl.LeftThumb, MappingTarget.ToControl(GamepadControl.A)));
        Assert.Equal(GamepadButtons.A, ButtonRemapper.Apply(State(GamepadButtons.LeftThumb), map).Gamepad.Buttons);
    }

    [Fact]
    public void TriggerToButton_UsesActivationState()
    {
        var map = Map((GamepadControl.RightTrigger, MappingTarget.ToControl(GamepadControl.X)));
        OutputFrame pressed = ButtonRemapper.Apply(State(rt: 0.8, rtPressed: true), map);
        Assert.Equal(GamepadButtons.X, pressed.Gamepad.Buttons);
        Assert.Equal(0, pressed.Gamepad.RightTrigger); // remapped away: analog no longer forwarded
        OutputFrame notPressed = ButtonRemapper.Apply(State(rt: 0.2, rtPressed: false), map);
        Assert.Equal(GamepadButtons.None, notPressed.Gamepad.Buttons);
    }

    [Fact]
    public void ButtonToTrigger_PressesFully()
    {
        var map = Map((GamepadControl.RightShoulder, MappingTarget.ToControl(GamepadControl.RightTrigger)));
        OutputFrame f = ButtonRemapper.Apply(State(GamepadButtons.RightShoulder, rt: 0.1), map);
        Assert.Equal(255, f.Gamepad.RightTrigger);
        Assert.Equal(GamepadButtons.None, f.Gamepad.Buttons);
    }

    [Fact]
    public void SwapTriggers_KeepsDigitalSemantics()
    {
        var map = Map((GamepadControl.LeftTrigger, MappingTarget.ToControl(GamepadControl.RightTrigger)),
            (GamepadControl.RightTrigger, MappingTarget.ToControl(GamepadControl.LeftTrigger)));
        OutputFrame f = ButtonRemapper.Apply(State(lt: 0.9, ltPressed: true), map);
        Assert.Equal(255, f.Gamepad.RightTrigger);
        Assert.Equal(0, f.Gamepad.LeftTrigger);
    }

    [Fact]
    public void KeyboardMapping_ProducesKeyAndNoButton()
    {
        const ushort vkSpace = 0x20;
        var map = Map((GamepadControl.A, MappingTarget.ToKey(vkSpace)));
        OutputFrame f = ButtonRemapper.Apply(State(GamepadButtons.A), map);
        Assert.True(f.Keys.Contains(vkSpace));
        Assert.Equal(GamepadButtons.None, f.Gamepad.Buttons);
        Assert.True(ButtonRemapper.Apply(State(), map).Keys.IsEmpty);
    }

    [Fact]
    public void MultipleSourcesToSameTarget_AreOrCombined()
    {
        var map = Map((GamepadControl.X, MappingTarget.ToControl(GamepadControl.A)));
        Assert.Equal(GamepadButtons.A, ButtonRemapper.Apply(State(GamepadButtons.A | GamepadButtons.X), map).Gamepad.Buttons);
    }

    [Fact]
    public void MappingTarget_ValidatesKeyRange()
    {
        Assert.NotEmpty(MappingTarget.ToKey(0).Validate());
        Assert.NotEmpty(MappingTarget.ToKey(300).Validate());
        Assert.NotEmpty(new MappingTarget { Kind = MappingKind.Control }.Validate());
        Assert.Empty(MappingTarget.ToKey(0x41).Validate());
    }

    [Fact]
    public void VirtualKeySet_SetOperations()
    {
        var a = new VirtualKeySet();
        a.Add(0x41);
        a.Add(0xFF);
        a.Add(0x01);
        var b = new VirtualKeySet();
        b.Add(0x41);
        Assert.Equal(new ushort[] { 0x01, 0xFF }, a.Except(b).ToArray());
        Assert.Equal(new ushort[] { 0x01, 0x41, 0xFF }, a.Keys.ToArray());
        Assert.NotEqual(a, b);
        a.Add(999); // ignored
        Assert.False(a.Contains(999));
    }
}
