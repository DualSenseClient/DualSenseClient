using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Settings.Sections;
using DualSenseClient.VIIPER.DualSense;
using DualSenseClient.VIIPER.DualShock4;
using DualSenseClient.VIIPER.Xbox360;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Pure translation helpers between the physical DualSense input report and the input state
/// formats of the virtual controllers. Axis conversions are stateless; button translations go
/// through <see cref="ButtonMappingTable"/> instances whose built-in defaults reproduce the
/// original one-to-one mappings, so custom remappings can be layered on top.
/// </summary>
public static class VirtualInputMapper
{
    /// <summary>
    /// Maps a physical stick/DPad byte (0-255, center 128) to the signed sbyte used
    /// by the virtual DualSense and DualShock 4 devices (center 0).
    /// </summary>
    public static sbyte DualSenseStick(byte raw) => (sbyte)(raw - 128);

    /// <summary>
    /// Maps a physical stick byte to the signed 16-bit value used by the virtual
    /// Xbox 360 device (full range, -32768..32767).
    /// </summary>
    public static short X360Axis(byte raw) => (short)Math.Clamp((raw - 128) * 256, short.MinValue, short.MaxValue);

    /// <summary>
    /// Maps a physical stick byte to the signed 16-bit value used by the virtual
    /// Xbox 360 device with the Y axis inverted (up is positive for XInput, while
    /// the physical DualSense reports up as 0).
    /// </summary>
    public static short X360AxisInverted(byte raw) => (short)Math.Clamp((128 - raw) * 256, short.MinValue, short.MaxValue);

    /// <summary>
    /// Converts the physical DualSense gyroscope counts (16.384 LSB/dps) to the
    /// fixed-point degrees-per-second scale (16 counts/dps) used by the virtual
    /// DualShock 4. Rounds to the nearest count.
    /// </summary>
    public static short GyroToDs4(short raw) => (short)((raw * 125 + (raw >= 0 ? 64 : -64)) / 128);

    /// <summary>
    /// Converts the physical DualSense accelerometer counts (8192 LSB/g) to the
    /// fixed-point metres-per-second-squared scale (512 counts/m/s²) used by the virtual
    /// DualShock 4. Rounds to the nearest count.
    /// </summary>
    public static short AccelToDs4(short raw) => (short)((raw * 981 + (raw >= 0 ? 800 : -800)) / 1600);

    /// <summary>
    /// Scales a physical DualSense touchpad Y coordinate (0-1079) onto the shorter
    /// virtual DualShock 4 touchpad (0-942), so touches keep their relative height
    /// instead of pinning to its bottom edge.
    /// </summary>
    public static ushort TouchYToDs4(ushort raw) => (ushort)Math.Min((raw * 942 + 539) / 1079, 942);

    // ── Built-in default mapping tables ─────────────────────────

    /// <summary>
    /// The built-in Xbox 360 mapping: PlayStation-standard face buttons (Cross=A,
    /// Circle=B, Square=X, Triangle=Y), Create/Options as Back/Start, PS as Guide,
    /// D-pad directions as plain flags, and analog trigger passthrough.
    /// </summary>
    public static readonly IReadOnlyDictionary<ButtonType, ResolvedMappingTarget> DefaultXbox360Targets =
        new Dictionary<ButtonType, ResolvedMappingTarget>
        {
            [ButtonType.Cross] = Flag(Xbox360Buttons.A),
            [ButtonType.Circle] = Flag(Xbox360Buttons.B),
            [ButtonType.Square] = Flag(Xbox360Buttons.X),
            [ButtonType.Triangle] = Flag(Xbox360Buttons.Y),
            [ButtonType.DPadUp] = Flag(Xbox360Buttons.DPadUp),
            [ButtonType.DPadDown] = Flag(Xbox360Buttons.DPadDown),
            [ButtonType.DPadLeft] = Flag(Xbox360Buttons.DPadLeft),
            [ButtonType.DPadRight] = Flag(Xbox360Buttons.DPadRight),
            [ButtonType.L1] = Flag(Xbox360Buttons.LeftShoulder),
            [ButtonType.R1] = Flag(Xbox360Buttons.RightShoulder),
            [ButtonType.L3] = Flag(Xbox360Buttons.LeftThumb),
            [ButtonType.R3] = Flag(Xbox360Buttons.RightThumb),
            [ButtonType.Create] = Flag(Xbox360Buttons.Back),
            [ButtonType.Options] = Flag(Xbox360Buttons.Start),
            [ButtonType.PS] = Flag(Xbox360Buttons.Guide),
            [ButtonType.L2] = new ResolvedMappingTarget
            {
                Trigger = MappableTriggerSide.Left,
                Output = MappingTriggerOutput.Mirror
            },
            [ButtonType.R2] = new ResolvedMappingTarget
            {
                Trigger = MappableTriggerSide.Right,
                Output = MappingTriggerOutput.Mirror
            }
        };

    /// <summary>
    /// The built-in DualShock 4 mapping: near one-to-one, with the physical Create button
    /// reported as Share and analog trigger passthrough.
    /// </summary>
    public static readonly IReadOnlyDictionary<ButtonType, ResolvedMappingTarget> DefaultDualShock4Targets =
        new Dictionary<ButtonType, ResolvedMappingTarget>
        {
            [ButtonType.Square] = Flag(DualShock4Buttons.Square),
            [ButtonType.Cross] = Flag(DualShock4Buttons.Cross),
            [ButtonType.Circle] = Flag(DualShock4Buttons.Circle),
            [ButtonType.Triangle] = Flag(DualShock4Buttons.Triangle),
            [ButtonType.DPadUp] = new ResolvedMappingTarget
            {
                DPad = VirtualDPad.Up
            },
            [ButtonType.DPadDown] = new ResolvedMappingTarget
            {
                DPad = VirtualDPad.Down
            },
            [ButtonType.DPadLeft] = new ResolvedMappingTarget
            {
                DPad = VirtualDPad.Left
            },
            [ButtonType.DPadRight] = new ResolvedMappingTarget
            {
                DPad = VirtualDPad.Right
            },
            [ButtonType.L1] = Flag(DualShock4Buttons.L1),
            [ButtonType.R1] = Flag(DualShock4Buttons.R1),
            [ButtonType.L2] = new ResolvedMappingTarget
            {
                Trigger = MappableTriggerSide.Left,
                Output = MappingTriggerOutput.Mirror,
                ButtonFlags = (ulong)DualShock4Buttons.L2
            },
            [ButtonType.R2] = new ResolvedMappingTarget
            {
                Trigger = MappableTriggerSide.Right,
                Output = MappingTriggerOutput.Mirror,
                ButtonFlags = (ulong)DualShock4Buttons.R2
            },
            [ButtonType.L3] = Flag(DualShock4Buttons.L3),
            [ButtonType.R3] = Flag(DualShock4Buttons.R3),
            [ButtonType.Create] = Flag(DualShock4Buttons.Share),
            [ButtonType.Options] = Flag(DualShock4Buttons.Options),
            [ButtonType.PS] = Flag(DualShock4Buttons.PS),
            [ButtonType.TouchPad] = Flag(DualShock4Buttons.Touchpad)
        };

    /// <summary>
    /// The built-in DualSense mapping: one-to-one including Edge function keys and paddles.
    /// </summary>
    public static readonly IReadOnlyDictionary<ButtonType, ResolvedMappingTarget> DefaultDualSenseTargets =
        new Dictionary<ButtonType, ResolvedMappingTarget>
        {
            [ButtonType.Square] = Flag(DualSenseButtons.Square),
            [ButtonType.Cross] = Flag(DualSenseButtons.Cross),
            [ButtonType.Circle] = Flag(DualSenseButtons.Circle),
            [ButtonType.Triangle] = Flag(DualSenseButtons.Triangle),
            [ButtonType.DPadUp] = new ResolvedMappingTarget
            {
                DPad = VirtualDPad.Up
            },
            [ButtonType.DPadDown] = new ResolvedMappingTarget
            {
                DPad = VirtualDPad.Down
            },
            [ButtonType.DPadLeft] = new ResolvedMappingTarget
            {
                DPad = VirtualDPad.Left
            },
            [ButtonType.DPadRight] = new ResolvedMappingTarget
            {
                DPad = VirtualDPad.Right
            },
            [ButtonType.L1] = Flag(DualSenseButtons.L1),
            [ButtonType.R1] = Flag(DualSenseButtons.R1),
            [ButtonType.L2] = new ResolvedMappingTarget
            {
                Trigger = MappableTriggerSide.Left,
                Output = MappingTriggerOutput.Mirror,
                ButtonFlags = (ulong)DualSenseButtons.L2
            },
            [ButtonType.R2] = new ResolvedMappingTarget
            {
                Trigger = MappableTriggerSide.Right,
                Output = MappingTriggerOutput.Mirror,
                ButtonFlags = (ulong)DualSenseButtons.R2
            },
            [ButtonType.Create] = Flag(DualSenseButtons.Create),
            [ButtonType.Options] = Flag(DualSenseButtons.Options),
            [ButtonType.L3] = Flag(DualSenseButtons.L3),
            [ButtonType.R3] = Flag(DualSenseButtons.R3),
            [ButtonType.PS] = Flag(DualSenseButtons.PS),
            [ButtonType.TouchPad] = Flag(DualSenseButtons.Touchpad),
            [ButtonType.Mute] = Flag(DualSenseButtons.MicMute),
            [ButtonType.Edge_LeftFunction] = Flag(DualSenseButtons.LeftFunction),
            [ButtonType.Edge_RightFunction] = Flag(DualSenseButtons.RightFunction),
            [ButtonType.Edge_LeftPaddle] = Flag(DualSenseButtons.L4),
            [ButtonType.Edge_RightPaddle] = Flag(DualSenseButtons.R4)
        };

    /// <summary>
    /// The built-in Xbox 360 table (no user entries).
    /// </summary>
    public static ButtonMappingTable Xbox360DefaultTable { get; } = ButtonMappingTable.Resolve(DefaultXbox360Targets, null, ParseXbox360Target, null);

    /// <summary>
    /// The built-in DualShock 4 table (no user entries).
    /// </summary>
    public static ButtonMappingTable DualShock4DefaultTable { get; } =
        ButtonMappingTable.Resolve(DefaultDualShock4Targets, null, ParseDualShock4Target, null);

    /// <summary>
    /// The built-in DualSense table (no user entries).
    /// </summary>
    public static ButtonMappingTable DualSenseDefaultTable { get; } =
        ButtonMappingTable.Resolve(DefaultDualSenseTargets, null, ParseDualSenseTarget, null);

    /// <summary>
    /// Builds an Xbox 360 table from the given user mapping entries overlaid on the defaults.
    /// Unknown entry names are reported through <paramref name="logWarning"/>.
    /// </summary>
    public static ButtonMappingTable Xbox360Table(IEnumerable<ButtonMappingEntry>? entries, Action<string>? logWarning = null)
        => ButtonMappingTable.Resolve(DefaultXbox360Targets, entries, ParseXbox360Target, logWarning);

    /// <summary>
    /// Builds a DualShock 4 table from the given user mapping entries overlaid on the defaults.
    /// </summary>
    public static ButtonMappingTable DualShock4Table(IEnumerable<ButtonMappingEntry>? entries, Action<string>? logWarning = null)
        => ButtonMappingTable.Resolve(DefaultDualShock4Targets, entries, ParseDualShock4Target, logWarning);

    /// <summary>
    /// Builds a DualSense table from the given user mapping entries overlaid on the defaults.
    /// </summary>
    public static ButtonMappingTable DualSenseTable(IEnumerable<ButtonMappingEntry>? entries, Action<string>? logWarning = null)
        => ButtonMappingTable.Resolve(DefaultDualSenseTargets, entries, ParseDualSenseTarget, logWarning);

    /// <summary>
    /// Parses an Xbox 360 target name: flag member names ("A", "DPadUp", ...), the analog
    /// trigger pseudo-targets "LeftTrigger"/"RightTrigger", or "None".
    /// </summary>
    public static ResolvedMappingTarget? ParseXbox360Target(string name)
    {
        if (name.Equals("LeftTrigger", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedMappingTarget
            {
                Trigger = MappableTriggerSide.Left,
                Output = MappingTriggerOutput.FullPull
            };
        }

        if (name.Equals("RightTrigger", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedMappingTarget
            {
                Trigger = MappableTriggerSide.Right,
                Output = MappingTriggerOutput.FullPull
            };
        }

        if (TryParseFlag(name, out Xbox360Buttons value))
        {
            return new ResolvedMappingTarget
            {
                ButtonFlags = Convert.ToUInt64(value)
            };
        }

        return null;
    }

    /// <summary>
    /// Parses a DualShock 4 target name. The L2/R2 members drive both the click flag and the
    /// analog byte (full pull unless the entry requests click-only output).
    /// </summary>
    public static ResolvedMappingTarget? ParseDualShock4Target(string name)
        => TryParseFlag(name, out DualShock4Buttons value) ? ResolveDeviceTarget(value, DualShock4Buttons.L2, DualShock4Buttons.R2) : null;

    /// <summary>
    /// Parses a DualSense target name. The L2/R2 members drive both the click flag and the
    /// analog byte (full pull unless the entry requests click-only output).
    /// </summary>
    public static ResolvedMappingTarget? ParseDualSenseTarget(string name)
        => TryParseFlag(name, out DualSenseButtons value) ? ResolveDeviceTarget(value, DualSenseButtons.L2, DualSenseButtons.R2) : null;

    /// <summary>
    /// Wraps a parsed device flag into a resolved target, tagging L2/R2 values as analog
    /// trigger targets.
    /// </summary>
    private static ResolvedMappingTarget ResolveDeviceTarget<TEnum>(TEnum value, TEnum leftTriggerFlag, TEnum rightTriggerFlag)
        where TEnum : struct, Enum
    {
        ulong bits = Convert.ToUInt64(value);
        return value.Equals(leftTriggerFlag)
            ? new ResolvedMappingTarget
            {
                ButtonFlags = bits,
                Trigger = MappableTriggerSide.Left,
                Output = MappingTriggerOutput.FullPull
            }
            : value.Equals(rightTriggerFlag)
                ? new ResolvedMappingTarget
                {
                    ButtonFlags = bits,
                    Trigger = MappableTriggerSide.Right,
                    Output = MappingTriggerOutput.FullPull
                }
                : new ResolvedMappingTarget
                {
                    ButtonFlags = bits
                };
    }

    /// <summary>
    /// Parses a single member name of a flags enum into its value. Combined comma-separated
    /// names are rejected because their result would not be a defined member.
    /// </summary>
    private static bool TryParseFlag<TEnum>(string name, out TEnum value) where TEnum : struct, Enum
        => Enum.TryParse(name, ignoreCase: true, out value) && Enum.IsDefined(value) && Convert.ToUInt64(value) != 0;

    // ── Built-in-default convenience translators ────────────────

    /// <summary>
    /// Maps the physical DualSense buttons to the virtual DualSense button bitmask using the
    /// built-in default mapping. The physical Create button maps to Create; Edge controls map
    /// to their function/paddle flags.
    /// </summary>
    public static DualSenseButtons ToDualSenseButtons(InputState input)
        => (DualSenseButtons)(uint)DualSenseDefaultTable.Evaluate(input).Buttons;

    /// <summary>
    /// Maps the physical DualSense buttons to the virtual DualShock 4 button bitmask using the
    /// built-in default mapping. The physical Create button is reported as the DS4's Share
    /// button.
    /// </summary>
    public static DualShock4Buttons ToDualShock4Buttons(InputState input)
        => (DualShock4Buttons)(ushort)DualShock4DefaultTable.Evaluate(input).Buttons;

    /// <summary>
    /// Maps the physical DualSense buttons to the virtual Xbox 360 button bitmask using the
    /// built-in default mapping. The physical Create and Options buttons map to Back and Start.
    /// </summary>
    public static Xbox360Buttons ToXbox360Buttons(InputState input)
        => (Xbox360Buttons)(uint)Xbox360DefaultTable.Evaluate(input).Buttons;

    /// <summary>
    /// Converts the physical D-pad to the virtual DualSense D-pad direction bitmask using the
    /// built-in default mapping.
    /// </summary>
    public static DualSenseDPad ToDualSenseDPad(InputState input)
        => (DualSenseDPad)(byte)DualSenseDefaultTable.Evaluate(input).DPad;

    /// <summary>
    /// Converts the physical D-pad to the virtual DualShock 4 D-pad value using the built-in
    /// default mapping. The Go device interprets the DS4 D-pad byte as a direction bitmask
    /// (Up=1, Down=2, Left=4, Right=8) even though the C# enum defines hat values, so the
    /// bitmask constants must be used here for the wire protocol to make sense.
    /// </summary>
    public static byte ToDualShock4DPad(InputState input)
        => (byte)DualShock4DefaultTable.Evaluate(input).DPad;

    /// <summary>
    /// Wraps a device flag enum member into a flag-only resolved target.
    /// </summary>
    private static ResolvedMappingTarget Flag<TEnum>(TEnum value) where TEnum : struct, Enum
        => new()
        {
            ButtonFlags = Convert.ToUInt64(value)
        };
}