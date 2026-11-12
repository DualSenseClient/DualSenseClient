using DualSenseClient.Controllers;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense.Feature;

public class DongleStatusReaderTests
{
    [Test]
    public void Read_BothReports_ReturnsFirmwareSignalAndAudioFlags()
    {
        FakeControllerDevice device = new FakeControllerDevice(new Dictionary<byte, byte[]>
        {
            [0xF8] = [0xF8, (byte)'1', (byte)'.', (byte)'2', (byte)'.', (byte)'3'],
            [0xF9] = [0xF9, 0xF8, 0x83] // -8 dB, valid marker + mic + speaker
        });

        DongleStatus? status = DongleStatusReader.Read(device);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.Not.Null);
            Assert.That(status!.Value.FirmwareVersion, Is.EqualTo("1.2.3"));
            Assert.That(status.Value.RssiDb, Is.EqualTo(-8));
            Assert.That(status.Value.HasAudioFlags, Is.True);
            Assert.That(status.Value.MicrophoneStreaming, Is.True);
            Assert.That(status.Value.SpeakerActive, Is.True);
            Assert.That(status.Value.IsValid, Is.True);
        });
    }

    [Test]
    public void Read_FirmwareOnly_LeavesSignalAndAudioFlagsUnavailable()
    {
        FakeControllerDevice device = new FakeControllerDevice(new Dictionary<byte, byte[]>
        {
            [0xF8] = [0xF8, (byte)'d', (byte)'e', (byte)'v']
        });

        DongleStatus? status = DongleStatusReader.Read(device);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.Not.Null);
            Assert.That(status!.Value.FirmwareVersion, Is.EqualTo("dev"));
            Assert.That(status.Value.RssiDb, Is.Null);
            Assert.That(status.Value.HasAudioFlags, Is.False);
            Assert.That(status.Value.MicrophoneStreaming, Is.False);
            Assert.That(status.Value.SpeakerActive, Is.False);
        });
    }

    [Test]
    public void Read_StatusWithoutAudioFlags_ReturnsSignalOnly()
    {
        FakeControllerDevice device = new FakeControllerDevice(new Dictionary<byte, byte[]>
        {
            [0xF9] = [0xF9, 0xC4] // -60 dB, older firmware without the flags byte
        });

        DongleStatus? status = DongleStatusReader.Read(device);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.Not.Null);
            Assert.That(status!.Value.FirmwareVersion, Is.Empty);
            Assert.That(status.Value.RssiDb, Is.EqualTo(-60));
            Assert.That(status.Value.HasAudioFlags, Is.False);
        });
    }

    [Test]
    public void Read_ZeroRssiWithAudioFlags_ReturnsIdealSignalAndFlags()
    {
        FakeControllerDevice device = new FakeControllerDevice(new Dictionary<byte, byte[]>
        {
            [0xF9] = [0xF9, 0x00, 0x81] // 0 dB is ideal range; valid marker + mic
        });

        DongleStatus? status = DongleStatusReader.Read(device);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.Not.Null);
            Assert.That(status!.Value.RssiDb, Is.EqualTo(0));
            Assert.That(status.Value.HasAudioFlags, Is.True);
            Assert.That(status.Value.MicrophoneStreaming, Is.True);
            Assert.That(status.Value.SpeakerActive, Is.False);
        });
    }

    [Test]
    public void Read_AudioFlagsWithoutValidMarker_AreIgnored()
    {
        FakeControllerDevice device = new FakeControllerDevice(new Dictionary<byte, byte[]>
        {
            [0xF8] = [0xF8, (byte)'d', (byte)'e', (byte)'v'],
            [0xF9] = [0xF9, 0xC4, 0x03] // bits set, but no 0x80 valid marker
        });

        DongleStatus? status = DongleStatusReader.Read(device);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.Not.Null);
            Assert.That(status!.Value.RssiDb, Is.EqualTo(-60));
            Assert.That(status.Value.HasAudioFlags, Is.False);
            Assert.That(status.Value.MicrophoneStreaming, Is.False);
            Assert.That(status.Value.SpeakerActive, Is.False);
        });
    }

    [Test]
    public void Read_FirmwareWithTrailingZeros_TrimsVersion()
    {
        FakeControllerDevice device = new FakeControllerDevice(new Dictionary<byte, byte[]>
        {
            [0xF8] = [0xF8, (byte)'d', (byte)'e', (byte)'v', 0x00, 0x00]
        });

        DongleStatus? status = DongleStatusReader.Read(device);

        Assert.That(status?.FirmwareVersion, Is.EqualTo("dev"));
    }

    [Test]
    public void Read_NonPrintableFirmware_ReturnsEmptyFirmware()
    {
        FakeControllerDevice device = new FakeControllerDevice(new Dictionary<byte, byte[]>
        {
            [0xF8] = [0xF8, (byte)'d', 0xFF],
            [0xF9] = [0xF9, 0xC4]
        });

        DongleStatus? status = DongleStatusReader.Read(device);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.Not.Null);
            Assert.That(status!.Value.FirmwareVersion, Is.Empty);
            Assert.That(status.Value.RssiDb, Is.EqualTo(-60));
        });
    }

    [Test]
    public void Read_NoReports_ReturnsNull()
    {
        FakeControllerDevice device = new FakeControllerDevice([]);

        Assert.That(DongleStatusReader.Read(device), Is.Null);
    }

    [Test]
    public void Read_ZeroRssiWithoutFlags_ReturnsIdealSignal()
    {
        FakeControllerDevice device = new FakeControllerDevice(new Dictionary<byte, byte[]>
        {
            [0xF9] = [0xF9, 0x00]
        });

        DongleStatus? status = DongleStatusReader.Read(device);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.Not.Null);
            Assert.That(status!.Value.RssiDb, Is.EqualTo(0));
        });
    }

    [Test]
    public void Read_NoReadingSentinel_ReturnsNull()
    {
        FakeControllerDevice device = new FakeControllerDevice(new Dictionary<byte, byte[]>
        {
            [0xF9] = [0xF9, 0x80] // -128: newer firmware's "no reading" sentinel
        });

        Assert.That(DongleStatusReader.Read(device), Is.Null);
    }

    [Test]
    public void Read_PositiveRssiWithoutFlags_ReturnsNull()
    {
        FakeControllerDevice device = new FakeControllerDevice(new Dictionary<byte, byte[]>
        {
            [0xF9] = [0xF9, 0x2A]
        });

        Assert.That(DongleStatusReader.Read(device), Is.Null);
    }

    private sealed class FakeControllerDevice(Dictionary<byte, byte[]> reports) : IControllerDevice
    {
        public IHidDeviceInfo Info
        {
            get
            {
                throw new NotSupportedException();
            }
        }

        public ConnectionType ConnectionType
        {
            get
            {
                return ConnectionType.Usb;
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

        public Task<int> ReadInputAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64)
        {
            if (reports.TryGetValue(reportId, out byte[]? report))
            {
                return report;
            }

            throw new HidException($"Feature report 0x{reportId:X2} is not supported");
        }

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "DualSense Test";

        public bool DisconnectController() => false;

        public void Dispose()
        {
        }
    }
}