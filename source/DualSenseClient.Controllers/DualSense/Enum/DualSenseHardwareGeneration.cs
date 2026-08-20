namespace DualSenseClient.Controllers.DualSense.Enum;

/// <summary>
/// Hardware generation of a DualSense controller, derived from bits 8-15 of the
/// hardware info value in feature report 0x20.
/// </summary>
public enum DualSenseHardwareGeneration : byte
{
    /// <summary>
    /// Generation could not be determined (no valid firmware info report).
    /// </summary>
    Unknown = 0x00,

    /// <summary>
    /// Generation 0x02, reported by original (BDM-010/020 era) boards.
    /// </summary>
    Generation2 = 0x02,

    /// <summary>
    /// Generation 0x03; full player-LED functionality (SpecialK).
    /// </summary>
    Generation3 = 0x03,

    /// <summary>
    /// Generation 0x04; player LEDs restricted to Mirrored Only (SpecialK).
    /// </summary>
    Generation4 = 0x04
}