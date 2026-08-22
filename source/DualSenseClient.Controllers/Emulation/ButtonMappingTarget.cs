namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Which analog trigger a mapping target drives, if any.
/// </summary>
public enum MappableTriggerSide
{
    /// <summary>
    /// The target drives no analog trigger.
    /// </summary>
    None = 0,

    /// <summary>
    /// The target drives the left trigger (LT / L2 byte).
    /// </summary>
    Left = 1,

    /// <summary>
    /// The target drives the right trigger (RT / R2 byte).
    /// </summary>
    Right = 2
}

/// <summary>
/// How an active trigger mapping writes to the virtual controller: mirrors are the built-in
/// defaults (analog passthrough of the physical trigger), custom mappings either force a full
/// pull or set only the click flag.
/// </summary>
public enum MappingTriggerOutput
{
    /// <summary>
    /// Mirrors the physical analog trigger byte (built-in default behavior only).
    /// </summary>
    Mirror = 0,

    /// <summary>
    /// Forces the analog byte to 255 while active and sets the click flag.
    /// </summary>
    FullPull = 1,

    /// <summary>
    /// Sets only the click flag while active; leaves the analog byte untouched.
    /// </summary>
    ClickOnly = 2
}

/// <summary>
/// D-pad direction bitmask shared by the virtual devices whose D-pad is reported as a
/// direction bitmask (the DualSense native bits and the DualShock 4 wire mask use the same
/// bit positions). Xbox 360 D-pad directions are plain button flags instead.
/// </summary>
[Flags]
public enum VirtualDPad : byte
{
    /// <summary>
    /// No direction.
    /// </summary>
    None = 0,

    /// <summary>
    /// Up direction.
    /// </summary>
    Up = 1,

    /// <summary>
    /// Down direction.
    /// </summary>
    Down = 2,

    /// <summary>
    /// Left direction.
    /// </summary>
    Left = 4,

    /// <summary>
    /// Right direction.
    /// </summary>
    Right = 8
}

/// <summary>
/// A resolved mapping target expressed mode-neutrally: device button flag bits, an optional
/// D-pad contribution, and optional analog-trigger semantics.
/// </summary>
public readonly struct ResolvedMappingTarget
{
    /// <summary>
    /// Device button flag bits (raw value of the concrete flags enum), OR-combinable.
    /// Zero when the target contributes no button flag.
    /// </summary>
    public ulong ButtonFlags { get; init; }

    /// <summary>
    /// The analog trigger driven by this target, if any.
    /// </summary>
    public MappableTriggerSide Trigger { get; init; }

    /// <summary>
    /// Output style applied while active when <see cref="Trigger"/> is not <c>None</c>.
    /// </summary>
    public MappingTriggerOutput Output { get; init; }

    /// <summary>
    /// D-pad directions contributed by this target.
    /// </summary>
    public VirtualDPad DPad { get; init; }

    /// <summary>
    /// Whether the target sends nothing at all (an explicit "None" mapping).
    /// </summary>
    public bool IsNone => ButtonFlags == 0 && Trigger == MappableTriggerSide.None && DPad == VirtualDPad.None;

    /// <summary>
    /// A target that sends nothing.
    /// </summary>
    public static ResolvedMappingTarget None { get; } = new ResolvedMappingTarget();
}

/// <summary>
/// Aggregated button translation result for one input report.
/// </summary>
public readonly struct MappedInputResult
{
    /// <summary>
    /// ORed device button flag bits.
    /// </summary>
    public ulong Buttons { get; init; }

    /// <summary>
    /// D-pad direction bitmask (<see cref="VirtualDPad"/>); Xbox 360 consumers ignore this
    /// because its directions arrive as plain button flags.
    /// </summary>
    public VirtualDPad DPad { get; init; }

    /// <summary>
    /// Left trigger byte (0-255).
    /// </summary>
    public byte LeftTrigger { get; init; }

    /// <summary>
    /// Right trigger byte (0-255).
    /// </summary>
    public byte RightTrigger { get; init; }
}