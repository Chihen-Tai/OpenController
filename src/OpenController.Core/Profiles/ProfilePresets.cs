using OpenController.Processing;

namespace OpenController.Profiles;

/// <summary>
/// Built-in starting points. "Default" is a pure passthrough (no processing) so that enabling the app
/// never changes behavior until the user tunes something. Other presets are reasonable starting values,
/// not measured optimums — run the drift test to get values for a specific controller.
/// </summary>
public static class ProfilePresets
{
    public static Profile Default() => new() { Name = "Default" };

    public static Profile Racing() => new()
    {
        Name = "Racing",
        LeftStick = new StickSettings { InnerDeadzone = 0.05, Curve = ResponseCurve.Power(1.3) },
        RightStick = new StickSettings { InnerDeadzone = 0.10 },
        LeftTrigger = new TriggerSettings { Deadzone = 0.02, MaxInput = 0.97 },
        RightTrigger = new TriggerSettings { Deadzone = 0.02, MaxInput = 0.97 },
    };

    public static Profile Fps() => new()
    {
        Name = "FPS",
        LeftStick = new StickSettings { InnerDeadzone = 0.08 },
        RightStick = new StickSettings { InnerDeadzone = 0.07, Curve = ResponseCurve.Power(1.6) },
        LeftTrigger = new TriggerSettings { Deadzone = 0.05, ActivationThreshold = 0.3 },
        RightTrigger = new TriggerSettings { Deadzone = 0.05, ActivationThreshold = 0.3 },
    };

    public static Profile Emulator() => new()
    {
        Name = "Emulator",
        LeftStick = new StickSettings { InnerDeadzone = 0.15, OuterDeadzone = 0.05 },
        RightStick = new StickSettings { InnerDeadzone = 0.15, OuterDeadzone = 0.05 },
        LeftTrigger = new TriggerSettings { Deadzone = 0.1 },
        RightTrigger = new TriggerSettings { Deadzone = 0.1 },
    };

    public static IReadOnlyList<Profile> All() => [Default(), Racing(), Fps(), Emulator()];
}
