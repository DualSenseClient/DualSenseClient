using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.DualSense.Utilities;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense;

public class DualSenseDeviceOutputTests
{
    private sealed class CapturingHidDevice : IHidDevice
    {
        public byte[]? LastWrite { get; private set; }

        public int LastWriteOffset { get; private set; }

        public int LastWriteCount { get; private set; }

        public int WriteCount { get; private set; }

        public bool FailWrites { get; set; }

        public ushort VendorId => 0x054C;

        public ushort ProductId => 0x0CE6;

        public string DevicePath => "test";

        public bool IsConnected => true;

        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count)
        {
            if (FailWrites)
            {
                throw new IOException("simulated write failure");
            }

            LastWrite = (byte[])buffer.Clone();
            LastWriteOffset = offset;
            LastWriteCount = count;
            WriteCount++;
            return count;
        }

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) => [];

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "Test";

        public void Dispose()
        {
        }
    }

    private sealed class DisposingHidDevice : IHidDevice
    {
        private bool _disposed;

        public ushort VendorId => 0x054C;

        public ushort ProductId => 0x0CE6;

        public string DevicePath => "test";

        public bool IsConnected => !_disposed;

        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return count;
        }

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) => [];

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "Test";

        public void Dispose()
        {
            _disposed = true;
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

    [Test]
    public void Usb_SendOutputState_FramesAsReport02()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.SendOutputState(new SetStateData
        {
            RumbleRight = 0x77
        });

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWriteOffset, Is.EqualTo(0));
            Assert.That(hid.LastWriteCount, Is.EqualTo(48));
            Assert.That(hid.LastWrite![0], Is.EqualTo(0x02));
            Assert.That(hid.LastWrite[1 + 2], Is.EqualTo(0x77));
        });
    }

    [Test]
    public void Bluetooth_SendOutputState_IncludesSequenceTagFlagsAndCrc()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Bluetooth));

        device.SendOutputState(new SetStateData
        {
            RumbleRight = 1
        });
        device.SendOutputState(new SetStateData
        {
            RumbleRight = 2
        });

        uint expected = DualSenseCRC32.Compute(hid.LastWrite!, 0, 74);
        uint actual = (uint)(hid.LastWrite![74]
                             | (hid.LastWrite[75] << 8)
                             | (hid.LastWrite[76] << 16)
                             | (hid.LastWrite[77] << 24));

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWriteCount, Is.EqualTo(78));
            Assert.That(hid.LastWrite[0], Is.EqualTo(0x31));
            Assert.That(hid.LastWrite[1], Is.EqualTo(0x10)); // second call uses sequence 1
            Assert.That(hid.LastWrite[2], Is.EqualTo(0x10));
            Assert.That(hid.LastWrite[3 + 2], Is.EqualTo(2));
            Assert.That(hid.LastWrite[50..74], Is.All.EqualTo(0));
            Assert.That(actual, Is.EqualTo(expected));
        });
    }

    [Test]
    public void Bluetooth_SequenceTag_IncrementsThenWraps()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Bluetooth));

        for (int i = 0; i < 16; i++)
        {
            // Every payload differs so the dedupe does not suppress the sends.
            device.SendOutputState(new SetStateData
            {
                RumbleRight = (byte)(i + 1)
            });
        }

        Assert.That(hid.LastWrite![1], Is.EqualTo(0xF0)); // 16th send → sequence 15

        device.SendOutputState(new SetStateData
        {
            RumbleRight = 17
        });
        Assert.That(hid.LastWrite[1], Is.EqualTo(0x00)); // wraps back to sequence 0
    }

    [Test]
    public void Bluetooth_DuplicatePayload_IsSkipped()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Bluetooth));
        SetStateData payload = new SetStateData
        {
            RumbleRight = 0x55,
            LedRed = 0x10,
            LedGreen = 0x20,
            LedBlue = 0x30
        };

        device.SendOutputState(payload);
        device.SendOutputState(payload);
        device.SendOutputState(payload);

        Assert.That(hid.WriteCount, Is.EqualTo(1), "byte-identical Bluetooth reports must be suppressed");
    }

    [Test]
    public void Bluetooth_ChangedPayload_IsSent()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Bluetooth));

        device.SendOutputState(new SetStateData
        {
            RumbleRight = 0x55
        });
        device.SendOutputState(new SetStateData
        {
            RumbleRight = 0x55,
            RumbleLeft = 0x11
        });

        Assert.That(hid.WriteCount, Is.EqualTo(2));
    }

    [Test]
    public void Bluetooth_FailedSendIsNotDedupedAway()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Bluetooth));

        SetStateData payload = new SetStateData
        {
            RumbleRight = 0x55
        };
        hid.FailWrites = true;
        Assert.Throws<IOException>(() => device.SendOutputState(payload));

        hid.FailWrites = false;
        device.SendOutputState(payload);

        Assert.That(hid.WriteCount, Is.EqualTo(1), "a payload whose send failed must be retried instead of deduped");
    }

    [Test]
    public void Usb_DuplicatePayload_IsStillSent()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));
        SetStateData payload = new SetStateData
        {
            RumbleRight = 0x55
        };

        device.SendOutputState(payload);
        device.SendOutputState(payload);

        Assert.That(hid.WriteCount, Is.EqualTo(2), "the dedupe must only apply to the bandwidth-constrained Bluetooth transport");
    }

    [Test]
    public void SendOutputState_AfterDispose_ThrowsHidException()
    {
        DisposingHidDevice hid = new DisposingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));
        hid.Dispose();

        Assert.Throws<HidException>(() => device.SendOutputState(new SetStateData()));
    }

    [Test]
    public void SetVibration_AfterDispose_DoesNotThrow()
    {
        DisposingHidDevice hid = new DisposingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));
        hid.Dispose();

        Assert.DoesNotThrow(() => device.SetVibration(0, 0));
    }
}