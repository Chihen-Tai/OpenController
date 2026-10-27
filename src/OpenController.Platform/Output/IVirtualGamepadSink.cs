using OpenController.Models;

namespace OpenController.Platform.Output;

/// <summary>Receives processed gamepad states from the sampling thread.</summary>
public interface IVirtualGamepadSink
{
    /// <summary>True while a virtual device exists and accepts reports.</summary>
    bool IsActive { get; }

    /// <summary>Sends a state. Implementations must be cheap and must not block for long.</summary>
    void Submit(in GamepadState state);

    /// <summary>
    /// XInput slots that must not be read as physical inputs (they are this sink's own virtual device),
    /// as a bit mask. Prevents virtual → physical feedback loops.
    /// </summary>
    int ExcludedXInputSlotMask { get; }
}
