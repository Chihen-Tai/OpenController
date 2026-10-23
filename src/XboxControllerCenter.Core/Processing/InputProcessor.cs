using XboxControllerCenter.Models;

namespace XboxControllerCenter.Processing;

/// <summary>Result of calibration/tuning, before remapping. Sticks and triggers are normalized.</summary>
public readonly record struct ProcessedState(
    GamepadButtons Buttons,
    StickVector LeftStick,
    StickVector RightStick,
    double LeftTrigger,
    double RightTrigger,
    bool LeftTriggerPressed,
    bool RightTriggerPressed);

/// <summary>Processing parameters for all analog inputs of one controller.</summary>
public sealed record ProcessingSettings
{
    public StickSettings LeftStick { get; init; } = StickSettings.Identity;
    public StickSettings RightStick { get; init; } = StickSettings.Identity;
    public TriggerSettings LeftTrigger { get; init; } = TriggerSettings.Identity;
    public TriggerSettings RightTrigger { get; init; } = TriggerSettings.Identity;
}

public static class InputProcessor
{
    public static ProcessedState Process(in GamepadState raw, ProcessingSettings settings)
    {
        double lt = TriggerProcessor.Process(raw.LeftTrigger, settings.LeftTrigger);
        double rt = TriggerProcessor.Process(raw.RightTrigger, settings.RightTrigger);
        return new ProcessedState(
            raw.Buttons,
            StickProcessor.Process(raw.LeftX, raw.LeftY, settings.LeftStick),
            StickProcessor.Process(raw.RightX, raw.RightY, settings.RightStick),
            lt,
            rt,
            TriggerProcessor.IsPressed(lt, settings.LeftTrigger),
            TriggerProcessor.IsPressed(rt, settings.RightTrigger));
    }
}
