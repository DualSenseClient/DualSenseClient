using DualSenseClient.Controllers.Devices;
using DualSenseClient.Hid;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.DualSense;

public class DualSenseDeviceProfileTests
{
    private const string ClientMac = "AA:BB:CC:DD:EE:FF";

    private sealed class CapturingHidDevice : IHidDevice
    {
        public byte[]? LastWrite { get; private set; }

        public byte[] PairingReport { get; init; } = [];

        public ushort VendorId => 0x054C;

        public ushort ProductId => 0x0CE6;

        public string DevicePath => "test";

        public bool IsConnected => true;

        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count)
        {
            LastWrite = (byte[])buffer.Clone();
            return count;
        }

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) => reportId == 0x09 ? PairingReport : [];

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "Test";

        public void Dispose()
        {
        }
    }

    private sealed class StubHidDeviceInfo(ConnectionType busType) : IHidDeviceInfo
    {
        public string Path => "test";

        public ushort VendorId => 0x054C;

        public ushort ProductId => 0x0CE6;

        public string ProductName => "DualSense Test";

        public string Manufacturer => "Sony";

        public int InterfaceNumber => 0;

        public ushort UsagePage => 1;

        public HidUsageId Usage => HidUsageId.GamePad;

        public ConnectionType BusType => busType;
    }

    private static byte[] CreatePairingReport(string clientMac)
    {
        // Client MAC is stored little-endian at bytes 1-6 of the 0x09 report.
        byte[] report = new byte[20];
        report[0] = 0x09;
        string[] octets = clientMac.Split(':');
        for (int i = 0; i < 6; i++)
        {
            report[1 + i] = Convert.ToByte(octets[5 - i], 16);
        }
        return report;
    }

    [Test]
    public void Constructor_DoesNotApplyAnyProfile()
    {
        // Profile application is deferred to the owning application (via ApplyProfile),
        // so constructing a device must not write any output state.
        CapturingHidDevice hid = new CapturingHidDevice { PairingReport = CreatePairingReport(ClientMac) };
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        Assert.That(hid.LastWrite, Is.Null);
    }

    [Test]
    public void ApplyProfile_WritesProfileStateToController()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        Profile profile = new Profile
        {
            Name = "Test",
            Lightbar = { Red = 0x01, Green = 0x02, Blue = 0x03 },
            MicLed = { Mode = 2 },
            PlayerLeds = { Mask = 0x1F }
        };
        device.ApplyProfile(profile);

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite, Is.Not.Null);
            Assert.That(hid.LastWrite![9], Is.EqualTo(2));
            Assert.That(hid.LastWrite[44], Is.EqualTo(0x1F));
            Assert.That(hid.LastWrite[45], Is.EqualTo(0x01));
            Assert.That(hid.LastWrite[46], Is.EqualTo(0x02));
            Assert.That(hid.LastWrite[47], Is.EqualTo(0x03));
        });
    }
}