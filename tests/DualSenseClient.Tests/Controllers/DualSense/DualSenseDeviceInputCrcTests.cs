using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Utilities;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense;

public class DualSenseDeviceInputCrcTests
{
    [Test]
    public void BluetoothInput_ValidCrc_RaisesInputReport()
    {
        using DualSenseDevice device = CreateDevice();
        int reports = 0;
        device.InputReportReceived += (_, _) => reports++;

        byte[] report = CreateBluetoothReport();
        device.ProcessInputReport(report, report.Length);

        Assert.That(reports, Is.EqualTo(1));
        Assert.That(device.InputReport, Is.Not.Null);
    }

    [Test]
    public void BluetoothInput_CorruptPayload_IsDropped()
    {
        using DualSenseDevice device = CreateDevice();
        int reports = 0;
        device.InputReportReceived += (_, _) => reports++;

        byte[] report = CreateBluetoothReport();
        report[10] ^= 0xFF; // payload byte; the CRC trailer is now stale
        device.ProcessInputReport(report, report.Length);

        Assert.That(reports, Is.Zero);
        Assert.That(device.InputReport, Is.Null);
    }

    [Test]
    public void BluetoothInput_CorruptCrc_IsDropped()
    {
        using DualSenseDevice device = CreateDevice();
        int reports = 0;
        device.InputReportReceived += (_, _) => reports++;

        byte[] report = CreateBluetoothReport();
        report[74] ^= 0xFF;
        device.ProcessInputReport(report, report.Length);

        Assert.That(reports, Is.Zero);
    }

    [Test]
    public void BluetoothInput_TruncatedReport_IsDropped()
    {
        using DualSenseDevice device = CreateDevice();
        int reports = 0;
        device.InputReportReceived += (_, _) => reports++;

        byte[] report = CreateBluetoothReport();
        device.ProcessInputReport(report, 32);

        Assert.That(reports, Is.Zero);
    }

    [Test]
    public void UsbInput_TruncatedReport_IsDropped()
    {
        using DualSenseDevice device = CreateDevice(ConnectionType.Usb);
        int reports = 0;
        device.InputReportReceived += (_, _) => reports++;

        byte[] report = new byte[64];
        report[0] = 0x01;
        device.ProcessInputReport(report, 32);

        Assert.That(reports, Is.Zero);
    }

    [Test]
    public void BluetoothInput_HasHidClear_IsDropped()
    {
        using DualSenseDevice device = CreateDevice();
        int reports = 0;
        device.InputReportReceived += (_, _) => reports++;

        byte[] report = CreateBluetoothReport();
        report[1] = 0x02; // HasMic set, HasHID clear: not a state packet
        UpdateCrc(report);
        device.ProcessInputReport(report, report.Length);

        Assert.That(reports, Is.Zero);
    }

    private static void UpdateCrc(byte[] report)
    {
        uint crc = DualSenseCRC32.Compute(report, 0, 74, DualSenseCRC32.InputReportSeed);
        report[74] = (byte)crc;
        report[75] = (byte)(crc >> 8);
        report[76] = (byte)(crc >> 16);
        report[77] = (byte)(crc >> 24);
    }

    private static DualSenseDevice CreateDevice(ConnectionType connectionType = ConnectionType.Bluetooth) =>
        new DualSenseDevice(new FakeHidDevice(), new FakeHidDeviceInfo(connectionType));

    private static byte[] CreateBluetoothReport()
    {
        byte[] report = new byte[78];
        report[0] = 0x31;
        report[1] = 0x01; // HasHID
        report[2 + 0] = 128;
        report[2 + 1] = 128;
        report[2 + 2] = 128;
        report[2 + 3] = 128;
        report[2 + 7] = 0x08;
        report[2 + 52] = 0x2A;

        uint crc = DualSenseCRC32.Compute(report, 0, 74, DualSenseCRC32.InputReportSeed);
        report[74] = (byte)crc;
        report[75] = (byte)(crc >> 8);
        report[76] = (byte)(crc >> 16);
        report[77] = (byte)(crc >> 24);
        return report;
    }

    private sealed class FakeHidDevice : IHidDevice
    {
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
                return "test-bt";
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

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) => [];

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "DualSense Test";

        public void Dispose()
        {
        }
    }

    private sealed class FakeHidDeviceInfo(ConnectionType connectionType) : IHidDeviceInfo
    {
        public string Path
        {
            get
            {
                return "test-bt";
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