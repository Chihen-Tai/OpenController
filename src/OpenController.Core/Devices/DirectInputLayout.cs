using OpenController.Models;
using OpenController.Processing;

namespace OpenController.Devices;

/// <summary>DirectInput axes in the order of <c>DirectInputRawState.Axes</c>.</summary>
public enum DiAxis
{
    X,
    Y,
    Z,
    Rx,
    Ry,
    Rz,
    Slider0,
    Slider1,
}

/// <summary>
/// How a DirectInput device's axes, buttons and first POV hat translate to the Xbox layout. Button positions are
/// matched physically (the bottom face button becomes A, whatever it is called on the controller).
/// Axis values are expected in the range <see cref="AxisMin"/>..<see cref="AxisMax"/>.
/// </summary>
public sealed record DirectInputLayout
{
    public const int AxisMin = -32768;
    public const int AxisMax = 32767;

    public required string Name { get; init; }
    public DiAxis LeftX { get; init; } = DiAxis.X;
    public DiAxis LeftY { get; init; } = DiAxis.Y;
    public required DiAxis RightX { get; init; }
    public required DiAxis RightY { get; init; }
    /// <summary>Analog trigger axes; null when the triggers are digital buttons.</summary>
    public DiAxis? LeftTriggerAxis { get; init; }
    public DiAxis? RightTriggerAxis { get; init; }
    /// <summary>Button index for each Xbox control (triggers: used only when there is no trigger axis).</summary>
    public required IReadOnlyDictionary<GamepadControl, int> Buttons { get; init; }

    /// <summary>
    /// Default for unknown gamepads (no standard exists): X/Y = left stick; Z/Rz = right stick with Rx/Ry as analog
    /// triggers when Rz exists, otherwise Rx/Ry = right stick and buttons 6/7 as triggers. Buttons follow the most
    /// common order A, B, X, Y, LB, RB, …
    /// </summary>
    public static DirectInputLayout Generic(bool hasRzAxis) => hasRzAxis
        ? new DirectInputLayout
        {
            Name = "Generic (Z/Rz right stick)",
            RightX = DiAxis.Z,
            RightY = DiAxis.Rz,
            LeftTriggerAxis = DiAxis.Rx,
            RightTriggerAxis = DiAxis.Ry,
            Buttons = Map(a: 0, b: 1, x: 2, y: 3, lb: 4, rb: 5, view: 6, menu: 7, l3: 8, r3: 9),
        }
        : new DirectInputLayout
        {
            Name = "Generic (Rx/Ry right stick)",
            RightX = DiAxis.Rx,
            RightY = DiAxis.Ry,
            Buttons = Map(a: 0, b: 1, x: 2, y: 3, lb: 4, rb: 5, lt: 6, rt: 7, view: 8, menu: 9, l3: 10, r3: 11),
        };

    /// <summary>
    /// DualShock 4, DualSense and DualSense Edge through the standard HID report (USB and Bluetooth):
    /// Z/Rz = right stick, Rx/Ry = L2/R2; buttons 0 Square, 1 Cross, 2 Circle, 3 Triangle, 4 L1, 5 R1, 6 L2, 7 R2,
    /// 8 Share/Create, 9 Options, 10 L3, 11 R3, 12 PS, 13 touchpad click.
    /// </summary>
    public static DirectInputLayout PlayStation { get; } = new()
    {
        Name = "PlayStation",
        RightX = DiAxis.Z,
        RightY = DiAxis.Rz,
        LeftTriggerAxis = DiAxis.Rx,
        RightTriggerAxis = DiAxis.Ry,
        Buttons = Map(a: 1, b: 2, x: 0, y: 3, lb: 4, rb: 5, lt: 6, rt: 7, view: 8, menu: 9, l3: 10, r3: 11, guide: 12, share: 13),
    };

    /// <summary>
    /// Nintendo Switch Pro Controller over Bluetooth (simple HID report): digital ZL/ZR; buttons 0 B, 1 A, 2 Y, 3 X,
    /// 4 L, 5 R, 6 ZL, 7 ZR, 8 −, 9 +, 10 L-stick, 11 R-stick, 12 Home, 13 Capture. The right stick axis pair
    /// depends on the Windows HID descriptor, so it is chosen like the generic layout.
    /// </summary>
    public static DirectInputLayout SwitchPro(bool hasRzAxis) => new()
    {
        Name = "Switch Pro",
        RightX = hasRzAxis ? DiAxis.Z : DiAxis.Rx,
        RightY = hasRzAxis ? DiAxis.Rz : DiAxis.Ry,
        Buttons = Map(a: 0, b: 1, x: 2, y: 3, lb: 4, rb: 5, lt: 6, rt: 7, view: 8, menu: 9, l3: 10, r3: 11, guide: 12, share: 13),
    };

    /// <summary>Layout for a controller family read through DirectInput.</summary>
    public static DirectInputLayout For(ControllerFamily family, bool hasRzAxis) => family switch
    {
        ControllerFamily.DualShock4 or ControllerFamily.DualSense or ControllerFamily.DualSenseEdge => PlayStation,
        ControllerFamily.SwitchPro => SwitchPro(hasRzAxis),
        _ => Generic(hasRzAxis),
    };

    /// <summary>Converts one DirectInput reading to the Xbox layout (Y axes are flipped: DirectInput Y grows downward).</summary>
    public GamepadState ToGamepad(IReadOnlyList<int> axes, IReadOnlyList<bool> buttons, int pov)
    {
        int Axis(DiAxis a) => (int)a < axes.Count ? axes[(int)a] : 0;
        bool Btn(GamepadControl c) => Buttons.TryGetValue(c, out int i) && i >= 0 && i < buttons.Count && buttons[i];

        byte Trigger(DiAxis? axis, GamepadControl control) =>
            axis is { } a ? AxisToTrigger(Axis(a)) : (byte)(Btn(control) ? 255 : 0);

        GamepadButtons pressed = PovToDPad(pov);
        foreach (GamepadControl c in Buttons.Keys)
        {
            if (!c.IsTrigger() && Btn(c))
                pressed |= c.ToButton();
        }

        return new GamepadState(
            pressed,
            Trigger(LeftTriggerAxis, GamepadControl.LeftTrigger),
            Trigger(RightTriggerAxis, GamepadControl.RightTrigger),
            Clamp(Axis(LeftX)),
            Invert(Axis(LeftY)),
            Clamp(Axis(RightX)),
            Invert(Axis(RightY)));
    }

    /// <summary>POV in hundredths of a degree clockwise from north; -1 / 0xFFFF = centered.</summary>
    public static GamepadButtons PovToDPad(int pov)
    {
        if (pov < 0 || (pov & 0xFFFF) == 0xFFFF || pov >= 36000)
            return GamepadButtons.None;
        GamepadButtons b = GamepadButtons.None;
        if (pov > 27000 || pov < 9000) b |= GamepadButtons.DPadUp;
        if (pov > 0 && pov < 18000) b |= GamepadButtons.DPadRight;
        if (pov > 9000 && pov < 27000) b |= GamepadButtons.DPadDown;
        if (pov > 18000) b |= GamepadButtons.DPadLeft;
        return b;
    }

    private static short Clamp(int v) => (short)Math.Clamp(v, AxisMin, AxisMax);

    private static short Invert(int v) => AxisMath.DenormalizeAxis(-AxisMath.NormalizeAxis(Clamp(v)));

    private static byte AxisToTrigger(int v) =>
        AxisMath.DenormalizeTrigger((Clamp(v) - (double)AxisMin) / (AxisMax - (double)AxisMin));

    private static Dictionary<GamepadControl, int> Map(int a, int b, int x, int y, int lb, int rb,
        int view, int menu, int l3, int r3, int lt = -1, int rt = -1, int guide = -1, int share = -1)
    {
        var map = new Dictionary<GamepadControl, int>
        {
            [GamepadControl.A] = a,
            [GamepadControl.B] = b,
            [GamepadControl.X] = x,
            [GamepadControl.Y] = y,
            [GamepadControl.LeftShoulder] = lb,
            [GamepadControl.RightShoulder] = rb,
            [GamepadControl.View] = view,
            [GamepadControl.Menu] = menu,
            [GamepadControl.LeftThumb] = l3,
            [GamepadControl.RightThumb] = r3,
        };
        if (lt >= 0) map[GamepadControl.LeftTrigger] = lt;
        if (rt >= 0) map[GamepadControl.RightTrigger] = rt;
        if (guide >= 0) map[GamepadControl.Guide] = guide;
        if (share >= 0) map[GamepadControl.Share] = share;
        return map;
    }
}
