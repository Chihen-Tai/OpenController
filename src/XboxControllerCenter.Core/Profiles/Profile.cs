using XboxControllerCenter.Mapping;
using XboxControllerCenter.Models;
using XboxControllerCenter.Processing;

namespace XboxControllerCenter.Profiles;

/// <summary>Settings for the optional virtual Xbox 360 controller.</summary>
public sealed record VirtualOutputSettings
{
    /// <summary>Start the virtual controller automatically when this profile is activated.</summary>
    public bool AutoStart { get; init; }
    /// <summary>Forward rumble requests from games to the physical controller (XInput sources only).</summary>
    public bool ForwardRumble { get; init; } = true;
}

/// <summary>
/// A complete, immutable configuration profile. Persisted as versioned JSON (see <see cref="ProfileSerializer"/>).
/// </summary>
public sealed record Profile
{
    public int SchemaVersion { get; init; } = ProfileSerializer.CurrentSchemaVersion;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Profile";
    public StickSettings LeftStick { get; init; } = StickSettings.Identity;
    public StickSettings RightStick { get; init; } = StickSettings.Identity;
    public TriggerSettings LeftTrigger { get; init; } = TriggerSettings.Identity;
    public TriggerSettings RightTrigger { get; init; } = TriggerSettings.Identity;
    public IReadOnlyDictionary<GamepadControl, MappingTarget> ButtonMap { get; init; } = new Dictionary<GamepadControl, MappingTarget>();
    public VirtualOutputSettings VirtualOutput { get; init; } = new();
    /// <summary>Executable names (e.g. "forza.exe") that activate this profile when auto-switching is enabled.</summary>
    public IReadOnlyList<string> AssociatedExecutables { get; init; } = [];
    /// <summary>Controller identity keys ("VID:PID") this profile is associated with.</summary>
    public IReadOnlyList<string> AssociatedControllers { get; init; } = [];

    public ProcessingSettings ToProcessingSettings() => new()
    {
        LeftStick = LeftStick,
        RightStick = RightStick,
        LeftTrigger = LeftTrigger,
        RightTrigger = RightTrigger,
    };

    public MappingTarget GetMapping(GamepadControl control) =>
        ButtonMap.TryGetValue(control, out MappingTarget? t) && t is not null ? t : MappingTarget.Default;

    public Profile WithMapping(GamepadControl control, MappingTarget target)
    {
        var map = new Dictionary<GamepadControl, MappingTarget>(ButtonMap);
        if (target.Kind == MappingKind.Default)
            map.Remove(control);
        else
            map[control] = target;
        return this with { ButtonMap = map };
    }

    /// <summary>True if any mapping targets the keyboard.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool UsesKeyboard => ButtonMap.Values.Any(t => t?.Kind == MappingKind.Keyboard);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Profile name must not be empty.");
        if (Name?.Length > 80)
            errors.Add("Profile name must be at most 80 characters.");
        if (Id == Guid.Empty)
            errors.Add("Profile id must not be empty.");
        AddPrefixed(errors, "Left stick", LeftStick?.Validate(), LeftStick is null);
        AddPrefixed(errors, "Right stick", RightStick?.Validate(), RightStick is null);
        AddPrefixed(errors, "Left trigger", LeftTrigger?.Validate(), LeftTrigger is null);
        AddPrefixed(errors, "Right trigger", RightTrigger?.Validate(), RightTrigger is null);
        if (ButtonMap is null)
        {
            errors.Add("Button map is missing.");
        }
        else
        {
            foreach ((GamepadControl control, MappingTarget target) in ButtonMap)
            {
                if (!Enum.IsDefined(control))
                    errors.Add($"Unknown control '{control}' in button map.");
                else if (target is null)
                    errors.Add($"Mapping for {control} is empty.");
                else
                    errors.AddRange(target.Validate().Select(e => $"{control}: {e}"));
            }
        }
        if (VirtualOutput is null)
            errors.Add("Virtual output settings are missing.");
        if (AssociatedExecutables is null || AssociatedControllers is null)
            errors.Add("Association lists are missing.");
        return errors;
    }

    private static void AddPrefixed(List<string> errors, string prefix, IReadOnlyList<string>? inner, bool missing)
    {
        if (missing)
            errors.Add($"{prefix}: settings are missing.");
        else if (inner is not null)
            errors.AddRange(inner.Select(e => $"{prefix}: {e}"));
    }
}
