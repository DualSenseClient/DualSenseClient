using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.DualSense.Triggers;
using DualSenseClient.Hid;
using DualSenseClient.Settings.Sections;

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
    public async Task ReadInputAsync_AfterDispose_FaultsWithHidException()
    {
        DisposingHidDevice hid = new DisposingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));
        hid.Dispose();

        await Assert.ThrowsAsync<HidException>(() => device.ReadInputAsync(new byte[64], 0, 64, CancellationToken.None));
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

    private sealed class RecordingHidDevice : IHidDevice
    {
        public List<byte[]> Writes { get; } = new List<byte[]>();

        /// <summary>
        /// When set, every read throws as a disconnected HID handle does.
        /// </summary>
        public bool FailReads { get; set; }

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

        public int Read(byte[] buffer, int offset, int count, int timeoutMs)
        {
            if (FailReads)
            {
                throw new HidException("hid_read_timeout failed");
            }

            return 0;
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count)
        {
            byte[] copy = new byte[count];
            Buffer.BlockCopy(buffer, offset, copy, 0, count);
            Writes.Add(copy);
            return count;
        }

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) => new byte[1];

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "Test";

        public void Dispose()
        {
        }
    }

    [Test]
    public void OutputMethods_AfterDispose_DoNotWrite()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));
        device.Dispose();
        hid.Writes.Clear();

        device.SendOutputState(new SetStateData
        {
            RumbleLeft = 1,
            RumbleRight = 2
        });
        device.ApplyProfile(new Profile
        {
            Name = "Test"
        });
        device.SetVibration(1, 2);
        device.SetTriggerEffects(TriggerEffectBuilder.Off(), TriggerEffectBuilder.Off());
        device.ResetOutputs();
        device.SetAudioOutput(AudioControl.OutputPathSpeaker, 0x50, 0x50);

        Assert.That(hid.Writes, Is.Empty);
    }

    [Test]
    public void BluetoothAudioMethods_AfterDispose_DoNotWrite()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Bluetooth));
        device.Dispose();
        hid.Writes.Clear();

        device.ResetBluetoothAudioStream();
        device.SendBluetoothAudioPrime(new SetStateData());
        device.SendBluetoothHaptics(new byte[64]);
        device.SendBluetoothAudioAndHaptics(new SetStateData(), new byte[200], new byte[64], BluetoothAudioRoute.Speaker);
        device.SendBluetoothAudio(new byte[200], BluetoothAudioRoute.Speaker);

        Assert.That(hid.Writes, Is.Empty);
    }

    [Test]
    public void Dispose_ZeroesRumbleMotors()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.Dispose();

        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes[0][4], Is.EqualTo(0), "left motor must be switched off");
            Assert.That(hid.Writes[0][5], Is.EqualTo(0), "right motor must be switched off");
        });
    }

    [Test]
    public void OutputMethods_AfterLinkLoss_DoNotWrite()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        hid.FailReads = true;
        Assert.That(SpinWait.SpinUntil(() => !device.IsConnected, TimeSpan.FromSeconds(2)), Is.True,
            "the read loop must observe the dropped link");

        hid.Writes.Clear();
        device.SendOutputState(new SetStateData
        {
            RumbleLeft = 1,
            RumbleRight = 2
        });
        device.SetVibration(1, 2);
        device.ResetOutputs();

        Assert.That(hid.Writes, Is.Empty);
    }

    [Test]
    public void BluetoothAudioMethods_AfterLinkLoss_DoNotWrite()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Bluetooth));

        hid.FailReads = true;
        Assert.That(SpinWait.SpinUntil(() => !device.IsConnected, TimeSpan.FromSeconds(2)), Is.True,
            "the read loop must observe the dropped link");

        hid.Writes.Clear();
        device.SendBluetoothAudioPrime(new SetStateData());
        device.SendBluetoothHaptics(new byte[64]);
        device.SendBluetoothAudioAndHaptics(new SetStateData(), new byte[200], new byte[64], BluetoothAudioRoute.Speaker);
        device.SendBluetoothAudio(new byte[200], BluetoothAudioRoute.Speaker);

        Assert.That(hid.Writes, Is.Empty);
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