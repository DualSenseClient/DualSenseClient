using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense.Feature;

public class DongleDetectorTests
{
    [Test]
    public void IsDongle_VersionReportWithPrintableVersion_ReturnsTrue()
    {
        FakeHidDevice device = new FakeHidDevice(new Dictionary<byte, byte[]>
        {
            [0xF8] = [0xF8, (byte)'d', (byte)'e', (byte)'v']
        });

        Assert.That(DongleDetector.IsDongle(device), Is.True);
    }

    [Test]
    public void IsDongle_VersionReportWithNoPayload_ReturnsFalse()
    {
        FakeHidDevice device = new FakeHidDevice(new Dictionary<byte, byte[]>
        {
            [0xF8] = [0xF8]
        });

        Assert.That(DongleDetector.IsDongle(device), Is.False);
    }

    [Test]
    public void IsDongle_VersionReportWithNonPrintablePayload_ReturnsFalse()
    {
        FakeHidDevice device = new FakeHidDevice(new Dictionary<byte, byte[]>
        {
            [0xF8] = [0xF8, 0xFF]
        });

        Assert.That(DongleDetector.IsDongle(device), Is.False);
    }

    [Test]
    public void IsDongle_VersionReportMissing_FallsBackToConfigReport()
    {
        FakeHidDevice device = new FakeHidDevice(new Dictionary<byte, byte[]>
        {
            [0xF7] = [0xF7, 0x05]
        });

        Assert.That(DongleDetector.IsDongle(device), Is.True);
    }

    [Test]
    public void IsDongle_ConfigReportWithInvalidVersion_ReturnsFalse()
    {
        FakeHidDevice device = new FakeHidDevice(new Dictionary<byte, byte[]>
        {
            [0xF7] = [0xF7, 0x00]
        });

        Assert.That(DongleDetector.IsDongle(device), Is.False);
    }

    [Test]
    public void IsDongle_NeitherReportAnswered_ReturnsFalse()
    {
        FakeHidDevice device = new FakeHidDevice([]);

        Assert.That(DongleDetector.IsDongle(device), Is.False);
    }

    private sealed class FakeHidDevice(Dictionary<byte, byte[]> reports) : IHidDevice
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
                return "test-dongle";
            }
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count) => count;

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
    }
}