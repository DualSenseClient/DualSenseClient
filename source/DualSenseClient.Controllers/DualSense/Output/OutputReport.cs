using DualSenseClient.Controllers.DualSense.Utilities;

namespace DualSenseClient.Controllers.DualSense.Output;

/// <summary>
/// A framed output report ready to send over USB or Bluetooth.
/// Both transports carry the same 47-byte <see cref="SetStateData"/> payload; only the
/// framing differs.
/// </summary>
public readonly struct OutputReport
{
    /// <summary>
    /// USB output report ID.
    /// </summary>
    private const byte UsbReportId = 0x02;

    /// <summary>
    /// Bluetooth output report ID.
    /// </summary>
    private const byte BluetoothReportId = 0x31;

    /// <summary>
    /// Bluetooth framing flags byte. Not the input report's 0x02.
    /// </summary>
    private const byte BluetoothFlags = 0x10;

    /// <summary>
    /// Bluetooth CRC32 starts at this offset and covers bytes 0-73.
    /// </summary>
    private const int BluetoothCrcOffset = 74;

    /// <summary>
    /// Raw report buffer including the report ID and transport framing.
    /// </summary>
    public byte[] Raw { get; }

    /// <summary>
    /// Total length of the report (48 for USB, 78 for Bluetooth).
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Initializes a framed report over a ready buffer.
    /// </summary>
    private OutputReport(byte[] raw, int length)
    {
        Raw = raw;
        Length = length;
    }

    /// <summary>
    /// Builds a USB output report (report ID 0x02, 48 bytes, no CRC).
    /// </summary>
    /// <param name="payload">The 47-byte output payload.</param>
    public static OutputReport ForUsb(SetStateData payload)
    {
        byte[] raw = new byte[1 + SetStateData.PayloadSize];
        raw[0] = UsbReportId;
        payload.CopyTo(raw, 1);
        return new OutputReport(raw, raw.Length);
    }

    /// <summary>
    /// Builds a Bluetooth output report (report ID 0x31, 78 bytes with CRC32).
    /// </summary>
    /// <param name="payload">The 47-byte output payload.</param>
    /// <param name="sequenceNumber">Rolling sequence tag; only the low nibble is transmitted.</param>
    public static OutputReport ForBluetooth(SetStateData payload, byte sequenceNumber)
    {
        byte[] raw = new byte[78];
        raw[0] = BluetoothReportId;
        raw[1] = (byte)((sequenceNumber & 0x0F) << 4);
        raw[2] = BluetoothFlags;
        payload.CopyTo(raw, 3);

        // Reserved bytes 50-73 stay zero. CRC32 over the first 74 bytes, seeded form.
        uint crc = DualSenseCRC32.Compute(raw, 0, BluetoothCrcOffset);
        raw[74] = (byte)crc;
        raw[75] = (byte)(crc >> 8);
        raw[76] = (byte)(crc >> 16);
        raw[77] = (byte)(crc >> 24);

        return new OutputReport(raw, raw.Length);
    }
}