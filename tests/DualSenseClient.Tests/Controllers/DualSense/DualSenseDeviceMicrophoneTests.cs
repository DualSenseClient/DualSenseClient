using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense;

public class DualSenseDeviceMicrophoneTests
{
    private sealed class CapturingHidDevice : IHidDevice
    {
        public List<byte[]> Writes { get; } = [];

        public byte[]? LastWrite
        {
            get
            {
                return Writes.Count == 0 ? null : Writes[^1];
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
            Writes.Add(buffer[offset..(offset + count)]);
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

    private sealed class FakeHidDeviceInfo(ConnectionType connectionType) : IHidDeviceInfo
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
                return connectionType;
            }
        }
    }

    [Test]
    public void ApplyMicrophoneSettings_SendsStateUpdateWithRouteAndLevel()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo(ConnectionType.Bluetooth));

        device.ApplyMicrophoneSettings(AudioControl.MicSelectExternal, 32);

        byte[] write = hid.LastWrite!;
        Assert.Multiple(() =>
        {
            Assert.That(write[0], Is.EqualTo(0x32));
            Assert.That(write[2], Is.EqualTo(0x90));
            Assert.That(write[3], Is.EqualTo(0x3F));
            Assert.That(write[4 + 7] & 0x03, Is.EqualTo(0x02)); // AudioControl mic bits
            Assert.That(write[4 + 6], Is.EqualTo(32)); // MicVolume
        });
    }

    [Test]
    public void BluetoothMicrophoneStreaming_True_SendsStatus03()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo(ConnectionType.Bluetooth));

        device.BluetoothMicrophoneStreaming = true;

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![0], Is.EqualTo(0x32));
            Assert.That(hid.LastWrite[4], Is.EqualTo(0x03));
        });
    }

    [Test]
    public void BluetoothMicrophoneStreaming_False_SendsStatus02()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo(ConnectionType.Bluetooth));
        device.BluetoothMicrophoneStreaming = true;
        hid.Writes.Clear();

        device.BluetoothMicrophoneStreaming = false;

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![0], Is.EqualTo(0x32));
            Assert.That(hid.LastWrite[4], Is.EqualTo(0x02));
        });
    }

    [Test]
    public void BluetoothMicrophoneStreaming_SameValue_DoesNotSendStatus()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo(ConnectionType.Bluetooth));
        device.BluetoothMicrophoneStreaming = true;
        int writesAfterEnable = hid.Writes.Count;

        device.BluetoothMicrophoneStreaming = true;

        Assert.That(hid.Writes.Count, Is.EqualTo(writesAfterEnable));
    }

    [Test]
    public void SendOutputState_PreservesOutputPathAndReplacesMicBits()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo(ConnectionType.Bluetooth));
        device.ApplyMicrophoneSettings(AudioControl.MicSelectInternal, 64);

        device.SendOutputState(new SetStateData
        {
            ValidFlag0 = ValidFlags.AllowAudioControl | ValidFlags.AllowMicVolume,
            AudioControl = AudioControl.OutputPathSpeaker | AudioControl.MicSelectAuto,
            MicVolume = 0
        });

        byte[] write = hid.LastWrite!;
        Assert.Multiple(() =>
        {
            Assert.That(write[0], Is.EqualTo(0x31));
            Assert.That(write[10] & 0x30, Is.EqualTo((byte)AudioControl.OutputPathSpeaker)); // payload byte 7, path preserved
            Assert.That(write[10] & 0x03, Is.EqualTo((byte)AudioControl.MicSelectInternal)); // route replaced
            Assert.That(write[9], Is.EqualTo(64)); // payload byte 6, level replaced
        });
    }

    [Test]
    public void SendBluetoothAudioPrime_NormalizesMicrophoneState()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo(ConnectionType.Bluetooth));
        device.ApplyMicrophoneSettings(AudioControl.MicSelectInternal, 32);

        device.SendBluetoothAudioPrime(new SetStateData());

        byte[] write = hid.Writes.Last(report => report[0] == 0x32 && report[2] == 0x90);
        Assert.Multiple(() =>
        {
            Assert.That(write[3], Is.EqualTo(0x3F));
            Assert.That(write[11] & 0x03, Is.EqualTo((byte)AudioControl.MicSelectInternal)); // AudioControl in the embedded state
            Assert.That(write[10], Is.EqualTo(32)); // MicVolume in the embedded state
            Assert.That(write[11] & 0x30, Is.EqualTo((byte)AudioControl.OutputPathSpeaker),
                "the prime keeps the speaker output path");
        });
    }

    [Test]
    public void SendBluetoothAudioAndHaptics_NormalizesMicrophoneState()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo(ConnectionType.Bluetooth));
        device.ApplyMicrophoneSettings(AudioControl.MicSelectInternal, 32);

        device.SendBluetoothAudioAndHaptics(new SetStateData
        {
            ValidFlag0 = ValidFlags.AllowAudioControl | ValidFlags.AllowMicVolume,
            AudioControl = AudioControl.OutputPathSpeaker | AudioControl.MicSelectAuto,
            MicVolume = 0
        }, new byte[200], new byte[64], BluetoothAudioRoute.Speaker);

        byte[] write = hid.Writes.Last(report => report[0] == 0x36);
        Assert.Multiple(() =>
        {
            Assert.That(write[13 + 7] & 0x03, Is.EqualTo((byte)AudioControl.MicSelectInternal)); // AudioControl in the embedded state
            Assert.That(write[13 + 6], Is.EqualTo(32)); // MicVolume in the embedded state
            Assert.That(write[13 + 7] & 0x30, Is.EqualTo((byte)AudioControl.OutputPathSpeaker),
                "the combined report keeps the speaker output path");
        });
    }

    [Test]
    public void SendBluetoothAudioPrime_WhenStreaming_ReSendsStatus()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo(ConnectionType.Bluetooth));
        device.BluetoothMicrophoneStreaming = true;

        device.SendBluetoothAudioPrime(new SetStateData());

        Assert.That(hid.Writes.Any(write => write[0] == 0x32 && write[4] == 0x03), Is.True);
    }

    [Test]
    public void SendBluetoothAudioPrime_WhenStreaming_ReSendsStatusAfterThePrime()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo(ConnectionType.Bluetooth));
        device.BluetoothMicrophoneStreaming = true;
        int writesBeforePrime = hid.Writes.Count;

        device.SendBluetoothAudioPrime(new SetStateData());

        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes.Count, Is.EqualTo(writesBeforePrime + 2));
            Assert.That(hid.Writes[writesBeforePrime][0], Is.EqualTo(0x32), "prime report");
            Assert.That(hid.Writes[^1][0], Is.EqualTo(0x32), "status report");
            Assert.That(hid.Writes[^1][4], Is.EqualTo(0x03));
        });
    }

    [Test]
    public void ClearMicrophoneSettings_StopsOverridingState()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo(ConnectionType.Bluetooth));
        device.ApplyMicrophoneSettings(AudioControl.MicSelectInternal, 64);
        device.ClearMicrophoneSettings();

        device.SendOutputState(new SetStateData
        {
            ValidFlag0 = ValidFlags.AllowAudioControl | ValidFlags.AllowMicVolume,
            AudioControl = AudioControl.OutputPathHeadphones | AudioControl.MicSelectExternal,
            MicVolume = 7
        });

        byte[] write = hid.LastWrite!;
        Assert.Multiple(() =>
        {
            Assert.That(write[10] & 0x03, Is.EqualTo((byte)AudioControl.MicSelectExternal));
            Assert.That(write[9], Is.EqualTo(7));
        });
    }

    [Test]
    public void ApplyMicrophoneSettings_Usb_SendsOutputReportWithRouteAndLevel()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo(ConnectionType.Usb));

        device.ApplyMicrophoneSettings(AudioControl.MicSelectExternal, 32);

        byte[] write = hid.LastWrite!;
        Assert.Multiple(() =>
        {
            Assert.That(write[0], Is.EqualTo(0x02), "USB output report");
            Assert.That(write[1] & (byte)ValidFlags.AllowAudioControl, Is.Not.Zero, "audio control flag");
            Assert.That(write[1] & (byte)ValidFlags.AllowMicVolume, Is.Not.Zero, "microphone volume flag");
            Assert.That(write[8] & 0x03, Is.EqualTo((byte)AudioControl.MicSelectExternal), "microphone route");
            Assert.That(write[7], Is.EqualTo(32), "microphone level");
            Assert.That(write[8] & 0x30, Is.EqualTo((byte)AudioControl.OutputPathSpeaker),
                "the default output path is kept");
        });
    }

    [Test]
    public void SendOutputState_Usb_PreservesOutputPathAndReplacesMicBits()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo(ConnectionType.Usb));
        device.ApplyMicrophoneSettings(AudioControl.MicSelectInternal, 64);
        hid.Writes.Clear();

        device.SendOutputState(new SetStateData
        {
            ValidFlag0 = ValidFlags.AllowAudioControl | ValidFlags.AllowMicVolume,
            AudioControl = AudioControl.OutputPathHeadphones | AudioControl.MicSelectAuto,
            MicVolume = 0
        });

        byte[] write = hid.LastWrite!;
        Assert.Multiple(() =>
        {
            Assert.That(write[0], Is.EqualTo(0x02));
            Assert.That(write[8] & 0x30, Is.EqualTo((byte)AudioControl.OutputPathHeadphones),
                "output path preserved");
            Assert.That(write[8] & 0x03, Is.EqualTo((byte)AudioControl.MicSelectInternal),
                "route replaced");
            Assert.That(write[7], Is.EqualTo(64), "level replaced");
        });
    }

    [Test]
    public void ClearMicrophoneSettings_Usb_StopsOverridingState()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo(ConnectionType.Usb));
        device.ApplyMicrophoneSettings(AudioControl.MicSelectInternal, 64);
        hid.Writes.Clear();

        device.SendOutputState(new SetStateData
        {
            ValidFlag0 = ValidFlags.AllowAudioControl | ValidFlags.AllowMicVolume,
            AudioControl = AudioControl.OutputPathHeadphones | AudioControl.MicSelectExternal,
            MicVolume = 7
        });

        byte[] overridden = hid.LastWrite!;
        Assert.Multiple(() =>
        {
            Assert.That(overridden[8] & 0x03, Is.EqualTo((byte)AudioControl.MicSelectInternal),
                "precondition: the applied route overrides the payload");
            Assert.That(overridden[7], Is.EqualTo(64), "precondition: the applied level overrides the payload");
        });

        device.ClearMicrophoneSettings();
        hid.Writes.Clear();
        device.SendOutputState(new SetStateData
        {
            ValidFlag0 = ValidFlags.AllowAudioControl | ValidFlags.AllowMicVolume,
            AudioControl = AudioControl.OutputPathHeadphones | AudioControl.MicSelectExternal,
            MicVolume = 7
        });

        byte[] write = hid.LastWrite!;
        Assert.Multiple(() =>
        {
            Assert.That(write[8] & 0x03, Is.EqualTo((byte)AudioControl.MicSelectExternal));
            Assert.That(write[7], Is.EqualTo(7));
        });
    }

    [Test]
    public void MicrophoneGate_Usb_DoesNotSendBluetoothStatus()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new FakeHidDeviceInfo(ConnectionType.Usb));

        device.BluetoothMicrophoneStreaming = true;
        device.SendMicrophoneState();

        Assert.That(hid.Writes, Is.Empty);
    }
}