// Rumble packet layout: the reference implementation pins XUSB_RUMBLE_SIZE 0x08 and treats a host
// transfer of exactly that length as the rumble packet, caching it whole and reading the large
// motor from byte 3 and the small motor from byte 4 (LargeMotor = _Rumble[3], SmallMotor =
// _Rumble[4] in the hand-off to the rumble notification). A behavior-oracle reference
// implementation documents the header bytes ([0] = 0x00 packet id, [1] = 0x08 packet size,
// [2] reserved) and ignores every other outbound packet; its LED packet carries id 0x01 and size
// 0x03.

namespace DualSenseClient.Emulation.Devices.Xbox360;

/// <summary>
/// Decoded host rumble packet of the Xbox 360 wired controller: the 8-byte host-to-device transfer
/// whose id byte is 0x00 and whose size byte is 0x08 (XUSB_RUMBLE_SIZE), carrying the large
/// (low-frequency) motor strength at byte 3 and the small (high-frequency) motor strength at byte 4.
/// <see cref="TryParse"/> reads that shape; the controller's other outbound packets (the LED packet
/// carries id 0x01, size 0x03) and short payloads yield no report.
/// </summary>
public readonly struct Xbox360OutputReport
{
    /// <summary>Packet id byte of the rumble packet (reference: byte 0 is 0x00).</summary>
    public const byte PacketId = 0x00;

    /// <summary>Declared packet size of the rumble packet (XUSB_RUMBLE_SIZE, 8).</summary>
    public const byte RumblePacketSize = 0x08;

    /// <summary>
    /// Minimum length of a parseable payload: the 8-byte rumble packet. Longer payloads parse as
    /// well when their header matches and their trailing bytes are ignored (a documented length
    /// tolerance; the reference accepts exactly the 8-byte transfer).
    /// </summary>
    public const int MinimumSize = 8;

    /// <summary>Packet offset of the left (large/low-frequency) motor strength (the reserved byte sits at offset 2).</summary>
    private const int OffsetLeftMotor = 3;

    /// <summary>Packet offset of the right (small/high-frequency) motor strength.</summary>
    private const int OffsetRightMotor = 4;

    /// <summary>Left (large/low-frequency) rumble motor strength, raw host value.</summary>
    public byte LeftMotor { get; init; }

    /// <summary>Right (small/high-frequency) rumble motor strength, raw host value.</summary>
    public byte RightMotor { get; init; }

    /// <summary>
    /// Parses one host payload: <paramref name="data"/> must carry at least <see cref="MinimumSize"/>
    /// bytes and start with the rumble packet's id and size bytes. Any other packet (the LED packet,
    /// an unknown id or size) and any short or empty payload yields <see langword="false"/> with
    /// <paramref name="report"/> at its default value; nothing throws.
    /// </summary>
    /// <param name="data">Host payload of the interrupt OUT transfer, packet id first.</param>
    /// <param name="report">Parsed report when the payload is recognized; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the payload is a rumble packet; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<byte> data, out Xbox360OutputReport report)
    {
        if (data.Length < MinimumSize || data[0] != PacketId || data[1] != RumblePacketSize)
        {
            report = default;
            return false;
        }

        // The reserved byte at offset 2 is ignored: the reference copies the packet whole and only
        // reads the two motor bytes (the byte is documented as reserved/status).
        report = new Xbox360OutputReport
        {
            LeftMotor = data[OffsetLeftMotor],
            RightMotor = data[OffsetRightMotor]
        };
        return true;
    }
}