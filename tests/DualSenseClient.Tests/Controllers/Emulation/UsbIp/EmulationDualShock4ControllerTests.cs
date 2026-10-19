using System.Buffers.Binary;
using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Controllers.Emulation.UsbIp;
using DualSenseClient.Settings.Sections;
using DualSenseClient.Emulation.Devices.DualShock4;
using DualSenseClient.VIIPER.DualShock4;
using ButtonMappingEntry = DualSenseClient.Settings.Sections.ButtonMappingEntry;
using DualShock4Variant = DualSenseClient.Emulation.Devices.DualShock4.DualShock4Variant;

namespace DualSenseClient.Tests.Controllers.Emulation.UsbIp;

/// <summary>
/// Input-side tests of <see cref="EmulationDualShock4Controller"/>: the adapter reuses the libVIIPER
/// wrapper's mapping pipeline (<see cref="VirtualInputMapper.DualShock4DefaultTable"/>, the
/// DualShock 4 touch-Y and motion scalers) and feeds the persona whose served 64-byte report is
/// compared against <see cref="DualShock4InputReport.Build"/> with an independently stated expected
/// state. The client never uses raw passthrough for the DualShock 4 (the wrapper has no raw path),
/// so an attached raw image must be ignored. Both descriptor variants are wrapped and pushed.
/// </summary>
[TestFixture]
public sealed class EmulationDualShock4ControllerTests
{
    /// <summary>Report offset of the left stick X axis (sticks follow at +1, +2, +3).</summary>
    private const int OffsetSticks = 1;

    /// <summary>Report offset of the face-button/D-pad byte.</summary>
    private const int OffsetFaceAndDpad = 5;

    /// <summary>Report offset of the shoulder-button byte.</summary>
    private const int OffsetShoulderButtons = 6;

    /// <summary>Report offset of the PS/touchpad/counter byte.</summary>
    private const int OffsetPsTouchpad = 7;

    /// <summary>Report offset of the battery/ext byte (level nibble and cable bit).</summary>
    private const int OffsetBattery = 30;

    /// <summary>Client payload offset of the raw battery byte.</summary>
    private const int PayloadBattery = 52;

    private static EmulationDualShock4Controller CreateController(DualShock4Device device, RecordingDualSenseOutputs outputs) =>
        new EmulationDualShock4Controller(device, outputs);

    private static ButtonMappingEntry Entry(string target, params string[] keys)
    {
        return new ButtonMappingEntry
        {
            Keys = [.. keys],
            Targets = [target]
        };
    }

    /// <summary>
    /// Builds a table whose four face buttons contribute D-pad directions, so every one of the 16
    /// <see cref="VirtualDPad"/> masks can be produced (the shipped mapping tables only expose the
    /// eight masks the physical hat can generate).
    /// </summary>
    private static ButtonMappingTable DpadTable()
    {
        IReadOnlyDictionary<ButtonType, ResolvedMappingTarget> defaults = new Dictionary<ButtonType, ResolvedMappingTarget>
        {
            [ButtonType.Cross] = new ResolvedMappingTarget
            {
                DPad = VirtualDPad.Up
            },
            [ButtonType.Square] = new ResolvedMappingTarget
            {
                DPad = VirtualDPad.Down
            },
            [ButtonType.Triangle] = new ResolvedMappingTarget
            {
                DPad = VirtualDPad.Left
            },
            [ButtonType.Circle] = new ResolvedMappingTarget
            {
                DPad = VirtualDPad.Right
            }
        };
        return ButtonMappingTable.Resolve(defaults, null, VirtualInputMapper.ParseDualShock4Target, null);
    }

    [Test]
    public void DualShock4Stick_SignedState_IsThePhysicalWireByteForEveryValue()
    {
        for (int raw = 0; raw < 256; raw++)
        {
            Assert.That((byte)(VirtualInputMapper.DualSenseStick((byte)raw) + 128), Is.EqualTo((byte)raw));
        }
    }

    [Test]
    public void PushInput_EveryStickByte_ServesThePhysicalWireValue()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        EmulationDualShock4Controller controller = CreateController(device, new RecordingDualSenseOutputs());

        for (int value = 0; value < 256; value++)
        {
            byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
            payload[0] = (byte)value;
            payload[1] = (byte)(255 - value);
            payload[2] = (byte)((value * 7) & 0xFF);
            payload[3] = (byte)((value ^ 0x5A) & 0xFF);
            controller.PushInput(EmulationAdapterTestSupport.Report(payload));

            byte[] report = EmulationAdapterTestSupport.Pull(device, 64);

            Assert.That(report[OffsetSticks], Is.EqualTo(payload[0]));
            Assert.That(report[OffsetSticks + 1], Is.EqualTo(payload[1]));
            Assert.That(report[OffsetSticks + 2], Is.EqualTo(payload[2]));
            Assert.That(report[OffsetSticks + 3], Is.EqualTo(payload[3]));
        }
    }

    [TestCase(DualShock4Buttons.Square, 5, 0x10)]
    [TestCase(DualShock4Buttons.Cross, 5, 0x20)]
    [TestCase(DualShock4Buttons.Circle, 5, 0x40)]
    [TestCase(DualShock4Buttons.Triangle, 5, 0x80)]
    [TestCase(DualShock4Buttons.L1, 6, 0x01)]
    [TestCase(DualShock4Buttons.R1, 6, 0x02)]
    [TestCase(DualShock4Buttons.L2, 6, 0x04)]
    [TestCase(DualShock4Buttons.R2, 6, 0x08)]
    [TestCase(DualShock4Buttons.Share, 6, 0x10)]
    [TestCase(DualShock4Buttons.Options, 6, 0x20)]
    [TestCase(DualShock4Buttons.L3, 6, 0x40)]
    [TestCase(DualShock4Buttons.R3, 6, 0x80)]
    [TestCase(DualShock4Buttons.PS, 7, 0x01)]
    [TestCase(DualShock4Buttons.Touchpad, 7, 0x02)]
    public void PushInput_EveryMappedButtonBit_SetsOnlyItsWireBit(DualShock4Buttons target, int wireOffset, int wireMask)
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        EmulationDualShock4Controller controller = CreateController(device, new RecordingDualSenseOutputs());
        controller.ButtonMappings = VirtualInputMapper.DualShock4Table([Entry(target.ToString(), "Cross")]);
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[7] |= 0x20; // Cross is the only pressed source

        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);
        Assert.Multiple(() =>
        {
            Assert.That(report[OffsetFaceAndDpad] & 0xF0, Is.EqualTo(wireOffset == OffsetFaceAndDpad ? wireMask : 0));
            Assert.That(report[OffsetShoulderButtons], Is.EqualTo(wireOffset == OffsetShoulderButtons ? wireMask : 0));
            Assert.That(report[OffsetPsTouchpad] & 0x03, Is.EqualTo(wireOffset == OffsetPsTouchpad ? wireMask : 0));
            Assert.That(report[OffsetFaceAndDpad] & 0x0F, Is.EqualTo(0x08), "the D-pad must stay released");
        });
    }

    [TestCase(0x0, 0x08)]
    [TestCase(0x1, 0x00)]
    [TestCase(0x2, 0x04)]
    [TestCase(0x3, 0x00)]
    [TestCase(0x4, 0x06)]
    [TestCase(0x5, 0x07)]
    [TestCase(0x6, 0x05)]
    [TestCase(0x7, 0x07)]
    [TestCase(0x8, 0x02)]
    [TestCase(0x9, 0x01)]
    [TestCase(0xA, 0x03)]
    [TestCase(0xB, 0x01)]
    [TestCase(0xC, 0x06)]
    [TestCase(0xD, 0x01)]
    [TestCase(0xE, 0x03)]
    [TestCase(0xF, 0x01)]
    public void PushInput_EveryDpadMask_ServesTheViiperHatValue(int mask, int expectedHat)
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        EmulationDualShock4Controller controller = CreateController(device, new RecordingDualSenseOutputs());
        controller.ButtonMappings = DpadTable();
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        byte faceButtons = 0;
        if ((mask & 0x1) != 0)
        {
            faceButtons |= 0x20; // Up <- Cross
        }

        if ((mask & 0x2) != 0)
        {
            faceButtons |= 0x10; // Down <- Square
        }

        if ((mask & 0x4) != 0)
        {
            faceButtons |= 0x80; // Left <- Triangle
        }

        if ((mask & 0x8) != 0)
        {
            faceButtons |= 0x40; // Right <- Circle
        }

        payload[7] |= faceButtons;

        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);
        Assert.Multiple(() =>
        {
            Assert.That(report[OffsetFaceAndDpad] & 0x0F, Is.EqualTo(expectedHat));
            Assert.That(report[OffsetFaceAndDpad] & 0xF0, Is.Zero);
        });
    }

    [Test]
    public void PushInput_RichReport_ProducesTheExpectedWireState()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        EmulationDualShock4Controller controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[0] = 10;
        payload[1] = 20;
        payload[2] = 30;
        payload[3] = 40;
        payload[4] = 200; // L2 analog
        payload[5] = 100; // R2 analog
        payload[7] = 0x01 | 0x20; // D-pad up-right plus Cross
        payload[8] = 0x01 | 0x04 | 0x10; // L1, L2 click, Create (Share)
        payload[9] = 0x01 | 0x02; // PS and touchpad click
        payload[32] = 0x05; // touch 1 active, tracking id 5
        payload[33] = 0x23; // x = 0x123
        payload[34] = 0x61;
        payload[35] = 0x45; // y = 0x456
        payload[36] = 0x07; // touch 2 active, tracking id 7
        payload[37] = 0x00;
        payload[38] = 0x81;
        payload[39] = 0x43; // y = 1080, scaled onto the DS4 range
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(15), 16384);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(17), -16384);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(19), 4096);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(21), 8192);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(23), -8192);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(25), 0);
        payload[PayloadBattery] = 0x19; // level 9 with the cable bit

        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        DualShock4InputState expectedState = new DualShock4InputState
        {
            LeftStickX = 10,
            LeftStickY = 20,
            RightStickX = 30,
            RightStickY = 40,
            Dpad = DualShock4DpadDirection.UpRight,
            Cross = true,
            L1 = true,
            L2 = true,
            Share = true,
            Ps = true,
            TouchpadPressed = true,
            LeftTrigger = 200,
            RightTrigger = 100,
            GyroPitch = VirtualInputMapper.GyroToDs4(16384),
            GyroYaw = VirtualInputMapper.GyroToDs4(-16384),
            GyroRoll = VirtualInputMapper.GyroToDs4(4096),
            AccelX = VirtualInputMapper.AccelToDs4(8192),
            AccelY = VirtualInputMapper.AccelToDs4(-8192),
            AccelZ = VirtualInputMapper.AccelToDs4(0),
            BatteryLevel = 9,
            Charging = true,
            Touch1Active = true,
            Touch1X = 0x123,
            Touch1Y = VirtualInputMapper.TouchYToDs4(0x456),
            Touch1Id = 5,
            Touch2Active = true,
            Touch2X = 0x100,
            Touch2Y = VirtualInputMapper.TouchYToDs4(1080),
            Touch2Id = 7
        };
        byte[] expected = new byte[64];
        DualShock4InputReport.Build(expectedState, 0, 0, 0, expected);

        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);
        Assert.That(report, Is.EqualTo(expected));
    }

    [Test]
    public void PushInput_AttachedRawImage_IsIgnored()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        EmulationDualShock4Controller controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] raw = EmulationAdapterTestSupport.RawReport(0x5A);
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[0] = 33;

        controller.PushInput(EmulationAdapterTestSupport.Report(payload, raw));

        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);
        Assert.Multiple(() =>
        {
            Assert.That(report[OffsetSticks], Is.EqualTo(33));
            Assert.That(report, Is.Not.EqualTo(raw), "the DualShock 4 mode has no raw passthrough");
        });
    }

    [Test]
    public void PushInput_FirstReportBattery_UsesTheWireLevelAndCableBits()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        EmulationDualShock4Controller controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[PayloadBattery] = 0x2A; // level 10 with power state Complete

        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        Assert.That(EmulationAdapterTestSupport.Pull(device, 64)[OffsetBattery], Is.EqualTo(0x0A),
            "the persona models the DS4 wire byte (level nibble plus cable bit), so the Complete nibble contributes no cable bit");
    }

    [Test]
    public void PushBattery_BeforeTheFirstInputReport_IsOverwrittenByThatReport()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        EmulationDualShock4Controller controller = CreateController(device, new RecordingDualSenseOutputs());
        controller.PushBattery(new BatteryState(0x19));
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[PayloadBattery] = 0x2A; // level 10 with power state Complete

        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        Assert.That(EmulationAdapterTestSupport.Pull(device, 64)[OffsetBattery], Is.EqualTo(0x0A),
            "the first input report overwrites a pre-input battery push, like the wrapper's EnsureInitialMeta");
    }

    [Test]
    public void PushBattery_AfterTheFirstInput_AppliesImmediatelyAndSticksAcrossLaterPushes()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        EmulationDualShock4Controller controller = CreateController(device, new RecordingDualSenseOutputs());
        controller.PushInput(EmulationAdapterTestSupport.Report(EmulationAdapterTestSupport.NeutralPayload()));

        controller.PushBattery(new BatteryState(0x19));
        Assert.That(EmulationAdapterTestSupport.Pull(device, 64)[OffsetBattery], Is.EqualTo(0x19),
            "an explicit battery push re-stamps the current state immediately, like the wrapper's meta push");

        byte[] later = EmulationAdapterTestSupport.NeutralPayload();
        later[PayloadBattery] = 0x05;
        controller.PushInput(EmulationAdapterTestSupport.Report(later));
        Assert.That(EmulationAdapterTestSupport.Pull(device, 64)[OffsetBattery], Is.EqualTo(0x19),
            "later reports do not overwrite the pushed battery once the first report initialized the meta");
    }

    [Test]
    public void PushBattery_EveryRawByte_RoundTripsOnTheNextReport()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        EmulationDualShock4Controller controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] neutral = EmulationAdapterTestSupport.NeutralPayload();
        controller.PushInput(EmulationAdapterTestSupport.Report(neutral));

        for (int raw = 0; raw < 256; raw++)
        {
            controller.PushBattery(new BatteryState((byte)raw));
            controller.PushInput(EmulationAdapterTestSupport.Report(neutral));
            Assert.That(EmulationAdapterTestSupport.Pull(device, 64)[OffsetBattery], Is.EqualTo((byte)(raw & 0x1F)));
        }
    }

    [Test]
    public void PushConnectionStatus_IsANoOp()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        EmulationDualShock4Controller controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[0] = 33;
        controller.PushInput(EmulationAdapterTestSupport.Report(payload));
        byte[] before = EmulationAdapterTestSupport.Pull(device, 64);

        Assert.That(() => controller.PushConnectionStatus(new ConnectionStatus(0xFF)), Throws.Nothing);
        byte[] after = EmulationAdapterTestSupport.Pull(device, 64);

        Assert.That(after[OffsetSticks], Is.EqualTo(before[OffsetSticks]));
    }

    [TestCase(DualShock4Variant.V1)]
    [TestCase(DualShock4Variant.V2)]
    public void Construction_WrapsBothDescriptorVariantsAndPushes(DualShock4Variant variant)
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity(), null, variant);
        EmulationDualShock4Controller controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[0] = 77;

        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);
        Assert.Multiple(() =>
        {
            Assert.That(controller.Mode, Is.EqualTo(EmulationMode.DualShock4));
            Assert.That(controller.DeviceHandle, Is.Null, "an Emulation persona is an object, not a native handle");
            Assert.That(report[OffsetSticks], Is.EqualTo(77));
        });
    }

    [Test]
    public void Dispose_StopsPushesAndOutputDeliveryWithoutRemovingTheDevice()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        EmulationDualShock4Controller controller = CreateController(device, outputs);
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[0] = 11;
        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        controller.Dispose();
        byte[] ignored = EmulationAdapterTestSupport.NeutralPayload();
        ignored[0] = 99;
        controller.PushInput(EmulationAdapterTestSupport.Report(ignored));
        byte[] wire = new byte[32];
        wire[0] = 0x05;
        wire[1] = 0x07;
        device.OnOutputReport(wire);

        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);
        Assert.Multiple(() =>
        {
            Assert.That(report[OffsetSticks], Is.EqualTo(11), "a disposed adapter ignores later input pushes");
            Assert.That(outputs.OutputStates, Is.Empty);
            Assert.That(outputs.Vibrations, Is.Empty);
            Assert.That(controller.DeviceHandle, Is.Null);
            Assert.That(() => controller.Dispose(), Throws.Nothing);
        });
    }
}