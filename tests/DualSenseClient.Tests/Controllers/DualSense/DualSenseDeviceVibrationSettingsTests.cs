using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Hid;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.DualSense;

public class DualSenseDeviceVibrationSettingsTests
{
    private sealed class CapturingHidDevice(byte[]? firmwareReport = null) : IHidDevice
    {
        public byte[]? LastWrite { get; private set; }

        public int LastWriteCount { get; private set; }

        public int WriteCount { get; private set; }

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
            LastWrite = (byte[])buffer.Clone();
            LastWriteCount = count;
            WriteCount++;
            return count;
        }

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) =>
            reportId == 0x20 && firmwareReport is not null ? (byte[])firmwareReport.Clone() : [];

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

    /// <summary>
    /// Builds a valid 0x20 firmware report with the given update version (bytes 44-45).
    /// </summary>
    private static byte[] FirmwareReport(byte major, byte minor)
    {
        byte[] raw = new byte[64];
        raw[0] = 0x20;
        raw[44] = minor;
        raw[45] = major;
        return raw;
    }

    [Test]
    public void ApplyVibrationSettings_Compatible_OverridesV2FirmwareToV1()
    {
        CapturingHidDevice hid = new CapturingHidDevice(FirmwareReport(0x02, 0x15));
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            RumbleEncoding = VibrationEncodingMode.Compatible
        });
        device.SetVibration(0x55, 0x55);

        Assert.Multiple(() =>
        {
            Assert.That(device.SupportsVibrationV2, Is.True);
            Assert.That(device.UsesVibrationV2, Is.False);
            Assert.That(hid.LastWrite![1], Is.EqualTo(0x03));
            Assert.That(hid.LastWrite[1 + 38], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void ApplyVibrationSettings_Improved_OnUnsupportedFirmware_FallsBackToV1()
    {
        CapturingHidDevice hid = new CapturingHidDevice(FirmwareReport(0x02, 0x14));
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            RumbleEncoding = VibrationEncodingMode.Improved
        });
        device.SetVibration(0x55, 0x55);

        Assert.Multiple(() =>
        {
            Assert.That(device.SupportsVibrationV2, Is.False);
            Assert.That(device.UsesVibrationV2, Is.False);
            Assert.That(hid.LastWrite![1], Is.EqualTo(0x03));
            Assert.That(hid.LastWrite[1 + 38], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void ApplyVibrationSettings_Improved_OnSupportedFirmware_UsesV2()
    {
        CapturingHidDevice hid = new CapturingHidDevice(FirmwareReport(0x02, 0x15));
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            RumbleEncoding = VibrationEncodingMode.Improved
        });
        device.SetVibration(0x55, 0x55);

        Assert.Multiple(() =>
        {
            Assert.That(device.UsesVibrationV2, Is.True);
            Assert.That(hid.LastWrite![1], Is.EqualTo(0x02));
            Assert.That(hid.LastWrite[1 + 38], Is.EqualTo(0x04));
        });
    }

    [Test]
    public void ApplyVibrationSettings_Auto_UsesFirmwareSupport()
    {
        CapturingHidDevice hid = new CapturingHidDevice(FirmwareReport(0x02, 0x15));
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            RumbleEncoding = VibrationEncodingMode.Auto
        });

        Assert.That(device.UsesVibrationV2, Is.True);
    }

    [Test]
    public void ApplyVibrationSettings_FirstApply_SendsClearedMotorPowerAndFilterState()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings());

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite, Is.Not.Null);
            Assert.That(hid.LastWrite![1 + 1], Is.EqualTo((byte)(ValidFlags.AllowMotorPowerLevel | ValidFlags.AllowHapticLpf)));
            Assert.That(hid.LastWrite[1 + 36], Is.Zero);
            Assert.That(hid.LastWrite[1 + 39], Is.Zero);
        });
    }

    [Test]
    public void ApplyVibrationSettings_DisablingMotorPower_SendsResetReport()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            MotorPowerLevelEnabled = true,
            RumblePowerReduction = 12,
            TriggerPowerReduction = 9
        });
        int writesAfterEnable = hid.WriteCount;

        device.ApplyVibrationSettings(new VibrationSettings());

        Assert.Multiple(() =>
        {
            Assert.That(hid.WriteCount, Is.EqualTo(writesAfterEnable + 1));
            Assert.That(hid.LastWrite![1 + 1], Is.EqualTo((byte)ValidFlags.AllowMotorPowerLevel));
            Assert.That(hid.LastWrite[1 + 36], Is.Zero);
        });
    }

    [Test]
    public void ApplyVibrationSettings_DisablingHapticLowPassFilter_SendsResetReport()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            HapticLowPassFilter = true
        });
        int writesAfterEnable = hid.WriteCount;

        device.ApplyVibrationSettings(new VibrationSettings());

        Assert.Multiple(() =>
        {
            Assert.That(hid.WriteCount, Is.EqualTo(writesAfterEnable + 1));
            Assert.That(hid.LastWrite![1 + 1], Is.EqualTo((byte)ValidFlags.AllowHapticLpf));
            Assert.That(hid.LastWrite[1 + 39], Is.Zero);
        });
    }

    [Test]
    public void ApplyVibrationSettings_EnablingMotorPower_SendsStateReport()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings());
        int writesAfterFirstApply = hid.WriteCount;

        device.ApplyVibrationSettings(new VibrationSettings
        {
            MotorPowerLevelEnabled = true,
            RumblePowerReduction = 12,
            TriggerPowerReduction = 9
        });

        Assert.Multiple(() =>
        {
            Assert.That(hid.WriteCount, Is.EqualTo(writesAfterFirstApply + 1));
            Assert.That(hid.LastWrite![1 + 1], Is.EqualTo((byte)ValidFlags.AllowMotorPowerLevel));
            Assert.That(hid.LastWrite[1 + 36], Is.EqualTo(0x9C));
        });
    }

    [Test]
    public void ApplyVibrationSettings_UnchangedSettings_DoesNotSendAgain()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings());
        int writesAfterFirstApply = hid.WriteCount;

        device.ApplyVibrationSettings(new VibrationSettings());

        Assert.That(hid.WriteCount, Is.EqualTo(writesAfterFirstApply));
    }

    [Test]
    public void SendOutputState_MotorPowerLevelEnabled_ForcesTheReductionByte()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            MotorPowerLevelEnabled = true,
            RumblePowerReduction = 12,
            TriggerPowerReduction = 9
        });
        device.SendOutputState(new SetStateData());

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![1 + 1], Is.EqualTo((byte)ValidFlags.AllowMotorPowerLevel));
            Assert.That(hid.LastWrite[1 + 36], Is.EqualTo(0x9C));
        });
    }

    [Test]
    public void SendOutputState_MotorPowerLevelDisabled_LeavesTheGameValue()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.SendOutputState(new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowMotorPowerLevel,
            MotorPowerReduction = 0x42
        });

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![1 + 1], Is.EqualTo((byte)ValidFlags.AllowMotorPowerLevel));
            Assert.That(hid.LastWrite[1 + 36], Is.EqualTo(0x42));
        });
    }

    [Test]
    public void SendOutputState_MotorPowerLevelEnabled_ClampsTheReductions()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            MotorPowerLevelEnabled = true,
            RumblePowerReduction = 200,
            TriggerPowerReduction = 200
        });
        device.SendOutputState(new SetStateData());

        Assert.That(hid.LastWrite![1 + 36], Is.EqualTo((10 << 4) | 15));
    }

    [Test]
    public void SendOutputState_HapticLowPassFilterEnabled_ForcesTheFilter()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            HapticLowPassFilter = true
        });
        device.SendOutputState(new SetStateData());

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![1 + 1], Is.EqualTo((byte)ValidFlags.AllowHapticLpf));
            Assert.That(hid.LastWrite[1 + 39], Is.EqualTo(0x01));
        });
    }

    [Test]
    public void SendOutputState_HapticLowPassFilterEnabled_PreservesUnknownBits()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            HapticLowPassFilter = true
        });
        device.SendOutputState(new SetStateData
        {
            HapticLowPassFilter = 0x80
        });

        Assert.That(hid.LastWrite![1 + 39], Is.EqualTo(0x81));
    }

    [Test]
    public void SendOutputState_HapticLowPassFilterDisabled_LeavesTheGameValue()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.SendOutputState(new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowHapticLpf,
            HapticLowPassFilter = 0x01
        });

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![1 + 1], Is.EqualTo((byte)ValidFlags.AllowHapticLpf));
            Assert.That(hid.LastWrite[1 + 39], Is.EqualTo(0x01));
        });
    }

    [Test]
    public void SendOutputState_ExplicitCompatible_RewritesTheV2Selector()
    {
        CapturingHidDevice hid = new CapturingHidDevice(FirmwareReport(0x02, 0x15));
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            RumbleEncoding = VibrationEncodingMode.Compatible
        });
        device.SendOutputState(new SetStateData
        {
            ValidFlag2 = ValidFlags.EnableImprovedRumbleEmu
        });

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![1], Is.EqualTo((byte)(ValidFlags.EnableRumbleEmulation | ValidFlags.UseRumbleNotHaptics)));
            Assert.That(hid.LastWrite[1 + 38], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void SendOutputState_ExplicitImproved_RewritesTheV1Selector()
    {
        CapturingHidDevice hid = new CapturingHidDevice(FirmwareReport(0x02, 0x15));
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            RumbleEncoding = VibrationEncodingMode.Improved
        });
        device.SendOutputState(new SetStateData
        {
            ValidFlag0 = ValidFlags.EnableRumbleEmulation
        });

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![1], Is.EqualTo((byte)ValidFlags.UseRumbleNotHaptics));
            Assert.That(hid.LastWrite[1 + 38], Is.EqualTo((byte)ValidFlags.EnableImprovedRumbleEmu));
        });
    }

    [Test]
    public void SendOutputState_ExplicitImproved_OnUnsupportedFirmware_UsesV1Selector()
    {
        CapturingHidDevice hid = new CapturingHidDevice(FirmwareReport(0x02, 0x14));
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            RumbleEncoding = VibrationEncodingMode.Improved
        });
        device.SendOutputState(new SetStateData
        {
            ValidFlag0 = ValidFlags.EnableRumbleEmulation
        });

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![1], Is.EqualTo((byte)(ValidFlags.EnableRumbleEmulation | ValidFlags.UseRumbleNotHaptics)));
            Assert.That(hid.LastWrite[1 + 38], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void SendOutputState_Auto_OnV1Firmware_RewritesTheV2SelectorToV1()
    {
        CapturingHidDevice hid = new CapturingHidDevice(FirmwareReport(0x02, 0x14));
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.SendOutputState(new SetStateData
        {
            ValidFlag2 = ValidFlags.EnableImprovedRumbleEmu
        });

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![1], Is.EqualTo((byte)(ValidFlags.EnableRumbleEmulation | ValidFlags.UseRumbleNotHaptics)));
            Assert.That(hid.LastWrite[1 + 38], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void SendOutputState_ExplicitCompatible_DoesNotSelectRumbleWhenNoRumbleIsActive()
    {
        CapturingHidDevice hid = new CapturingHidDevice(FirmwareReport(0x02, 0x15));
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            RumbleEncoding = VibrationEncodingMode.Compatible
        });
        device.SendOutputState(new SetStateData());

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![1], Is.EqualTo(0x00));
            Assert.That(hid.LastWrite[1 + 38], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void SendBluetoothAudioPrime_AppliesVibrationOverrides()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Bluetooth));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            MotorPowerLevelEnabled = true,
            RumblePowerReduction = 5,
            TriggerPowerReduction = 3,
            HapticLowPassFilter = true
        });
        device.SendBluetoothAudioPrime(new SetStateData());

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![0], Is.EqualTo(0x32));
            Assert.That(hid.LastWrite[4 + 1], Is.EqualTo((byte)(ValidFlags.AllowMotorPowerLevel | ValidFlags.AllowHapticLpf)));
            Assert.That(hid.LastWrite[4 + 36], Is.EqualTo((3 << 4) | 5));
            Assert.That(hid.LastWrite[4 + 39], Is.EqualTo(0x01));
        });
    }

    [Test]
    public void ApplyVibrationSettings_WhileAudioLaneActive_LeavesOverrideStatePending()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Bluetooth));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            MotorPowerLevelEnabled = true,
            RumblePowerReduction = 4
        });
        Assert.That(device.HasPendingVibrationState, Is.False);

        Assert.That(device.AudioLane.TryEnter(new object(), AudioPriority.Forwarder), Is.True);
        device.ApplyVibrationSettings(new VibrationSettings());

        Assert.That(device.HasPendingVibrationState, Is.True);
    }

    [Test]
    public void SendBluetoothAudioAndHaptics_AppliesPendingClearStateAndClearsPendingFlag()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Bluetooth));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            MotorPowerLevelEnabled = true,
            RumblePowerReduction = 6,
            TriggerPowerReduction = 4,
            HapticLowPassFilter = true
        });
        Assert.That(device.AudioLane.TryEnter(new object(), AudioPriority.Forwarder), Is.True);
        device.ApplyVibrationSettings(new VibrationSettings());
        Assert.That(device.HasPendingVibrationState, Is.True);

        device.SendBluetoothAudioAndHaptics(
            new SetStateData(),
            new byte[BluetoothAudioReportBuilder.AudioPayloadSize],
            new byte[BluetoothAudioReportBuilder.HapticsPayloadSize],
            BluetoothAudioRoute.Speaker);

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![0], Is.EqualTo(0x36));
            Assert.That(hid.LastWrite[13 + 1], Is.EqualTo((byte)(ValidFlags.AllowMotorPowerLevel | ValidFlags.AllowHapticLpf)));
            Assert.That(hid.LastWrite[13 + 36], Is.Zero);
            Assert.That(hid.LastWrite[13 + 39], Is.Zero);
            Assert.That(device.HasPendingVibrationState, Is.False);
        });
    }

    [Test]
    public void SendBluetoothAudioPrime_AppliesPendingClearStateAndClearsPendingFlag()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Bluetooth));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            MotorPowerLevelEnabled = true,
            RumblePowerReduction = 6,
            HapticLowPassFilter = true
        });
        Assert.That(device.AudioLane.TryEnter(new object(), AudioPriority.Forwarder), Is.True);
        device.ApplyVibrationSettings(new VibrationSettings());
        Assert.That(device.HasPendingVibrationState, Is.True);

        device.SendBluetoothAudioPrime(new SetStateData());

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![0], Is.EqualTo(0x32));
            Assert.That(hid.LastWrite[4 + 1], Is.EqualTo((byte)(ValidFlags.AllowMotorPowerLevel | ValidFlags.AllowHapticLpf)));
            Assert.That(hid.LastWrite[4 + 36], Is.Zero);
            Assert.That(hid.LastWrite[4 + 39], Is.Zero);
            Assert.That(device.HasPendingVibrationState, Is.False);
        });
    }

    [Test]
    public void DualSenseDeviceOutputs_NeedsOutputStateBlock_IncludesPendingVibrationState()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Bluetooth));
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device);

        Assert.That(outputs.NeedsOutputStateBlock, Is.False);

        Assert.That(device.AudioLane.TryEnter(new object(), AudioPriority.Forwarder), Is.True);
        device.ApplyVibrationSettings(new VibrationSettings
        {
            MotorPowerLevelEnabled = true
        });

        Assert.That(outputs.NeedsOutputStateBlock, Is.True);
    }

    [Test]
    public void RumbleAmplitudeScale_FollowsMotorPowerReduction()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.ApplyVibrationSettings(new VibrationSettings());
        Assert.That(device.RumbleAmplitudeScale, Is.EqualTo(1f));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            MotorPowerLevelEnabled = true,
            RumblePowerReduction = 5
        });
        Assert.That(device.RumbleAmplitudeScale, Is.EqualTo(2f / 3f).Within(0.001f));

        device.ApplyVibrationSettings(new VibrationSettings
        {
            MotorPowerLevelEnabled = true,
            RumblePowerReduction = VibrationSettings.MaxRumblePowerReduction
        });
        Assert.That(device.RumbleAmplitudeScale, Is.Zero);
    }
}