namespace OpenController.Models;

/// <summary>Windows input API through which a controller is read.</summary>
public enum InputApi
{
    XInput,
    DirectInput,
}

/// <summary>Physical transport. <see cref="Unknown"/> whenever it cannot be determined reliably.</summary>
public enum ConnectionType
{
    Unknown,
    Usb,
    Bluetooth,
    /// <summary>Xbox Wireless (proprietary 2.4 GHz via adapter or Xbox 360 wireless receiver).</summary>
    XboxWireless,
}

public enum ControllerFamily
{
    Unknown,
    XboxSeries,
    XboxElite2,
    XboxOne,
    Xbox360,
    GenericXInput,
    GenericDirectInput,
    DualShock4,
    DualSense,
    DualSenseEdge,
    SwitchPro,
}

/// <summary>Which names the physical controller prints on its buttons.</summary>
public enum ButtonLabelStyle
{
    /// <summary>A/B/X/Y, LB/RB, LT/RT, View/Menu.</summary>
    Xbox,
    /// <summary>Cross/Circle/Square/Triangle, L1/R1, L2/R2, Share or Create/Options.</summary>
    PlayStation,
    /// <summary>B/A/Y/X (positions swapped against Xbox), L/R, ZL/ZR, −/+.</summary>
    Nintendo,
}

/// <summary>Capabilities that the input API actually reported (not guessed).</summary>
public sealed record ControllerCapabilities
{
    public bool HasGuideButton { get; init; }
    public bool HasShareButton { get; init; }
    public bool HasRumble { get; init; }
    public bool IsWirelessFlagSet { get; init; }
    public int AxisCount { get; init; }
    public int ButtonCount { get; init; }
    public int PovCount { get; init; }
    /// <summary>Free-form description of the subtype reported by the API, e.g. "Gamepad".</summary>
    public string? SubType { get; init; }
}

/// <summary>Static description of a detected controller.</summary>
public sealed record ControllerInfo
{
    /// <summary>Stable key while the device stays attached, e.g. "xinput:0" or "dinput:{guid}".</summary>
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required InputApi Api { get; init; }
    public ConnectionType Connection { get; init; } = ConnectionType.Unknown;
    /// <summary>How <see cref="Connection"/> was determined: "ProductId", "HidPath", "GipAmbiguous" or "WirelessFlagOnly".</summary>
    public string? ConnectionEvidence { get; init; }
    public ControllerFamily Family { get; init; } = ControllerFamily.Unknown;
    /// <summary>XInput user index 0..3 (shown to users as player 1..4).</summary>
    public int? XInputSlot { get; init; }
    public ushort? VendorId { get; init; }
    public ushort? ProductId { get; init; }
    public Guid? InstanceGuid { get; init; }
    public ControllerCapabilities Capabilities { get; init; } = new();

    /// <summary>"VID:PID" key used to associate profiles with a controller model, or null if unknown.</summary>
    public string? IdentityKey => VendorId is { } v && ProductId is { } p ? $"{v:X4}:{p:X4}" : null;

    public bool IsXboxFamily => Family is ControllerFamily.XboxSeries or ControllerFamily.XboxElite2
        or ControllerFamily.XboxOne or ControllerFamily.Xbox360;

    public ButtonLabelStyle LabelStyle => Family.LabelStyle();
}

public static class ControllerFamilies
{
    public static ButtonLabelStyle LabelStyle(this ControllerFamily family) => family switch
    {
        ControllerFamily.DualShock4 or ControllerFamily.DualSense or ControllerFamily.DualSenseEdge => ButtonLabelStyle.PlayStation,
        ControllerFamily.SwitchPro => ButtonLabelStyle.Nintendo,
        _ => ButtonLabelStyle.Xbox,
    };
}
