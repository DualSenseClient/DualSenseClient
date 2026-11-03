using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense;

public class DualSenseDeviceFeatureProbeTests
{
    [Test]
    public void Constructor_ProbesPairingFirmwareAndCalibration()
    {
        FakeHidDevice hid = new FakeHidDevice();

        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo());

        Assert.Multiple(() =>
        {
            Assert.That(hid.RequestedFeatureIds, Is.EqualTo(new byte[]
            {
                0x09, 0x20, 0x05
            }));
            Assert.That(device.PairingInfo, Is.Not.Null);
            Assert.That(device.FirmwareInfo, Is.Not.Null);
            Assert.That(device.CalibrationInfo, Is.Not.Null);
            Assert.That(device.CalibrationInfo!.Value.IsValid, Is.True);
        });
    }

    private sealed class FakeHidDevice : IHidDevice
    {
        private readonly Dictionary<byte, byte[]> _reports = new Dictionary<byte, byte[]>
        {
            [0x09] = CreatePairingReport(),
            [0x20] = CreateFirmwareReport(),
            [0x05] = CreateCalibrationReport()
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
                return "test-usb";
            }
        }

        public bool IsConnected
        {
            get
            {
                return true;
            }
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count) => count;

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64)
        {
            RequestedFeatureIds.Add(reportId);
            return _reports.GetValueOrDefault(reportId, []);
        }

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "DualSense Test";

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

    private sealed class FakeHidDeviceInfo : IHidDeviceInfo
    {
        public string Path
        {
            get
            {
                return "test-usb";
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
                return ConnectionType.Usb;
            }
        }
    }
}