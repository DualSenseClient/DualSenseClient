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

        public ushort VendorId => 0x054C;

        public ushort ProductId => 0x0CE6;

        public string DevicePath => "test";

        public bool IsConnected => true;

        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count)
        {
            LastWrite = (byte[])buffer.Clone();
            LastWriteOffset = offset;
            LastWriteCount = count;
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

        device.SendOutputState(new SetStateData { RumbleRight = 0x77 });

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

        device.SendOutputState(new SetStateData { RumbleRight = 1 });
        device.SendOutputState(new SetStateData { RumbleRight = 2 });

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

        SetStateData payload = new SetStateData();
        for (int i = 0; i < 16; i++)
        {
            device.SendOutputState(payload);
        }

        Assert.That(hid.LastWrite![1], Is.EqualTo(0xF0)); // 16th send → sequence 15

        device.SendOutputState(payload);
        Assert.That(hid.LastWrite[1], Is.EqualTo(0x00)); // wraps back to sequence 0
    }
}