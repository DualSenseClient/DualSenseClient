using System.Text;
using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Emulation.Core;

[TestFixture]
public sealed class VirtualUsbDeviceTests
{
    [Test]
    public void HandleControlRequest_GetDescriptorDevice_ReturnsExactDeviceDescriptor()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x80, 0x06, 0x0100, length: 64);

        Assert.That(result.Count, Is.EqualTo(18));
        Assert.That(result.Data[..result.Count], Is.EqualTo(TestDevices.CreateSimpleDeviceDescriptor()));
    }

    [Test]
    public void HandleControlRequest_GetDescriptorConfiguration_ReturnsConfigDescriptorBlob()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x80, 0x06, 0x0200, length: 64);

        Assert.That(result.Count, Is.EqualTo(TestDevices.CreateSimpleConfigDescriptor().Length));
        Assert.That(result.Data[..result.Count], Is.EqualTo(TestDevices.CreateSimpleConfigDescriptor()));
    }

    [Test]
    public void HandleControlRequest_GetDescriptorString_ReturnsUtf16LeStringDescriptor()
    {
        TestVirtualDevice device = CreateDevice();
        string manufacturer = TestDevices.SimpleManufacturer;
        byte[] expected = [(byte)(2 + manufacturer.Length * 2), 0x03, .. Encoding.Unicode.GetBytes(manufacturer)];

        ControlResult result = Send(device, 0x80, 0x06, 0x0301, 0x0409, 64);

        Assert.That(result.Count, Is.EqualTo(expected.Length));
        Assert.That(result.Data[..result.Count], Is.EqualTo(expected));
    }

    [Test]
    public void HandleControlRequest_GetDescriptorStringIndexZero_ReturnsEnglishLanguageId()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x80, 0x06, 0x0300, 0x0409, 64);

        Assert.That(result.Count, Is.EqualTo(4));
        Assert.That(result.Data[..result.Count], Is.EqualTo(new byte[]
        {
            0x04, 0x03, 0x09, 0x04 // bLength 4, STRING, LANGID 0x0409 (US English, little-endian)
        }));
    }

    [Test]
    public void HandleControlRequest_GetDescriptorStringUnknownIndex_ReturnsStall()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x80, 0x06, 0x0304, 0x0409, 64);

        Assert.That(result.Count, Is.EqualTo(-1));
    }

    [Test]
    public void HandleControlRequest_GetDescriptorReport_ReturnsHidReportDescriptor()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x80, 0x06, 0x2200, 0, 64);

        Assert.That(result.Count, Is.EqualTo(TestDevices.CreateSimpleHidReportDescriptor().Length));
        Assert.That(result.Data[..result.Count], Is.EqualTo(TestDevices.CreateSimpleHidReportDescriptor()));
    }

    [Test]
    public void HandleControlRequest_GetDescriptorReportWithoutHidReport_ReturnsStall()
    {
        TestVirtualDevice device = new TestVirtualDevice(UsbDescriptorSet.Create(
            TestDevices.CreateSimpleDeviceDescriptor(),
            TestDevices.CreateSimpleConfigDescriptor(),
            null,
            TestDevices.CreateSimpleStrings()));

        ControlResult result = Send(device, 0x80, 0x06, 0x2200, 0, 64);

        Assert.That(result.Count, Is.EqualTo(-1));
    }

    [Test]
    public void HandleControlRequest_GetDescriptorReportUnknownInterface_ReturnsStall()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x80, 0x06, 0x2200, 5, 64);

        Assert.That(result.Count, Is.EqualTo(-1));
    }

    [Test]
    public void HandleControlRequest_GetDescriptorUnknownType_ReturnsStall()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x80, 0x06, 0x9900, length: 64);

        Assert.That(result.Count, Is.EqualTo(-1));
    }

    [Test]
    public void HandleControlRequest_GetDescriptorWithWrongDirection_ReturnsStall()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x00, 0x06, 0x0100, length: 18);

        Assert.That(result.Count, Is.EqualTo(-1));
    }

    [Test]
    public void HandleControlRequest_GetDescriptorShorterThanPayload_TruncatesResponse()
    {
        TestVirtualDevice device = CreateDevice();
        byte[] response = new byte[64];
        response.AsSpan().Fill(0xEE);

        ControlResult result = Send(device, 0x80, 0x06, 0x0100, length: 8, response: response);

        Assert.That(result.Count, Is.EqualTo(8));
        Assert.That(result.Data[..8], Is.EqualTo(TestDevices.CreateSimpleDeviceDescriptor()[..8]));
        Assert.That(result.Data[8..], Is.All.EqualTo((byte)0xEE));
    }

    [Test]
    public void HandleControlRequest_GetConfigurationBeforeSet_ReturnsZero()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x80, 0x08, length: 1);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result.Data[0], Is.Zero);
    }

    [Test]
    public void HandleControlRequest_SetConfiguration_NotifiesDeviceAndUpdatesGetConfiguration()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult setResult = Send(device, 0x00, 0x09, 1);
        ControlResult getResult = Send(device, 0x80, 0x08, length: 1);

        Assert.That(setResult.Count, Is.Zero);
        Assert.That(device.ConfigurationChanges, Is.EqualTo(new byte[]
        {
            1
        }));
        Assert.That(getResult.Count, Is.EqualTo(1));
        Assert.That(getResult.Data[0], Is.EqualTo((byte)1));
    }

    [Test]
    public void HandleControlRequest_SetAddress_SucceedsWithoutWritingData()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x00, 0x05, 5);

        Assert.That(result.Count, Is.Zero);
    }

    [Test]
    public void HandleControlRequest_GetStatusDevice_ReturnsTwoZeroBytes()
    {
        TestVirtualDevice device = CreateDevice();
        byte[] response = new byte[2];
        response.AsSpan().Fill(0xEE);

        ControlResult result = Send(device, 0x80, 0x00, length: 2, response: response);

        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result.Data, Is.All.Zero);
    }

    [TestCase(0x03)] // SET_FEATURE
    [TestCase(0x01)] // CLEAR_FEATURE
    public void HandleControlRequest_SetOrClearFeatureEndpointHalt_Succeeds(byte request)
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x02, request, 0, TestDevices.SimpleInterruptInAddress);

        Assert.That(result.Count, Is.Zero);
    }

    [Test]
    public void HandleControlRequest_GetInterfaceDefaultSetting_ReturnsZero()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x81, 0x0A, index: 0, length: 1);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result.Data[0], Is.Zero);
    }

    [Test]
    public void HandleControlRequest_SetInterfaceDefaultSetting_Succeeds()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x01, 0x0B, 0, 0);

        Assert.That(result.Count, Is.Zero);
    }

    [Test]
    public void HandleControlRequest_GetInterfaceUnknownInterface_ReturnsStall()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x81, 0x0A, index: 5, length: 1);

        Assert.That(result.Count, Is.EqualTo(-1));
    }

    [Test]
    public void HandleControlRequest_UnknownStandardRequest_ReturnsStall()
    {
        TestVirtualDevice device = CreateDevice();

        ControlResult result = Send(device, 0x80, 0x99, length: 8);

        Assert.That(result.Count, Is.EqualTo(-1));
    }

    [Test]
    public void HandleControlRequest_ClassRequest_RoutesToOnClassRequest()
    {
        TestVirtualDevice device = CreateDevice();
        byte[] payload =
        [
            0x05, 0x01, 0x02
        ];

        ControlResult result = Send(device, 0x21, 0x09, 0x0202, 0, 0, payload);

        Assert.That(result.Count, Is.EqualTo(-1)); // the base class request handler stalls
        Assert.That(device.ClassRequestCount, Is.EqualTo(1));
        Assert.That(device.LastOutData, Is.EqualTo(payload));
    }

    [Test]
    public void HandleControlRequest_ClassResponseLongerThanRequest_TruncatesToRequestLength()
    {
        TestVirtualDevice device = CreateDevice();
        device.ClassResponse =
        [
            0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF
        ];

        ControlResult result = Send(device, 0xA1, 0x01, 0x0100, 0, 4);

        Assert.That(result.Count, Is.EqualTo(4));
        Assert.That(result.Data, Is.EqualTo(new byte[]
        {
            0xAA, 0xBB, 0xCC, 0xDD
        }));
    }

    [Test]
    public void ProvideInputReport_DefaultImplementation_ReturnsZero()
    {
        TestVirtualDevice device = CreateDevice();
        byte[] destination = new byte[8];

        int written = device.ProvideInputReport(destination);

        Assert.That(written, Is.Zero);
    }

    [Test]
    public void OnOutputReport_DefaultImplementation_IgnoresPayload()
    {
        TestVirtualDevice device = CreateDevice();
        byte[] payload =
        [
            0x05
        ];

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);
    }

    /// <summary>Sends one control transfer through the public entry point of <paramref name="device"/>.</summary>
    private static ControlResult Send(TestVirtualDevice device, byte requestType, byte request, ushort value = 0, ushort index = 0,
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

    private static TestVirtualDevice CreateDevice() => new TestVirtualDevice(TestDevices.CreateSimpleDevice());

    /// <summary>Result of one handled control transfer: bytes written (or -1 for STALL) and the response buffer.</summary>
    private readonly record struct ControlResult(int Count, byte[] Data);

    /// <summary>
    /// Test double of <see cref="VirtualUsbDevice"/>: records the configuration changes and class
    /// requests it observes and can serve a canned class response.
    /// </summary>
    private sealed class TestVirtualDevice : VirtualUsbDevice
    {
        public TestVirtualDevice(UsbDescriptorSet descriptors)
            : base(descriptors)
        {
        }

        /// <summary>Configuration values reported by SET_CONFIGURATION, in call order.</summary>
        public List<byte> ConfigurationChanges { get; } = [];

        /// <summary>Number of control requests routed to the class request handler.</summary>
        public int ClassRequestCount { get; private set; }

        /// <summary>Canned class response; <see langword="null"/> keeps the default stall.</summary>
        public byte[]? ClassResponse { get; set; }

        /// <summary>Last host-to-device payload passed to the class request handler.</summary>
        public byte[] LastOutData { get; private set; } = [];

        protected override int OnClassRequest(in UsbSetupPacket setup, ReadOnlySpan<byte> outData, Span<byte> response)
        {
            ClassRequestCount++;
            LastOutData = outData.ToArray();
            if (ClassResponse is null)
            {
                return base.OnClassRequest(setup, outData, response);
            }

            int count = Math.Min(ClassResponse.Length, response.Length);
            ClassResponse.AsSpan(0, count).CopyTo(response);
            return ClassResponse.Length;
        }

        protected override void OnConfigurationChanged(byte configurationValue) => ConfigurationChanges.Add(configurationValue);
    }
}