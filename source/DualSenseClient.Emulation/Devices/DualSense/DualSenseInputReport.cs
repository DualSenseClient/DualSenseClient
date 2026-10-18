using System.Buffers.Binary;

// Report layout:
// - The neutral interrupt-in payload (interrupt_in_data, 63 bytes) and the BT-to-USB translation:
//   each BT 0x31 interrupt payload is copied verbatim ("memcpy(interrupt_in_data, data + 3, 63)")
//   and served as USB report 0x01 with 63 payload bytes, so BT payload byte N is USB report offset
//   N + 1 with the report id at offset 0.
// - The field-by-field USBGetStateData layout block of that 63-byte payload (byte 0
//   LeftStickX through byte 62 AesCmac[8]): sticks and triggers 0-5, the SeqNo byte 6, the D-pad
//   plus face byte 7, the shoulder byte 8, the utility byte 9 (PS, touchpad, mute, Edge Fn and
//   paddle bits), the reserved counter word 11-14, the gyro words 15-20 in X, Z, Y order, the
//   accelerometer words 21-26, the sensor timestamp 27-30, the temperature byte 31, the 9-byte
//   TouchData block 32-40, the trigger stop/status nibbles 41-42, the host timestamp 43-46, the
//   trigger effect nibbles 47, the device timestamp 48-51, the percent/power byte 52 and the
//   plug/mute echo bytes 53-54.
// - The wake path ("byte 7 low nibble: D-pad direction (0x08 idle); high nibble: face buttons",
//   "byte 8: L1, R1, L2 click, R2 click, share, options, L3, R3", "byte 9: PS (bit 0),
//   touchpad-click (bit 1), mute (bit 2)") and the Bluetooth inactivity check (sticks in
//   120-140, both triggers zero, the D-pad byte 0x08 and the shoulder/utility bytes zero) pin the
//   button bytes; the PS-shortcut path reads the PS bit as byte 9 bit 0.
// - The battery path reads the power byte as percent bits 0-3 and power-state bits 4-7 with
//   discharging = 0x0; the main path reads the headset bit 0 and the mic-mute echo bit 2 of the
//   plug byte.
// - The 321-byte HID report descriptor (report id 1 block) fixes
//   the field widths: six 8-bit axes, one vendor byte, a 4-bit hat plus 15 button bits, a 13-bit
//   vendor field and 52 further vendor bytes.

namespace DualSenseClient.Emulation.Devices.DualSense;

/// <summary>
/// Builds the 64-byte DualSense USB input report (report id <see cref="ReportId"/>) from an input
/// state: centered sticks are 128, the D-pad nibble is the hat encoding of
/// <see cref="DualSenseDpadDirection"/>, an idle touch point keeps the reference's 0x80 contact
/// marker and the pair shares one rolling touch timestamp byte. The builder is pure: the persona
/// device owns the packet counter and the touch counters and passes them in every frame, and
/// nothing is written beyond <see cref="Size"/> bytes.
/// </summary>
public static class DualSenseInputReport
{
    /// <summary>Length in bytes of the report: the report id byte plus the 63-byte payload.</summary>
    public const int Size = 64;

    /// <summary>Report id of the main DualSense input report (USB report 0x01).</summary>
    public const byte ReportId = 0x01;

    /// <summary>Report offset of the report id.</summary>
    private const int OffsetReportId = 0;

    /// <summary>Report offset of the left stick X axis.</summary>
    private const int OffsetLeftStickX = 1;

    /// <summary>Report offset of the left stick Y axis.</summary>
    private const int OffsetLeftStickY = 2;

    /// <summary>Report offset of the right stick X axis.</summary>
    private const int OffsetRightStickX = 3;

    /// <summary>Report offset of the right stick Y axis.</summary>
    private const int OffsetRightStickY = 4;

    /// <summary>Report offset of the L2 analog trigger.</summary>
    private const int OffsetLeftTrigger = 5;

    /// <summary>Report offset of the R2 analog trigger.</summary>
    private const int OffsetRightTrigger = 6;

    /// <summary>Report offset of the report sequence counter (reference byte 6, "SeqNo").</summary>
    private const int OffsetSequenceCounter = 7;

    /// <summary>Report offset of the D-pad nibble (low) and the face buttons (high).</summary>
    private const int OffsetFaceAndDpad = 8;

    /// <summary>Report offset of the shoulder/directional button byte.</summary>
    private const int OffsetShoulderButtons = 9;

    /// <summary>Report offset of the PS/touchpad/mute byte carrying the Edge Fn and paddle bits.</summary>
    private const int OffsetUtilityButtons = 10;

    // The reference byte 10 ("appears unused") and the reserved 32-bit counter word in its bytes
    // 11-14 -- report offsets 11-15 -- stay zero: no consumer of the reference reads them and the
    // input state carries no such data.

    /// <summary>Report offset of the little-endian gyro X word (the first motion word).</summary>
    private const int OffsetGyroX = 16;

    /// <summary>Report offset of the little-endian gyro Z word (the reference orders the axes X, Z, Y).</summary>
    private const int OffsetGyroZ = 18;

    /// <summary>Report offset of the little-endian gyro Y word.</summary>
    private const int OffsetGyroY = 20;

    /// <summary>Report offset of the little-endian accelerometer x word (the first accelerometer axis).</summary>
    private const int OffsetAccelX = 22;

    /// <summary>Report offset of the little-endian accelerometer y word.</summary>
    private const int OffsetAccelY = 24;

    /// <summary>Report offset of the little-endian accelerometer z word.</summary>
    private const int OffsetAccelZ = 26;

    /// <summary>Report offset of the little-endian sensor timestamp word.</summary>
    private const int OffsetSensorTimestamp = 28;

    /// <summary>Report offset of the signed temperature byte (reference "int8_t Temperature").</summary>
    private const int OffsetTemperature = 32;

    /// <summary>Report offset of the first touch point (contact byte, then three packed coordinate bytes).</summary>
    private const int OffsetTouchPoint1 = 33;

    /// <summary>Length in bytes of one touch point: contact byte plus three packed coordinate bytes.</summary>
    private const int TouchPointSize = 4;

    /// <summary>Report offset of the second touch point (the first point plus one point stride).</summary>
    private const int OffsetTouchPoint2 = OffsetTouchPoint1 + TouchPointSize;

    /// <summary>Report offset of the rolling touch timestamp of the pair (the reference's TouchData.Timestamp).</summary>
    private const int OffsetTouchTimestamp = 41;

    /// <summary>Report offset of the right trigger stop-location (low) and feedback-status (high) nibbles.</summary>
    private const int OffsetTriggerRightStatus = 42;

    /// <summary>Report offset of the left trigger stop-location (low) and feedback-status (high) nibbles.</summary>
    private const int OffsetTriggerLeftStatus = 43;

    /// <summary>Report offset of the little-endian host timestamp echo.</summary>
    private const int OffsetHostTimestamp = 44;

    /// <summary>Report offset of the right (low) and left (high) active trigger effect nibbles.</summary>
    private const int OffsetTriggerEffects = 48;

    /// <summary>Report offset of the little-endian device timestamp word.</summary>
    private const int OffsetDeviceTimestamp = 49;

    /// <summary>Report offset of the battery/power byte (percent bits 0-3, power state bits 4-7).</summary>
    private const int OffsetBattery = 53;

    /// <summary>Report offset of the host status byte carrying the mic-mute echo bit.</summary>
    private const int OffsetHostStatus = 54;

    // The reference byte 54 (external microphone / haptics low-pass status, report offset 55) and
    // the eight-byte tail of its bytes 55-62 ("AesCmac[8]", report offsets 56-63) stay zero: the
    // input state carries no such data.

    /// <summary>Face button bit of Square in the high nibble of the hat/face byte (reference bit 7.4).</summary>
    private const byte SquareBit = 0x10;

    /// <summary>Face button bit of Cross in the high nibble of the hat/face byte.</summary>
    private const byte CrossBit = 0x20;

    /// <summary>Face button bit of Circle in the high nibble of the hat/face byte.</summary>
    private const byte CircleBit = 0x40;

    /// <summary>Face button bit of Triangle in the high nibble of the hat/face byte.</summary>
    private const byte TriangleBit = 0x80;

    /// <summary>Mask of the 4-bit hat nibble (the D-pad direction) in the hat/face byte.</summary>
    private const byte DpadMask = 0x0F;

    /// <summary>Button bit of L1 in the shoulder byte (reference bit 8.0).</summary>
    private const byte L1Bit = 0x01;

    /// <summary>Button bit of R1 in the shoulder byte.</summary>
    private const byte R1Bit = 0x02;

    /// <summary>Button bit of the full L2 press in the shoulder byte (distinct from the analog trigger).</summary>
    private const byte L2Bit = 0x04;

    /// <summary>Button bit of the full R2 press in the shoulder byte (distinct from the analog trigger).</summary>
    private const byte R2Bit = 0x08;

    /// <summary>Button bit of Create in the shoulder byte.</summary>
    private const byte CreateBit = 0x10;

    /// <summary>Button bit of Options in the shoulder byte.</summary>
    private const byte OptionsBit = 0x20;

    /// <summary>Button bit of L3 in the shoulder byte.</summary>
    private const byte L3Bit = 0x40;

    /// <summary>Button bit of R3 in the shoulder byte.</summary>
    private const byte R3Bit = 0x80;

    /// <summary>Button bit of PS in the utility byte (reference bit 9.0, ButtonHome).</summary>
    private const byte PsBit = 0x01;

    /// <summary>Button bit of the touchpad click in the utility byte (reference bit 9.1).</summary>
    private const byte TouchpadBit = 0x02;

    /// <summary>Button bit of the microphone mute button in the utility byte (reference bit 9.2).</summary>
    private const byte MuteBit = 0x04;

    /// <summary>Button bit of the Edge left Fn button in the utility byte (reference bit 9.4).</summary>
    private const byte FnLeftBit = 0x10;

    /// <summary>Button bit of the Edge right Fn button in the utility byte (reference bit 9.5).</summary>
    private const byte FnRightBit = 0x20;

    /// <summary>Button bit of the Edge left back paddle in the utility byte (reference bit 9.6).</summary>
    private const byte PaddleLeftBit = 0x40;

    /// <summary>Button bit of the Edge right back paddle in the utility byte (reference bit 9.7).</summary>
    private const byte PaddleRightBit = 0x80;

    /// <summary>Mask of the battery percent nibble in the power byte (reference bits 52.0-52.3).</summary>
    private const byte BatteryLevelMask = 0x0F;

    /// <summary>Mask of the power state nibble in the power byte (reference bits 52.4-52.7).</summary>
    private const byte PowerStateMask = 0x0F;

    /// <summary>Shift of the power state nibble within the power byte (reference bits 52.4-52.7).</summary>
    private const int PowerStateShift = 4;

    /// <summary>Mask of one 4-bit trigger stop/status or effect nibble.</summary>
    private const byte TriggerNibbleMask = 0x0F;

    /// <summary>Shift of the high nibble of a trigger stop/status or effect byte.</summary>
    private const int TriggerNibbleShift = 4;

    /// <summary>Contact byte of an idle touch point: the not-touching bit set with tracking index zero.</summary>
    private const byte TouchContactInactive = 0x80;

    /// <summary>Mask of the 7-bit touch point tracking index (bit 7 is the not-touching flag).</summary>
    private const byte TouchIdMask = 0x7F;

    /// <summary>Mask of the 12-bit touch point coordinates.</summary>
    private const int TouchCoordinateMask = 0x0FFF;

    /// <summary>Shift extracting the high nibble of an x coordinate (bits 11-8) from the coordinate word.</summary>
    private const int TouchCoordinateHighShift = 8;

    /// <summary>Mask of the x-high nibble and the y-low nibble in the packed coordinate byte.</summary>
    private const byte TouchCoordinateNibbleMask = 0x0F;

    /// <summary>Shift placing the y-low nibble in the high half of the packed coordinate byte, and extracting the y-high byte from the y word.</summary>
    private const int TouchCoordinateNibbleShift = 4;

    /// <summary>Host status bit of the microphone mute echo (reference byte 53 bit 2).</summary>
    private const byte MicMutedBit = 0x04;

    /// <summary>
    /// Writes the full 64-byte input report for one frame and returns <see cref="Size"/>.
    /// </summary>
    /// <param name="state">Input state to report; each field is masked to its report width.</param>
    /// <param name="packetCounter">
    /// Rolling sequence number of the persona device, written verbatim into the reference's SeqNo
    /// byte; the reference pins no mask for it (it records "always 0x01 on BT"), so the device
    /// controls the wrap of the counter byte.
    /// </param>
    /// <param name="touch1Counter">
    /// Rolling counter of the first touch point, written as the pair's touch timestamp while the
    /// first point is active; the device increments it while the point is active and holds it
    /// otherwise, mirroring the DualShock 4 counter rule.
    /// </param>
    /// <param name="touch2Counter">
    /// Rolling counter of the second touch point. The DualSense's touch section carries one
    /// timestamp for the pair of points (not one per sample like the DualShock 4), so the builder
    /// writes this counter only while the first point is inactive and the second one is active;
    /// the second point's samples are then the samples the timestamp describes.
    /// </param>
    /// <param name="destination">Destination of at least <see cref="Size"/> bytes; only those are written.</param>
    /// <returns>The number of bytes written (<see cref="Size"/>).</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <see cref="Size"/> bytes.</exception>
    public static int Build(in DualSenseInputState state, byte packetCounter, byte touch1Counter, byte touch2Counter, Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException($"A DualSense input report requires {Size} bytes; got {destination.Length}.", nameof(destination));
        }

        // Everything the state does not carry (the reserved counter word, the reference tail, the
        // external-mic/haptics status and idle touch coordinate bytes) stays zero from this bounded clear.
        Span<byte> report = destination[..Size];
        report.Clear();

        report[OffsetReportId] = ReportId;
        report[OffsetLeftStickX] = state.LeftStickX;
        report[OffsetLeftStickY] = state.LeftStickY;
        report[OffsetRightStickX] = state.RightStickX;
        report[OffsetRightStickY] = state.RightStickY;
        report[OffsetLeftTrigger] = state.LeftTrigger;
        report[OffsetRightTrigger] = state.RightTrigger;
        report[OffsetSequenceCounter] = packetCounter;

        byte faceButtons = (byte)(Bit(state.Square, SquareBit) | Bit(state.Cross, CrossBit) | Bit(state.Circle, CircleBit) | Bit(state.Triangle, TriangleBit));
        report[OffsetFaceAndDpad] = (byte)(((byte)state.Dpad & DpadMask) | faceButtons);
        report[OffsetShoulderButtons] = (byte)(Bit(state.L1, L1Bit) | Bit(state.R1, R1Bit) | Bit(state.L2, L2Bit) | Bit(state.R2, R2Bit)
                                               | Bit(state.Create, CreateBit) | Bit(state.Options, OptionsBit) | Bit(state.L3, L3Bit) | Bit(state.R3, R3Bit));
        report[OffsetUtilityButtons] = (byte)(Bit(state.Ps, PsBit) | Bit(state.TouchpadPressed, TouchpadBit) | Bit(state.Mute, MuteBit)
                                              | Bit(state.FnLeft, FnLeftBit) | Bit(state.FnRight, FnRightBit)
                                              | Bit(state.PaddleLeft, PaddleLeftBit) | Bit(state.PaddleRight, PaddleRightBit));

        BinaryPrimitives.WriteInt16LittleEndian(report[OffsetGyroX..], state.GyroX);
        BinaryPrimitives.WriteInt16LittleEndian(report[OffsetGyroZ..], state.GyroZ);
        BinaryPrimitives.WriteInt16LittleEndian(report[OffsetGyroY..], state.GyroY);
        BinaryPrimitives.WriteInt16LittleEndian(report[OffsetAccelX..], state.AccelX);
        BinaryPrimitives.WriteInt16LittleEndian(report[OffsetAccelY..], state.AccelY);
        BinaryPrimitives.WriteInt16LittleEndian(report[OffsetAccelZ..], state.AccelZ);
        BinaryPrimitives.WriteUInt32LittleEndian(report[OffsetSensorTimestamp..], state.SensorTimestamp);
        report[OffsetTemperature] = (byte)state.Temperature;

        WriteTouchPoints(report, state, touch1Counter, touch2Counter);
        WriteNibblePair(report, OffsetTriggerRightStatus, state.TriggerRightStopLocation, state.TriggerRightStatus);
        WriteNibblePair(report, OffsetTriggerLeftStatus, state.TriggerLeftStopLocation, state.TriggerLeftStatus);
        BinaryPrimitives.WriteUInt32LittleEndian(report[OffsetHostTimestamp..], state.HostTimestamp);
        WriteNibblePair(report, OffsetTriggerEffects, state.TriggerRightEffect, state.TriggerLeftEffect);
        BinaryPrimitives.WriteUInt32LittleEndian(report[OffsetDeviceTimestamp..], state.DeviceTimestamp);
        report[OffsetBattery] = (byte)((state.BatteryLevel & BatteryLevelMask) | (((byte)state.PowerState & PowerStateMask) << PowerStateShift));
        report[OffsetHostStatus] = Bit(state.MicMuted, MicMutedBit);
        return Size;
    }

    /// <summary>
    /// Writes the two touch points and the rolling touch timestamp of the pair: the point's contact
    /// byte (tracking index with the not-touching bit inverted) followed by the 12-bit x/y
    /// coordinates packed over three bytes, and the counter of the point the sample belongs to
    /// (the first point while it is active, otherwise the second point's).
    /// </summary>
    /// <param name="report">Report span of exactly <see cref="Size"/> bytes.</param>
    /// <param name="state">Input state carrying both touch points.</param>
    /// <param name="touch1Counter">Rolling counter of the first touch point.</param>
    /// <param name="touch2Counter">Rolling counter of the second touch point.</param>
    private static void WriteTouchPoints(Span<byte> report, in DualSenseInputState state, byte touch1Counter, byte touch2Counter)
    {
        WriteTouchPoint(report, OffsetTouchPoint1, state.Touch1Active, state.Touch1Id, state.Touch1X, state.Touch1Y);
        WriteTouchPoint(report, OffsetTouchPoint2, state.Touch2Active, state.Touch2Id, state.Touch2X, state.Touch2Y);
        report[OffsetTouchTimestamp] = state.Touch1Active || !state.Touch2Active ? touch1Counter : touch2Counter;
    }

    /// <summary>
    /// Writes one touch point slot: the contact byte (tracking index with the not-touching bit
    /// inverted) followed by the 12-bit x/y coordinates packed over three bytes. An idle point
    /// writes only the inactive marker; its coordinate bytes stay zero.
    /// </summary>
    /// <param name="report">Report span holding the touch point.</param>
    /// <param name="offset">Offset of the point's contact byte within the report.</param>
    /// <param name="active">Whether the point currently touches the pad.</param>
    /// <param name="id">Tracking index of the point (masked to seven bits).</param>
    /// <param name="x">X coordinate (masked to twelve bits).</param>
    /// <param name="y">Y coordinate (masked to twelve bits).</param>
    private static void WriteTouchPoint(Span<byte> report, int offset, bool active, byte id, ushort x, ushort y)
    {
        if (!active)
        {
            report[offset] = TouchContactInactive;
            return;
        }

        int coordinateX = x & TouchCoordinateMask;
        int coordinateY = y & TouchCoordinateMask;
        report[offset] = (byte)(id & TouchIdMask);
        report[offset + 1] = (byte)coordinateX;
        report[offset + 2] = (byte)(((coordinateX >> TouchCoordinateHighShift) & TouchCoordinateNibbleMask)
                                    | ((coordinateY & TouchCoordinateNibbleMask) << TouchCoordinateNibbleShift));
        report[offset + 3] = (byte)(coordinateY >> TouchCoordinateNibbleShift);
    }

    /// <summary>
    /// Writes one byte that packs two 4-bit fields: the first value in the low nibble and the
    /// second in the high nibble (the reference's trigger stop-location/status bytes 41 and 42,
    /// and the trigger effect byte 47).
    /// </summary>
    /// <param name="report">Report span holding the packed byte.</param>
    /// <param name="offset">Offset of the packed byte within the report.</param>
    /// <param name="low">Value written into the low nibble (masked to four bits).</param>
    /// <param name="high">Value written into the high nibble (masked to four bits).</param>
    private static void WriteNibblePair(Span<byte> report, int offset, byte low, byte high) =>
        report[offset] = (byte)((low & TriggerNibbleMask) | ((high & TriggerNibbleMask) << TriggerNibbleShift));

    /// <summary>
    /// Returns <paramref name="mask"/> when the button is set, otherwise zero.
    /// </summary>
    /// <param name="set">Whether the button is held.</param>
    /// <param name="mask">Bit of the button in its report byte.</param>
    /// <returns>The button bit, or zero.</returns>
    private static byte Bit(bool set, byte mask) => set ? mask : (byte)0;
}