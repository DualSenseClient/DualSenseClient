using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers;

public class ControllerFactoryTests
{
    private sealed class StubHidDevice : IHidDevice
    {
        public ushort VendorId => 0x054C;

        public ushort ProductId => 0x0CE6;

        public string DevicePath => "stub";

        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count) => count;

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) => [];

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "Wireless Controller";

        public bool IsConnected => true;

        public void Dispose()
        {
        }
    }

    private sealed class StubHidDeviceInfo(ushort vendorId, ushort productId) : IHidDeviceInfo
    {
        public string Path => $"stub:{vendorId:X4}:{productId:X4}";

        public ushort VendorId => vendorId;

        public ushort ProductId => productId;

        public string ProductName => "Wireless Controller";

        public string Manufacturer => "Sony";

        public int InterfaceNumber => 3;

        public ushort UsagePage => 0x01;

        public HidUsageId Usage => HidUsageId.GamePad;

        public ConnectionType BusType => ConnectionType.Usb;
    }

    private sealed class StubHidEnumerator : IHidDeviceEnumerator
    {
        public IHidDevice? OpenedDevice { get; private set; }

        public IReadOnlyList<IHidDeviceInfo> Enumerate(ushort? vendorId = null, ushort? productId = null) => [];

        public IReadOnlyList<IHidDeviceInfo> Enumerate(IEnumerable<(ushort VendorId, ushort ProductId)> deviceIds) => [];

        public IReadOnlyList<IHidDeviceInfo> EnumerateIncludingExcluded(ushort? vendorId = null, ushort? productId = null) => [];

        public IHidDevice OpenDevice(string path)
        {
            OpenedDevice?.Dispose();
            OpenedDevice = new StubHidDevice();
            return OpenedDevice;
        }

        public void StartWatching(int intervalMs = 1000)
        {
        }

        public void StopWatching()
        {
        }

        public void ExcludeDevice(string path)
        {
        }

        public void RemoveExcludedDevice(string path)
        {
        }

        public event EventHandler<DeviceConnectionEventArgs>? DeviceConnected
        {
            add { }
            remove { }
        }

        public event EventHandler<DeviceConnectionEventArgs>? DeviceDisconnected
        {
            add { }
            remove { }
        }

        public void Dispose() => OpenedDevice?.Dispose();
    }

    [Test]
    public void GetType_BaseDualSenseIds_ReturnsDualSense()
    {
        ControllerType type = ControllerFactory.GetType(new StubHidDeviceInfo(0x054C, 0x0CE6));
        Assert.That(type, Is.EqualTo(ControllerType.DualSense));
    }

    [Test]
    public void GetType_EdgeIds_ReturnsDualSenseEdge()
    {
        ControllerType type = ControllerFactory.GetType(new StubHidDeviceInfo(0x054C, 0x0DF2));
        Assert.That(type, Is.EqualTo(ControllerType.DualSenseEdge));
    }

    [Test]
    public void GetType_UnknownIds_ReturnsUnknown()
    {
        ControllerType type = ControllerFactory.GetType(new StubHidDeviceInfo(0x054C, 0x05C4));
        Assert.That(type, Is.EqualTo(ControllerType.Unknown));
    }

    [Test]
    public void Create_BaseDualSenseIds_CreatesBaseDevice()
    {
        using StubHidEnumerator enumerator = new();
        using DualSenseDevice? device = ControllerFactory.Create(enumerator, new StubHidDeviceInfo(0x054C, 0x0CE6)) as DualSenseDevice;

        Assert.Multiple(() =>
        {
            Assert.That(device, Is.Not.Null);
            Assert.That(device!.ControllerType, Is.EqualTo(ControllerType.DualSense));
            Assert.That(device.IsEdge, Is.False);
        });
    }

    [Test]
    public void Create_EdgeIds_CreatesEdgeDeviceWithVibrationV2()
    {
        using StubHidEnumerator enumerator = new();
        using DualSenseEdgeDevice? device = ControllerFactory.Create(enumerator, new StubHidDeviceInfo(0x054C, 0x0DF2)) as DualSenseEdgeDevice;

        Assert.Multiple(() =>
        {
            Assert.That(device, Is.Not.Null);
            Assert.That(device!.ControllerType, Is.EqualTo(ControllerType.DualSenseEdge));
            Assert.That(device.IsEdge, Is.True);
            Assert.That(device.UsesVibrationV2, Is.True);
        });
    }
}