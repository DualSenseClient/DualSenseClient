using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Tests.Emulation.Devices;

/// <summary>
/// Pins the DualShock 4 static feature-report table and the persona's GET_REPORT path: the four
/// anchors both variants serve (0x02 calibration, 0x12 pairing, 0x81 controller MAC and 0xA3
/// firmware) answer their pinned lengths and bodies, a short request clamps to the requested
/// length, and every other declared or unknown id stalls. The 0x02/0xA3 bodies are the fixed
/// reference responses; the 0x12/0x81 MAC order and marker bytes follow the reference's Bluetooth
/// and main paths. A virtual persona has no physical controller to proxy, so the fixed bodies stand
/// in for both variants. The host-MAC region of 0x12 stays zero because a virtual persona has no
/// host adapter address.
/// </summary>
[TestFixture]
public sealed class DualShock4FeatureReportTests
{
    /// <summary>HID class request code of GET_REPORT (HID 1.11, section 7.2.1).</summary>
    private const byte RequestGetReport = 0x01;

    /// <summary>Interface number of the persona's HID function (the audio function occupies interfaces 0-2).</summary>
    private const byte HidInterfaceNumber = 3;

    /// <summary>Device-to-host class interface request (direction IN, type class, recipient interface).</summary>
    private const byte ClassInterfaceIn = 0xA1;

    /// <summary>Address-order MAC of the custom-identity tests.</summary>
    private static readonly byte[] CustomMac = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06];

    /// <summary>Creates the persona device with <paramref name="variant"/> and the custom MAC identity.</summary>
    private static DualShock4Device CreateDevice(DualShock4Variant variant) =>
        new DualShock4Device(new DualShock4Identity
        {
            MacAddress = CustomMac
        }, null, variant);

    /// <summary>Reads one feature report of <paramref name="reportId"/> through a GET_REPORT control transfer.</summary>
    private static ControlResult GetFeature(DualShock4Device device, byte reportId, int length) =>
        Send(device, ClassInterfaceIn, RequestGetReport, (ushort)(0x0300 | reportId), (ushort)length);

    [TestCase(DualShock4Variant.V1)]
    [TestCase(DualShock4Variant.V2)]
    public void GetReport_Anchors_ServeTheirPinnedLengthsOnBothVariants(DualShock4Variant variant)
    {
        DualShock4Device device = CreateDevice(variant);

        Assert.Multiple(() =>
        {
            Assert.That(GetFeature(device, 0x02, 37).Count, Is.EqualTo(37));
            Assert.That(GetFeature(device, 0x12, 16).Count, Is.EqualTo(16));
            Assert.That(GetFeature(device, 0x81, 7).Count, Is.EqualTo(7));
            Assert.That(GetFeature(device, 0xA3, 49).Count, Is.EqualTo(49));
        });
    }

    [Test]
    public void GetReport_Calibration_ServesTheFixedBody()
    {
        DualShock4Device device = CreateDevice(DualShock4Variant.V2);

        ControlResult result = GetFeature(device, 0x02, 37);

        Assert.That(result.Count, Is.EqualTo(37));
        Assert.That(result.Data, Is.EqualTo(new byte[]
        {
            0x02, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x87, 0x22, 0x7B, 0xDD, 0xB2, 0x22, 0x47, 0xDD, 0xBD, 0x22, 0x43, 0xDD, 0x1C, 0x02, 0x1C, 0x02, 0x7F,
            0x1E, 0x2E, 0xDF, 0x60, 0x1F, 0x4C, 0xE0, 0x3A, 0x1D, 0xC6, 0xDE, 0x08, 0x00
        }));
    }

    [Test]
    public void GetReport_Firmware_ServesTheFixedBody()
    {
        DualShock4Device device = CreateDevice(DualShock4Variant.V2);

        ControlResult result = GetFeature(device, 0xA3, 49);

        Assert.That(result.Count, Is.EqualTo(49));
        Assert.That(result.Data, Is.EqualTo(new byte[]
        {
            0xA3, 0x41, 0x75, 0x67, 0x20, 0x20, 0x33, 0x20, 0x32, 0x30, 0x31, 0x33, 0x00, 0x00, 0x00, 0x00, 0x00, 0x30, 0x37, 0x3A, 0x30, 0x31, 0x3A, 0x31,
            0x32, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x31, 0x03, 0x00, 0x00, 0x00, 0x49, 0x00, 0x05, 0x00, 0x00, 0x80, 0x03, 0x00
        }));
    }

    [Test]
    public void GetReport_Pairing_CarriesTheReversedControllerMacAndZeroHostMac()
    {
        DualShock4Device device = CreateDevice(DualShock4Variant.V2);

        ControlResult result = GetFeature(device, 0x12, 16);

        Assert.That(result.Count, Is.EqualTo(16));
        Assert.That(result.Data, Is.EqualTo(new byte[]
        {
            0x12, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, 0x08, 0x25, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        }));
    }

    [Test]
    public void GetReport_MacAddress_ServesTheReversedControllerMac()
    {
        DualShock4Device device = CreateDevice(DualShock4Variant.V2);

        ControlResult result = GetFeature(device, 0x81, 7);

        Assert.That(result.Count, Is.EqualTo(7));
        Assert.That(result.Data, Is.EqualTo(new byte[]
        {
            0x81, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01
        }));
    }

    [Test]
    public void GetReport_UnservedDeclaredId_Stalls()
    {
        // 0x04 is declared with Report Count 36 but is not one of the four anchors.
        DualShock4Device device = CreateDevice(DualShock4Variant.V2);

        ControlResult result = GetFeature(device, 0x04, 64);

        Assert.That(result.Count, Is.EqualTo(-1));
        Assert.That(result.Data, Is.EqualTo(new byte[64])); // a stalled request writes nothing
    }

    [Test]
    public void GetReport_UnknownId_Stalls()
    {
        DualShock4Device device = CreateDevice(DualShock4Variant.V2);

        ControlResult result = GetFeature(device, 0x99, 64);

        Assert.That(result.Count, Is.EqualTo(-1));
        Assert.That(result.Data, Is.EqualTo(new byte[64]));
    }

    [Test]
    public void GetReport_ShortRequest_ClampsToTheRequestedLength()
    {
        DualShock4Device device = CreateDevice(DualShock4Variant.V2);
        ControlResult full = GetFeature(device, 0x02, 37);

        ControlResult clamped = GetFeature(device, 0x02, 8);

        Assert.That(clamped.Count, Is.EqualTo(8));
        Assert.That(clamped.Data[..8], Is.EqualTo(full.Data[..8]));
    }

    [Test]
    public void GetReport_RepeatedReads_ReturnTheSameBody()
    {
        DualShock4Device device = CreateDevice(DualShock4Variant.V2);

        ControlResult first = GetFeature(device, 0x12, 16);
        ControlResult second = GetFeature(device, 0x12, 16);

        Assert.That(second.Count, Is.EqualTo(first.Count));
        Assert.That(second.Data, Is.EqualTo(first.Data)); // the feature bodies carry no counters
    }

    /// <summary>Sends one control transfer through the public entry point of <paramref name="device"/>.</summary>
    private static ControlResult Send(DualShock4Device device, byte requestType, byte request, ushort value = 0, ushort length = 0, byte[]? outData = null)
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

    /// <summary>Result of one handled control transfer: bytes written (or -1 for STALL) and the response buffer.</summary>
    private readonly record struct ControlResult(int Count, byte[] Data);
}