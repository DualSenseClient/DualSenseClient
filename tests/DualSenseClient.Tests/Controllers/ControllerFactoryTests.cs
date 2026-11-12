using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers;

public class ControllerFactoryTests
{
    private sealed class StubHidDevice(Func<byte, byte[]>? featureReports = null) : IHidDevice
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
                return "stub";
            }
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count) => count;

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) =>
            featureReports?.Invoke(reportId) ?? [];

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "Wireless Controller";

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

    private sealed class StubHidDeviceInfo(ushort vendorId, ushort productId) : IHidDeviceInfo
    {
        public string Path
        {
            get
            {
                return $"stub:{vendorId:X4}:{productId:X4}";
            }
        }

        public ushort VendorId
        {
            get
            {
                return vendorId;
            }
        }

        public ushort ProductId
        {
            get
            {
                return productId;
            }
        }

        public string ProductName
        {
            get
            {
                return "Wireless Controller";
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
                return ConnectionType.Usb;
            }
        }
    }

    private sealed class StubHidEnumerator(Func<IHidDevice>? deviceFactory = null) : IHidDeviceEnumerator
    {
        public IHidDevice? OpenedDevice { get; private set; }

        public IReadOnlyList<IHidDeviceInfo> Enumerate(ushort? vendorId = null, ushort? productId = null) => [];

        public IReadOnlyList<IHidDeviceInfo> Enumerate(IEnumerable<(ushort VendorId, ushort ProductId)> deviceIds) => [];

        public IReadOnlyList<IHidDeviceInfo> EnumerateIncludingExcluded(ushort? vendorId = null, ushort? productId = null) => [];

        public IHidDevice OpenDevice(string path)
        {
            OpenedDevice?.Dispose();
            OpenedDevice = deviceFactory?.Invoke() ?? new StubHidDevice();
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
        using StubHidEnumerator enumerator = new StubHidEnumerator();
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
        using StubHidEnumerator enumerator = new StubHidEnumerator();
        using DualSenseDevice? device = ControllerFactory.Create(enumerator, new StubHidDeviceInfo(0x054C, 0x0DF2)) as DualSenseDevice;

        Assert.Multiple(() =>
        {
            Assert.That(device, Is.Not.Null);
            Assert.That(device!.ControllerType, Is.EqualTo(ControllerType.DualSenseEdge));
            Assert.That(device.IsEdge, Is.True);
            Assert.That(device.UsesVibrationV2, Is.True);
        });
    }

    [Test]
    public void Create_UsbDeviceAnsweringDongleVersion_CreatesDongleOverUsb()
    {
        using StubHidEnumerator enumerator = new StubHidEnumerator(() => new StubHidDevice(reportId =>
            reportId == 0xF8 ? [0xF8, (byte)'d', (byte)'e', (byte)'v'] : []));
        using DualSenseDongle? device = ControllerFactory.Create(enumerator, new StubHidDeviceInfo(0x054C, 0x0CE6)) as DualSenseDongle;

        Assert.Multiple(() =>
        {
            Assert.That(device, Is.Not.Null);
            Assert.That(device!.ConnectionType, Is.EqualTo(ConnectionType.Usb));
            Assert.That(device.Info.BusType, Is.EqualTo(ConnectionType.Usb));
            Assert.That(device.IsEdge, Is.False);
        });
    }

    [Test]
    public void Create_EdgeDeviceAnsweringDongleVersion_CreatesDongleEdgeOverUsb()
    {
        using StubHidEnumerator enumerator = new StubHidEnumerator(() => new StubHidDevice(reportId =>
            reportId == 0xF8 ? [0xF8, (byte)'d', (byte)'e', (byte)'v'] : []));
        using DualSenseDongle? device = ControllerFactory.Create(enumerator, new StubHidDeviceInfo(0x054C, 0x0DF2)) as DualSenseDongle;

        Assert.Multiple(() =>
        {
            Assert.That(device, Is.Not.Null);
            Assert.That(device!.ConnectionType, Is.EqualTo(ConnectionType.Usb));
            Assert.That(device.Info.BusType, Is.EqualTo(ConnectionType.Usb));
            Assert.That(device.ControllerType, Is.EqualTo(ControllerType.DualSenseEdge));
            Assert.That(device.IsEdge, Is.True);
        });
    }

    [Test]
    public void Create_UsbDeviceWithoutDongleReports_StaysUsb()
    {
        using StubHidEnumerator enumerator = new StubHidEnumerator();
        using DualSenseDevice? device = ControllerFactory.Create(enumerator, new StubHidDeviceInfo(0x054C, 0x0CE6)) as DualSenseDevice;

        Assert.Multiple(() =>
        {
            Assert.That(device, Is.Not.Null);
            Assert.That(device!.ConnectionType, Is.EqualTo(ConnectionType.Usb));
            Assert.That(device.Info.BusType, Is.EqualTo(ConnectionType.Usb));
        });
    }
}