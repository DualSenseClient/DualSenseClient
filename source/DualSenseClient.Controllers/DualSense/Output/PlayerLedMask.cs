namespace DualSenseClient.Controllers.DualSense.Output;

/// <summary>
/// Player LED mask for <see cref="SetStateData.PlayerLeds"/> (payload offset 43).
/// Bits 0-4 select the five LEDs and bit 5 is a fade-enable flag.
/// </summary>
[Flags]
public enum PlayerLedMask : byte
{
    /// <summary>
    /// No player LEDs lit.
    /// </summary>
    None = 0x00,

    /// <summary>
    /// LED 1 (leftmost).
    /// </summary>
    Led1 = 0x01,

    /// <summary>
    /// LED 2.
    /// </summary>
    Led2 = 0x02,

    /// <summary>
    /// LED 3 (center).
    /// </summary>
    Led3 = 0x04,

    /// <summary>
    /// LED 4.
    /// </summary>
    Led4 = 0x08,

    /// <summary>
    /// LED 5 (rightmost).
    /// </summary>
    Led5 = 0x10,

    /// <summary>
    /// All five player LEDs.
    /// </summary>
    All = Led1 | Led2 | Led3 | Led4 | Led5,

    /// <summary>
    /// Player light fade animation flag. Write the bare mask unless fading is wanted —
    /// setting this bit unconditionally enables the fade animation as a side effect.
    /// </summary>
    Fade = 0x20
}