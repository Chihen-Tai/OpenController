namespace XboxControllerCenter.Models;

/// <summary>
/// Digital gamepad buttons. The low 16 bits intentionally match the documented
/// <c>XINPUT_GAMEPAD_*</c> bit masks so an XInput <c>wButtons</c> value can be cast directly.
/// </summary>
[Flags]
public enum GamepadButtons : uint
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    /// <summary>"Menu" on Xbox One/Series controllers, "Start" on Xbox 360.</summary>
    Menu = 0x0010,
    /// <summary>"View" on Xbox One/Series controllers, "Back" on Xbox 360.</summary>
    View = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    /// <summary>Xbox/Guide button. Only reported by the undocumented XInputGetStateEx export.</summary>
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
    /// <summary>Share button (Xbox Series). Not exposed through XInput; outside the XInput bit range on purpose.</summary>
    Share = 0x0001_0000,

    /// <summary>All bits that an Xbox 360 virtual controller can represent.</summary>
    XInputMask = 0xF7FF,
}
