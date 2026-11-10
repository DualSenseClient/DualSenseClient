using DualSenseClient.Controllers;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.Controllers.DualSense.Utilities;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense.Feature;

public class FeatureReaderTests
{
    [Test]
    public void ReadCalibration_ValidReport_ReturnsParsedValues()
    {
        FakeControllerDevice device = new FakeControllerDevice(CreateCalibrationReport());

        CalibrationInfo? info = FeatureReader.ReadCalibration(device);

        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Value.IsValid, Is.True);
        Assert.That(info.Value.GyroPitchPlus, Is.EqualTo(16384));
    }

    [Test]
    public void ReadCalibration_InvalidReport_ReturnsNull()
    {
        FakeControllerDevice device = new FakeControllerDevice(new byte[10]);

        Assert.That(FeatureReader.ReadCalibration(device), Is.Null);
    }

    [Test]
    public void ReadCalibration_BluetoothInvalidCrc_StillReturnsStructurallyValidReport()
    {
        // The calibration GET is what moves a Bluetooth controller to full 0x31 state
        // reports, so a bad checksum must not leave the pad stuck in the minimal mode.
        // The app exposes raw motion values and does not rescale them, so the parsed
        // words are harmless even if a corrupt report supplied them.
        FakeControllerDevice device = new FakeControllerDevice(CreateCalibrationReport());
        device.ConnectionType = ConnectionType.Bluetooth;

        CalibrationInfo? info = FeatureReader.ReadCalibration(device);

        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Value.IsValid, Is.True);
    }

    [Test]
    public void ReadFirmwareInfo_BluetoothValidCrc_ReturnsInfo()
    {
        FakeControllerDevice device = new FakeControllerDevice(new byte[0]);
        device.ConnectionType = ConnectionType.Bluetooth;
        device.WithReport(0x20, CreateSignedFirmwareReport());

        FirmwareInfo? info = FeatureReader.ReadFirmwareInfo(device);

        Assert.That(info, Is.Not.Null);
    }

    [Test]
    public void ReadFirmwareInfo_BluetoothInvalidCrc_ReturnsNull()
    {
        FakeControllerDevice device = new FakeControllerDevice(new byte[0]);
        device.ConnectionType = ConnectionType.Bluetooth;
        byte[] report = CreateSignedFirmwareReport();
        report[10] ^= 0xFF;
        device.WithReport(0x20, report);

        Assert.That(FeatureReader.ReadFirmwareInfo(device), Is.Null);
    }

    [Test]
    public void ReadFirmwareInfo_UsbReportWithBluetoothCrc_IsNotValidated()
    {
        FakeControllerDevice device = new FakeControllerDevice(new byte[0]);
        device.ConnectionType = ConnectionType.Usb;
        byte[] report = CreateSignedFirmwareReport();
        report[10] ^= 0xFF;
        device.WithReport(0x20, report);

        Assert.That(FeatureReader.ReadFirmwareInfo(device), Is.Not.Null);
    }

    [Test]
    public void ReadPairingInfo_ReversesTheReportMacIntoDisplayOrder()
    {
        // The persona writes the identity MAC in the controller's report order (last byte
        // first); the client restores the address order used for display and filtering.
        byte[] report = new byte[20];
        report[0] = 0x09;
        byte[] addressOrder = [0x02, 0xD5, 0x11, 0x22, 0x33, 0x44];
        for (int i = 0; i < addressOrder.Length; i++)
        {
            report[1 + i] = addressOrder[addressOrder.Length - 1 - i];
        }

        report[7] = 0x08;
        report[8] = 0x25;
        FakeControllerDevice device = new FakeControllerDevice(new byte[0]);
        device.WithReport(0x09, report);

        PairingInfo? info = FeatureReader.ReadPairingInfo(device);

        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Value.ClientMac, Is.EqualTo("02:D5:11:22:33:44"));
    }

    [Test]
    public void ReadFirmwareInfo_FeatureReadAndProductNameThrow_ReturnsNull()
    {
        // A disposed controller makes both the feature read and the product-name
        // lookup fail. Logging the failure must not replace the reader's null result
        // with a HidException.
        FakeControllerDevice device = new FakeControllerDevice(new byte[0])
        {
            ThrowOnGetFeatureReport = true,
            ThrowOnGetProductName = true
        };

        Assert.That(FeatureReader.ReadFirmwareInfo(device), Is.Null);
    }

    private static byte[] CreateSignedFirmwareReport()
    {
        byte[] report = new byte[64];
        report[0] = 0x20;
        uint crc = DualSenseCRC32.Compute(report, 0, 60, DualSenseCRC32.FeatureGetSeed);
        report[60] = (byte)crc;
        report[61] = (byte)(crc >> 8);
        report[62] = (byte)(crc >> 16);
        report[63] = (byte)(crc >> 24);
        return report;
    }

    private static byte[] CreateCalibrationReport()
    {
        byte[] report = new byte[41];
        report[0] = 0x05;
        // Gyro pitch plus at wire offset 7-8 (payload offsets 6-7).
        report[7] = 0x00;
        report[8] = 0x40;
        return report;
    }

    private sealed class FakeControllerDevice : IControllerDevice
    {
        private readonly Dictionary<byte, byte[]> _reports = new Dictionary<byte, byte[]>();

        public FakeControllerDevice(byte[] calibrationReport)
        {
            if (calibrationReport.Length > 0)
            {
                _reports[0x05] = calibrationReport;
            }
        }

        public ConnectionType ConnectionType { get; set; } = ConnectionType.Usb;

        /// <summary>
        /// When <c>true</c>, <see cref="GetProductName"/> throws like a disposed controller.
        /// </summary>
        public bool ThrowOnGetProductName { get; set; }

        /// <summary>
        /// When <c>true</c>, <see cref="GetFeatureReport"/> throws like a dropped link.
        /// </summary>
        public bool ThrowOnGetFeatureReport { get; set; }

        public FakeControllerDevice WithReport(byte reportId, byte[] report)
        {
            _reports[reportId] = report;
            return this;
        }

        public IHidDeviceInfo Info
        {
            get
            {
                throw new NotSupportedException();
            }
        }

        public ControllerType ControllerType
        {
            get
            {
                return ControllerType.DualSense;
            }
        }

        public bool IsConnected
        {
            get
            {
                return true;
            }
        }

        public int MaxOutputReportLength
        {
            get
            {
                return 48;
            }
        }

        public int PollingRateHz
        {
            get
            {
                return 0;
            }
        }

        public int ReadInput(byte[] buffer, int offset, int count, int timeoutMs) => 0;

        public void SendOutput(byte[] buffer, int offset, int count)
        {
        }

        public Task<int> ReadInputAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            Task.FromResult(0);

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) =>
            ThrowOnGetFeatureReport
                ? throw new HidException("simulated feature read failure")
                : _reports.GetValueOrDefault(reportId, []);

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() =>
            ThrowOnGetProductName ? throw new HidException("simulated product name failure") : "Test";

        public bool DisconnectController() => false;

        public void Dispose()
        {
        }
    }
}