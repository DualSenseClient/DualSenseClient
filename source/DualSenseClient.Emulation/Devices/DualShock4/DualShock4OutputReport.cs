namespace DualSenseClient.Emulation.Devices.DualShock4;

// Output report layout: the v2 reference forwards the interrupt-OUT report (report id first) and
// the SET_REPORT payload (the report id travels in wValue) to a shared decoder whose comment pins
// the shared layout ("payload is the USB-0x05-style payload (flags byte first); layout is identical
// between USB 0x05 bytes 1..N and BT 0x11 bytes 3..N+2"). The LED-enable flag sits in payload byte
// 0 (USB report byte 1) and the RGB bytes at payload 5-7 (USB report bytes 6-8); the rumble motors
// and flash durations follow the same layout (USB report bytes 4-5 and 9-10), which the v1
// reference corroborates by snapshotting exactly USB output-report bytes 4-8: the two rumble motors
// followed by the RGB bytes. Flag bits 0x01/0x02/0x04 select the rumble, lightbar and flash updates
// (the high nibble selects the volume updates of the DS4 v2).

/// <summary>
/// Decoded host output report of the DualShock 4 (USB report id 0x05): the enable flags and the
/// rumble, lightbar and flash fields of the 31-byte payload. <see cref="TryParse"/> reads the
/// id-prefixed shape both delivery paths produce: the interrupt OUT path carries the id in the
/// payload and the HID class SET_REPORT path gets it prepended from wValue. A bare payload is only
/// possible through a direct <c>OnOutputReport</c> call and is therefore not recognized.
/// </summary>
public readonly struct DualShock4OutputReport
{
    /// <summary>Report id of the output report (USB report 0x05).</summary>
    public const byte ReportId = 0x05;

    /// <summary>
    /// Minimum length of a parseable payload: the report id byte followed by the ten common report
    /// bytes through the flash-off duration. Longer payloads (the full report is 32 bytes) parse as
    /// well and their trailing bytes are ignored.
    /// </summary>
    public const int MinimumSize = 11;

    /// <summary>Flag bit of the rumble update in the flags byte.</summary>
    private const byte RumbleEnabledBit = 0x01;

    /// <summary>Flag bit of the lightbar (LED color) update in the flags byte.</summary>
    private const byte LightbarEnabledBit = 0x02;

    /// <summary>Flag bit of the lightbar flash update in the flags byte.</summary>
    private const byte FlashEnabledBit = 0x04;

    /// <summary>Report offset of the flags byte (valid_flag0 of the reference layout).</summary>
    private const int OffsetFlags = 1;

    /// <summary>Report offset of the right (fast/weak) rumble motor.</summary>
    private const int OffsetRightMotor = 4;

    /// <summary>Report offset of the left (strong/slow) rumble motor.</summary>
    private const int OffsetLeftMotor = 5;

    /// <summary>Report offset of the lightbar red component.</summary>
    private const int OffsetRed = 6;

    /// <summary>Report offset of the lightbar green component.</summary>
    private const int OffsetGreen = 7;

    /// <summary>Report offset of the lightbar blue component.</summary>
    private const int OffsetBlue = 8;

    /// <summary>Report offset of the flash-on duration.</summary>
    private const int OffsetFlashOn = 9;

    /// <summary>Report offset of the flash-off duration.</summary>
    private const int OffsetFlashOff = 10;

    /// <summary>Whether the host enabled the rumble motors for this report.</summary>
    public bool RumbleEnabled { get; init; }

    /// <summary>Right (fast/weak) rumble motor strength, raw host value.</summary>
    public byte RightMotor { get; init; }

    /// <summary>Left (strong/slow) rumble motor strength, raw host value.</summary>
    public byte LeftMotor { get; init; }

    /// <summary>Whether the host enabled the lightbar color update for this report.</summary>
    public bool LightbarEnabled { get; init; }

    /// <summary>Lightbar red component, raw host value.</summary>
    public byte Red { get; init; }

    /// <summary>Lightbar green component, raw host value.</summary>
    public byte Green { get; init; }

    /// <summary>Lightbar blue component, raw host value.</summary>
    public byte Blue { get; init; }

    /// <summary>Whether the host enabled the lightbar flash update for this report.</summary>
    public bool FlashEnabled { get; init; }

    /// <summary>Flash-on duration, raw host value.</summary>
    public byte FlashOn { get; init; }

    /// <summary>Flash-off duration, raw host value.</summary>
    public byte FlashOff { get; init; }

    /// <summary>
    /// Parses one id-prefixed host payload: <paramref name="data"/> must start with
    /// <see cref="ReportId"/> and carry at least <see cref="MinimumSize"/> bytes. Any other report id
    /// (or an empty, truncated or bare control-path payload) yields
    /// <see langword="false"/> with <paramref name="report"/> at its default value.
    /// </summary>
    /// <param name="data">Host payload, report id first (the shape both the interrupt OUT and HID SET_REPORT paths produce).</param>
    /// <param name="report">Parsed report when the payload is recognized; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the payload carries output report <see cref="ReportId"/>; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<byte> data, out DualShock4OutputReport report)
    {
        if (data.Length < MinimumSize || data[0] != ReportId)
        {
            report = default;
            return false;
        }

        byte flags = data[OffsetFlags];
        report = new DualShock4OutputReport
        {
            RumbleEnabled = (flags & RumbleEnabledBit) != 0,
            RightMotor = data[OffsetRightMotor],
            LeftMotor = data[OffsetLeftMotor],
            LightbarEnabled = (flags & LightbarEnabledBit) != 0,
            Red = data[OffsetRed],
            Green = data[OffsetGreen],
            Blue = data[OffsetBlue],
            FlashEnabled = (flags & FlashEnabledBit) != 0,
            FlashOn = data[OffsetFlashOn],
            FlashOff = data[OffsetFlashOff]
        };
        return true;
    }
}