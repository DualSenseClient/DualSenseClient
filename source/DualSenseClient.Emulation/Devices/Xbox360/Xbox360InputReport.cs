using System.Buffers.Binary;

// Packet layout: the reference implementation serves the 20-byte stage-4 initialization packet
// — 0x00 (Id), 0x14 (Size), the button word, LT, RT, the four little-endian stick words and six
// zero tail bytes — and serves the same packet shape with the submitted report; it pins
// XUSB_INTERRUPT_IN_PACKET { Id; Size; Report } and the declared size 0x14. A behavior-oracle
// reference implementation documents the same offsets and pins the button bit table (the XInput
// word layout). The packet carries no HID report id and no counters: a vendor-class interrupt
// transfer of exactly 20 bytes.

namespace DualSenseClient.Emulation.Devices.Xbox360;

/// <summary>
/// Builds the 20-byte Xbox 360 USB input packet from an input state: the reference Id and Size
/// bytes, the little-endian button word (D-pad bits, shoulders, Back/Start, stick clicks, Guide
/// and the face buttons), both analog triggers and the four little-endian stick words; the six
/// tail bytes stay zero. The builder is pure and stateless: the format carries no counters, so
/// nothing is written beyond <see cref="Size"/> bytes and an unchanged state yields byte-identical
/// packets frame after frame.
/// </summary>
public static class Xbox360InputReport
{
    /// <summary>Length in bytes of the input packet (the reference's declared packet size 0x14).</summary>
    public const int Size = 20;

    /// <summary>Packet id byte of the input packet (XUSB_INTERRUPT_IN_PACKET.Id; not a HID report id).</summary>
    public const byte PacketId = 0x00;

    /// <summary>Packet size byte of the input packet (XUSB_INTERRUPT_IN_PACKET.Size; 0x14 = 20).</summary>
    public const byte PacketSize = 0x14;

    /// <summary>Packet offset of the id byte.</summary>
    private const int OffsetPacketId = 0;

    /// <summary>Packet offset of the size byte.</summary>
    private const int OffsetPacketSize = 1;

    /// <summary>Packet offset of the little-endian button word.</summary>
    private const int OffsetButtons = 2;

    /// <summary>Packet offset of the left analog trigger.</summary>
    private const int OffsetLeftTrigger = 4;

    /// <summary>Packet offset of the right analog trigger.</summary>
    private const int OffsetRightTrigger = 5;

    /// <summary>Packet offset of the little-endian left stick X word (the first stick word).</summary>
    private const int OffsetLeftStickX = 6;

    /// <summary>Packet offset of the little-endian left stick Y word.</summary>
    private const int OffsetLeftStickY = 8;

    /// <summary>Packet offset of the little-endian right stick X word.</summary>
    private const int OffsetRightStickX = 10;

    /// <summary>Packet offset of the little-endian right stick Y word.</summary>
    private const int OffsetRightStickY = 12;

    // The six tail bytes (offsets 14-19) stay zero: both references serve them zero.

    /// <summary>Button bit of Start in the button word (0x0010).</summary>
    private const ushort StartBit = 0x0010;

    /// <summary>Button bit of Back in the button word (0x0020).</summary>
    private const ushort BackBit = 0x0020;

    /// <summary>Button bit of the left stick click (L3) in the button word (0x0040).</summary>
    private const ushort L3Bit = 0x0040;

    /// <summary>Button bit of the right stick click (R3) in the button word (0x0080).</summary>
    private const ushort R3Bit = 0x0080;

    /// <summary>Button bit of the left shoulder (LB) in the button word (0x0100).</summary>
    private const ushort LeftShoulderBit = 0x0100;

    /// <summary>Button bit of the right shoulder (RB) in the button word (0x0200).</summary>
    private const ushort RightShoulderBit = 0x0200;

    /// <summary>Button bit of Guide in the button word (0x0400); the 0x0800 bit is unused.</summary>
    private const ushort GuideBit = 0x0400;

    /// <summary>Button bit of A in the button word (0x1000).</summary>
    private const ushort ABit = 0x1000;

    /// <summary>Button bit of B in the button word (0x2000).</summary>
    private const ushort BBit = 0x2000;

    /// <summary>Button bit of X in the button word (0x4000).</summary>
    private const ushort XBit = 0x4000;

    /// <summary>Button bit of Y in the button word (0x8000).</summary>
    private const ushort YBit = 0x8000;

    /// <summary>
    /// Writes the full 20-byte input packet for one frame and returns <see cref="Size"/>.
    /// </summary>
    /// <param name="state">Input state to report; every field is written as-is.</param>
    /// <param name="destination">Destination of at least <see cref="Size"/> bytes; only those are written.</param>
    /// <returns>The number of bytes written (<see cref="Size"/>).</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <see cref="Size"/> bytes.</exception>
    public static int Build(in Xbox360InputState state, Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException($"An Xbox 360 input packet requires {Size} bytes; got {destination.Length}.", nameof(destination));
        }

        // The tail bytes (14-19) are the only bytes the state does not carry; this bounded clear
        // serves them zero, exactly like the reference packet.
        Span<byte> packet = destination[..Size];
        packet.Clear();

        packet[OffsetPacketId] = PacketId;
        packet[OffsetPacketSize] = PacketSize;

        ushort buttons = (ushort)state.Dpad;
        buttons |= Bit(state.Start, StartBit);
        buttons |= Bit(state.Back, BackBit);
        buttons |= Bit(state.L3, L3Bit);
        buttons |= Bit(state.R3, R3Bit);
        buttons |= Bit(state.LeftShoulder, LeftShoulderBit);
        buttons |= Bit(state.RightShoulder, RightShoulderBit);
        buttons |= Bit(state.Guide, GuideBit);
        buttons |= Bit(state.A, ABit);
        buttons |= Bit(state.B, BBit);
        buttons |= Bit(state.X, XBit);
        buttons |= Bit(state.Y, YBit);
        BinaryPrimitives.WriteUInt16LittleEndian(packet[OffsetButtons..], buttons);

        packet[OffsetLeftTrigger] = state.LeftTrigger;
        packet[OffsetRightTrigger] = state.RightTrigger;

        BinaryPrimitives.WriteInt16LittleEndian(packet[OffsetLeftStickX..], state.LeftStickX);
        BinaryPrimitives.WriteInt16LittleEndian(packet[OffsetLeftStickY..], state.LeftStickY);
        BinaryPrimitives.WriteInt16LittleEndian(packet[OffsetRightStickX..], state.RightStickX);
        BinaryPrimitives.WriteInt16LittleEndian(packet[OffsetRightStickY..], state.RightStickY);

        return Size;
    }

    /// <summary>
    /// Returns <paramref name="mask"/> when the button is set, otherwise zero.
    /// </summary>
    /// <param name="set">Whether the button is held.</param>
    /// <param name="mask">Bit of the button in the button word.</param>
    /// <returns>The button bit, or zero.</returns>
    private static ushort Bit(bool set, ushort mask) => set ? mask : (ushort)0;
}