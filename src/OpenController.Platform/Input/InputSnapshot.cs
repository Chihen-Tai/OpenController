using OpenController.Diagnostics;
using OpenController.Mapping;
using OpenController.Models;
using OpenController.Platform.Input.DirectInput;
using OpenController.Processing;

namespace OpenController.Platform.Input;

/// <summary>One history sample: raw input plus processed output (normalized), with a Stopwatch timestamp.</summary>
public readonly record struct InputSample(
    long Timestamp,
    GamepadState Raw,
    StickVector LeftOut,
    StickVector RightOut,
    float LeftTriggerOut,
    float RightTriggerOut);

/// <summary>Immutable per-controller state published by the sampler.</summary>
public sealed record ControllerSnapshot(
    ControllerInfo Info,
    GamepadState Raw,
    ProcessedState Processed,
    OutputFrame Output,
    uint PacketNumber,
    // Rate of reported state changes per second (XInput packet number); see Diagnostics.RateMeter.
    double StateChangeRateHz,
    DirectInputRawState? DirectInputRaw,
    long Timestamp,
    // Update spacing while moving and suspected stalls since this controller connected.
    LinkQualityStats LinkQuality);

/// <summary>Immutable view of the whole input system at one instant.</summary>
public sealed record InputSnapshot
{
    public static InputSnapshot Empty { get; } = new();

    public long Timestamp { get; init; }
    public IReadOnlyList<ControllerSnapshot> Controllers { get; init; } = [];
    /// <summary>Measured loop rate of the sampler.</summary>
    public double SamplerRateHz { get; init; }
    public int TargetRateHz { get; init; }
    public bool HighResolutionTimer { get; init; }
    public bool XInputAvailable { get; init; }
    public bool GuideButtonSupported { get; init; }
    public bool DirectInputAvailable { get; init; }
    public string? DirectInputError { get; init; }
    /// <summary>XInput slots hidden because they belong to this application's virtual controller.</summary>
    public int ExcludedXInputSlotMask { get; init; }
    /// <summary>Controller currently feeding the virtual controller / keyboard output (null = none).</summary>
    public string? OutputSourceId { get; init; }

    public ControllerSnapshot? Find(string? id) => id is null ? null : Controllers.FirstOrDefault(c => c.Info.Id == id);
}
