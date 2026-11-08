using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Hid;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.DualSense;

[TestFixture]
public sealed class DualSenseDeviceAudioLightingTests
{
    [Test]
    public void BluetoothAudioReport_EmbedsResolvedProfileLights()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new BluetoothHidDeviceInfo());
        device.ApplyProfile(new Profile
        {
            Name = "Test",
            OverrideVirtualLights = true,
            Lightbar =
            {
                Red = 10,
                Green = 20,
                Blue = 30
            },
            MicLed =
            {
                Mode = 2
            },
            PlayerLeds =
            {
                Mask = 0x04
            }
        });

        object owner = new object();
        Assert.That(device.AudioLane.TryEnter(owner, AudioPriority.Player), Is.True);
        bool sent = device.AudioLane.SendAudioAndHaptics(owner, new SetStateData
        {
            ValidFlag0 = ValidFlags.AllowSpeakerVolume | ValidFlags.AllowAudioControl,
            SpeakerVolume = 0x50,
            AudioControl = AudioControl.OutputPathSpeaker
        }, new byte[200], new byte[64], BluetoothAudioRoute.Speaker);

        Assert.That(sent, Is.True);
        byte[] write = hid.Writes.Single(write => write[0] == 0x36);
        const int state = 13;
        Assert.Multiple(() =>
        {
            Assert.That(write[state + 1] & (byte)ValidFlags.AllowLedColor, Is.Not.Zero,
                "the embedded state block must claim the lightbar");
            Assert.That(write[state + 44], Is.EqualTo((byte)10));
            Assert.That(write[state + 45], Is.EqualTo((byte)20));
            Assert.That(write[state + 46], Is.EqualTo((byte)30));
            Assert.That(write[state + 1] & (byte)ValidFlags.AllowPlayerIndicators, Is.Not.Zero,
                "the embedded state block must claim the player LEDs");
            Assert.That(write[state + 43], Is.EqualTo((byte)0x04));
            Assert.That(write[state + 1] & (byte)ValidFlags.AllowMuteLight, Is.Not.Zero);
            Assert.That(write[state + 8], Is.EqualTo((byte)2));
        });
    }

    private sealed class RecordingHidDevice : IHidDevice
    {
        private readonly object _writesLock = new object();
        private readonly List<byte[]> _writes = [];

        public IReadOnlyList<byte[]> Writes
        {
            get
            {
                lock (_writesLock)
                {
                    return _writes.ToArray();
                }
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
            byte[] copy = new byte[count];
            Buffer.BlockCopy(buffer, offset, copy, 0, count);
            lock (_writesLock)
            {
                _writes.Add(copy);
            }

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