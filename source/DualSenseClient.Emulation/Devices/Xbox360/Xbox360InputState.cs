// Packet layout: the reference implementation defines XUSB_INTERRUPT_IN_PACKET { Id; Size; Report }
// and serves the initialization blob whose stage-4 packet pins the wire shape (Id 0x00, Size 0x14,
// the button word, both triggers, the four little-endian stick words and a six-byte zero tail); the
// packet's declared size 0x14 is 20 bytes. A behavior-oracle reference implementation pins the
// field semantics against a complete descriptor set: it documents the same offsets byte-by-byte,
// pins the button masks (the XInput button word layout, the D-pad directions as the low four bits),
// and its neutral state is all zero.

namespace DualSenseClient.Emulation.Devices.Xbox360;

/// <summary>
/// D-pad direction values of the Xbox 360 input packet. Unlike the DualShock 4 hat switch, the
/// wired controller reports the four D-pad directions as independent bits of the 16-bit button
/// word, so each value is the combined bit mask the builder writes (the ButtonDPad* masks; the
/// numeric values are the XInput button bits). <see cref="Released"/> is zero, so a default
/// state carries no direction.
/// </summary>
public enum Xbox360DpadDirection : ushort
{
    /// <summary>D-pad up, button word bit 0x0001.</summary>
    Up = 0x0001,

    /// <summary>D-pad up and right, button word bits 0x0001 | 0x0008.</summary>
    UpRight = 0x0009,

    /// <summary>D-pad right, button word bit 0x0008.</summary>
    Right = 0x0008,

    /// <summary>D-pad down and right, button word bits 0x0002 | 0x0008.</summary>
    DownRight = 0x000A,

    /// <summary>D-pad down, button word bit 0x0002.</summary>
    Down = 0x0002,

    /// <summary>D-pad down and left, button word bits 0x0002 | 0x0004.</summary>
    DownLeft = 0x0006,

    /// <summary>D-pad left, button word bit 0x0004.</summary>
    Left = 0x0004,

    /// <summary>D-pad up and left, button word bits 0x0001 | 0x0004.</summary>
    UpLeft = 0x0005,

    /// <summary>No direction held, no D-pad bit; the default of the state.</summary>
    Released = 0x0000
}

/// <summary>
/// Mutable input-state carrier of the Xbox 360 persona: the field set of the 20-byte USB input
/// packet (the button word, both analog triggers and the four stick axes). The state is an
/// XInput-style carrier (behavior oracle): the sticks are signed 16-bit
/// values with zero as the neutral center, the triggers 0 (released) to 255 (fully pressed), and
/// the buttons and D-pad collapse into the packet's little-endian button word.
/// <see cref="Xbox360InputReport.Build"/> reads it every frame; the packet carries no counters and
/// no device-owned fields, so the device holds no reporting state beyond this state.
/// </summary>
public struct Xbox360InputState
{
    /// <summary>Left stick X, -32768 (left) to 32767 (right), neutral zero.</summary>
    public short LeftStickX { get; set; }

    /// <summary>Left stick Y, -32768 (up) to 32767 (down), neutral zero.</summary>
    public short LeftStickY { get; set; }

    /// <summary>Right stick X, -32768 (left) to 32767 (right), neutral zero.</summary>
    public short RightStickX { get; set; }

    /// <summary>Right stick Y, -32768 (up) to 32767 (down), neutral zero.</summary>
    public short RightStickY { get; set; }

    /// <summary>Left analog trigger, 0 (released) to 255 (fully pressed).</summary>
    public byte LeftTrigger { get; set; }

    /// <summary>Right analog trigger, 0 (released) to 255 (fully pressed).</summary>
    public byte RightTrigger { get; set; }

    /// <summary>Held D-pad direction; <see cref="Xbox360DpadDirection.Released"/> while none is held.</summary>
    public Xbox360DpadDirection Dpad { get; set; }

    /// <summary>Whether the A button is held (button word bit 0x1000).</summary>
    public bool A { get; set; }

    /// <summary>Whether the B button is held (button word bit 0x2000).</summary>
    public bool B { get; set; }

    /// <summary>Whether the X button is held (button word bit 0x4000).</summary>
    public bool X { get; set; }

    /// <summary>Whether the Y button is held (button word bit 0x8000).</summary>
    public bool Y { get; set; }

    /// <summary>Whether the left shoulder button is held (LB, button word bit 0x0100).</summary>
    public bool LeftShoulder { get; set; }

    /// <summary>Whether the right shoulder button is held (RB, button word bit 0x0200).</summary>
    public bool RightShoulder { get; set; }

    /// <summary>Whether the Back button is held (button word bit 0x0020).</summary>
    public bool Back { get; set; }

    /// <summary>Whether the Start button is held (button word bit 0x0010).</summary>
    public bool Start { get; set; }

    /// <summary>Whether the left stick is clicked (L3, button word bit 0x0040).</summary>
    public bool L3 { get; set; }

    /// <summary>Whether the right stick is clicked (R3, button word bit 0x0080).</summary>
    public bool R3 { get; set; }

    /// <summary>Whether the Guide button is held (the Xbox logo button, button word bit 0x0400).</summary>
    public bool Guide { get; set; }
}