using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense;

public class DualSenseDongleStatusTests
{
    [Test]
    public void Constructor_DongleConnection_ReadsDongleStatus()
    {
        FakeHidDevice hid = new FakeHidDevice();

        using DualSenseDongle device = new DualSenseDongle(hid, new FakeHidDeviceInfo(ConnectionType.Usb));

        Assert.Multiple(() =>
        {
            Assert.That(hid.RequestedFeatureIds, Is.EqualTo(new byte[]
            {
                0x09, 0x20, 0x05, 0xF8, 0xF9
            }));
            Assert.That(device.DongleStatus, Is.Not.Null);
            Assert.That(device.DongleStatus!.Value.FirmwareVersion, Is.EqualTo("dev"));
            Assert.That(device.DongleStatus.Value.RssiDb, Is.EqualTo(-46));
            Assert.That(device.DongleStatus.Value.HasAudioFlags, Is.True);
            Assert.That(device.DongleStatus.Value.MicrophoneStreaming, Is.False);
            Assert.That(device.DongleStatus.Value.SpeakerActive, Is.True);
        });
    }

    [Test]
    public void RefreshDongleStatus_ReReadsReports()
    {
        FakeHidDevice hid = new FakeHidDevice();
        using DualSenseDongle device = new DualSenseDongle(hid, new FakeHidDeviceInfo(ConnectionType.Usb));

        hid.Reports[0xF9] = [0xF9, 0xBA, 0x81]; // -70 dB, mic only
        device.RefreshDongleStatus();

        Assert.Multiple(() =>
        {
            Assert.That(device.DongleStatus, Is.Not.Null);
            Assert.That(device.DongleStatus!.Value.RssiDb, Is.EqualTo(-70));
            Assert.That(device.DongleStatus.Value.MicrophoneStreaming, Is.True);
            Assert.That(device.DongleStatus.Value.SpeakerActive, Is.False);
        });
    }

    [Test]
    public void RefreshDongleStatus_ReadFailure_PreservesLastSnapshot()
    {
        FakeHidDevice hid = new FakeHidDevice();
        using DualSenseDongle device = new DualSenseDongle(hid, new FakeHidDeviceInfo(ConnectionType.Usb));
        DongleStatus previous = device.DongleStatus!.Value;
        hid.Reports.Remove(0xF8);
        hid.Reports.Remove(0xF9);

        device.RefreshDongleStatus();

        Assert.That(device.DongleStatus, Is.EqualTo(previous));
    }

    [Test]
    public void RefreshDongleStatus_RaisesDongleStatusChanged()
    {
        FakeHidDevice hid = new FakeHidDevice();
        using DualSenseDongle device = new DualSenseDongle(hid, new FakeHidDeviceInfo(ConnectionType.Usb));
        int changes = 0;
        device.DongleStatusChanged += (_, _) => changes++;

        hid.Reports[0xF9] = [0xF9, 0xF8, 0x81];
        device.RefreshDongleStatus();

        Assert.That(changes, Is.EqualTo(1));
    }

    private sealed class FakeHidDevice : IHidDevice
    {
        public Dictionary<byte, byte[]> Reports { get; } = new Dictionary<byte, byte[]>
        {
            [0x09] = CreatePairingReport(),
            [0x20] = CreateFirmwareReport(),
            [0x05] = CreateCalibrationReport(),
            [0xF8] = [0xF8, (byte)'d', (byte)'e', (byte)'v'],
            [0xF9] = [0xF9, 0xD2, 0x82] // -46 dB, valid marker + speaker
        };

        public List<byte> RequestedFeatureIds { get; } = [];

        public ushort VendorId
        {
            get
            {
                return 0x054C;
            }
        }

        public ushort ProductId
        {
            get
            {
                return 0x0CE6;
            }
        }

        public string DevicePath
        {
            get
            {
                return "test-dongle";
            }
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count) => count;

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64)
        {
            RequestedFeatureIds.Add(reportId);
            return Reports.GetValueOrDefault(reportId, []);
        }

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "DualSense Test";

        public bool IsConnected
        {
            get
            {
                return true;
            }
        }

        public void Dispose()
        {
        }

        private static byte[] CreatePairingReport()
        {
            byte[] report = new byte[20];
            report[0] = 0x09;
            for (int i = 0; i < 6; i++)
            {
                report[1 + i] = (byte)(0x10 + i);
                report[10 + i] = (byte)(0x20 + i);
            }

            report[7] = 0x08;
            report[8] = 0x25;
            return report;
        }

        private static byte[] CreateFirmwareReport()
        {
            byte[] report = new byte[64];
            report[0] = 0x20;
            return report;
        }

        private static byte[] CreateCalibrationReport()
        {
            byte[] report = new byte[41];
            report[0] = 0x05;
            return report;
        }
    }

    private sealed class FakeHidDeviceInfo(ConnectionType connectionType) : IHidDeviceInfo
    {
        public string Path
        {
            get
            {
                return "test-dongle";
            }
        }

        public ushort VendorId
        {
            get
            {
                return 0x054C;
            }
        }

        public ushort ProductId
        {
            get
            {
                return 0x0CE6;
            }
        }

        public string ProductName
        {
            get
            {
                return "DualSense Test";
            }
        }

        public string Manufacturer
        {
            get
            {
                return "Sony";
            }
        }

        public int InterfaceNumber
        {
            get
            {
                return 3;
            }
        }

        public ushort UsagePage
        {
            get
            {
                return 0x01;
            }
        }

        public HidUsageId Usage
        {
            get
            {
                return HidUsageId.GamePad;
            }
        }

        public ConnectionType BusType
        {
            get
            {
                return connectionType;
            }
        }
    }
}