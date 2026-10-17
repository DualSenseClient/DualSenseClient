using System.Buffers.Binary;

// Report layout: the v2 reference serves the neutral report ("hat released (0x08)", centered
// sticks) and notes that the BT 0x11 payload "matches the USB report 0x01 payload byte-for-byte",
// stored from payload index 0 (payload offset N here is report offset N + 1, report id at offset 0).
// Its wake path documents the button bytes (offset 4 low nibble D-pad, offset 5 L1..R3, offset 6
// PS/touchpad and the report counter in bits 2-7) and its battery path the battery/ext byte at
// payload offset 29 (level bits 0-3, cable bit 4). The reference pins num_touch_reports at payload
// offset 32 and the rolling touch-report timestamp at payload offset 33. The served 507-byte HID
// report descriptor (report id 1 block) fixes the field order: four stick axes (X Y Z Rz),
// hat + 14 buttons, 6-bit counter, Rx/Ry triggers and the 54-byte vendor tail holding the sensors,
// battery and touch samples.

namespace DualSenseClient.Emulation.Devices.DualShock4;

/// <summary>
/// Builds the 64-byte DualShock 4 USB input report (report id <see cref="ReportId"/>) from an input
/// state: centered sticks are 128, the D-pad nibble is the hat encoding of
/// <see cref="DualShock4DpadDirection"/> and an idle touch point keeps the reference's 0x80 contact
/// marker. The builder is pure: the persona device owns the packet counter and the two touch rolling
/// counters and passes them in every frame, and nothing is written beyond <see cref="Size"/> bytes.
/// </summary>
public static class DualShock4InputReport
{
    /// <summary>Length in bytes of the report: the report id byte plus the 63-byte payload.</summary>
    public const int Size = 64;

    /// <summary>Report id of the main DualShock 4 input report (USB report 0x01).</summary>
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

    /// <summary>Report offset of the D-pad nibble (low) and the face buttons (high).</summary>
    private const int OffsetFaceAndDpad = 5;

    /// <summary>Report offset of the shoulder/directional button byte.</summary>
    private const int OffsetShoulderButtons = 6;

    /// <summary>Report offset of the PS/touchpad bits and the 6-bit packet counter.</summary>
    private const int OffsetPsTouchpadAndCounter = 7;

    /// <summary>Report offset of the L2 analog trigger.</summary>
    private const int OffsetLeftTrigger = 8;

    /// <summary>Report offset of the R2 analog trigger.</summary>
    private const int OffsetRightTrigger = 9;

    // The sensor timestamp (10-11) and temperature (12) stay zero: the input state carries no such data.

    /// <summary>Report offset of the little-endian gyro pitch word (the first motion axis).</summary>
    private const int OffsetGyroPitch = 13;

    /// <summary>Report offset of the little-endian gyro yaw word.</summary>
    private const int OffsetGyroYaw = 15;

    /// <summary>Report offset of the little-endian gyro roll word.</summary>
    private const int OffsetGyroRoll = 17;

    /// <summary>Report offset of the little-endian accelerometer x word (the first accelerometer axis).</summary>
    private const int OffsetAccelX = 19;

    /// <summary>Report offset of the little-endian accelerometer y word.</summary>
    private const int OffsetAccelY = 21;

    /// <summary>Report offset of the little-endian accelerometer z word.</summary>
    private const int OffsetAccelZ = 23;

    /// <summary>Report offset of the battery/ext byte (level bits 0-3, cable state bit 4).</summary>
    private const int OffsetBattery = 30;

    /// <summary>Report offset of the number of touch report samples in this report.</summary>
    private const int OffsetTouchReportCount = 33;

    /// <summary>
    /// Report offset of the first touch report sample: a rolling counter byte followed by two 4-byte
    /// touch points. The reference device fills three samples (and the USB report truncates a fourth).
    /// </summary>
    private const int OffsetTouchReports = 34;

    /// <summary>Length in bytes of one touch report sample: counter plus two touch points.</summary>
    private const int TouchReportSize = 9;

    /// <summary>Offset of the first touch point within a touch report sample.</summary>
    private const int TouchPoint1Offset = 1;

    /// <summary>Offset of the second touch point within a touch report sample.</summary>
    private const int TouchPoint2Offset = 5;

    /// <summary>
    /// Report offset of the idle contact marker of the fourth touch sample, truncated by the USB
    /// report size: observed real-controller captures keep the 0x80 idle marker there (the sample's
    /// counter and x-low byte stay zero), so this builder reproduces that capture shape.
    /// </summary>
    private const int OffsetTruncatedTouchContact = 62;

    /// <summary>Face button bit of Square in the high nibble of the hat/face byte (reference layout).</summary>
    private const byte SquareBit = 0x10;

    /// <summary>Face button bit of Cross in the high nibble of the hat/face byte.</summary>
    private const byte CrossBit = 0x20;

    /// <summary>Face button bit of Circle in the high nibble of the hat/face byte.</summary>
    private const byte CircleBit = 0x40;

    /// <summary>Face button bit of Triangle in the high nibble of the hat/face byte.</summary>
    private const byte TriangleBit = 0x80;

    /// <summary>Mask of the 4-bit hat nibble (the D-pad direction) in the hat/face byte.</summary>
    private const byte DpadMask = 0x0F;

    /// <summary>Button bit of L1 in the shoulder byte (reference byte 5 order).</summary>
    private const byte L1Bit = 0x01;

    /// <summary>Button bit of R1 in the shoulder byte.</summary>
    private const byte R1Bit = 0x02;

    /// <summary>Button bit of the full L2 press in the shoulder byte (distinct from the analog trigger).</summary>
    private const byte L2Bit = 0x04;

    /// <summary>Button bit of the full R2 press in the shoulder byte (distinct from the analog trigger).</summary>
    private const byte R2Bit = 0x08;

    /// <summary>Button bit of Share in the shoulder byte.</summary>
    private const byte ShareBit = 0x10;

    /// <summary>Button bit of Options in the shoulder byte.</summary>
    private const byte OptionsBit = 0x20;

    /// <summary>Button bit of L3 in the shoulder byte.</summary>
    private const byte L3Bit = 0x40;

    /// <summary>Button bit of R3 in the shoulder byte.</summary>
    private const byte R3Bit = 0x80;

    /// <summary>Button bit of PS in the PS/touchpad byte (reference byte 6 bit 0).</summary>
    private const byte PsBit = 0x01;

    /// <summary>Button bit of the touchpad click in the PS/touchpad byte (bit 1).</summary>
    private const byte TouchpadBit = 0x02;

    /// <summary>Shift of the 6-bit packet counter within the PS/touchpad byte (bits 2-7).</summary>
    private const int PacketCounterShift = 2;

    /// <summary>Mask of the 6-bit packet counter.</summary>
    private const byte PacketCounterMask = 0x3F;

    /// <summary>Mask of the battery capacity nibble in the battery/ext byte (reference bits 0-3).</summary>
    private const byte BatteryLevelMask = 0x0F;

    /// <summary>Cable/charging state bit of the battery/ext byte (bit 4, "cable" in the reference).</summary>
    private const byte ChargingBit = 0x10;

    /// <summary>Contact byte of an idle touch point: the inactive bit set with id zero.</summary>
    private const byte TouchContactInactive = 0x80;

    /// <summary>Mask of the 7-bit touch point tracking id (bit 7 is the inactive flag).</summary>
    private const byte TouchIdMask = 0x7F;

    /// <summary>Mask of the 12-bit touch point coordinates.</summary>
    private const int TouchCoordinateMask = 0x0FFF;

    /// <summary>Shift extracting the high nibble of an x coordinate (bits 11-8) from the coordinate word.</summary>
    private const int TouchCoordinateHighShift = 8;

    /// <summary>Mask of the x-high nibble and the y-low nibble in the packed coordinate byte.</summary>
    private const byte TouchCoordinateNibbleMask = 0x0F;

    /// <summary>Shift placing the y-low nibble in the high half of the packed coordinate byte, and extracting the y-high byte from the y word.</summary>
    private const int TouchCoordinateNibbleShift = 4;

    /// <summary>
    /// Writes the full 64-byte input report for one frame and returns <see cref="Size"/>.
    /// </summary>
    /// <param name="state">Input state to report; each field is masked to its report width.</param>
    /// <param name="packetCounter">
    /// Rolling packet counter of the persona device, written into the six counter bits; it wraps
    /// within those bits and the device increments it once per report.
    /// </param>
    /// <param name="touch1Counter">
    /// Rolling counter of the first touch point, written as the first touch sample's timestamp; the
    /// device increments it while the point is active and holds it otherwise.
    /// </param>
    /// <param name="touch2Counter">
    /// Rolling counter of the second touch point, written as the second touch sample's timestamp.
    /// </param>
    /// <param name="destination">Destination of at least <see cref="Size"/> bytes; only those are written.</param>
    /// <returns>The number of bytes written (<see cref="Size"/>).</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <see cref="Size"/> bytes.</exception>
    public static int Build(in DualShock4InputState state, byte packetCounter, byte touch1Counter, byte touch2Counter, Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException($"A DualShock 4 input report requires {Size} bytes; got {destination.Length}.", nameof(destination));
        }

        // Everything the state does not carry (sensor timestamp and temperature, reserved bytes,
        // idle touch slots) stays zero from this bounded clear.
        Span<byte> report = destination[..Size];
        report.Clear();

        report[OffsetReportId] = ReportId;
        report[OffsetLeftStickX] = state.LeftStickX;
        report[OffsetLeftStickY] = state.LeftStickY;
        report[OffsetRightStickX] = state.RightStickX;
        report[OffsetRightStickY] = state.RightStickY;

        byte faceButtons = (byte)(Bit(state.Square, SquareBit) | Bit(state.Cross, CrossBit) | Bit(state.Circle, CircleBit) | Bit(state.Triangle, TriangleBit));
        report[OffsetFaceAndDpad] = (byte)(((byte)state.Dpad & DpadMask) | faceButtons);
        report[OffsetShoulderButtons] = (byte)(Bit(state.L1, L1Bit) | Bit(state.R1, R1Bit) | Bit(state.L2, L2Bit) | Bit(state.R2, R2Bit)
                                               | Bit(state.Share, ShareBit) | Bit(state.Options, OptionsBit) | Bit(state.L3, L3Bit) | Bit(state.R3, R3Bit));
        report[OffsetPsTouchpadAndCounter] = (byte)(((packetCounter & PacketCounterMask) << PacketCounterShift)
                                                    | Bit(state.Ps, PsBit) | Bit(state.TouchpadPressed, TouchpadBit));
        report[OffsetLeftTrigger] = state.LeftTrigger;
        report[OffsetRightTrigger] = state.RightTrigger;

        BinaryPrimitives.WriteInt16LittleEndian(report[OffsetGyroPitch..], state.GyroPitch);
        BinaryPrimitives.WriteInt16LittleEndian(report[OffsetGyroYaw..], state.GyroYaw);
        BinaryPrimitives.WriteInt16LittleEndian(report[OffsetGyroRoll..], state.GyroRoll);
        BinaryPrimitives.WriteInt16LittleEndian(report[OffsetAccelX..], state.AccelX);
        BinaryPrimitives.WriteInt16LittleEndian(report[OffsetAccelY..], state.AccelY);
        BinaryPrimitives.WriteInt16LittleEndian(report[OffsetAccelZ..], state.AccelZ);

        report[OffsetBattery] = (byte)((state.BatteryLevel & BatteryLevelMask) | Bit(state.Charging, ChargingBit));

        WriteTouchSamples(report, state, touch1Counter, touch2Counter);
        return Size;
    }

    /// <summary>
    /// Writes the touch section: one fresh sample per active touch point (the count at
    /// <see cref="OffsetTouchReportCount"/>), the two rolling counters as the sample timestamps, and
    /// the current touch points in every sample (reference captures repeat the contact set per
    /// sample). Idle points keep the 0x80 contact marker.
    /// </summary>
    /// <param name="report">Report span of exactly <see cref="Size"/> bytes.</param>
    /// <param name="state">Input state carrying both touch points.</param>
    /// <param name="touch1Counter">Rolling counter of the first touch point.</param>
    /// <param name="touch2Counter">Rolling counter of the second touch point.</param>
    private static void WriteTouchSamples(Span<byte> report, in DualShock4InputState state, byte touch1Counter, byte touch2Counter)
    {
        byte activePoints = (byte)((state.Touch1Active ? 1 : 0) + (state.Touch2Active ? 1 : 0));
        report[OffsetTouchReportCount] = activePoints;
        report[OffsetTouchReports] = touch1Counter;
        report[OffsetTouchReports + TouchReportSize] = touch2Counter;

        WriteTouchPoint(report, OffsetTouchReports + TouchPoint1Offset, state.Touch1Active, state.Touch1Id, state.Touch1X, state.Touch1Y);
        WriteTouchPoint(report, OffsetTouchReports + TouchPoint2Offset, state.Touch2Active, state.Touch2Id, state.Touch2X, state.Touch2Y);
        WriteTouchPoint(report, OffsetTouchReports + TouchReportSize + TouchPoint1Offset, state.Touch1Active, state.Touch1Id, state.Touch1X, state.Touch1Y);
        WriteTouchPoint(report, OffsetTouchReports + TouchReportSize + TouchPoint2Offset, state.Touch2Active, state.Touch2Id, state.Touch2X, state.Touch2Y);
        WriteTouchPoint(report, OffsetTouchReports + 2 * TouchReportSize + TouchPoint1Offset, false, 0, 0, 0);
        WriteTouchPoint(report, OffsetTouchReports + 2 * TouchReportSize + TouchPoint2Offset, false, 0, 0, 0);
        report[OffsetTruncatedTouchContact] = TouchContactInactive;
    }

    /// <summary>
    /// Writes one touch point slot: the contact byte (tracking id with the inactive bit inverted)
    /// followed by the 12-bit x/y coordinates packed over three bytes. An idle point writes only the
    /// inactive marker; its coordinate bytes stay zero.
    /// </summary>
    /// <param name="report">Report span holding the touch sample.</param>
    /// <param name="offset">Offset of the point's contact byte within the report.</param>
    /// <param name="active">Whether the point currently touches the pad.</param>
    /// <param name="id">Tracking id of the point (masked to seven bits).</param>
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
    /// Returns <paramref name="mask"/> when the button is set, otherwise zero.
    /// </summary>
    /// <param name="set">Whether the button is held.</param>
    /// <param name="mask">Bit of the button in its report byte.</param>
    /// <returns>The button bit, or zero.</returns>
    private static byte Bit(bool set, byte mask) => set ? mask : (byte)0;
}