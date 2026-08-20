using System.Text;
using DualSenseClient.Controllers.DualSense.Enum;

namespace DualSenseClient.Controllers.DualSense.Feature;

/// <summary>
/// Firmware and hardware information parsed from feature report 0x20.
/// All values are read from the raw report buffer at construction time.
/// </summary>
public readonly struct FirmwareInfo
{
    /// <summary>
    /// The raw 0x20 feature report buffer this struct reads from.
    /// </summary>
    private readonly byte[] _raw;

    /// <summary>
    /// Creates a new firmware info view over a raw feature report buffer.
    /// </summary>
    /// <param name="raw">The raw 0x20 feature report buffer.</param>
    public FirmwareInfo(byte[] raw)
    {
        _raw = raw;
    }

    /// <summary>
    /// Whether the buffer holds a valid 0x20 firmware info report.
    /// </summary>
    public bool IsValid => _raw.Length >= 60 && _raw[0] == 0x20;

    /// <summary>
    /// Build date as ASCII string (bytes 1-11, null-terminated).
    /// </summary>
    public string BuildDate => IsValid ? GetAsciiString(1, 11) : string.Empty;

    /// <summary>
    /// Build time as ASCII string (bytes 12-19, null-terminated).
    /// </summary>
    public string BuildTime => IsValid ? GetAsciiString(12, 8) : string.Empty;

    /// <summary>
    /// Firmware type (bytes 20-21, uint16 LE).
    /// </summary>
    public ushort FirmwareType => IsValid ? GetUInt16(20) : (ushort)0;

    /// <summary>
    /// Software series (bytes 22-23, uint16 LE).
    /// </summary>
    public ushort SoftwareSeries => IsValid ? GetUInt16(22) : (ushort)0;

    /// <summary>
    /// Hardware info (bytes 24-27, uint32 LE). The low 16 bits are the model revision number.
    /// </summary>
    public uint HardwareInfo => IsValid ? GetUInt32(24) : 0;

    /// <summary>
    /// Model revision number (low 16 bits of <see cref="HardwareInfo"/>).
    /// </summary>
    public ushort ModelRevision => (ushort)(HardwareInfo & 0xFFFF);

    /// <summary>
    /// Hardware generation (bits 8-15 of <see cref="HardwareInfo"/>). Generation 0x03
    /// controllers have full player-LED support while 0x04 is restricted to Mirrored Only.
    /// </summary>
    public DualSenseHardwareGeneration HardwareGeneration => IsValid
        ? (DualSenseHardwareGeneration)((HardwareInfo >> 8) & 0xFF)
        : DualSenseHardwareGeneration.Unknown;

    /// <summary>
    /// Whether the hardware generation supports full player-LED functionality. Generations
    /// below 0x04 do; generation 0x04 and above are restricted to Mirrored Only.
    /// </summary>
    public bool HasFullPlayerLedSupport =>
        HardwareGeneration is (DualSenseHardwareGeneration.Generation2 or DualSenseHardwareGeneration.Generation3);

    /// <summary>
    /// Main firmware version (bytes 28-31, uint32 LE) as major.minor.patch.
    /// </summary>
    public string MainFirmwareVersion => IsValid ? FormatVersion(GetUInt32(28)) : string.Empty;

    /// <summary>
    /// Device info (bytes 32-43, 12 bytes).
    /// </summary>
    public byte[] DeviceInfo => IsValid ? _raw[32..44] : [];

    /// <summary>
    /// Update version (bytes 44-45, uint16 LE) rendered as hex(x).hex(y).
    /// </summary>
    public string UpdateVersion => IsValid ? FormatUpdateVersion(GetUInt16(44)) : string.Empty;

    /// <summary>
    /// Raw update version (bytes 44-45, uint16 LE). The high byte is the major
    /// version and the low byte the minor version.
    /// </summary>
    public ushort UpdateVersionValue => IsValid ? GetUInt16(44) : (ushort)0;

    /// <summary>
    /// Update image info (byte 46).
    /// </summary>
    public byte UpdateImageInfo => IsValid ? _raw[46] : (byte)0;

    /// <summary>
    /// SBL firmware version (bytes 48-51, uint32 LE) as major.minor.patch.
    /// </summary>
    public string SblFirmwareVersion => IsValid ? FormatVersion(GetUInt32(48)) : string.Empty;

    /// <summary>
    /// DSP firmware version (bytes 52-55, uint32 LE) rendered as hex_hex.
    /// </summary>
    public string DspFirmwareVersion => IsValid ? FormatDspVersion(GetUInt32(52)) : string.Empty;

    /// <summary>
    /// MCU/Spider DSP firmware version (bytes 56-59, uint32 LE) as major.minor.patch.
    /// </summary>
    public string McuSpiderDspFirmwareVersion => IsValid ? FormatVersion(GetUInt32(56)) : string.Empty;

    /// <summary>
    /// Reads an unsigned 16-bit little-endian value from the report.
    /// </summary>
    /// <param name="offset">Byte offset of the value.</param>
    private ushort GetUInt16(int offset) =>
        (ushort)(_raw[offset] | (_raw[offset + 1] << 8));

    /// <summary>
    /// Reads an unsigned 32-bit little-endian value from the report.
    /// </summary>
    /// <param name="offset">Byte offset of the value.</param>
    private uint GetUInt32(int offset) =>
        (uint)(_raw[offset] | (_raw[offset + 1] << 8) | (_raw[offset + 2] << 16) | (_raw[offset + 3] << 24));

    /// <summary>
    /// Reads a null-terminated ASCII string of at most <paramref name="maxLength"/> bytes.
    /// </summary>
    /// <param name="offset">Byte offset of the string start.</param>
    /// <param name="maxLength">Maximum number of bytes to read.</param>
    private string GetAsciiString(int offset, int maxLength)
    {
        int count = 0;
        while (count < maxLength && offset + count < _raw.Length && _raw[offset + count] != 0)
        {
            count++;
        }
        return Encoding.ASCII.GetString(_raw, offset, count);
    }

    /// <summary>
    /// Formats a firmware version as major.minor.patch.
    /// </summary>
    /// <param name="v">Raw uint32 firmware version.</param>
    private static string FormatVersion(uint v) =>
        $"{(v >> 24) & 0xFF}.{(v >> 16) & 0xFF}.{v & 0xFFFF}";

    /// <summary>
    /// Formats an update version as hex(x).hex(y).
    /// </summary>
    /// <param name="v">Raw uint16 update version.</param>
    private static string FormatUpdateVersion(ushort v) =>
        $"{((v >> 8) & 0xFF):X}.{v & 0xFF:X}";

    /// <summary>
    /// Formats a DSP version as hex_hex.
    /// </summary>
    /// <param name="v">Raw uint32 DSP firmware version.</param>
    private static string FormatDspVersion(uint v) =>
        $"{((v >> 16) & 0xFFFF):X4}_{v & 0xFFFF:X4}";
}