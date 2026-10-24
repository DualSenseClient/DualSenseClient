namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Input translations shared by the Emulation virtual controller adapters.
/// </summary>
internal static class VirtualInputMapping
{
    /// <summary>
    /// Converts the mode-neutral <see cref="VirtualDPad"/> direction mask into the hat value the
    /// DualSense and DualShock 4 input reports carry (Up 0, clockwise through UpLeft 7, Released 8).
    /// The switch uses the physical pad's precedence: diagonal pairs first, then the single
    /// directions in the order Up, Down, Left, Right, then the released hat. It is exhaustive over
    /// all 16 four-bit masks. The eight masks the physical hat cannot produce (custom remaps
    /// combining opposing directions) resolve through the same precedence, so a remap always
    /// resolves deterministically.
    /// </summary>
    /// <param name="dpad">Direction mask built from <see cref="VirtualDPad"/> flags.</param>
    /// <returns>The 4-bit hat value (0-8).</returns>
    internal static byte DpadToHat(VirtualDPad dpad)
    {
        return ((byte)dpad & 0x0F) switch
        {
            0x0 => 0x08, // Released
            0x1 => 0x00, // Up
            0x2 => 0x04, // Down
            0x3 => 0x00, // Up+Down: Up wins (fallthrough order)
            0x4 => 0x06, // Left
            0x5 => 0x07, // Up+Left
            0x6 => 0x05, // Down+Left
            0x7 => 0x07, // Up+Down+Left: Up+Left wins
            0x8 => 0x02, // Right
            0x9 => 0x01, // Up+Right
            0xA => 0x03, // Down+Right
            0xB => 0x01, // Up+Down+Right: Up+Right wins
            0xC => 0x06, // Left+Right: Left wins
            0xD => 0x01, // Up+Left+Right: Up+Right wins
            0xE => 0x03, // Down+Left+Right: Down+Right wins
            _ => 0x01 // All four: Up+Right wins
        };
    }
}