using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Emulation.Core;

[TestFixture]
public sealed class VirtualHidDeviceTests
{
    /// <summary>HID class request code of GET_REPORT (HID 1.11, section 7.2.1).</summary>
    private const byte RequestGetReport = 0x01;

    /// <summary>HID class request code of GET_IDLE.</summary>
    private const byte RequestGetIdle = 0x02;

    /// <summary>HID class request code of GET_PROTOCOL.</summary>
    private const byte RequestGetProtocol = 0x03;

    /// <summary>HID class request code of SET_REPORT.</summary>
    private const byte RequestSetReport = 0x09;

    /// <summary>HID class request code of SET_IDLE.</summary>
    private const byte RequestSetIdle = 0x0A;

    /// <summary>HID class request code of SET_PROTOCOL.</summary>
    private const byte RequestSetProtocol = 0x0B;

    /// <summary>Device-to-host class interface request (direction IN, type class, recipient interface).</summary>
    private const byte ClassInterfaceIn = 0xA1;

    /// <summary>Host-to-device class interface request (direction OUT, type class, recipient interface).</summary>
    private const byte ClassInterfaceOut = 0x21;

    [Test]
    public void HandleControlRequest_GetReportInput_ReturnsDeviceReport()
    {
        TestHidDevice device = CreateDevice();

        ControlResult result = Send(device, ClassInterfaceIn, RequestGetReport, 0x0101, length: 4);

        Assert.That(result.Count, Is.EqualTo(4));
        Assert.That(result.Data[..4], Is.EqualTo(device.InputReport));
    }

    [Test]
    public void HandleControlRequest_GetReportInputWithoutReport_ReturnsStall()
    {
        TestHidDevice device = CreateDevice();
        device.InputReport = null;

        ControlResult result = Send(device, ClassInterfaceIn, RequestGetReport, 0x0101, length: 4);

        Assert.That(result.Count, Is.EqualTo(-1));
    }

    [Test]
    public void HandleControlRequest_GetReportFeature_ReturnsFeatureReport()
    {
        TestHidDevice device = CreateDevice();

        ControlResult result = Send(device, ClassInterfaceIn, RequestGetReport, 0x0305, length: 2);

        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result.Data[..2], Is.EqualTo(device.FeatureReport));
        Assert.That(device.ReportedFeatureIds, Is.EqualTo(new byte[]
        {
            0x05
        }));
    }

    [Test]
    public void HandleControlRequest_GetReportFeatureUnknownId_ReturnsStall()
    {
        TestHidDevice device = CreateDevice();

        ControlResult result = Send(device, ClassInterfaceIn, RequestGetReport, 0x0309, length: 2);

        Assert.That(result.Count, Is.EqualTo(-1));
        Assert.That(device.ReportedFeatureIds, Is.EqualTo(new byte[]
        {
            0x09
        }));
    }

    [Test]
    public void HandleControlRequest_GetReportOutputType_ReturnsStall()
    {
        TestHidDevice device = CreateDevice();

        ControlResult result = Send(device, ClassInterfaceIn, RequestGetReport, 0x0205, length: 4);

        Assert.That(result.Count, Is.EqualTo(-1));
    }

    [Test]
    public void HandleControlRequest_SetReportOutput_ReachesOnOutputReport()
    {
        TestHidDevice device = CreateDevice();
        byte[] payload = CreateOutputPayload(32);

        ControlResult result = Send(device, ClassInterfaceOut, RequestSetReport, 0x0205, length: 32, outData: payload);

        Assert.That(result.Count, Is.Zero);
        Assert.That(device.OutputReports, Has.Count.EqualTo(1));
        Assert.That(device.OutputReports[0], Is.EqualTo(new byte[]
        {
            0x05
        }.Concat(payload).ToArray()));
    }

    [Test]
    public void HandleControlRequest_SetReportOutputReportIdZero_PassesPayloadUnchanged()
    {
        TestHidDevice device = CreateDevice();
        byte[] payload = CreateOutputPayload(8);

        ControlResult result = Send(device, ClassInterfaceOut, RequestSetReport, 0x0200, length: 8, outData: payload);

        Assert.That(result.Count, Is.Zero);
        Assert.That(device.OutputReports, Has.Count.EqualTo(1));
        Assert.That(device.OutputReports[0], Is.EqualTo(payload));
    }

    [Test]
    public void HandleControlRequest_SetReportOutputTruncated_ReturnsStallWithoutCallingDevice()
    {
        TestHidDevice device = CreateDevice();
        byte[] payload = CreateOutputPayload(2);

        ControlResult result = Send(device, ClassInterfaceOut, RequestSetReport, 0x0205, length: 32, outData: payload);

        Assert.That(result.Count, Is.EqualTo(-1));
        Assert.That(device.OutputReports, Is.Empty);
    }

    [Test]
    public void HandleControlRequest_SetReportOutputLargerThanRequest_TrimsToRequestLength()
    {
        TestHidDevice device = CreateDevice();
        byte[] payload = CreateOutputPayload(12);

        ControlResult result = Send(device, ClassInterfaceOut, RequestSetReport, 0x0205, length: 8, outData: payload);

        Assert.That(result.Count, Is.Zero);
        Assert.That(device.OutputReports, Has.Count.EqualTo(1));
        Assert.That(device.OutputReports[0], Is.EqualTo(new byte[]
        {
            0x05
        }.Concat(payload[..8]).ToArray()));
    }

    [Test]
    public void HandleControlRequest_SetReportFeature_ReachesOnFeatureReport()
    {
        TestHidDevice device = CreateDevice();
        byte[] payload =
        [
            0xAA, 0xBB, 0xCC, 0xDD
        ];

        ControlResult result = Send(device, ClassInterfaceOut, RequestSetReport, 0x0305, length: 4, outData: payload);

        Assert.That(result.Count, Is.Zero);
        Assert.That(device.FeatureWrites, Has.Count.EqualTo(1));
        Assert.That(device.FeatureWrites[0].ReportId, Is.EqualTo((byte)0x05));
        Assert.That(device.FeatureWrites[0].Data, Is.EqualTo(payload));
    }

    [Test]
    public void HandleControlRequest_SetReportFeatureUnknownId_ReturnsStall()
    {
        TestHidDevice device = CreateDevice();
        byte[] payload =
        [
            0xAA, 0xBB, 0xCC, 0xDD
        ];

        ControlResult result = Send(device, ClassInterfaceOut, RequestSetReport, 0x0309, length: 4, outData: payload);

        Assert.That(result.Count, Is.EqualTo(-1));
        Assert.That(device.ReportedFeatureIds, Is.EqualTo(new byte[]
        {
            0x09
        }));
        Assert.That(device.FeatureWrites, Is.Empty);
    }

    [Test]
    public void HandleControlRequest_SetReportFeatureTruncated_ReturnsStallWithoutCallingDevice()
    {
        TestHidDevice device = CreateDevice();
        byte[] payload =
        [
            0xAA
        ];

        ControlResult result = Send(device, ClassInterfaceOut, RequestSetReport, 0x0305, length: 4, outData: payload);

        Assert.That(result.Count, Is.EqualTo(-1));
        Assert.That(device.FeatureWrites, Is.Empty);
    }

    [Test]
    public void HandleControlRequest_GetIdle_ReturnsZeroIdleRate()
    {
        TestHidDevice device = CreateDevice();

        ControlResult result = Send(device, ClassInterfaceIn, RequestGetIdle, 0x0000, length: 1, response: [0xEE]);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result.Data[0], Is.Zero);
    }

    [Test]
    public void HandleControlRequest_SetIdle_Succeeds()
    {
        TestHidDevice device = CreateDevice();

        ControlResult result = Send(device, ClassInterfaceOut, RequestSetIdle, 0x2000);

        Assert.That(result.Count, Is.Zero);
    }

    [Test]
    public void HandleControlRequest_GetProtocol_ReturnsReportProtocol()
    {
        TestHidDevice device = CreateDevice();

        ControlResult result = Send(device, ClassInterfaceIn, RequestGetProtocol, length: 1, response: [0xEE]);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result.Data[0], Is.EqualTo((byte)1));
    }

    [Test]
    public void HandleControlRequest_SetProtocol_Succeeds()
    {
        TestHidDevice device = CreateDevice();

        ControlResult result = Send(device, ClassInterfaceOut, RequestSetProtocol, 1);

        Assert.That(result.Count, Is.Zero);
    }

    [TestCase(ClassInterfaceOut, RequestGetReport)] // GET_REPORT must be device-to-host
    [TestCase(ClassInterfaceIn, RequestSetReport)] // SET_REPORT must be host-to-device
    [TestCase(ClassInterfaceOut, RequestGetIdle)] // GET_IDLE must be device-to-host
    [TestCase(ClassInterfaceIn, RequestSetIdle)] // SET_IDLE must be host-to-device
    [TestCase(ClassInterfaceOut, RequestGetProtocol)] // GET_PROTOCOL must be device-to-host
    [TestCase(ClassInterfaceIn, RequestSetProtocol)] // SET_PROTOCOL must be host-to-device
    public void HandleControlRequest_WrongDirection_ReturnsStall(byte requestType, byte request)
    {
        TestHidDevice device = CreateDevice();

        ControlResult result = Send(device, requestType, request, length: 4);

        Assert.That(result.Count, Is.EqualTo(-1));
    }

    [Test]
    public void HandleControlRequest_VendorTypeRequest_ReturnsStall()
    {
        TestHidDevice device = CreateDevice();

        ControlResult result = Send(device, 0x41, RequestSetIdle);

        Assert.That(result.Count, Is.EqualTo(-1));
    }

    [TestCase(0xA0, RequestGetReport)] // class request addressed to the device
    [TestCase(0x20, RequestSetReport)] // class request addressed to the device
    public void HandleControlRequest_DeviceRecipient_ReturnsStall(byte requestType, byte request)
    {
        TestHidDevice device = CreateDevice();

        ControlResult result = Send(device, requestType, request, length: 4);

        Assert.That(result.Count, Is.EqualTo(-1));
    }

    [Test]
    public void HandleControlRequest_UnknownInterface_ReturnsStall()
    {
        TestHidDevice device = CreateDevice();

        ControlResult result = Send(device, ClassInterfaceIn, RequestGetProtocol, index: 5, length: 1);

        Assert.That(result.Count, Is.EqualTo(-1));
    }

    [Test]
    public void HandleControlRequest_InterfaceThatIsNotHid_ReturnsStall()
    {
        TestHidDevice device = new TestHidDevice(CreateNonHidDevice());

        ControlResult result = Send(device, ClassInterfaceIn, RequestGetProtocol, length: 1);

        Assert.That(result.Count, Is.EqualTo(-1));
    }

    /// <summary>Sends one control transfer through the public entry point of <paramref name="device"/>.</summary>
    private static ControlResult Send(TestHidDevice device, byte requestType, byte request, ushort value = 0, ushort index = 0,
        ushort length = 0, byte[]? outData = null, byte[]? response = null)
    {
        byte[] buffer = response ?? new byte[length];
        UsbSetupPacket setup = new UsbSetupPacket
        {
            RequestType = requestType,
            Request = request,
            Value = value,
            Index = index,
            Length = length
        };

        int count = device.HandleControlRequest(setup, outData, buffer);
        return new ControlResult(count, buffer);
    }

    private static TestHidDevice CreateDevice() => new TestHidDevice(TestDevices.CreateSimpleDevice());

    /// <summary>
    /// Creates the simple device with its interface class changed from HID to audio: class requests
    /// addressed to a non-HID interface must stall.
    /// </summary>
    private static UsbDescriptorSet CreateNonHidDevice()
    {
        byte[] config = TestDevices.CreateSimpleConfigDescriptor();
        config[14] = 0x01; // bInterfaceClass of the first interface descriptor (offset 9 + 5)

        return UsbDescriptorSet.Create(
            TestDevices.CreateSimpleDeviceDescriptor(),
            config,
            TestDevices.CreateSimpleHidReportDescriptor(),
            TestDevices.CreateSimpleStrings());
    }

    /// <summary>Creates a deterministic output report payload of <paramref name="length"/> bytes.</summary>
    private static byte[] CreateOutputPayload(int length)
    {
        byte[] payload = new byte[length];
        for (int i = 0; i < length; i++)
        {
            payload[i] = (byte)(0x40 + i);
        }

        return payload;
    }

    /// <summary>Result of one handled control transfer: bytes written (or -1 for STALL) and the response buffer.</summary>
    private readonly record struct ControlResult(int Count, byte[] Data);

    /// <summary>
    /// Test double of <see cref="VirtualHidDevice"/>: serves canned input and feature reports and
    /// records the output and feature payloads it observes.
    /// </summary>
    private sealed class TestHidDevice : VirtualHidDevice
    {
        public TestHidDevice(UsbDescriptorSet descriptors)
            : base(descriptors)
        {
        }

        /// <summary>Report served by <see cref="VirtualHidDevice.ProvideInputReport"/>; <see langword="null"/> serves none.</summary>
        public byte[]? InputReport { get; set; } =
        [
            0x11, 0x22, 0x33, 0x44
        ];

        /// <summary>Report id the device answers feature requests for.</summary>
        public byte FeatureReportId { get; set; } = 0x05;

        /// <summary>Payload served for <see cref="FeatureReportId"/>.</summary>
        public byte[] FeatureReport { get; set; } =
        [
            0xAA, 0xBB
        ];

        /// <summary>Report ids of the feature requests (reads and writes) the device observed, in call order.</summary>
        public List<byte> ReportedFeatureIds { get; } = [];

        /// <summary>Payloads delivered through <see cref="VirtualHidDevice.OnOutputReport"/>.</summary>
        public List<byte[]> OutputReports { get; } = [];

        /// <summary>Feature report writes the device accepted, in call order.</summary>
        public List<(byte ReportId, byte[] Data)> FeatureWrites { get; } = [];

        public override int ProvideInputReport(Span<byte> destination)
        {
            if (InputReport is null)
            {
                return 0;
            }

            int count = Math.Min(InputReport.Length, destination.Length);
            InputReport.AsSpan(0, count).CopyTo(destination);
            return count;
        }

        public override void OnOutputReport(ReadOnlySpan<byte> data) => OutputReports.Add(data.ToArray());

        protected override int ProvideFeatureReport(byte reportId, Span<byte> destination)
        {
            ReportedFeatureIds.Add(reportId);
            if (reportId != FeatureReportId)
            {
                return -1;
            }

            int count = Math.Min(FeatureReport.Length, destination.Length);
            FeatureReport.AsSpan(0, count).CopyTo(destination);
            return count;
        }

        protected override bool OnFeatureReport(byte reportId, ReadOnlySpan<byte> data)
        {
            ReportedFeatureIds.Add(reportId);
            if (reportId != FeatureReportId)
            {
                return false;
            }

            FeatureWrites.Add((reportId, data.ToArray()));
            return true;
        }
    }
}