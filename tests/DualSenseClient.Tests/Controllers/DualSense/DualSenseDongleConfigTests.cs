using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense;

public class DualSenseDongleConfigTests
{
    [Test]
    public void ReadDongleConfig_DongleConnection_ReturnsDecodedConfig()
    {
        FakeHidDevice hid = new FakeHidDevice();
        hid.Reports[0xF7] = BuildConfigReport(CreateModernBody());
        using DualSenseDongle device = new DualSenseDongle(hid, new FakeHidDeviceInfo(ConnectionType.Usb));

        DongleConfig? config = device.ReadDongleConfig();

        Assert.Multiple(() =>
        {
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.HapticsGain, Is.EqualTo(1.5f));
            Assert.That(config.SleepOnDock, Is.True);
            Assert.That(config.StatusGpioPin, Is.EqualTo(5));
            Assert.That(hid.RequestedFeatureIds, Does.Contain((byte)0xF7));
        });
    }

    [Test]
    public void ReadDongleConfig_LegacyBody_ReturnsLegacyConfig()
    {
        FakeHidDevice hid = new FakeHidDevice();
        hid.Reports[0xF7] = BuildConfigReport(CreateLegacyBody());
        using DualSenseDongle device = new DualSenseDongle(hid, new FakeHidDeviceInfo(ConnectionType.Usb));

        DongleConfig? config = device.ReadDongleConfig();

        Assert.Multiple(() =>
        {
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.BodyLength, Is.EqualTo(19));
            Assert.That(config.Layout, Is.EqualTo(DongleConfigLayout.Legacy));
            Assert.That(config.LegacyPsShortcutEnabled, Is.True);
            Assert.That(config.LegacyDisableMicrophone, Is.True);
        });
    }

    [Test]
    public void ReadDongleConfig_UnsupportedBodyLength_ReturnsNull()
    {
        FakeHidDevice hid = new FakeHidDevice();
        byte[] body = new byte[22];
        body[0] = 5;
        BitConverter.GetBytes(1.0f).CopyTo(body, 1);
        hid.Reports[0xF7] = BuildConfigReport(body);
        using DualSenseDongle device = new DualSenseDongle(hid, new FakeHidDeviceInfo(ConnectionType.Usb));

        Assert.That(device.ReadDongleConfig(), Is.Null);
    }

    [Test]
    public void ApplyDongleConfig_DongleConnection_SendsUpdateThenReadsBack()
    {
        FakeHidDevice hid = new FakeHidDevice();
        hid.Reports[0xF7] = BuildConfigReport(CreateModernBody());
        using DualSenseDongle device = new DualSenseDongle(hid, new FakeHidDeviceInfo(ConnectionType.Usb));
        DongleConfig requested = DongleConfig.CreateDefault(25) with
        {
            HapticsGain = 1.75f,
            SleepOnDock = true,
            StatusGpioPin = 8
        };
        hid.RequestedFeatureIds.Clear();

        DongleConfig? applied = device.ApplyDongleConfig(requested);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.Not.Null);
            Assert.That(applied!.HapticsGain, Is.EqualTo(1.75f));
            Assert.That(applied.SleepOnDock, Is.True);
            Assert.That(applied.StatusGpioPin, Is.EqualTo(8));
            Assert.That(applied.BodyLength, Is.EqualTo(25));
            Assert.That(hid.SentFeatureReports, Has.Count.EqualTo(1));
            Assert.That(hid.SentFeatureReports[0][0], Is.EqualTo(0xF6));
            Assert.That(hid.SentFeatureReports[0][1], Is.EqualTo(0x01));
            Assert.That(hid.SentFeatureReports[0], Has.Length.EqualTo(64));
            Assert.That(hid.RequestedFeatureIds, Does.Contain((byte)0xF7));
        });
    }

    [Test]
    public void SaveDongleConfigToFlash_SendsSaveCommand()
    {
        FakeHidDevice hid = new FakeHidDevice();
        using DualSenseDongle device = new DualSenseDongle(hid, new FakeHidDeviceInfo(ConnectionType.Usb));
        hid.RequestedFeatureIds.Clear();

        device.SaveDongleConfigToFlash();

        Assert.Multiple(() =>
        {
            Assert.That(hid.SentFeatureReports, Has.Count.EqualTo(1));
            Assert.That(hid.SentFeatureReports[0][0], Is.EqualTo(0xF6));
            Assert.That(hid.SentFeatureReports[0][1], Is.EqualTo(0x02));
            Assert.That(hid.SentFeatureReports[0], Has.Length.EqualTo(64));
            Assert.That(hid.RequestedFeatureIds, Is.Empty);
        });
    }

    [Test]
    public void ReconnectDongleUsb_SendsReconnectCommand()
    {
        FakeHidDevice hid = new FakeHidDevice();
        using DualSenseDongle device = new DualSenseDongle(hid, new FakeHidDeviceInfo(ConnectionType.Usb));
        hid.RequestedFeatureIds.Clear();

        device.ReconnectDongleUsb();

        Assert.Multiple(() =>
        {
            Assert.That(hid.SentFeatureReports, Has.Count.EqualTo(1));
            Assert.That(hid.SentFeatureReports[0][0], Is.EqualTo(0xF6));
            Assert.That(hid.SentFeatureReports[0][1], Is.EqualTo(0x03));
            Assert.That(hid.SentFeatureReports[0], Has.Length.EqualTo(64));
            Assert.That(hid.RequestedFeatureIds, Is.Empty);
        });
    }

    private static byte[] CreateModernBody()
    {
        byte[] body = new byte[25];
        body[0] = 5;
        BitConverter.GetBytes(1.5f).CopyTo(body, 1);
        body[5] = 80;
        body[6] = 63;
        body[7] = 2;
        body[8] = 30;
        body[10] = (byte)DonglePollingRateMode.Hz500;
        body[11] = 64;
        body[12] = (byte)DongleControllerMode.Auto;
        body[14] = 1;
        body[15] = (byte)DongleAudioDevice.Headphone;
        body[16] = (byte)DongleAudioDevice.Disabled;
        body[17] = 1;
        body[18] = 4;
        body[19] = 1;
        body[20] = 5;
        body[21] = (byte)DongleStatusGpioMode.ConnectPulse;
        body[22] = (byte)DonglePacketMode.Single;
        body[23] = 1;
        body[24] = 1;
        return body;
    }

    private static byte[] CreateLegacyBody()
    {
        byte[] body = new byte[19];
        body[0] = 5;
        BitConverter.GetBytes(1.25f).CopyTo(body, 1);
        body[5] = 100;
        body[6] = 90;
        body[7] = 3;
        body[8] = 15;
        body[9] = 1;
        body[10] = (byte)DonglePollingRateMode.Hz250;
        body[11] = 64;
        body[12] = (byte)DongleControllerMode.DualSenseEdge;
        body[13] = 1;
        body[14] = 1;
        body[15] = 1;
        body[16] = 0;
        body[17] = 1;
        body[18] = 3;
        return body;
    }

    private static byte[] BuildConfigReport(byte[] body)
    {
        byte[] report = new byte[body.Length + 1];
        report[0] = 0xF7;
        body.CopyTo(report, 1);
        return report;
    }

    private sealed class FakeHidDevice : IHidDevice
    {
        public Dictionary<byte, byte[]> Reports { get; } = new Dictionary<byte, byte[]>
        {
            [0x09] = CreatePairingReport(),
            [0x20] = CreateFirmwareReport(),
            [0x05] = CreateCalibrationReport(),
            [0xF8] = [0xF8, (byte)'d', (byte)'e', (byte)'v'],
            [0xF9] = [0xF9, 0xD2, 0x82]
        };

        public List<byte> RequestedFeatureIds { get; } = [];

        public List<byte[]> SentFeatureReports { get; } = [];

        public int ReadBackBodyLength { get; set; } = 25;

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
            byte[] report = buffer[offset..(offset + count)];
            SentFeatureReports.Add(report);

            if (report[0] == 0xF6 && report[1] == DongleConfigProtocol.UpdateConfigCommand)
            {
                byte[] body = report[2..];
                int bodyLength = Math.Min(ReadBackBodyLength, body.Length);
                byte[] configReport = new byte[bodyLength + 1];
                configReport[0] = 0xF7;
                body.AsSpan(0, bodyLength).CopyTo(configReport.AsSpan(1));
                Reports[0xF7] = configReport;
            }
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