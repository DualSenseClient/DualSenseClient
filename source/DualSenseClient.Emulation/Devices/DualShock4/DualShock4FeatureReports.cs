// Source of the DualShock 4 static feature-report table:
// - The 0x02 calibration (37 bytes, report id plus Report Count 36) and 0xA3 firmware (49 bytes,
//   report id plus Report Count 48) bodies are the fixed v1 reference responses. A virtual persona
//   has no physical DS4 to proxy, so these fixed bodies stand in for both the v1 and the v2
//   controller.
// - The 0x12 pairing (16 bytes, Report Count 15) and 0x81 controller-MAC (7 bytes, Report Count 6)
//   bodies follow the v2 reference: the controller address is served in reverse order at bytes 1..6
//   and the pairing body carries the fixed markers 0x08/0x25 at bytes 7/8. The host-MAC region of
//   0x12 stays zero because a virtual persona has no host adapter address.
// - Both variants serve 0x81 even though the v1 reference's switch omits it: both descriptors
//   declare it and the v2 reference serves it.
// - The descriptors declare more feature ids (for example 0x04 with Report Count 36), but the table
//   serves only the four anchors above and returns -1 (STALL) for every other id.

namespace DualSenseClient.Emulation.Devices.DualShock4;

/// <summary>
/// Static feature-report table of the DualShock 4 persona: maps a report id to its pinned reference
/// length and writes the report body. The table serves the calibration report 0x02 (37 bytes), the
/// pairing report 0x12 (16 bytes), the controller-MAC report 0x81 (7 bytes) and the firmware report
/// 0xA3 (49 bytes) on both variants; every other report id, including the declared 0x04, returns -1
/// (STALL). The table is pure: it answers from the supplied address-order MAC and never mutates it
/// or the device.
/// </summary>
public static class DualShock4FeatureReports
{
    /// <summary>Value returned for a report id the table does not serve (STALL).</summary>
    private const int Stall = -1;

    /// <summary>Report id of the motion-sensor calibration feature report (descriptor report 2).</summary>
    public const byte CalibrationReportId = 0x02;

    /// <summary>Reference length in bytes of the calibration report: report id plus a 36-byte payload.</summary>
    public const int CalibrationSize = 37;

    /// <summary>Report id of the BT pairing-info feature report (descriptor report 18).</summary>
    public const byte PairingReportId = 0x12;

    /// <summary>Reference length in bytes of the pairing report: report id plus a 15-byte payload.</summary>
    public const int PairingSize = 16;

    /// <summary>Report id of the controller-MAC feature report (descriptor report 129).</summary>
    public const byte MacAddressReportId = 0x81;

    /// <summary>Reference length in bytes of the controller-MAC report: report id plus a 6-byte payload.</summary>
    public const int MacAddressSize = 7;

    /// <summary>Report id of the firmware/hardware-info feature report (descriptor report 163).</summary>
    public const byte FirmwareReportId = 0xA3;

    /// <summary>Reference length in bytes of the firmware report: report id plus a 48-byte payload.</summary>
    public const int FirmwareSize = 49;

    /// <summary>Length in bytes of a controller MAC address.</summary>
    public const int MacAddressLength = 6;

    /// <summary>
    /// Writes the feature report of <paramref name="reportId"/> to <paramref name="destination"/>:
    /// the four anchors answer their fixed bodies with <paramref name="macAddress"/> reversed into
    /// the pairing/MAC regions, and every other id returns -1 (STALL) without writing. The report is
    /// clamped to <paramref name="destination"/> (a short request receives the report prefix), and
    /// nothing is written beyond it.
    /// </summary>
    /// <param name="reportId">Report id of the requested feature report (wValue low byte).</param>
    /// <param name="macAddress">Controller MAC address in address order; shorter values leave the MAC region zero.</param>
    /// <param name="destination">Buffer the report body is written to.</param>
    /// <returns>The number of bytes written (including the report id byte), or -1 for STALL.</returns>
    public static int Write(byte reportId, ReadOnlySpan<byte> macAddress, Span<byte> destination)
    {
        ReadOnlySpan<byte> body = reportId switch
        {
            CalibrationReportId => CalibrationBody,
            PairingReportId => PairingBody,
            MacAddressReportId => MacAddressBody,
            FirmwareReportId => FirmwareBody,
            _ => default
        };
        if (body.IsEmpty)
        {
            return Stall;
        }

        Span<byte> response = stackalloc byte[FirmwareSize];
        body.CopyTo(response);
        if (reportId is PairingReportId or MacAddressReportId && macAddress.Length >= MacAddressLength)
        {
            for (int i = 0; i < MacAddressLength; i++)
            {
                response[1 + i] = macAddress[MacAddressLength - 1 - i];
            }
        }

        int count = Math.Min(body.Length, destination.Length);
        response[..count].CopyTo(destination);
        return count;
    }

    /// <summary>Fixed 37-byte calibration response of the v1 reference, report id first.</summary>
    private static ReadOnlySpan<byte> CalibrationBody
    {
        get
        {
            return
            [
                0x02, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x87,
                0x22, 0x7B, 0xDD, 0xB2, 0x22, 0x47, 0xDD, 0xBD,
                0x22, 0x43, 0xDD, 0x1C, 0x02, 0x1C, 0x02, 0x7F,
                0x1E, 0x2E, 0xDF, 0x60, 0x1F, 0x4C, 0xE0, 0x3A,
                0x1D, 0xC6, 0xDE, 0x08, 0x00
            ];
        }
    }

    /// <summary>Pairing body: zero controller-MAC region, the fixed 0x08/0x25 markers and a zero host-MAC region.</summary>
    private static ReadOnlySpan<byte> PairingBody
    {
        get
        {
            return
            [
                0x12, 0, 0, 0, 0, 0, 0, 0x08, 0x25, 0x00, 0, 0, 0, 0, 0, 0
            ];
        }
    }

    /// <summary>Controller-MAC body: zero MAC region the writer fills with the reversed address.</summary>
    private static ReadOnlySpan<byte> MacAddressBody
    {
        get
        {
            return
            [
                0x81, 0, 0, 0, 0, 0, 0
            ];
        }
    }

    /// <summary>Fixed 49-byte firmware response of the v1 reference, report id first.</summary>
    private static ReadOnlySpan<byte> FirmwareBody
    {
        get
        {
            return
            [
                0xA3, 0x41, 0x75, 0x67, 0x20, 0x20, 0x33, 0x20,
                0x32, 0x30, 0x31, 0x33, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x30, 0x37, 0x3A, 0x30, 0x31, 0x3A, 0x31,
                0x32, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x01, 0x00, 0x31, 0x03, 0x00, 0x00,
                0x00, 0x49, 0x00, 0x05, 0x00, 0x00, 0x80, 0x03,
                0x00
            ];
        }
    }
}