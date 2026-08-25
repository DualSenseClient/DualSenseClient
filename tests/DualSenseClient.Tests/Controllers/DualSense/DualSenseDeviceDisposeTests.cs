using DualSenseClient.Controllers.Devices;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense;

public class DualSenseDeviceDisposeTests
{
    private sealed class DisposingHidDevice : IHidDevice
    {
        private bool _disposed;

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
                return "test";
            }
        }

        public bool IsConnected
        {
            get
            {
                return !_disposed;
            }
        }

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

        public void SendFeatureReport(byte[] buffer, int offset, int count) => ObjectDisposedException.ThrowIf(_disposed, this);

        public string GetProductName()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return "Test";
        }

        public void Dispose() => _disposed = true;
    }

    private sealed class StubHidDeviceInfo(ConnectionType busType) : IHidDeviceInfo
    {
        public string Path
        {
            get
            {
                return "test";
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
                return 0;
            }
        }

        public ushort UsagePage
        {
            get
            {
                return 1;
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
                return busType;
            }
        }
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

    private sealed class BlockingHidDevice : IHidDevice
    {
        /// <summary>
        /// Completed once the read loop has entered <see cref="Read"/>.
        /// </summary>
        public readonly TaskCompletionSource ReadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly ManualResetEventSlim _disposeSignal = new ManualResetEventSlim();
        private int _readReturned;
        private volatile bool _disposed;

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
                return "test";
            }
        }

        public bool IsConnected
        {
            get
            {
                return !_disposed;
            }
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ReadEntered.TrySetResult();

            // Simulate a real blocking read that only unblocks when the handle closes.
            _disposeSignal.Wait();
            Interlocked.Exchange(ref _readReturned, 1);
            return 0;
        }

        /// <summary>
        /// True once a blocked <see cref="Read"/> has been released by disposal.
        /// </summary>
        public bool ReadReturned
        {
            get
            {
                return Volatile.Read(ref _readReturned) == 1;
            }
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

        public void SendFeatureReport(byte[] buffer, int offset, int count) => ObjectDisposedException.ThrowIf(_disposed, this);

        public string GetProductName()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return "Test";
        }

        public void Dispose()
        {
            _disposed = true;
            _disposeSignal.Set();
        }
    }

    [Test]
    public void Dispose_StopsTheReadLoop()
    {
        BlockingHidDevice hid = new BlockingHidDevice();
        DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        Assert.That(hid.ReadEntered.Task.Wait(TimeSpan.FromSeconds(2)), Is.True);
        device.Dispose();

        Assert.That(hid.ReadReturned, Is.True);
    }
}