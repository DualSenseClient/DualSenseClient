// Feature-report ids, lengths and field layouts:
// - The 321-byte DualSense descriptor declares report id 5 with "Report Count (40)", report id 9
//   with 19, report id 32 (0x20) with 63 and report id 129 (0x81) with 63. It declares no report id
//   18 (0x12): that is the DualShock 4 pairing-info report ("Report ID (18)"). The 437-byte Edge
//   descriptor adds the profile and function reports 96-101, 104 and 112-123 and widens report id
//   242 (0xF2) from count 15 to 52.
// - GET_REPORT serves feature reads from the controller-backed cache and answers a read the cache
//   never filled with zero bytes, which the stack turns into a STALL; SET_REPORT has no failure
//   path and therefore acknowledges every feature write.
// - The reference prefetches 0x09 (20 bytes), 0x20 (64), 0x22 (64) and 0x05 (41); the pairing MAC
//   is read out of the 0x09 body by the kernel (Sources/linux/hid-playstation.c copies buf[1..6])
//   and by the in-repo client (report[6] first to report[1] last), the calibration words are the
//   kernel's 17 little-endian int16 fields at buf[1..34] (layout documented in
//   docs/01-DualSense-HID-Protocol-Implementation.md, section 8.1) and the firmware body is the
//   reference's own synthetic 63-byte body (<c>report20</c>, which it substitutes for a DualSense
//   Edge in its compatibility mode); a controller is classified as an Edge when the firmware
//   body's byte 22 is 0x44 ("Connected DSE Controller").
// - Behavior oracle (no code copied): GET_REPORT serves *every* report id the persona's descriptors
//   list -- getFeatureReport() returns real content for 0x05/0x09/0x20/0x81 (and Edge 0x65) and an
//   exact-length zero stub with the report id byte set for every other listed id; the lengths come
//   from descriptor-derived maps that match this repo's descriptor counts one for one; the Edge
//   profile block 0x60-0x7B is served as static stubs until a feeder pushes physical snapshots.
//   GET 0x65 echoes the stored handshake payload or, when the host never wrote one, the current
//   0x20 firmware body, Edge-only. Feature reports are clamped to the requested wLength. Its
//   calibration default body is the only concrete calibration default in the reference set.
// - Consumer corroboration for 0x22 (the second hardware-info page): the in-repo protocol doc lists
//   it as "served by virtual devices" (docs/01-DualSense-HID-Protocol-Implementation.md, section 8)
//   and a third-party driver zero-stubs it at 64 bytes because its traceability test reads it once
//   the 0x20 report looks real; its getBtPatchInfo bails when byte 0 of the reply is not the report
//   id.

namespace DualSenseClient.Emulation.Devices.DualSense;

/// <summary>
/// Feature-report table of the DualSense persona: maps a report id to its declared reference length
/// and writes the report body. The table serves the calibration report 0x05 (41 bytes,
/// motion-sensor calibration), the pairing report 0x09 (20 bytes, controller MAC), the firmware
/// report 0x20 (64 bytes, firmware/hardware info), the factory command response 0x81 (64 bytes,
/// answering the last 0x80 command) and, on the Edge persona, the factory handshake report 0x65
/// (64 bytes, echoing the stored handshake or the firmware body). Every other report id the
/// persona's descriptors declare answers its declared length as a zero stub with only the report id
/// byte set, the behavior the reference serves for every descriptor-listed id; a report id no
/// descriptor declares returns -1 (STALL), like the reference and its unfilled controller cache.
/// The table is pure: it answers from the supplied <see cref="DualSenseFeatureReportState"/> and
/// never mutates it or the device.
/// </summary>
public static class DualSenseFeatureReports
{
    /// <summary>Value returned for a report id no persona descriptor declares (STALL).</summary>
    private const int Stall = -1;

    /// <summary>Report id of the motion-sensor calibration feature report (descriptor report 5).</summary>
    public const byte CalibrationReportId = 0x05;

    /// <summary>Reference length in bytes of the calibration report: report id plus a 40-byte payload.</summary>
    public const int CalibrationSize = 41;

    /// <summary>Report id of the BT pairing-info feature report (descriptor report 9).</summary>
    public const byte PairingReportId = 0x09;

    /// <summary>Reference length in bytes of the pairing report: report id plus a 19-byte payload.</summary>
    public const int PairingSize = 20;

    /// <summary>Report id of the firmware/hardware-info feature report (descriptor report 32).</summary>
    public const byte FirmwareReportId = 0x20;

    /// <summary>Reference length in bytes of the firmware report: report id plus a 63-byte payload.</summary>
    public const int FirmwareSize = 64;

    /// <summary>Report id of the factory-command feature report the host writes (descriptor report 128).</summary>
    public const byte CommandReportId = 0x80;

    /// <summary>Report id of the factory command-response feature report (descriptor report 129).</summary>
    public const byte CommandResponseReportId = 0x81;

    /// <summary>Reference length in bytes of the command-response report: report id plus a 63-byte payload.</summary>
    public const int CommandResponseSize = 64;

    /// <summary>
    /// Payload length in bytes of a factory command whose sub-command the device records; the
    /// reference's write path records the command only when at least two payload bytes (the
    /// sub-command id and its parameter) arrived after the report id.
    /// </summary>
    public const int CommandPayloadLength = 2;

    /// <summary>Report id of the Edge factory handshake report (descriptor report 101, Edge only).</summary>
    public const byte EdgeHandshakeReportId = 0x65;

    /// <summary>Reference length in bytes of the Edge handshake report: report id plus a 63-byte payload.</summary>
    public const int EdgeHandshakeSize = 64;

    /// <summary>
    /// Payload length in bytes of an Edge handshake write the device stores; the reference stores
    /// the 63 payload bytes after the report id and only for a full-length write.
    /// </summary>
    public const int EdgeHandshakePayloadLength = 63;

    /// <summary>Length in bytes of the controller MAC address region of the pairing report.</summary>
    private const int MacAddressLength = 6;

    /// <summary>Report offset of the first MAC byte of the pairing report (the six bytes are reversed).</summary>
    private const int PairingMacOffset = 1;

    /// <summary>Report offset of the pairing report's first fixed marker byte ("Hard08" of the in-repo client's parse comment).</summary>
    private const int PairingHardMarkerOffset0 = 7;

    /// <summary>Report offset of the pairing report's second fixed marker byte ("Hard25").</summary>
    private const int PairingHardMarkerOffset1 = 8;

    /// <summary>First fixed marker byte of the pairing report.</summary>
    private const byte PairingHardMarker0 = 0x08;

    /// <summary>Second fixed marker byte of the pairing report.</summary>
    private const byte PairingHardMarker1 = 0x25;

    /// <summary>Report offset of the firmware body's controller marker byte (0x44 when the reference classifies the controller as an Edge).</summary>
    private const int FirmwareControllerMarkerOffset = 22;

    /// <summary>Marker byte the reference reads to classify a DualSense Edge ("Connected DSE Controller").</summary>
    private const byte DseControllerMarker = 0x44;

    /// <summary>Report offset of the command-response body's first payload byte (the response marker).</summary>
    private const int CommandResponseMarkerOffset = 1;

    /// <summary>Report offset of the command-response body's serial region (18 ASCII bytes).</summary>
    private const int CommandResponseSerialOffset = 3;

    /// <summary>Length in bytes of the command-response serial region.</summary>
    private const int CommandResponseSerialLength = 18;

    /// <summary>Report offset of the command-response body's status byte.</summary>
    private const int CommandResponseStatusOffset = 4;

    /// <summary>Marker byte the command response carries as its first payload byte.</summary>
    private const byte CommandResponseMarker = 0x01;

    /// <summary>Status byte of the status sub-command response.</summary>
    private const byte CommandResponseStatus = 0x01;

    /// <summary>Report id of the factory sub-command selecting the serial-number response.</summary>
    private const byte SerialSubcommand = 0x01;

    /// <summary>Report id of the factory sub-command selecting the status response.</summary>
    private const byte StatusSubcommand = 0x03;

    /// <summary>Largest report the table serves; the write buffer of <see cref="Write"/>.</summary>
    private const int MaximumReportSize = FirmwareSize;

    /// <summary>Replacement character served for a serial character outside the 7-bit ASCII range.</summary>
    private const byte NonAsciiReplacement = (byte)'?';

    /// <summary>
    /// Total lengths (report id plus payload) of the feature reports the base 321-byte descriptor
    /// declares, transcribed from its report list (<c>DualSenseDescriptorSet</c>): every "Report ID
    /// (n)" / "Report Count (c)" pair of the descriptor contributes n -> c + 1. The rows the table
    /// fills with real content are the calibration, pairing, firmware and command-response reports;
    /// every other row is served as the reference's zero stub.
    /// </summary>
    private static readonly (byte ReportId, int Size)[] BaseDeclaredLengths =
    [
        (0x05, 41), // Report ID (5), "Report Count (40)" -- calibration body
        (0x08, 48), // "Report Count (47)" -- zero stub
        (0x09, 20), // "Report Count (19)" -- pairing body
        (0x0A, 27), // "Report Count (26)" -- zero stub
        (0x0B, 42), // "Report Count (41)" -- zero stub
        (0x0C, 42), // "Report Count (41)" -- zero stub
        (0x20, 64), // "Report Count (63)" -- firmware body
        (0x21, 5), // "Report Count (4)" -- zero stub
        (0x22, 64), // "Report Count (63)" -- second hardware-info page, zero stub
        (0x80, 64), // "Report Count (63)" -- write-only command, the read answers a zero stub
        (0x81, 64), // "Report Count (63)" -- command-response body
        (0x82, 10), // "Report Count (9)" -- zero stub
        (0x83, 64), // "Report Count (63)" -- zero stub
        (0x84, 64), // "Report Count (63)" -- zero stub
        (0x85, 3), // "Report Count (2)" -- zero stub
        (0xA0, 2), // "Report Count (1)" -- zero stub
        (0xE0, 64), // "Report Count (63)" -- zero stub
        (0xF0, 64), // "Report Count (63)" -- zero stub
        (0xF1, 64), // "Report Count (63)" -- zero stub
        (0xF2, 16), // "Report Count (15)" -- zero stub (53 bytes on the Edge descriptor)
        (0xF4, 64), // "Report Count (63)" -- zero stub
        (0xF5, 4), // "Report Count (3)" -- zero stub
        (0xF6, 64), // "Report Count (63)" -- zero stub
        (0xF7, 64), // "Report Count (63)" -- zero stub
        (0xF8, 64), // "Report Count (63)" -- zero stub
        (0xF9, 64) // "Report Count (63)" -- zero stub
    ];

    /// <summary>
    /// Total lengths (report id plus payload) the 437-byte Edge descriptor adds to or widens over
    /// the base list, transcribed from its report list (<c>DualSenseEdgeDescriptorSet</c>): the
    /// profile and function reports 0x60-0x65 and 0x68 (all count 63), the profile snapshot slots
    /// 0x70-0x7B (all count 63) and the widened lightbar-setup report 0xF2 (count 52). The Edge
    /// handshake report 0x65 is filled with the stored-or-firmware echo; every other row is a zero
    /// stub.
    /// </summary>
    private static readonly (byte ReportId, int Size)[] EdgeDeclaredLengths =
    [
        (0x60, 64), // profile/function report -- zero stub
        (0x61, 64), // profile/function report -- zero stub
        (0x62, 64), // profile/function report -- zero stub
        (0x63, 64), // profile/function report -- zero stub
        (0x64, 64), // profile/function report -- zero stub
        (0x65, 64), // Edge factory handshake -- stored-or-firmware echo
        (0x68, 64), // profile/function report -- zero stub
        (0x70, 64), // profile snapshot slot -- zero stub
        (0x71, 64), // profile snapshot slot -- zero stub
        (0x72, 64), // profile snapshot slot -- zero stub
        (0x73, 64), // profile snapshot slot -- zero stub
        (0x74, 64), // profile snapshot slot -- zero stub
        (0x75, 64), // profile snapshot slot -- zero stub
        (0x76, 64), // profile snapshot slot -- zero stub
        (0x77, 64), // profile snapshot slot -- zero stub
        (0x78, 64), // profile snapshot slot -- zero stub
        (0x79, 64), // profile snapshot slot -- zero stub
        (0x7A, 64), // profile snapshot slot -- zero stub
        (0x7B, 64), // profile snapshot slot -- zero stub
        (0xF2, 53) // "Report Count (52)" -- zero stub, widened over the base descriptor's 16
    ];

    /// <summary>
    /// Writes the feature report <paramref name="reportId"/> into <paramref name="destination"/>: a
    /// report the persona's descriptors declare answers its declared length, carrying the real body
    /// for the ids the table fills and a zero stub (only the report id byte set) for the rest, the
    /// reference behavior for every descriptor-listed id. A report id no descriptor declares writes
    /// nothing and returns -1 (STALL). A shorter destination receives the report prefix (the
    /// behavior oracle clamps feature reports to the requested wLength; the reference does not).
    /// Nothing is ever written beyond <paramref name="destination"/>.
    /// </summary>
    /// <param name="reportId">Report id of the requested feature report (wValue low byte).</param>
    /// <param name="state">Snapshot of the persona metadata the report answers from.</param>
    /// <param name="destination">Buffer the report body is written to.</param>
    /// <returns>The number of bytes written, or -1 for STALL.</returns>
    public static int Write(byte reportId, in DualSenseFeatureReportState state, Span<byte> destination)
    {
        Span<byte> body = stackalloc byte[MaximumReportSize];
        body.Clear(); // the writers below define only the bytes their report carries
        int size = WriteBody(reportId, state, body);
        if (size == 0)
        {
            return Stall;
        }

        int count = Math.Min(size, destination.Length);
        body[..count].CopyTo(destination);
        return count;
    }

    /// <summary>
    /// Writes the body of one declared report and returns its length, or zero for a report id no
    /// persona descriptor declares: the ids the table fills carry their body, and every other
    /// declared id carries the reference's zero stub with only the report id byte set.
    /// </summary>
    /// <param name="reportId">Report id of the requested feature report.</param>
    /// <param name="state">Snapshot of the persona metadata the report answers from.</param>
    /// <param name="body">Cleared report buffer of at least the returned length.</param>
    /// <returns>The number of bytes written, or zero for an undeclared report id.</returns>
    private static int WriteBody(byte reportId, in DualSenseFeatureReportState state, Span<byte> body)
    {
        switch (reportId)
        {
            case CalibrationReportId:
                return WriteCalibration(body);

            case PairingReportId:
                return WritePairing(state, body);

            case FirmwareReportId:
                return WriteFirmware(state, body);

            case CommandResponseReportId:
                return WriteCommandResponse(state, body);

            case EdgeHandshakeReportId when state.EdgePersona:
                return WriteEdgeHandshake(state, body);

            default:
                int declaredSize = DeclaredSize(reportId, state.EdgePersona);
                return declaredSize == 0 ? 0 : WriteZeroStub(reportId, declaredSize, body);
        }
    }

    /// <summary>
    /// Returns the total declared length of <paramref name="reportId"/>, or zero when no persona
    /// descriptor declares it: the Edge list (additions and the widened 0xF2) wins on the Edge
    /// persona, the base list answers the rest, exactly like the reference's lookup.
    /// </summary>
    /// <param name="reportId">Report id to look up.</param>
    /// <param name="edgePersona">Whether the persona serves the 437-byte Edge descriptor.</param>
    /// <returns>The declared length in bytes (report id plus payload), or zero.</returns>
    private static int DeclaredSize(byte reportId, bool edgePersona)
    {
        if (edgePersona)
        {
            foreach ((byte id, int size) in EdgeDeclaredLengths)
            {
                if (id == reportId)
                {
                    return size;
                }
            }
        }

        foreach ((byte id, int size) in BaseDeclaredLengths)
        {
            if (id == reportId)
            {
                return size;
            }
        }

        return 0;
    }

    /// <summary>
    /// Writes the zero stub of a declared report id the table has no content for: the report id byte
    /// and zero payload bytes (the reference's shape, which its consumers validate by report id).
    /// </summary>
    /// <param name="reportId">Declared report id.</param>
    /// <param name="size">Declared length of the report.</param>
    /// <param name="body">Cleared report buffer of at least <paramref name="size"/> bytes.</param>
    /// <returns>The number of bytes written (<paramref name="size"/>).</returns>
    private static int WriteZeroStub(byte reportId, int size, Span<byte> body)
    {
        body[0] = reportId;
        return size;
    }

    /// <summary>
    /// Writes the calibration report 0x05: <see cref="CalibrationBody"/> verbatim (the kernel's
    /// int16 field layout carrying the behavior-oracle defaults; the reference itself forwards the
    /// physical controller's calibration).
    /// </summary>
    /// <param name="body">Report buffer of at least <see cref="CalibrationSize"/> bytes.</param>
    /// <returns>The number of bytes written (<see cref="CalibrationSize"/>).</returns>
    private static int WriteCalibration(Span<byte> body)
    {
        CalibrationBody.CopyTo(body);
        return CalibrationSize;
    }

    /// <summary>
    /// Writes the pairing report 0x09: the controller MAC in reverse address order (the controller
    /// serves the address with report byte 6 first, so the identity's address-order bytes land
    /// reversed), the two fixed "Hard" marker bytes and zeros for the host-adapter MAC region a
    /// virtual controller has no counterpart for. A MAC shorter than six bytes serves a zero MAC
    /// region.
    /// </summary>
    /// <param name="state">Metadata snapshot carrying the controller MAC.</param>
    /// <param name="body">Report buffer of at least <see cref="PairingSize"/> bytes.</param>
    /// <returns>The number of bytes written (<see cref="PairingSize"/>).</returns>
    private static int WritePairing(in DualSenseFeatureReportState state, Span<byte> body)
    {
        body[0] = PairingReportId;
        if (state.MacAddress.Length >= MacAddressLength)
        {
            for (int i = 0; i < MacAddressLength; i++)
            {
                body[PairingMacOffset + i] = state.MacAddress[MacAddressLength - 1 - i];
            }
        }

        body[PairingHardMarkerOffset0] = PairingHardMarker0;
        body[PairingHardMarkerOffset1] = PairingHardMarker1;
        return PairingSize;
    }

    /// <summary>
    /// Writes the firmware report 0x20: the reference body verbatim (<see cref="FirmwareBody"/>),
    /// with the controller marker byte rewritten to 0x44 for the Edge persona (the byte the
    /// reference reads to classify a controller as a DualSense Edge).
    /// </summary>
    /// <param name="state">Metadata snapshot carrying the persona variant.</param>
    /// <param name="body">Report buffer of at least <see cref="FirmwareSize"/> bytes.</param>
    /// <returns>The number of bytes written (<see cref="FirmwareSize"/>).</returns>
    private static int WriteFirmware(in DualSenseFeatureReportState state, Span<byte> body)
    {
        FirmwareBody.CopyTo(body);
        if (state.EdgePersona)
        {
            body[FirmwareControllerMarkerOffset] = DseControllerMarker;
        }

        return FirmwareSize;
    }

    /// <summary>
    /// Writes the Edge factory handshake report 0x65 (Edge persona only): the handshake payload the
    /// host last wrote, or, when it never wrote one, an echo of the current firmware report 0x20
    /// body under the 0x65 report id (the reference's stored-or-firmware echo). A stored payload
    /// replaces the whole payload region verbatim.
    /// </summary>
    /// <param name="state">Metadata snapshot carrying the persona variant and the stored handshake.</param>
    /// <param name="body">Report buffer of at least <see cref="EdgeHandshakeSize"/> bytes.</param>
    /// <returns>The number of bytes written (<see cref="EdgeHandshakeSize"/>).</returns>
    private static int WriteEdgeHandshake(in DualSenseFeatureReportState state, Span<byte> body)
    {
        WriteFirmware(state, body);
        body[0] = EdgeHandshakeReportId;
        if (state.EdgeHandshake is not null)
        {
            state.EdgeHandshake.CopyTo(body[1..]);
        }

        return EdgeHandshakeSize;
    }

    /// <summary>
    /// Writes the factory command response 0x81 from the last recorded command: the serial
    /// sub-command answers with the persona's serial number in the 18-byte ASCII region, the status
    /// sub-command with its two status bytes, and any other (or no) sub-command with the response
    /// marker alone.
    /// </summary>
    /// <param name="state">Metadata snapshot carrying the serial number and the last recorded sub-command.</param>
    /// <param name="body">Report buffer of at least <see cref="CommandResponseSize"/> bytes.</param>
    /// <returns>The number of bytes written (<see cref="CommandResponseSize"/>).</returns>
    private static int WriteCommandResponse(in DualSenseFeatureReportState state, Span<byte> body)
    {
        body[0] = CommandResponseReportId;
        switch (state.CommandSubcommand)
        {
            case SerialSubcommand:
                WriteSerial(state.SerialNumber, body);
                break;

            case StatusSubcommand:
                body[CommandResponseMarkerOffset] = CommandResponseMarker;
                body[CommandResponseStatusOffset] = CommandResponseStatus;
                break;

            default:
                body[CommandResponseMarkerOffset] = CommandResponseMarker;
                break;
        }

        return CommandResponseSize;
    }

    /// <summary>
    /// Writes the serial number into the command-response serial region: at most 18 characters,
    /// ASCII-encoded (characters outside 7-bit ASCII become '?'), with the remaining bytes zero.
    /// </summary>
    /// <param name="serialNumber">Serial number of the persona; may be empty.</param>
    /// <param name="body">Report buffer of at least <see cref="CommandResponseSize"/> bytes.</param>
    private static void WriteSerial(string serialNumber, Span<byte> body)
    {
        int count = Math.Min(serialNumber.Length, CommandResponseSerialLength);
        for (int i = 0; i < count; i++)
        {
            char value = serialNumber[i];
            body[CommandResponseSerialOffset + i] = value <= 0x7F ? (byte)value : NonAsciiReplacement;
        }
    }

    /// <summary>
    /// The calibration report 0x05 body (41 bytes, report id at index 0): the kernel's 17
    /// little-endian int16 fields at report offsets 1-34 (gyro pitch/yaw/roll bias, the six
    /// plus/minus words, gyro speed plus/minus and the six accelerometer plus/minus words) plus the
    /// trailer byte. The field values are the behavior oracle's defaults (no code copied); the
    /// reference itself forwards the physical controller's calibration.
    /// </summary>
    private static ReadOnlySpan<byte> CalibrationBody
    {
        get
        {
            return
            [
                0x05, // report id (0x05)
                0x00, 0x00, // gyro pitch bias: 0
                0x00, 0x00, // gyro yaw bias: 0
                0x00, 0x00, // gyro roll bias: 0
                0x00, 0x20, // gyro pitch plus: 8192
                0x00, 0xE0, // gyro pitch minus: -8192
                0x00, 0x20, // gyro yaw plus: 8192
                0x00, 0xE0, // gyro yaw minus: -8192
                0x00, 0x20, // gyro roll plus: 8192
                0x00, 0xE0, // gyro roll minus: -8192
                0xF4, 0x01, // gyro speed plus: 500
                0xF4, 0x01, // gyro speed minus: 500
                0x00, 0x20, // accelerometer X plus: 8192
                0x00, 0xE0, // accelerometer X minus: -8192
                0x00, 0x20, // accelerometer Y plus: 8192
                0x00, 0xE0, // accelerometer Y minus: -8192
                0x00, 0x20, // accelerometer Z plus: 8192
                0x00, 0xE0, // accelerometer Z minus: -8192
                0x0B, // trailer byte of the behavior-oracle body (the kernel reads no field here)
                0x00, 0x00, 0x00, 0x00, 0x00 // zero padding to the 40-byte payload
            ];
        }
    }

    /// <summary>
    /// The firmware report 0x20 body (64 bytes, report id at index 0): the reference's synthetic
    /// 63-byte body (<c>report20</c>), which the reference serves for a DualSense Edge in its
    /// compatibility mode. Report offsets:
    /// 1-11 the build date string ("Jul  4 2025"), 12-19 the build time string ("10:38:40"),
    /// 20 the hardware type (0x03), 22 the controller marker (0x0B in this body), 24-27 the
    /// hardware version (0x00000811), 28-31 the firmware version (0x0110002A) and 44-45 the update
    /// version (0x0630).
    /// </summary>
    private static ReadOnlySpan<byte> FirmwareBody
    {
        get
        {
            return
            [
                0x20, // report id (0x20)
                0x4A, 0x75, 0x6C, 0x20, 0x20, 0x34, 0x20, 0x32, // build date "Jul  4 2"
                0x30, 0x32, 0x35, 0x31, 0x30, 0x3A, 0x33, 0x38, // build date "025" and build time "10:38"
                0x3A, 0x34, 0x30, 0x03, 0x00, 0x0B, 0x00, 0x11, // build time ":40", hardware type 0x03, marker 0x0B
                0x08, 0x00, 0x00, 0x2A, 0x00, 0x10, 0x01, 0x00, // hardware version 0x00000811 (offsets 24-27)
                0x28, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // firmware version bytes and reserved fields
                0x00, 0x00, 0x00, 0x30, 0x06, 0x00, 0x00, 0x01, // update version 0x0630 (offsets 44-45)
                0x00, 0x03, 0x00, 0x10, 0x10, 0x03, 0x00, 0x06,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            ];
        }
    }
}

/// <summary>
/// Snapshot of the persona metadata the feature reports answer from, built by
/// <see cref="DualSenseDevice"/> under its state lock. Every field is a resolved value: the device
/// carries the identity's MAC and serial number until a metadata update replaces them, and the
/// sub-command and the Edge handshake payload stay empty until the host writes them.
/// </summary>
public readonly struct DualSenseFeatureReportState
{
    /// <summary>
    /// Initializes a state with every field at its default: no MAC address, no serial number, the
    /// base persona and no recorded factory sub-command or Edge handshake payload.
    /// </summary>
    public DualSenseFeatureReportState()
    {
    }

    /// <summary>
    /// Controller MAC address, six bytes in address order (the pairing report serves them
    /// reversed); empty leaves the pairing report's MAC region zero.
    /// </summary>
    public byte[] MacAddress { get; init; } = [];

    /// <summary>Serial number served by the command response; empty serves a zero region.</summary>
    public string SerialNumber { get; init; } = string.Empty;

    /// <summary>Whether the persona serves the Edge variant (rewrites the firmware controller marker byte).</summary>
    public bool EdgePersona { get; init; }

    /// <summary>Factory sub-command of the last 0x80 command the host wrote; zero when none was recorded.</summary>
    public byte CommandSubcommand { get; init; }

    /// <summary>
    /// Edge handshake payload (63 bytes) of the last 0x65 write, or <see langword="null"/> when the
    /// host never wrote one; the handshake report echoes the firmware body in that case.
    /// </summary>
    public byte[]? EdgeHandshake { get; init; }
}