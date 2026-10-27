namespace XboxControllerCenter.Models;

/// <summary>
/// A remappable control. Unlike <see cref="GamepadButtons"/> this includes the analog
/// triggers, which act as digital sources/targets in the remapper.
/// </summary>
public enum GamepadControl
{
    A,
    B,
    X,
    Y,
    LeftShoulder,
    RightShoulder,
    LeftTrigger,
    RightTrigger,
    View,
    Menu,
    LeftThumb,
    RightThumb,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    Guide,
    Share,
}

public static class GamepadControls
{
    /// <summary>All controls in display order.</summary>
    public static IReadOnlyList<GamepadControl> All { get; } = Enum.GetValues<GamepadControl>();

    public static bool IsTrigger(this GamepadControl control) =>
        control is GamepadControl.LeftTrigger or GamepadControl.RightTrigger;

    /// <summary>Button mask for a control, or <see cref="GamepadButtons.None"/> for triggers.</summary>
    public static GamepadButtons ToButton(this GamepadControl control) => control switch
    {
        GamepadControl.A => GamepadButtons.A,
        GamepadControl.B => GamepadButtons.B,
        GamepadControl.X => GamepadButtons.X,
        GamepadControl.Y => GamepadButtons.Y,
        GamepadControl.LeftShoulder => GamepadButtons.LeftShoulder,
        GamepadControl.RightShoulder => GamepadButtons.RightShoulder,
        GamepadControl.View => GamepadButtons.View,
        GamepadControl.Menu => GamepadButtons.Menu,
        GamepadControl.LeftThumb => GamepadButtons.LeftThumb,
        GamepadControl.RightThumb => GamepadButtons.RightThumb,
        GamepadControl.DPadUp => GamepadButtons.DPadUp,
        GamepadControl.DPadDown => GamepadButtons.DPadDown,
        GamepadControl.DPadLeft => GamepadButtons.DPadLeft,
        GamepadControl.DPadRight => GamepadButtons.DPadRight,
        GamepadControl.Guide => GamepadButtons.Guide,
        GamepadControl.Share => GamepadButtons.Share,
        _ => GamepadButtons.None,
    };

    /// <summary>Short label printed on the physical controller.</summary>
    public static string ShortLabel(this GamepadControl control) => control switch
    {
        GamepadControl.LeftShoulder => "LB",
        GamepadControl.RightShoulder => "RB",
        GamepadControl.LeftTrigger => "LT",
        GamepadControl.RightTrigger => "RT",
        GamepadControl.LeftThumb => "L3",
        GamepadControl.RightThumb => "R3",
        GamepadControl.DPadUp => "D-pad ↑",
        GamepadControl.DPadDown => "D-pad ↓",
        GamepadControl.DPadLeft => "D-pad ←",
        GamepadControl.DPadRight => "D-pad →",
        _ => control.ToString(),
    };

    /// <summary>
    /// Label printed on a controller of the given family for the button that sits where the Xbox control sits
    /// (positional: Xbox A = PlayStation Cross = Nintendo B).
    /// </summary>
    public static string ShortLabel(this GamepadControl control, ControllerFamily family) => family.LabelStyle() switch
    {
        ButtonLabelStyle.PlayStation => control switch
        {
            GamepadControl.A => "✕",
            GamepadControl.B => "○",
            GamepadControl.X => "□",
            GamepadControl.Y => "△",
            GamepadControl.LeftShoulder => "L1",
            GamepadControl.RightShoulder => "R1",
            GamepadControl.LeftTrigger => "L2",
            GamepadControl.RightTrigger => "R2",
            GamepadControl.View => family == ControllerFamily.DualShock4 ? "Share" : "Create",
            GamepadControl.Menu => "Options",
            GamepadControl.Guide => "PS",
            GamepadControl.Share => "Touchpad",
            _ => control.ShortLabel(),
        },
        ButtonLabelStyle.Nintendo => control switch
        {
            GamepadControl.A => "B",
            GamepadControl.B => "A",
            GamepadControl.X => "Y",
            GamepadControl.Y => "X",
            GamepadControl.LeftShoulder => "L",
            GamepadControl.RightShoulder => "R",
            GamepadControl.LeftTrigger => "ZL",
            GamepadControl.RightTrigger => "ZR",
            GamepadControl.View => "−",
            GamepadControl.Menu => "+",
            GamepadControl.Guide => "Home",
            GamepadControl.Share => "Capture",
            _ => control.ShortLabel(),
        },
        _ => control.ShortLabel(),
    };
}
