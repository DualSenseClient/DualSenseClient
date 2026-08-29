using DualSenseClient.Controllers.Devices;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers;

public class ControllerDeviceDisconnectTests
{
    /// <summary>
    /// HID device simulating a controller that is mid-disconnect / already disposed:
    /// querying the product name from the live device fails.
    /// </summary>
    private sealed class ThrowingProductNameHidDevice : IHidDevice
    {
        public int GetProductNameCalls { get; private set; }

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
                return false;
            }
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count) => count;

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) => [];

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName()
        {
            GetProductNameCalls++;
            throw new ObjectDisposedException(nameof(ThrowingProductNameHidDevice));
        }

        public void Dispose()
        {
        }
    }

    private sealed class StubHidDeviceInfo(ConnectionType busType, string productName) : IHidDeviceInfo
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
                return productName;
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
    public void DisconnectController_UsbDevice_UsesCachedNameWithoutLiveQuery()
    {
        ThrowingProductNameHidDevice hid = new ThrowingProductNameHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb, "DualSense Wireless Controller"));

        // Regression test: this previously queried the live device, which throws
        // (HidException) when the controller is mid-disconnect.
        bool result = device.DisconnectController();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(hid.GetProductNameCalls, Is.Zero);
        });
    }

    [Test]
    public void DisconnectController_BluetoothWithoutMac_UsesCachedNameWithoutLiveQuery()
    {
        ThrowingProductNameHidDevice hid = new ThrowingProductNameHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Bluetooth, "DualSense Wireless Controller"));

        // No pairing info was read, so there is no MAC address to disconnect.
        bool result = device.DisconnectController();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(hid.GetProductNameCalls, Is.Zero);
        });
    }

    [Test]
    public void DisconnectController_EmptyCachedName_FallsBackToLiveDeviceAndSwallowsFailure()
    {
        ThrowingProductNameHidDevice hid = new ThrowingProductNameHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Bluetooth, string.Empty));

        // The cached name is empty, so the live device is queried once; the
        // failure must be swallowed (fallback to "Unknown controller"), not thrown.
        bool result = device.DisconnectController();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(hid.GetProductNameCalls, Is.EqualTo(1));
        });
    }
}