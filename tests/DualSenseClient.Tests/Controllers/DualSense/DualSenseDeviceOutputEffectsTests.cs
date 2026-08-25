using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.DualSense.Triggers;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense;

public class DualSenseDeviceOutputEffectsTests
{
    private sealed class CapturingHidDevice(byte[]? firmwareReport = null) : IHidDevice
    {
        public byte[]? LastWrite { get; private set; }

        public int LastWriteOffset { get; private set; }

        public int LastWriteCount { get; private set; }

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
            LastWriteOffset = offset;
            LastWriteCount = count;
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
    public void SetVibration_NoFirmwareInfo_UsesV1Encoding()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.SetVibration(0x55, 0x55);

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWriteCount, Is.EqualTo(48));
            Assert.That(hid.LastWrite![0], Is.EqualTo(0x02));
            // flag0 = HAPTICS_SELECT | COMPATIBLE_VIBRATION
            Assert.That(hid.LastWrite[1], Is.EqualTo(0x03));
            // no v2 flag byte
            Assert.That(hid.LastWrite[1 + 38], Is.EqualTo(0x00));
            Assert.That(hid.LastWrite[1 + 2], Is.EqualTo(0x55));
            Assert.That(hid.LastWrite[1 + 3], Is.EqualTo(0x55));
            // trigger blocks stay off
            Assert.That(hid.LastWrite[1 + 10], Is.EqualTo(0x00));
            Assert.That(hid.LastWrite[1 + 21], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void SetVibration_V2Firmware_UsesImprovedRumbleEncoding()
    {
        CapturingHidDevice hid = new CapturingHidDevice(FirmwareReport(0x02, 0x15));
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        Assert.That(device.UsesVibrationV2, Is.True);
        device.SetVibration(0x55, 0x55);

        Assert.Multiple(() =>
        {
            // flag0 = HAPTICS_SELECT only
            Assert.That(hid.LastWrite![1], Is.EqualTo(0x02));
            // flag2 = COMPATIBLE_VIBRATION2
            Assert.That(hid.LastWrite[1 + 38], Is.EqualTo(0x04));
        });
    }

    [Test]
    public void SetVibration_V1Firmware_DoesNotUseImprovedRumbleEncoding()
    {
        CapturingHidDevice hid = new CapturingHidDevice(FirmwareReport(0x02, 0x14));
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        Assert.That(device.UsesVibrationV2, Is.False);
        device.SetVibration(0x55, 0x55);

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![1], Is.EqualTo(0x03));
            Assert.That(hid.LastWrite[1 + 38], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void SetVibration_Zero_TurnsMotorsOff()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.SetVibration(0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![1], Is.EqualTo(0x03));
            Assert.That(hid.LastWrite[1 + 2], Is.EqualTo(0x00));
            Assert.That(hid.LastWrite[1 + 3], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void SetVibration_LeftOnly_ZeroesRightMotor()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.SetVibration(0x55, 0);

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![1 + 3], Is.EqualTo(0x55)); // payload byte 3 = left motor
            Assert.That(hid.LastWrite[1 + 2], Is.EqualTo(0x00)); // payload byte 2 = right motor
        });
    }

    [Test]
    public void SetVibration_RightOnly_ZeroesLeftMotor()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.SetVibration(0, 0x55);

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![1 + 2], Is.EqualTo(0x55)); // payload byte 2 = right motor
            Assert.That(hid.LastWrite[1 + 3], Is.EqualTo(0x00)); // payload byte 3 = left motor
        });
    }

    [Test]
    public void SetTriggerEffects_SendsTriggerFlagsAndBlocks()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        TriggerEffectBlock resistance = TriggerEffectBuilder.Resistance(0x20, 0x64);
        device.SetTriggerEffects(resistance, resistance);

        Assert.Multiple(() =>
        {
            // flag0 = allow L2 + R2 trigger FFB
            Assert.That(hid.LastWrite![1], Is.EqualTo(0x0C));
            // R2 block at payload offset 10
            Assert.That(hid.LastWrite[1 + 10], Is.EqualTo((byte)TriggerEffectType.Resistance));
            Assert.That(hid.LastWrite[1 + 11], Is.EqualTo(0x20));
            Assert.That(hid.LastWrite[1 + 12], Is.EqualTo(0x64));
            // L2 block at payload offset 21
            Assert.That(hid.LastWrite[1 + 21], Is.EqualTo((byte)TriggerEffectType.Resistance));
            Assert.That(hid.LastWrite[1 + 22], Is.EqualTo(0x20));
            Assert.That(hid.LastWrite[1 + 23], Is.EqualTo(0x64));
        });
    }

    [Test]
    public void SetTriggerEffects_Off_SendsOffBlocks()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.SetTriggerEffects(TriggerEffectBuilder.Off(), TriggerEffectBuilder.Off());

        Assert.Multiple(() =>
        {
            Assert.That(hid.LastWrite![1], Is.EqualTo(0x0C));
            Assert.That(hid.LastWrite[1 + 10], Is.EqualTo(0x00));
            Assert.That(hid.LastWrite[1 + 21], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void ResetOutputs_ZeroesRumbleAndClearsTriggers()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));

        device.SetVibration(0xFF, 0xFF);
        device.SetTriggerEffects(TriggerEffectBuilder.Resistance(0x20, 0x64), TriggerEffectBuilder.Resistance(0x20, 0x64));
        device.ResetOutputs();

        Assert.Multiple(() =>
        {
            // final write is the trigger-off report; rumble was zeroed first
            Assert.That(hid.LastWrite![1], Is.EqualTo(0x0C));
            Assert.That(hid.LastWrite[1 + 2], Is.EqualTo(0x00));
            Assert.That(hid.LastWrite[1 + 3], Is.EqualTo(0x00));
            Assert.That(hid.LastWrite[1 + 10], Is.EqualTo(0x00));
            Assert.That(hid.LastWrite[1 + 21], Is.EqualTo(0x00));
        });
    }
}