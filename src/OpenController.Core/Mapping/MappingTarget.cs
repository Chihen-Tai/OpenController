using OpenController.Models;

namespace OpenController.Mapping;

public enum MappingKind
{
    /// <summary>Identity: the control drives itself.</summary>
    Default,
    /// <summary>The control produces no output.</summary>
    Disabled,
    /// <summary>The control drives another gamepad control.</summary>
    Control,
    /// <summary>The control presses a keyboard key (requires keyboard output to be explicitly enabled).</summary>
    Keyboard,
}

/// <summary>What a physical control is mapped to.</summary>
public sealed record MappingTarget
{
    public MappingKind Kind { get; init; } = MappingKind.Default;
    /// <summary>Target control when <see cref="Kind"/> is <see cref="MappingKind.Control"/>.</summary>
    public GamepadControl? Control { get; init; }
    /// <summary>Windows virtual-key code (1..254) when <see cref="Kind"/> is <see cref="MappingKind.Keyboard"/>.</summary>
    public ushort? VirtualKey { get; init; }

    public static MappingTarget Default { get; } = new();
    public static MappingTarget Disabled { get; } = new() { Kind = MappingKind.Disabled };
    public static MappingTarget ToControl(GamepadControl control) => new() { Kind = MappingKind.Control, Control = control };
    public static MappingTarget ToKey(ushort virtualKey) => new() { Kind = MappingKind.Keyboard, VirtualKey = virtualKey };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        switch (Kind)
        {
            case MappingKind.Default:
            case MappingKind.Disabled:
                break;
            case MappingKind.Control:
                if (Control is not { } c || !Enum.IsDefined(c))
                    errors.Add("A control mapping needs a valid target control.");
                break;
            case MappingKind.Keyboard:
                if (VirtualKey is not { } vk || vk < 1 || vk > 254)
                    errors.Add("A keyboard mapping needs a virtual-key code between 1 and 254.");
                break;
            default:
                errors.Add($"Unknown mapping kind '{Kind}'.");
                break;
        }
        return errors;
    }
}
