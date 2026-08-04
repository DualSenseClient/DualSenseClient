using DualSenseClient.Controllers.Devices;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense;

public class DualSenseDeviceDisposeTests
{
    private sealed class DisposingHidDevice : IHidDevice
    {
        private bool _disposed;

        public ushort VendorId => 0x054C;

        public ushort ProductId => 0x0CE6;

        public string DevicePath => "test";

        public bool IsConnected => !_disposed;

        public int Read(byte[] buffer, int offset, int count, int timeoutMs)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return 0;
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Task.FromResult(0);
        }

        public int Write(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return count;
        }

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return [];
        }

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        public string GetProductName()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return "Test";
        }

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
    public void IsConnected_AfterDispose_ReturnsFalse()
    {
        DisposingHidDevice hid = new DisposingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));
        hid.Dispose();

        Assert.That(device.IsConnected, Is.False);
    }

    [Test]
    public void ReadInput_AfterDispose_ThrowsHidException()
    {
        DisposingHidDevice hid = new DisposingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));
        hid.Dispose();

        Assert.Throws<HidException>(() => device.ReadInput(new byte[64], 0, 64, 100));
    }

    [Test]
    public void ReadInputAsync_AfterDispose_FaultsWithHidException()
    {
        DisposingHidDevice hid = new DisposingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));
        hid.Dispose();

        Assert.ThrowsAsync<HidException>(async () => await device.ReadInputAsync(new byte[64], 0, 64, CancellationToken.None));
    }

    [Test]
    public void GetFeatureReport_AfterDispose_ThrowsHidException()
    {
        DisposingHidDevice hid = new DisposingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));
        hid.Dispose();

        Assert.Throws<HidException>(() => device.GetFeatureReport(0x20));
    }

    [Test]
    public void SendFeatureReport_AfterDispose_ThrowsHidException()
    {
        DisposingHidDevice hid = new DisposingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));
        hid.Dispose();

        Assert.Throws<HidException>(() => device.SendFeatureReport(new byte[64], 0, 64));
    }

    [Test]
    public void GetProductName_AfterDispose_ThrowsHidException()
    {
        DisposingHidDevice hid = new DisposingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));
        hid.Dispose();

        Assert.Throws<HidException>(() => device.GetProductName());
    }
}