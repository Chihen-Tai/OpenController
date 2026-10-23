using XboxControllerCenter.Models;
using XboxControllerCenter.Processing;

namespace XboxControllerCenter.Mapping;

/// <summary>Final output for one controller: the virtual gamepad state plus keys to hold down.</summary>
public readonly record struct OutputFrame(GamepadState Gamepad, VirtualKeySet Keys);

/// <summary>
/// Applies a button map to a processed state.
/// <para>Rules:</para>
/// <list type="bullet">
/// <item>Unmapped controls (missing from the map or <see cref="MappingKind.Default"/>) drive themselves.
/// A trigger left on Default keeps its full analog value.</item>
/// <item>A trigger that is remapped elsewhere no longer drives its own analog output.</item>
/// <item>A control mapped onto a trigger presses that trigger fully (255).</item>
/// <item>Several sources mapped onto the same target are OR-combined (the largest trigger value wins).</item>
/// <item>A trigger counts as pressed when its processed value reaches its activation threshold.</item>
/// </list>
/// </summary>
public static class ButtonRemapper
{
    public static OutputFrame Apply(in ProcessedState input, IReadOnlyDictionary<GamepadControl, MappingTarget> map)
    {
        GamepadButtons buttons = GamepadButtons.None;
        double lt = 0, rt = 0;
        var keys = new VirtualKeySet();

        foreach (GamepadControl source in GamepadControls.All)
        {
            MappingTarget target = map.TryGetValue(source, out MappingTarget? t) && t is not null ? t : MappingTarget.Default;

            if (target.Kind == MappingKind.Default && source.IsTrigger())
            {
                // Analog passthrough.
                if (source == GamepadControl.LeftTrigger)
                    lt = Math.Max(lt, input.LeftTrigger);
                else
                    rt = Math.Max(rt, input.RightTrigger);
                continue;
            }

            if (!IsPressed(source, input))
                continue;

            switch (target.Kind)
            {
                case MappingKind.Default:
                    buttons |= source.ToButton();
                    break;
                case MappingKind.Control when target.Control is { } dest:
                    if (dest == GamepadControl.LeftTrigger)
                        lt = 1.0;
                    else if (dest == GamepadControl.RightTrigger)
                        rt = 1.0;
                    else
                        buttons |= dest.ToButton();
                    break;
                case MappingKind.Keyboard when target.VirtualKey is { } vk:
                    keys.Add(vk);
                    break;
            }
        }

        var gamepad = new GamepadState(
            buttons,
            AxisMath.DenormalizeTrigger(lt),
            AxisMath.DenormalizeTrigger(rt),
            AxisMath.DenormalizeAxis(input.LeftStick.X),
            AxisMath.DenormalizeAxis(input.LeftStick.Y),
            AxisMath.DenormalizeAxis(input.RightStick.X),
            AxisMath.DenormalizeAxis(input.RightStick.Y));
        return new OutputFrame(gamepad, keys);
    }

    public static bool IsPressed(GamepadControl control, in ProcessedState input) => control switch
    {
        GamepadControl.LeftTrigger => input.LeftTriggerPressed,
        GamepadControl.RightTrigger => input.RightTriggerPressed,
        _ => (input.Buttons & control.ToButton()) != 0,
    };

    /// <summary>The identity map (every control on Default).</summary>
    public static IReadOnlyDictionary<GamepadControl, MappingTarget> IdentityMap { get; } =
        GamepadControls.All.ToDictionary(c => c, _ => MappingTarget.Default);
}
