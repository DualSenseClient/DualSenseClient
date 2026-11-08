using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense;

[TestFixture]
public sealed class DualSenseDeviceAudioSerializationTests
{
    [Test]
    public void OutputStateAndBluetoothAudio_WritesDoNotOverlap()
    {
        ConcurrentHidDevice hid = new ConcurrentHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new BluetoothHidDeviceInfo());

        Task outputWriter = Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                device.SendOutputState(new SetStateData
                {
                    ValidFlag1 = ValidFlags.AllowLedColor,
                    LedRed = (byte)i
                });
            }
        });

        Task audioWriter = Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                device.SendBluetoothAudio(new byte[200], BluetoothAudioRoute.Speaker);
            }
        });

        Task.WaitAll(outputWriter, audioWriter);

        Assert.That(hid.Overlapped, Is.False, "audio and state reports must not overlap on the interrupt channel");
    }

    private sealed class ConcurrentHidDevice : IHidDevice
    {
        private int _insideWrite;

        public bool Overlapped { get; private set; }

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
                return true;
            }
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count)
        {
            if (Interlocked.Increment(ref _insideWrite) != 1)
            {
                Overlapped = true;
            }

            Thread.Sleep(1);
            Interlocked.Decrement(ref _insideWrite);
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

    private sealed class BluetoothHidDeviceInfo : IHidDeviceInfo
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
                return ConnectionType.Bluetooth;
            }
        }
    }
}