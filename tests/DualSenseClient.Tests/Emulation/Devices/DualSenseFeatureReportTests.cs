using System.Buffers.Binary;
using System.Text;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.DualSense;

namespace DualSenseClient.Tests.Emulation.Devices;

/// <summary>
/// Pins the DualSense feature-report table (report ids, fixed lengths and their key fields), the
/// persona device's GET_REPORT/SET_REPORT feature paths and the runtime metadata merge. Lengths and
/// ids: the descriptor declares report id 5 with "Report Count (40)", report id 9 with 19, report id
/// 32 (0x20) with 63 and report id 129 (0x81) with 63, matching the kernel's declared sizes 41/20/64
/// (docs/01-DualSense-HID-Protocol-Implementation.md, section 8). Bodies are traceable per report;
/// the firmware echo is the reference's own 63-byte body, the calibration values are the behavior
/// oracle's defaults (no code copied) and the pairing body honors the identity's MAC. Feature ids
/// the persona does not serve stall through the T5 engine, exactly like the reference stalls an id
/// its controller-backed cache never filled.
/// </summary>
[TestFixture]
public sealed class DualSenseFeatureReportTests
{
    /// <summary>HID class request code of GET_REPORT (HID 1.11, section 7.2.1).</summary>
    private const byte RequestGetReport = 0x01;

    /// <summary>HID class request code of SET_REPORT (HID 1.11, section 7.2.2).</summary>
    private const byte RequestSetReport = 0x09;

    /// <summary>Interface number of the persona's HID function (the audio function occupies interfaces 0-2).</summary>
    private const byte HidInterfaceNumber = 3;

    /// <summary>Device-to-host class interface request (direction IN, type class, recipient interface).</summary>
    private const byte ClassInterfaceIn = 0xA1;

    /// <summary>Host-to-device class interface request (direction OUT, type class, recipient interface).</summary>
    private const byte ClassInterfaceOut = 0x21;

    /// <summary>Report id of the motion-sensor calibration feature report (0x05).</summary>
    private const byte CalibrationReportId = 0x05;

    /// <summary>Reference length in bytes of the calibration report: report id plus a 40-byte payload.</summary>
    private const int CalibrationSize = 41;

    /// <summary>Report id of the BT pairing-info feature report (0x09).</summary>
    private const byte PairingReportId = 0x09;

    /// <summary>Reference length in bytes of the pairing report: report id plus a 19-byte payload.</summary>
    private const int PairingSize = 20;

    /// <summary>Report id of the firmware/hardware-info feature report (0x20).</summary>
    private const byte FirmwareReportId = 0x20;

    /// <summary>Reference length in bytes of the firmware report: report id plus a 63-byte payload.</summary>
    private const int FirmwareSize = 64;

    /// <summary>Report id of the factory-command feature report (0x80; write-only).</summary>
    private const byte CommandReportId = 0x80;

    /// <summary>Report id of the factory command-response feature report (0x81).</summary>
    private const byte CommandResponseReportId = 0x81;

    /// <summary>Reference length in bytes of the command-response report: report id plus a 63-byte payload.</summary>
    private const int CommandResponseSize = 64;

    /// <summary>Report id of the DualShock 4 pairing-info feature report ("Report ID (18)"); no DualSense source declares it.</summary>
    private const byte Ds4PairingReportId = 0x12;

    /// <summary>Report id of the second hardware-info page the reference descriptor declares but no static body of the reference fills (0x22).</summary>
    private const byte HardwareInfoPage2ReportId = 0x22;

    /// <summary>Report id of the first Edge profile snapshot slot (0x70); the Edge descriptor declares the slots 0x70-0x7B.</summary>
    private const byte EdgeProfileReportId = 0x70;

    /// <summary>Report offset of the first calibration word (17 little-endian int16 fields follow).</summary>
    private const int OffsetCalibrationFirstWord = 1;

    /// <summary>Number of calibration words of the reference layout.</summary>
    private const int CalibrationWordCount = 17;

    /// <summary>Report offset of the calibration trailer byte the reference body carries.</summary>
    private const int OffsetCalibrationTrailer = 35;

    /// <summary>Calibration trailer byte of the behavior-oracle body (the kernel reads no field there).</summary>
    private const byte CalibrationTrailer = 0x0B;

    /// <summary>Report offset of the first MAC byte of the pairing body (six bytes in reverse address order).</summary>
    private const int OffsetPairingMac = 1;

    /// <summary>Report offset of the pairing body's first fixed marker byte ("Hard08" of the in-repo client's parse comment).</summary>
    private const int OffsetPairingHardMarker0 = 7;

    /// <summary>Report offset of the pairing body's second fixed marker byte ("Hard25").</summary>
    private const int OffsetPairingHardMarker1 = 8;

    /// <summary>First fixed marker byte of the pairing body.</summary>
    private const byte PairingHardMarker0 = 0x08;

    /// <summary>Second fixed marker byte of the pairing body.</summary>
    private const byte PairingHardMarker1 = 0x25;

    /// <summary>Report offset of the firmware body's build date string (11 ASCII bytes).</summary>
    private const int OffsetFirmwareBuildDate = 1;

    /// <summary>Report offset of the firmware body's build time string (8 ASCII bytes).</summary>
    private const int OffsetFirmwareBuildTime = 12;

    /// <summary>Report offset of the firmware body's hardware type byte.</summary>
    private const int OffsetFirmwareHardwareType = 20;

    /// <summary>Report offset of the firmware body's controller marker byte (0x44 identifies a DualSense Edge).</summary>
    private const int OffsetFirmwareControllerMarker = 22;

    /// <summary>Report offset of the firmware body's hardware version word (u32 LE).</summary>
    private const int OffsetFirmwareHardwareVersion = 24;

    /// <summary>Report offset of the firmware body's firmware version word (u32 LE).</summary>
    private const int OffsetFirmwareVersion = 28;

    /// <summary>Report offset of the firmware body's update version word (u16 LE).</summary>
    private const int OffsetFirmwareUpdateVersion = 44;

    /// <summary>Hardware type byte of the reference firmware body.</summary>
    private const byte FirmwareHardwareType = 0x03;

    /// <summary>Marker byte the reference reads to classify a DualSense Edge ("Connected DSE Controller").</summary>
    private const byte DseControllerMarker = 0x44;

    /// <summary>Marker byte of the reference firmware body itself at the controller marker offset.</summary>
    private const byte BaseControllerMarker = 0x0B;

    /// <summary>Hardware version word of the reference firmware body.</summary>
    private const uint FirmwareHardwareVersion = 0x00000811;

    /// <summary>Firmware version word of the reference firmware body.</summary>
    private const uint FirmwareFirmwareVersion = 0x0110002A;

    /// <summary>Update version word of the reference firmware body.</summary>
    private const ushort FirmwareUpdateVersion = 0x0630;

    /// <summary>Report offset of the command-response body's serial region (18 ASCII bytes).</summary>
    private const int OffsetCommandResponseSerial = 3;

    /// <summary>Length in bytes of the command-response serial region.</summary>
    private const int CommandResponseSerialLength = 18;

    /// <summary>Report offset of the command-response body's status byte.</summary>
    private const int OffsetCommandResponseStatus = 4;

    /// <summary>Marker byte the command response carries as its first payload byte.</summary>
    private const byte CommandResponseMarker = 0x01;

    /// <summary>Status byte of the status sub-command response.</summary>
    private const byte CommandResponseStatus = 0x01;

    /// <summary>Factory sub-command selecting the serial-number response.</summary>
    private const byte SerialSubcommand = 0x01;

    /// <summary>Factory sub-command selecting the status response.</summary>
    private const byte StatusSubcommand = 0x03;

    /// <summary>Sub-command id no reference branch handles.</summary>
    private const byte UnknownSubcommand = 0x9F;

    /// <summary>Product id of the reference Edge persona (the Edge constructor resolves it).</summary>
    private const ushort EdgeProductId = 0x0DF2;

    /// <summary>Report id of the Edge factory handshake report (0x65; declared by the Edge descriptor only).</summary>
    private const byte EdgeHandshakeReportId = 0x65;

    /// <summary>Reference length in bytes of the Edge handshake report: report id plus a 63-byte payload.</summary>
    private const int EdgeHandshakeSize = 64;

    /// <summary>Payload length in bytes of an Edge handshake write the device stores.</summary>
    private const int EdgeHandshakePayloadLength = 63;

    /// <summary>Report id of the lightbar-setup feature report the Edge descriptor widens (0xF2).</summary>
    private const byte LightbarSetupReportId = 0xF2;

    /// <summary>Declared length of the lightbar-setup report on the 321-byte base descriptor (report count 15).</summary>
    private const int BaseLightbarSetupSize = 16;

    /// <summary>Declared length of the lightbar-setup report on the 437-byte Edge descriptor (report count 52).</summary>
    private const int EdgeLightbarSetupSize = 53;

    /// <summary>Number of feature reports the 321-byte base descriptor declares.</summary>
    private const int BaseDeclaredReportCount = 26;

    /// <summary>Number of feature reports the 437-byte Edge descriptor declares.</summary>
    private const int EdgeDeclaredReportCount = 45;

    /// <summary>Report offset of the input report's report sequence counter (the T5 packet counter).</summary>
    private const int OffsetInputPacketCounter = 7;

    /// <summary>Report offset of the input report's battery/power byte.</summary>
    private const int OffsetInputBattery = 53;

    /// <summary>Battery byte of the reference identity's neutral state (percent 0x0A, power state Complete).</summary>
    private const byte ReferenceBatteryByte = 0x2A;

    /// <summary>Address-order MAC of the custom-identity tests.</summary>
    private static readonly byte[] CustomMac = [0xA5, 0xFA, 0x9C, 0xCF, 0x92, 0x00];

    /// <summary>The identity serial as served by the command response (ASCII, zero-padded to the 18-byte region).</summary>
    private static readonly byte[] IdentitySerialRegion = "T6-SERIAL-000001\0\0"u8.ToArray();

    /// <summary>The updated serial as served by the command response (ASCII, zero-padded to the 18-byte region).</summary>
    private static readonly byte[] UpdatedSerialRegion = "META-SERIAL-42\0\0\0\0"u8.ToArray();

    /// <summary>Serial number string of the custom-identity tests (17 characters, the response region holds 18).</summary>
    private const string IdentitySerial = "T6-SERIAL-000001";

    /// <summary>Serial number a metadata update installs (14 characters).</summary>
    private const string UpdatedSerial = "META-SERIAL-42";

    /// <summary>The identity serial as served in the 18-byte command-response serial region (ASCII, zero-padded).</summary>
    private const string IdentitySerialPadded = "T6-SERIAL-000001\0\0";

    /// <summary>The updated serial as served in the 18-byte command-response serial region (ASCII, zero-padded).</summary>
    private const string UpdatedSerialPadded = "META-SERIAL-42\0\0\0\0";

    [Test]
    public void GetReport_Calibration_ServesTheReferenceLengthAndBody()
    {
        DualSenseDevice device = CreateDevice();

        ControlResult result = GetFeature(device, CalibrationReportId, CalibrationSize);

        Assert.That(result.Count, Is.EqualTo(CalibrationSize));
        Assert.That(result.Data[0], Is.EqualTo(CalibrationReportId));
        short[] expectedWords =
        [
            0, 0, 0, // gyro pitch/yaw/roll bias
            8192, -8192, 8192, -8192, 8192, -8192, // the six pitch/yaw/roll plus/minus words
            500, 500, // gyro speed plus/minus
            8192, -8192, 8192, -8192, 8192, -8192 // the six accelerometer plus/minus words
        ];
        for (int i = 0; i < CalibrationWordCount; i++)
        {
            short word = BinaryPrimitives.ReadInt16LittleEndian(result.Data.AsSpan(OffsetCalibrationFirstWord + i * 2));
            Assert.That(word, Is.EqualTo(expectedWords[i]), $"calibration word {i}");
        }

        Assert.That(result.Data[OffsetCalibrationTrailer], Is.EqualTo(CalibrationTrailer));
        Assert.That(result.Data[(OffsetCalibrationTrailer + 1)..CalibrationSize], Is.EqualTo(new byte[CalibrationSize - OffsetCalibrationTrailer - 1]));
    }

    [Test]
    public void GetReport_Pairing_ServesTheIdentityMacReversedAndTheFixedMarkers()
    {
        DualSenseDevice device = CreateDevice(new DualSenseIdentity
        {
            MacAddress = CustomMac
        });

        ControlResult result = GetFeature(device, PairingReportId, PairingSize);

        Assert.That(result.Count, Is.EqualTo(PairingSize));
        Assert.That(result.Data[0], Is.EqualTo(PairingReportId));
        // The controller serves the address in reverse order: report bytes 6..1 spell the address
        // (the in-repo client composes $"{report[6]}:...:{report[1]}"), so the identity's address-order
        // bytes land reversed here.
        byte[] expectedMac = [.. CustomMac.Reverse()];
        Assert.That(result.Data[OffsetPairingMac..(OffsetPairingMac + 6)], Is.EqualTo(expectedMac));
        Assert.That(result.Data[OffsetPairingHardMarker0], Is.EqualTo(PairingHardMarker0));
        Assert.That(result.Data[OffsetPairingHardMarker1], Is.EqualTo(PairingHardMarker1));
        Assert.That(result.Data[(OffsetPairingHardMarker1 + 1)..PairingSize], Is.EqualTo(new byte[PairingSize - OffsetPairingHardMarker1 - 1]));
    }

    [Test]
    public void GetReport_Pairing_DefaultIdentity_ServesTheZeroMac()
    {
        DualSenseDevice device = CreateDevice();

        ControlResult result = GetFeature(device, PairingReportId, PairingSize);

        Assert.That(result.Count, Is.EqualTo(PairingSize));
        Assert.That(result.Data[OffsetPairingMac..(OffsetPairingMac + 6)], Is.EqualTo(new byte[6]));
        Assert.That(result.Data[OffsetPairingHardMarker0], Is.EqualTo(PairingHardMarker0));
        Assert.That(result.Data[OffsetPairingHardMarker1], Is.EqualTo(PairingHardMarker1));
    }

    [Test]
    public void GetReport_Firmware_ServesTheReferenceBodyByteForByte()
    {
        DualSenseDevice device = CreateDevice();

        ControlResult result = GetFeature(device, FirmwareReportId, FirmwareSize);

        Assert.That(result.Count, Is.EqualTo(FirmwareSize));
        Assert.That(result.Data[0], Is.EqualTo(FirmwareReportId));
        Assert.That(result.Data[1..FirmwareSize], Is.EqualTo(ReferenceFirmwareBody.ToArray()));
        Assert.That(result.Data[OffsetFirmwareBuildDate..OffsetFirmwareBuildTime], Is.EqualTo("Jul  4 2025"u8.ToArray()));
        Assert.That(result.Data[OffsetFirmwareBuildTime..OffsetFirmwareHardwareType], Is.EqualTo("10:38:40"u8.ToArray()));
        Assert.That(result.Data[OffsetFirmwareHardwareType], Is.EqualTo(FirmwareHardwareType));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(result.Data.AsSpan(OffsetFirmwareHardwareVersion)), Is.EqualTo(FirmwareHardwareVersion));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(result.Data.AsSpan(OffsetFirmwareVersion)), Is.EqualTo(FirmwareFirmwareVersion));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Data.AsSpan(OffsetFirmwareUpdateVersion)), Is.EqualTo(FirmwareUpdateVersion));
    }

    [Test]
    public void GetReport_Firmware_EdgePersona_CarriesTheDseMarkerAndTheEdgeDescriptors()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity(), true);

        ControlResult result = GetFeature(device, FirmwareReportId, FirmwareSize);

        Assert.That(result.Count, Is.EqualTo(FirmwareSize));
        Assert.That(result.Data[OffsetFirmwareControllerMarker], Is.EqualTo(DseControllerMarker)); // the reference classifies this as a DualSense Edge
        Assert.That(device.Descriptors.IdProduct, Is.EqualTo(EdgeProductId)); // the Edge factory supersedes the identity's product id
        Assert.That(result.Data[OffsetFirmwareHardwareType], Is.EqualTo(FirmwareHardwareType)); // the rest of the body is shared
        Assert.That(result.Data[OffsetFirmwareBuildDate..OffsetFirmwareBuildTime], Is.EqualTo("Jul  4 2025"u8.ToArray()));
    }

    [Test]
    public void GetReport_Firmware_BasePersona_CarriesTheReferenceMarkerByte()
    {
        DualSenseDevice device = CreateDevice();

        ControlResult result = GetFeature(device, FirmwareReportId, FirmwareSize);

        Assert.That(result.Data[OffsetFirmwareControllerMarker], Is.EqualTo(BaseControllerMarker)); // the reference body's own byte, not the Edge marker
    }

    [Test]
    public void GetReport_EveryDescriptorDeclaredId_AnswersItsDeclaredLength()
    {
        DualSenseDevice basePersona = CreateDevice();
        List<(byte ReportId, int PayloadCount)> baseDeclared =
            ExtractDeclaredFeatureReports(basePersona.Descriptors.HidReportDescriptor!.Value.Span);

        Assert.That(baseDeclared, Has.Count.EqualTo(BaseDeclaredReportCount)); // guards the parser against a silent miss
        AssertDeclaredSurface(basePersona, baseDeclared, false);

        DualSenseDevice edgePersona = new DualSenseDevice(new DualSenseIdentity(), true);
        List<(byte ReportId, int PayloadCount)> edgeDeclared =
            ExtractDeclaredFeatureReports(edgePersona.Descriptors.HidReportDescriptor!.Value.Span);

        Assert.That(edgeDeclared, Has.Count.EqualTo(EdgeDeclaredReportCount));
        AssertDeclaredSurface(edgePersona, edgeDeclared, true);
    }

    [TestCase(0x08, 48)]
    [TestCase(0x0A, 27)]
    [TestCase(0x0B, 42)]
    [TestCase(0x0C, 42)]
    [TestCase(0x21, 5)]
    [TestCase(HardwareInfoPage2ReportId, 64)] // the second hardware-info page: 64-byte zero stub
    [TestCase(CommandReportId, 64)] // the write-only factory command reads back as a zero stub
    [TestCase(0x82, 10)]
    [TestCase(0x83, 64)]
    [TestCase(0x84, 64)]
    [TestCase(0x85, 3)]
    [TestCase(0xA0, 2)]
    [TestCase(0xE0, 64)]
    [TestCase(0xF0, 64)]
    [TestCase(0xF1, 64)]
    [TestCase(LightbarSetupReportId, BaseLightbarSetupSize)]
    [TestCase(0xF4, 64)]
    [TestCase(0xF5, 4)]
    [TestCase(0xF6, 64)]
    [TestCase(0xF7, 64)]
    [TestCase(0xF8, 64)]
    [TestCase(0xF9, 64)]
    public void GetReport_DeclaredButUnfilledIds_ServeZeroStubs(byte reportId, int size)
    {
        DualSenseDevice device = CreateDevice();

        ControlResult result = GetFeature(device, reportId, size);

        Assert.That(result.Count, Is.EqualTo(size));
        Assert.That(result.Data[0], Is.EqualTo(reportId)); // the report id byte must be present for consumers
        Assert.That(result.Data[1..size], Is.EqualTo(new byte[size - 1])); // the payload is a deliberate zero stub
    }

    [Test]
    public void GetReport_EdgeDeclaredIds_ServeZeroStubsAndTheWidenedLightbarReport()
    {
        DualSenseDevice edgePersona = new DualSenseDevice(new DualSenseIdentity(), true);
        List<byte> stubIds = [];
        for (byte reportId = 0x60; reportId <= 0x64; reportId++)
        {
            stubIds.Add(reportId); // profile/function reports (0x65 has the stored-or-firmware echo)
        }

        stubIds.Add(0x68);
        for (byte reportId = EdgeProfileReportId; reportId <= 0x7B; reportId++)
        {
            stubIds.Add(reportId); // profile snapshot slots
        }

        foreach (byte reportId in stubIds)
        {
            ControlResult result = GetFeature(edgePersona, reportId, FirmwareSize);
            Assert.That(result.Count, Is.EqualTo(FirmwareSize), $"declared length of report 0x{reportId:X2}");
            Assert.That(result.Data[0], Is.EqualTo(reportId), $"report id byte of report 0x{reportId:X2}");
            Assert.That(result.Data[1..], Is.EqualTo(new byte[FirmwareSize - 1]), $"zero stub body of report 0x{reportId:X2}");
        }

        ControlResult baseLightbar = GetFeature(CreateDevice(), LightbarSetupReportId, BaseLightbarSetupSize);
        ControlResult edgeLightbar = GetFeature(edgePersona, LightbarSetupReportId, EdgeLightbarSetupSize);

        Assert.That(baseLightbar.Count, Is.EqualTo(BaseLightbarSetupSize)); // the base descriptor declares count 15
        Assert.That(edgeLightbar.Count, Is.EqualTo(EdgeLightbarSetupSize)); // the Edge descriptor widens it to count 52
        Assert.That(edgeLightbar.Data[1..], Is.EqualTo(new byte[EdgeLightbarSetupSize - 1]));
    }

    [Test]
    public void GetReport_EdgeHandshake_WithoutAWrite_EchoesTheFirmwareBody()
    {
        DualSenseDevice edgePersona = new DualSenseDevice(new DualSenseIdentity(), true);

        ControlResult firmware = GetFeature(edgePersona, FirmwareReportId, FirmwareSize);
        ControlResult handshake = GetFeature(edgePersona, EdgeHandshakeReportId, EdgeHandshakeSize);

        Assert.That(handshake.Count, Is.EqualTo(EdgeHandshakeSize));
        Assert.That(handshake.Data[0], Is.EqualTo(EdgeHandshakeReportId)); // served under its own id
        Assert.That(handshake.Data[1..], Is.EqualTo(firmware.Data[1..])); // the echo of the current firmware body
        Assert.That(handshake.Data[OffsetFirmwareControllerMarker], Is.EqualTo(DseControllerMarker)); // the Edge marker is part of it
    }

    [Test]
    public void SetReport_EdgeHandshake_ThenResponse_EchoesTheStoredPayload()
    {
        DualSenseDevice edgePersona = new DualSenseDevice(new DualSenseIdentity(), true);
        byte[] payload = CreateEdgeHandshakePayload();

        int acknowledged = SetFeature(edgePersona, EdgeHandshakeReportId, payload);
        ControlResult handshake = GetFeature(edgePersona, EdgeHandshakeReportId, EdgeHandshakeSize);

        Assert.That(acknowledged, Is.Zero);
        Assert.That(handshake.Count, Is.EqualTo(EdgeHandshakeSize));
        Assert.That(handshake.Data[0], Is.EqualTo(EdgeHandshakeReportId));
        Assert.That(handshake.Data[1..], Is.EqualTo(payload)); // the stored payload replaces the firmware echo verbatim
    }

    [Test]
    public void SetReport_EdgeHandshake_OnTheBasePersona_IsAcknowledgedWithoutBeingServed()
    {
        DualSenseDevice basePersona = CreateDevice();

        int acknowledged = SetFeature(basePersona, EdgeHandshakeReportId, CreateEdgeHandshakePayload());
        ControlResult handshake = GetFeature(basePersona, EdgeHandshakeReportId, EdgeHandshakeSize);

        Assert.That(acknowledged, Is.Zero); // every feature write is acknowledged
        Assert.That(handshake.Count, Is.EqualTo(-1)); // the base descriptor does not declare 0x65
    }

    [Test]
    public void SetReport_TruncatedEdgeHandshake_AcknowledgedWithoutStoring()
    {
        DualSenseDevice edgePersona = new DualSenseDevice(new DualSenseIdentity(), true);

        int acknowledged = SetFeature(edgePersona, EdgeHandshakeReportId, new byte[EdgeHandshakePayloadLength - 1]);
        ControlResult firmware = GetFeature(edgePersona, FirmwareReportId, FirmwareSize);
        ControlResult handshake = GetFeature(edgePersona, EdgeHandshakeReportId, EdgeHandshakeSize);

        Assert.That(acknowledged, Is.Zero);
        Assert.That(handshake.Data[1..], Is.EqualTo(firmware.Data[1..])); // still the firmware echo
    }

    [Test]
    public void GetReport_CommandResponse_WithoutACommand_ServesTheUnknownSubcommandMarker()
    {
        DualSenseDevice device = CreateDevice();

        ControlResult result = GetFeature(device, CommandResponseReportId, CommandResponseSize);

        Assert.That(result.Count, Is.EqualTo(CommandResponseSize));
        Assert.That(result.Data[0], Is.EqualTo(CommandResponseReportId));
        Assert.That(result.Data[1], Is.EqualTo(CommandResponseMarker));
        Assert.That(result.Data[2..CommandResponseSize], Is.EqualTo(new byte[CommandResponseSize - 2]));
    }

    [Test]
    public void SetReport_SerialSubcommand_ThenResponse_CarriesTheIdentitySerial()
    {
        DualSenseDevice device = CreateDevice(new DualSenseIdentity
        {
            SerialNumber = IdentitySerial
        });

        int acknowledged = SetFeature(device, CommandReportId, [SerialSubcommand, 0x00]);
        ControlResult result = GetFeature(device, CommandResponseReportId, CommandResponseSize);

        Assert.That(acknowledged, Is.Zero); // the reference acknowledges every feature write
        Assert.That(result.Data[0], Is.EqualTo(CommandResponseReportId));
        Assert.That(result.Data[OffsetCommandResponseSerial..(OffsetCommandResponseSerial + CommandResponseSerialLength)],
            Is.EqualTo(IdentitySerialRegion));
    }

    [Test]
    public void SetReport_StatusSubcommand_ServesTheStatusBytes()
    {
        DualSenseDevice device = CreateDevice();

        int acknowledged = SetFeature(device, CommandReportId, [StatusSubcommand, 0x00]);
        ControlResult result = GetFeature(device, CommandResponseReportId, CommandResponseSize);

        Assert.That(acknowledged, Is.Zero);
        Assert.That(result.Data[1], Is.EqualTo(CommandResponseMarker));
        Assert.That(result.Data[OffsetCommandResponseStatus], Is.EqualTo(CommandResponseStatus));
        Assert.That(result.Data[5..CommandResponseSize], Is.EqualTo(new byte[CommandResponseSize - 5]));
    }

    [Test]
    public void SetReport_UnknownSubcommand_ServesTheUnknownSubcommandMarker()
    {
        DualSenseDevice device = CreateDevice();

        int acknowledged = SetFeature(device, CommandReportId, [UnknownSubcommand, 0x42]);
        ControlResult result = GetFeature(device, CommandResponseReportId, CommandResponseSize);

        Assert.That(acknowledged, Is.Zero);
        Assert.That(result.Data[1], Is.EqualTo(CommandResponseMarker));
        Assert.That(result.Data[OffsetCommandResponseStatus], Is.Zero);
        Assert.That(result.Data[OffsetCommandResponseSerial..(OffsetCommandResponseSerial + CommandResponseSerialLength)],
            Is.EqualTo(new byte[CommandResponseSerialLength]));
    }

    [Test]
    public void SetReport_TruncatedCommandPayload_AcknowledgedWithoutRecordingASubcommand()
    {
        // The reference's write path records the two payload bytes after the report id; a shorter
        // payload is acknowledged and changes nothing, so the response keeps the no-command marker.
        DualSenseDevice device = CreateDevice(new DualSenseIdentity
        {
            SerialNumber = IdentitySerial
        });

        int acknowledged = SetFeature(device, CommandReportId, [SerialSubcommand]);
        ControlResult result = GetFeature(device, CommandResponseReportId, CommandResponseSize);

        Assert.That(acknowledged, Is.Zero);
        Assert.That(result.Data[1], Is.EqualTo(CommandResponseMarker));
        Assert.That(result.Data[OffsetCommandResponseSerial..(OffsetCommandResponseSerial + CommandResponseSerialLength)],
            Is.EqualTo(new byte[CommandResponseSerialLength]));
    }

    [TestCase(CalibrationReportId)]
    [TestCase(PairingReportId)]
    [TestCase(FirmwareReportId)]
    [TestCase(CommandResponseReportId)]
    [TestCase(0x60)]
    [TestCase(0x99)]
    public void SetReport_FeatureWrites_AreAcknowledged(byte reportId)
    {
        // The reference's class SET_REPORT callback (and the behavior oracle's feature-write branch)
        // accepts every feature write; only the 0x80 factory command carries observable state.
        DualSenseDevice device = CreateDevice();

        int acknowledged = SetFeature(device, reportId, [0x11, 0x22]);

        Assert.That(acknowledged, Is.Zero);
    }

    [TestCase(Ds4PairingReportId)] // the DS4 pairing-info id (Ds4Pdo "Report ID (18)"); no DualSense descriptor declares it
    [TestCase(0x60)] // an Edge profile id: the base descriptor does not declare it
    [TestCase(EdgeHandshakeReportId)] // the Edge handshake id: the base descriptor does not declare it
    [TestCase(0x99)] // no descriptor declares it
    [TestCase((byte)0x00)]
    public void GetReport_UnservedFeatureIds_StallWithoutThrowing(byte reportId)
    {
        DualSenseDevice device = CreateDevice();
        byte[] buffer = new byte[FirmwareSize];

        ControlResult result = Send(device, ClassInterfaceIn, RequestGetReport, (ushort)(0x0300 | reportId), (ushort)buffer.Length);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => Send(device, ClassInterfaceIn, RequestGetReport, (ushort)(0x0300 | reportId), (ushort)buffer.Length), Throws.Nothing);
            Assert.That(result.Count, Is.EqualTo(-1));
        }

        Assert.That(result.Data, Is.EqualTo(new byte[FirmwareSize])); // a stalled request writes nothing
    }

    [Test]
    public void GetReport_ShortRequest_ClampsToTheRequestedLength()
    {
        DualSenseDevice device = CreateDevice(new DualSenseIdentity
        {
            MacAddress = CustomMac
        });
        ControlResult full = GetFeature(device, PairingReportId, PairingSize);

        ControlResult clamped = GetFeature(device, PairingReportId, 7);

        Assert.That(clamped.Count, Is.EqualTo(7));
        Assert.That(clamped.Data[..7], Is.EqualTo(full.Data[..7]));
    }

    [Test]
    public void SetMetaState_SerialUpdate_IsServedByTheCommandResponse()
    {
        DualSenseDevice device = CreateDevice();
        SetFeature(device, CommandReportId, [SerialSubcommand, 0x00]);

        device.SetMetaState(new DualSenseMetaState
        {
            SerialNumber = UpdatedSerial
        });
        ControlResult result = GetFeature(device, CommandResponseReportId, CommandResponseSize);

        Assert.That(result.Data[OffsetCommandResponseSerial..(OffsetCommandResponseSerial + CommandResponseSerialLength)],
            Is.EqualTo(UpdatedSerialRegion));
    }

    [Test]
    public void SetMetaState_MacUpdate_IsServedByThePairingReport()
    {
        DualSenseDevice device = CreateDevice();

        device.SetMetaState(new DualSenseMetaState
        {
            MacAddress = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06]
        });
        ControlResult result = GetFeature(device, PairingReportId, PairingSize);

        Assert.That(result.Data[OffsetPairingMac..(OffsetPairingMac + 6)], Is.EqualTo(new byte[]
        {
            0x06, 0x05, 0x04, 0x03, 0x02, 0x01
        }));
    }

    [Test]
    public void SetMetaState_BatteryUpdate_ReachesTheInputReportAndKeepsUntouchedMetadata()
    {
        DualSenseDevice device = CreateDevice(new DualSenseIdentity
        {
            MacAddress = CustomMac,
            SerialNumber = IdentitySerial
        });
        SetFeature(device, CommandReportId, [SerialSubcommand, 0x00]);

        device.SetMetaState(new DualSenseMetaState
        {
            BatteryLevel = 0x05,
            ChargingState = DualSenseChargingState.Charging
        });
        byte[] input = new byte[DualSenseInputReport.Size];
        device.ProvideInputReport(input);
        ControlResult pairing = GetFeature(device, PairingReportId, PairingSize);
        ControlResult command = GetFeature(device, CommandResponseReportId, CommandResponseSize);

        Assert.That(input[OffsetInputBattery], Is.EqualTo(0x15)); // percent 5, power state Charging in the high nibble
        Assert.That(pairing.Data[OffsetPairingMac..(OffsetPairingMac + 6)], Is.EqualTo(CustomMac.Reverse().ToArray())); // the untouched identity MAC
        Assert.That(command.Data[OffsetCommandResponseSerial..(OffsetCommandResponseSerial + CommandResponseSerialLength)],
            Is.EqualTo(IdentitySerialRegion)); // the untouched identity serial
    }

    [Test]
    public void SetMetaState_SerialOnlyUpdate_PreservesTheBatteryOfAnEarlierBatteryUpdate()
    {
        DualSenseDevice device = CreateDevice();
        device.SetMetaState(new DualSenseMetaState
        {
            BatteryLevel = 0x07,
            ChargingState = DualSenseChargingState.Discharging
        });

        device.SetMetaState(new DualSenseMetaState
        {
            SerialNumber = UpdatedSerial
        });
        byte[] input = new byte[DualSenseInputReport.Size];
        device.ProvideInputReport(input);
        SetFeature(device, CommandReportId, [SerialSubcommand, 0x00]);
        ControlResult command = GetFeature(device, CommandResponseReportId, CommandResponseSize);

        Assert.That(input[OffsetInputBattery], Is.EqualTo(0x07)); // the earlier battery update survives the serial-only merge
        Assert.That(command.Data[OffsetCommandResponseSerial..(OffsetCommandResponseSerial + CommandResponseSerialLength)],
            Is.EqualTo(UpdatedSerialRegion));
    }

    [Test]
    public void SetMetaState_EmptyPatch_ChangesNothing()
    {
        DualSenseDevice device = CreateDevice(new DualSenseIdentity
        {
            MacAddress = CustomMac,
            SerialNumber = IdentitySerial
        });
        SetFeature(device, CommandReportId, [SerialSubcommand, 0x00]);

        device.SetMetaState(default);
        byte[] input = new byte[DualSenseInputReport.Size];
        device.ProvideInputReport(input);
        ControlResult pairing = GetFeature(device, PairingReportId, PairingSize);
        ControlResult command = GetFeature(device, CommandResponseReportId, CommandResponseSize);

        Assert.That(input[OffsetInputBattery], Is.EqualTo(ReferenceBatteryByte));
        Assert.That(pairing.Data[OffsetPairingMac..(OffsetPairingMac + 6)], Is.EqualTo(CustomMac.Reverse().ToArray()));
        Assert.That(command.Data[OffsetCommandResponseSerial..(OffsetCommandResponseSerial + CommandResponseSerialLength)],
            Is.EqualTo(IdentitySerialRegion));
    }

    [Test]
    public void SetMetaState_EmptySerial_KeepsTheCurrentSerial()
    {
        // The behavior oracle merges only non-empty strings, so an empty value keeps the current serial.
        DualSenseDevice device = CreateDevice(new DualSenseIdentity
        {
            SerialNumber = IdentitySerial
        });
        SetFeature(device, CommandReportId, [SerialSubcommand, 0x00]);

        device.SetMetaState(new DualSenseMetaState
        {
            SerialNumber = string.Empty
        });
        ControlResult command = GetFeature(device, CommandResponseReportId, CommandResponseSize);

        Assert.That(command.Data[OffsetCommandResponseSerial..(OffsetCommandResponseSerial + CommandResponseSerialLength)],
            Is.EqualTo(IdentitySerialRegion));
    }

    [Test]
    public void SetMetaState_FollowingSetInput_ReportsTheInputStatesBattery()
    {
        // The battery bytes live in the input state (T5); SetInput asserts every field, so the most
        // recent writer wins and a metadata battery update is superseded by a later SetInput.
        DualSenseDevice device = CreateDevice();
        device.SetMetaState(new DualSenseMetaState
        {
            BatteryLevel = 0x05,
            ChargingState = DualSenseChargingState.Charging
        });

        device.SetInput(CreateNeutralState());
        byte[] input = new byte[DualSenseInputReport.Size];
        device.ProvideInputReport(input);

        Assert.That(input[OffsetInputBattery], Is.EqualTo(ReferenceBatteryByte));
    }

    [Test]
    public void SetMetaState_ConcurrentWithFeatureReads_ServesOneCompleteSerialSnapshot()
    {
        DualSenseDevice device = CreateDevice(new DualSenseIdentity
        {
            SerialNumber = IdentitySerial
        });
        SetFeature(device, CommandReportId, [SerialSubcommand, 0x00]);
        const string first = "CCCCCCCCCCCCCCCCCC";
        const string second = "DDDDDDDDDDDDDDDDDD";
        const int iterations = 200;
        List<string> observed = [];
        using Barrier start = new Barrier(2);
        Task writer = Task.Run(() =>
        {
            start.SignalAndWait();
            for (int i = 0; i < iterations; i++)
            {
                device.SetMetaState(new DualSenseMetaState
                {
                    SerialNumber = (i & 1) == 0 ? first : second
                });
            }
        });

        start.SignalAndWait();
        for (int i = 0; i < iterations; i++)
        {
            byte[] body = GetFeature(device, CommandResponseReportId, CommandResponseSize).Data;
            observed.Add(Encoding.ASCII.GetString(body, OffsetCommandResponseSerial, CommandResponseSerialLength));
        }

        Task.WaitAll(writer);

        Assert.That(observed, Is.Not.Empty);
        foreach (string serial in observed)
        {
            Assert.That(serial, Is.EqualTo(first).Or.EqualTo(second).Or.EqualTo(IdentitySerialPadded)); // never a torn or stale mix
        }
    }

    [Test]
    public void FeatureReads_LeaveTheInputReportCountersAndBodyUntouched()
    {
        DualSenseDevice device = CreateDevice();
        byte[] before = new byte[DualSenseInputReport.Size];
        device.ProvideInputReport(before);

        GetFeature(device, CalibrationReportId, CalibrationSize);
        GetFeature(device, PairingReportId, PairingSize);
        GetFeature(device, FirmwareReportId, FirmwareSize);
        GetFeature(device, CommandResponseReportId, CommandResponseSize);
        Send(device, ClassInterfaceIn, RequestGetReport, 0x0300 | Ds4PairingReportId, FirmwareSize);

        byte[] after = new byte[DualSenseInputReport.Size];
        device.ProvideInputReport(after);

        Assert.That(before[OffsetInputPacketCounter], Is.Zero);
        Assert.That(after[OffsetInputPacketCounter], Is.EqualTo(1)); // the feature reads consumed no input-report counter
        Assert.That(after[..OffsetInputPacketCounter], Is.EqualTo(before[..OffsetInputPacketCounter]));
        Assert.That(after[(OffsetInputPacketCounter + 1)..], Is.EqualTo(before[(OffsetInputPacketCounter + 1)..]));
    }

    [Test]
    public void FeatureReads_RepeatReads_ReturnTheSameBody()
    {
        DualSenseDevice device = CreateDevice(new DualSenseIdentity
        {
            MacAddress = CustomMac
        });

        ControlResult first = GetFeature(device, PairingReportId, PairingSize);
        ControlResult second = GetFeature(device, PairingReportId, PairingSize);

        Assert.That(second.Count, Is.EqualTo(first.Count));
        Assert.That(second.Data, Is.EqualTo(first.Data)); // the feature bodies carry no counters
    }

    [TestCase(CalibrationReportId, CalibrationSize)]
    [TestCase(PairingReportId, PairingSize)]
    [TestCase(FirmwareReportId, FirmwareSize)]
    [TestCase(CommandResponseReportId, CommandResponseSize)]
    public void Table_ServedReportIds_WriteTheirPinnedLengths(byte reportId, int size)
    {
        DualSenseFeatureReportState state = new DualSenseFeatureReportState
        {
            MacAddress = CustomMac,
            SerialNumber = IdentitySerial
        };
        byte[] destination = new byte[size];

        int written = DualSenseFeatureReports.Write(reportId, state, destination);

        Assert.That(written, Is.EqualTo(size));
        Assert.That(destination[0], Is.EqualTo(reportId));
    }

    [TestCase(Ds4PairingReportId)]
    [TestCase(0x60)] // an Edge profile id: undeclared on the base persona
    [TestCase(EdgeHandshakeReportId)] // the Edge handshake id: undeclared on the base persona
    [TestCase((byte)0x00)]
    [TestCase(0x99)]
    public void Table_UnservedReportIds_ReturnStallAndWriteNothing(byte reportId)
    {
        byte[] destination = new byte[FirmwareSize];

        int written = DualSenseFeatureReports.Write(reportId, new DualSenseFeatureReportState(), destination);

        Assert.That(written, Is.EqualTo(-1));
        Assert.That(destination, Is.EqualTo(new byte[FirmwareSize]));
    }

    [Test]
    public void Table_EdgePersona_StubsTheProfileBlockAndWidensTheLightbarReport()
    {
        DualSenseFeatureReportState edgeState = new DualSenseFeatureReportState
        {
            EdgePersona = true
        };
        byte[] profileSlot = new byte[FirmwareSize];
        byte[] baseLightbar = new byte[BaseLightbarSetupSize];
        byte[] edgeLightbar = new byte[EdgeLightbarSetupSize];

        int profileWritten = DualSenseFeatureReports.Write(0x70, edgeState, profileSlot);
        int baseLightbarWritten = DualSenseFeatureReports.Write(LightbarSetupReportId, new DualSenseFeatureReportState(), baseLightbar);
        int edgeLightbarWritten = DualSenseFeatureReports.Write(LightbarSetupReportId, edgeState, edgeLightbar);

        Assert.That(profileWritten, Is.EqualTo(FirmwareSize)); // the profile snapshot slot answers a zero stub
        Assert.That(profileSlot[0], Is.EqualTo((byte)0x70));
        Assert.That(profileSlot[1..], Is.EqualTo(new byte[FirmwareSize - 1]));
        Assert.That(baseLightbarWritten, Is.EqualTo(BaseLightbarSetupSize));
        Assert.That(edgeLightbarWritten, Is.EqualTo(EdgeLightbarSetupSize));
        Assert.That(edgeLightbar[0], Is.EqualTo(LightbarSetupReportId));
        Assert.That(edgeLightbar[1..], Is.EqualTo(new byte[EdgeLightbarSetupSize - 1]));
    }

    [Test]
    public void Table_EdgeHandshake_EchoesTheStoredPayloadOrDefaultsToTheFirmwareBody()
    {
        byte[] payload = CreateEdgeHandshakePayload();
        byte[] defaultEcho = new byte[EdgeHandshakeSize];
        byte[] storedEcho = new byte[EdgeHandshakeSize];
        byte[] expectedDefault = new byte[EdgeHandshakeSize];
        int expectedDefaultWritten = DualSenseFeatureReports.Write(FirmwareReportId, new DualSenseFeatureReportState
        {
            EdgePersona = true
        }, expectedDefault);

        int defaultWritten = DualSenseFeatureReports.Write(EdgeHandshakeReportId, new DualSenseFeatureReportState
        {
            EdgePersona = true
        }, defaultEcho);
        int storedWritten = DualSenseFeatureReports.Write(EdgeHandshakeReportId, new DualSenseFeatureReportState
        {
            EdgePersona = true,
            EdgeHandshake = payload
        }, storedEcho);

        Assert.That(defaultWritten, Is.EqualTo(EdgeHandshakeSize));
        Assert.That(storedWritten, Is.EqualTo(EdgeHandshakeSize));
        Assert.That(expectedDefaultWritten, Is.EqualTo(FirmwareSize));
        Assert.That(defaultEcho[0], Is.EqualTo(EdgeHandshakeReportId));
        Assert.That(defaultEcho[1..], Is.EqualTo(expectedDefault[1..])); // the current firmware body under the 0x65 id
        Assert.That(storedEcho[0], Is.EqualTo(EdgeHandshakeReportId));
        Assert.That(storedEcho[1..], Is.EqualTo(payload)); // the stored payload wins over the echo
    }

    [Test]
    public void Table_ShortMac_LeavesThePairingMacRegionZeroWithoutThrowing()
    {
        // A metadata update may carry a MAC shorter than six bytes; the writer must neither throw
        // nor scatter bytes, so the MAC region stays zero (the device only installs six-byte
        // addresses from the identity, which always carries six).
        DualSenseFeatureReportState state = new DualSenseFeatureReportState
        {
            MacAddress = [0x0A, 0x0B, 0x0C]
        };
        byte[] destination = new byte[PairingSize];

        int written = 0;
        Assert.That(() => written = DualSenseFeatureReports.Write(PairingReportId, state, destination), Throws.Nothing);

        Assert.That(written, Is.EqualTo(PairingSize));
        Assert.That(destination[OffsetPairingMac..(OffsetPairingMac + 6)], Is.EqualTo(new byte[6]));
        Assert.That(destination[OffsetPairingHardMarker0], Is.EqualTo(PairingHardMarker0)); // the rest of the body is unaffected
    }

    [Test]
    public void Table_EdgePersona_RewritesOnlyTheControllerMarker()
    {
        byte[] baseBody = new byte[FirmwareSize];
        byte[] edgeBody = new byte[FirmwareSize];

        DualSenseFeatureReports.Write(FirmwareReportId, new DualSenseFeatureReportState(), baseBody);
        DualSenseFeatureReports.Write(FirmwareReportId, new DualSenseFeatureReportState
        {
            EdgePersona = true
        }, edgeBody);

        List<int> differing = [];
        for (int i = 0; i < FirmwareSize; i++)
        {
            if (baseBody[i] != edgeBody[i])
            {
                differing.Add(i);
            }
        }

        Assert.That(differing, Is.EqualTo(new[]
        {
            OffsetFirmwareControllerMarker
        }));
        Assert.That(edgeBody[OffsetFirmwareControllerMarker], Is.EqualTo(DseControllerMarker));
    }

    /// <summary>Creates the persona device with the reference identity.</summary>
    private static DualSenseDevice CreateDevice() => new DualSenseDevice(new DualSenseIdentity());

    /// <summary>Creates the persona device with <paramref name="identity"/>.</summary>
    private static DualSenseDevice CreateDevice(DualSenseIdentity identity) => new DualSenseDevice(identity);

    /// <summary>Reads one feature report of <paramref name="reportId"/> through a GET_REPORT control transfer.</summary>
    private static ControlResult GetFeature(DualSenseDevice device, byte reportId, int length) =>
        Send(device, ClassInterfaceIn, RequestGetReport, (ushort)(0x0300 | reportId), (ushort)length);

    /// <summary>Writes one feature report of <paramref name="reportId"/> through a SET_REPORT control transfer.</summary>
    private static int SetFeature(DualSenseDevice device, byte reportId, byte[] payload) =>
        Send(device, ClassInterfaceOut, RequestSetReport, (ushort)(0x0300 | reportId), (ushort)payload.Length, payload).Count;

    /// <summary>Sends one control transfer through the public entry point of <paramref name="device"/>.</summary>
    private static ControlResult Send(DualSenseDevice device, byte requestType, byte request, ushort value = 0, ushort length = 0, byte[]? outData = null)
    {
        byte[] buffer = new byte[length];
        UsbSetupPacket setup = new UsbSetupPacket
        {
            RequestType = requestType,
            Request = request,
            Value = value,
            Index = HidInterfaceNumber,
            Length = length
        };

        int count = device.HandleControlRequest(setup, outData, buffer);
        return new ControlResult(count, buffer);
    }

    /// <summary>
    /// Creates the neutral input state of the T5 device tests: centered sticks, released D-pad, the
    /// reference identity's battery defaults (percent 0x0A, power state Complete).
    /// </summary>
    private static DualSenseInputState CreateNeutralState()
    {
        return new DualSenseInputState
        {
            LeftStickX = 128,
            LeftStickY = 128,
            RightStickX = 128,
            RightStickY = 128,
            Dpad = DualSenseDpadDirection.Released,
            BatteryLevel = 0x0A,
            PowerState = DualSenseChargingState.Complete
        };
    }

    /// <summary>Creates a deterministic 63-byte Edge handshake payload (the stored 0x65 echo).</summary>
    private static byte[] CreateEdgeHandshakePayload()
    {
        byte[] payload = new byte[EdgeHandshakePayloadLength];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(0x30 + i);
        }

        return payload;
    }

    /// <summary>
    /// Asserts the table answers every declared feature report of <paramref name="device"/> with its
    /// declared length and report id byte, and with a zero stub body unless the report is one the
    /// table fills with real content (the ids pinned by their own tests).
    /// </summary>
    private static void AssertDeclaredSurface(DualSenseDevice device, List<(byte ReportId, int PayloadCount)> declared, bool edgePersona)
    {
        foreach ((byte reportId, int payloadCount) in declared)
        {
            int size = payloadCount + 1;
            ControlResult result = GetFeature(device, reportId, size);
            Assert.That(result.Count, Is.EqualTo(size), $"declared length of report 0x{reportId:X2}");
            Assert.That(result.Data[0], Is.EqualTo(reportId), $"report id byte of report 0x{reportId:X2}");
            if (IsContentReport(reportId, edgePersona))
            {
                continue;
            }

            Assert.That(result.Data[1..size], Is.EqualTo(new byte[size - 1]), $"zero stub body of report 0x{reportId:X2}");
        }
    }

    /// <summary>Returns whether the table fills <paramref name="reportId"/> with real content.</summary>
    private static bool IsContentReport(byte reportId, bool edgePersona)
    {
        return reportId is CalibrationReportId or PairingReportId or FirmwareReportId or CommandResponseReportId
               || (edgePersona && reportId == EdgeHandshakeReportId);
    }

    /// <summary>
    /// Extracts the (report id, payload count) pair of every Feature main item of a HID report
    /// descriptor by walking its short items (HID 1.11, section 6.2.2: bits 1-0 item size, bits 3-2
    /// item type, bits 7-4 item tag): the current Report ID (global tag 8) and Report Count (global
    /// tag 9, retained across items so blocks that inherit the previous count are covered) apply to
    /// each Feature item (main tag 11, type 0). The persona descriptors use short items only.
    /// </summary>
    private static List<(byte ReportId, int PayloadCount)> ExtractDeclaredFeatureReports(ReadOnlySpan<byte> descriptor)
    {
        const int ItemTypeMain = 0;
        const int ItemTypeGlobal = 1;
        const int ReportIdTag = 8;
        const int ReportCountTag = 9;
        const int FeatureTag = 11;
        List<(byte ReportId, int PayloadCount)> declared = [];
        byte reportId = 0;
        int reportCount = 0;
        int offset = 0;
        while (offset < descriptor.Length)
        {
            byte prefix = descriptor[offset];
            int sizeCode = prefix & 0x03;
            int size = sizeCode == 3 ? 4 : sizeCode;
            int data = 0;
            for (int i = 0; i < size; i++)
            {
                data |= descriptor[offset + 1 + i] << (8 * i);
            }

            int type = (prefix >> 2) & 0x03;
            int tag = prefix >> 4;
            offset += 1 + size;
            if (type == ItemTypeGlobal && tag == ReportIdTag)
            {
                reportId = (byte)data;
            }
            else if (type == ItemTypeGlobal && tag == ReportCountTag)
            {
                reportCount = data;
            }
            else if (type == ItemTypeMain && tag == FeatureTag)
            {
                declared.Add((reportId, reportCount));
            }
        }

        return declared;
    }

    /// <summary>
    /// The 63-byte firmware report body of the reference (<c>report20</c>),
    /// report-id-exclusive; the byte comments mark the fields the tests read back.
    /// </summary>
    private static ReadOnlySpan<byte> ReferenceFirmwareBody
    {
        get
        {
            return
            [
                0x4A, 0x75, 0x6C, 0x20, 0x20, 0x34, 0x20, 0x32, // "Jul  4 2"
                0x30, 0x32, 0x35, 0x31, 0x30, 0x3A, 0x33, 0x38, // "02510:38"
                0x3A, 0x34, 0x30, 0x03, 0x00, 0x0B, 0x00, 0x11, // ":40", hardware type 0x03 and the marker/revision bytes
                0x08, 0x00, 0x00, 0x2A, 0x00, 0x10, 0x01, 0x00, // hardware version 0x00000811
                0x28, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x30, 0x06, 0x00, 0x00, 0x01, // update version 0x0630
                0x00, 0x03, 0x00, 0x10, 0x10, 0x03, 0x00, 0x06,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            ];
        }
    }

    /// <summary>Result of one handled control transfer: bytes written (or -1 for STALL) and the response buffer.</summary>
    private readonly record struct ControlResult(int Count, byte[] Data);
}