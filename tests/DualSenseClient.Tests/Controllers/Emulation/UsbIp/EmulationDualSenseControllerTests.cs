using System.Buffers.Binary;
using System.Reflection;
using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Controllers.Emulation.UsbIp;
using DualSenseClient.Settings.Sections;
using DualSenseClient.Emulation.Devices.DualSense;
using DualSenseClient.VIIPER.DualSense;

namespace DualSenseClient.Tests.Controllers.Emulation.UsbIp;

/// <summary>
/// Input-side tests of <see cref="EmulationDualSenseController"/>: the adapter feeds the persona
/// from the same <see cref="ButtonMappingTable"/> / <see cref="VirtualInputMapper"/> pipeline as the
/// libVIIPER wrapper, so every assertion here reads the persona's served 64-byte wire report and
/// pins the bytes the host would see. Raw passthrough, the first-report battery meta, the
/// exhaustive D-pad switch and the single-bit sweep of every mapper-reachable button flag are
/// covered.
/// </summary>
[TestFixture]
public sealed class EmulationDualSenseControllerTests
{
    /// <summary>Report offset of the left stick X axis (sticks follow at +1, +2, +3).</summary>
    private const int OffsetSticks = 1;

    /// <summary>Report offset of the face-button/D-pad byte.</summary>
    private const int OffsetFaceAndDpad = 8;

    /// <summary>Report offset of the shoulder-button byte.</summary>
    private const int OffsetShoulderButtons = 9;

    /// <summary>Report offset of the utility-button byte (PS, touchpad, mute, Edge Fn and paddles).</summary>
    private const int OffsetUtilityButtons = 10;

    /// <summary>Report offset of the battery/power byte.</summary>
    private const int OffsetBattery = 53;

    /// <summary>Report offset of the host-status byte (carries the mic-mute echo, bit 2).</summary>
    private const int OffsetHostStatus = 54;

    /// <summary>Bit of the host-status byte carrying the mic-mute echo (reference MicMuted).</summary>
    private const byte MicMutedBit = 0x04;

    /// <summary>Client payload offset of the raw battery byte.</summary>
    private const int PayloadBattery = 52;

    /// <summary>
    /// Private mute-light cache the adapter serves as the mic-mute echo (report byte 54 bit 2);
    /// read by reflection because it has no public surface.
    /// </summary>
    private static readonly FieldInfo MuteLightModeField = typeof(EmulationDualSenseController)
        .GetField("_muteLightMode", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static EmulationDualSenseController CreateController(DualSenseDevice device, RecordingDualSenseOutputs outputs) =>
        new EmulationDualSenseController(device, outputs, false);

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
        return ButtonMappingTable.Resolve(defaults, null, VirtualInputMapper.ParseDualSenseTarget, null);
    }

    private static short ReadInt16(byte[] report, int offset) => BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(offset));

    [Test]
    public void DualSenseStick_SignedState_IsThePhysicalWireByteForEveryValue()
    {
        for (int raw = 0; raw < 256; raw++)
        {
            Assert.That((byte)(VirtualInputMapper.DualSenseStick((byte)raw) + 128), Is.EqualTo((byte)raw));
        }
    }

    [Test]
    public void PushInput_EveryStickByte_ServesThePhysicalWireValue()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());

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

    [Test]
    public void PushInput_AllButtonGroups_MapToTheirWireBytes()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[7] = 0x08 | 0xF0; // released D-pad plus every face button
        payload[8] = 0xFF; // L1 R1 L2 R2 Create Options L3 R3
        payload[9] = 0xFF; // PS touchpad mute FnL FnR paddleL paddleR (bit 3 unused)

        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);
        Assert.Multiple(() =>
        {
            Assert.That(report[OffsetFaceAndDpad], Is.EqualTo(0xF8));
            Assert.That(report[OffsetShoulderButtons], Is.EqualTo(0xFF));
            Assert.That(report[OffsetUtilityButtons], Is.EqualTo(0xF7));
        });
    }

    [TestCase(DualSenseButtons.Square, 8, 0x10)]
    [TestCase(DualSenseButtons.Cross, 8, 0x20)]
    [TestCase(DualSenseButtons.Circle, 8, 0x40)]
    [TestCase(DualSenseButtons.Triangle, 8, 0x80)]
    [TestCase(DualSenseButtons.L1, 9, 0x01)]
    [TestCase(DualSenseButtons.R1, 9, 0x02)]
    [TestCase(DualSenseButtons.L2, 9, 0x04)]
    [TestCase(DualSenseButtons.R2, 9, 0x08)]
    [TestCase(DualSenseButtons.Create, 9, 0x10)]
    [TestCase(DualSenseButtons.Options, 9, 0x20)]
    [TestCase(DualSenseButtons.L3, 9, 0x40)]
    [TestCase(DualSenseButtons.R3, 9, 0x80)]
    [TestCase(DualSenseButtons.PS, 10, 0x01)]
    [TestCase(DualSenseButtons.Touchpad, 10, 0x02)]
    [TestCase(DualSenseButtons.MicMute, 10, 0x04)]
    [TestCase(DualSenseButtons.LeftFunction, 10, 0x10)]
    [TestCase(DualSenseButtons.RightFunction, 10, 0x20)]
    [TestCase(DualSenseButtons.L4, 10, 0x40)]
    [TestCase(DualSenseButtons.R4, 10, 0x80)]
    public void PushInput_EveryMappedButtonBit_SetsOnlyItsWireBit(DualSenseButtons target, int wireOffset, int wireMask)
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());
        controller.ButtonMappings = VirtualInputMapper.DualSenseTable([Entry(target.ToString(), "Cross")]);
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[7] |= 0x20; // Cross is the only pressed source

        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);
        Assert.Multiple(() =>
        {
            Assert.That(report[OffsetFaceAndDpad] & 0xF0, Is.EqualTo(wireOffset == OffsetFaceAndDpad ? wireMask : 0));
            Assert.That(report[OffsetShoulderButtons], Is.EqualTo(wireOffset == OffsetShoulderButtons ? wireMask : 0));
            Assert.That(report[OffsetUtilityButtons], Is.EqualTo(wireOffset == OffsetUtilityButtons ? wireMask : 0));
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
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());
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
    public void PushInput_TriggersTouchpadAndMotion_MapFieldForField()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[4] = 200;
        payload[5] = 100;

        payload[32] = 0x05; // touch 1 active, tracking id 5
        payload[33] = 0x23; // x = 0x123
        payload[34] = 0x61; // x high nibble + y low nibble
        payload[35] = 0x45; // y high byte: y = 0x456
        payload[36] = 0x80; // touch 2 inactive, id 0

        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(15), 0x0102); // gyro X
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(17), 0x0304); // gyro Y
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(19), 0x0506); // gyro Z
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(21), 0x1112); // accel X
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(23), 0x1314); // accel Y
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(25), 0x1516); // accel Z

        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);
        Assert.Multiple(() =>
        {
            Assert.That(report[5], Is.EqualTo(200));
            Assert.That(report[6], Is.EqualTo(100));
            Assert.That(report[33], Is.EqualTo(0x05));
            Assert.That(report[34], Is.EqualTo(0x23));
            Assert.That(report[35], Is.EqualTo(0x61));
            Assert.That(report[36], Is.EqualTo(0x45));
            Assert.That(report[37], Is.EqualTo(0x80), "the inactive touch point keeps the persona's 0x80 marker");
            Assert.That(report[38], Is.Zero);
            Assert.That(report[39], Is.Zero);
            Assert.That(report[40], Is.Zero);
            Assert.That(ReadInt16(report, 16), Is.EqualTo(0x0102), "gyro X is the first motion word");
            Assert.That(ReadInt16(report, 18), Is.EqualTo(0x0304), "physical gyro Y is the second wire word");
            Assert.That(ReadInt16(report, 20), Is.EqualTo(0x0506), "physical gyro Z is the third wire word");
            Assert.That(ReadInt16(report, 22), Is.EqualTo(0x1112));
            Assert.That(ReadInt16(report, 24), Is.EqualTo(0x1314));
            Assert.That(ReadInt16(report, 26), Is.EqualTo(0x1516));
        });
    }

    [Test]
    public void PushInput_RawReport_IsServedVerbatimUntilASynthesisedPushFollows()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] raw = EmulationAdapterTestSupport.RawReport(0x5A);
        byte[] rawPayload = EmulationAdapterTestSupport.NeutralPayload();
        rawPayload[0] = 1; // deliberately different from the raw byte at report offset 1

        controller.PushInput(EmulationAdapterTestSupport.Report(rawPayload, raw));

        Assert.That(EmulationAdapterTestSupport.Pull(device, 64), Is.EqualTo(raw));
        Assert.That(controller.ShouldUseRaw, Is.True);

        byte[] synthetic = EmulationAdapterTestSupport.NeutralPayload();
        synthetic[0] = 7;
        controller.PushInput(EmulationAdapterTestSupport.Report(synthetic));

        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);
        Assert.Multiple(() =>
        {
            Assert.That(report[0], Is.EqualTo(0x01));
            Assert.That(report[OffsetSticks], Is.EqualTo(7));
            Assert.That(report, Is.Not.EqualTo(raw), "the synthesised report replaces the raw one");
        });
    }

    [TestCase(63, 0x01)]
    [TestCase(64, 0x02)]
    public void PushInput_InvalidRawReport_FallsBackToTheSynthesisedState(int rawLength, byte reportId)
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] raw = new byte[rawLength];
        raw[0] = reportId;
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[0] = 42;

        controller.PushInput(EmulationAdapterTestSupport.Report(payload, raw));

        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);
        Assert.That(report[OffsetSticks], Is.EqualTo(42));
    }

    [Test]
    public void PushInput_CustomMapping_IgnoresTheRawReport()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());
        controller.ButtonMappings = VirtualInputMapper.DualSenseTable([Entry("Square", "Cross")]);
        byte[] raw = EmulationAdapterTestSupport.RawReport(0x5A);
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[0] = 9;

        controller.PushInput(EmulationAdapterTestSupport.Report(payload, raw));

        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);
        Assert.Multiple(() =>
        {
            Assert.That(controller.ShouldUseRaw, Is.False);
            Assert.That(report[OffsetSticks], Is.EqualTo(9));
            Assert.That(report, Is.Not.EqualTo(raw));
        });
    }

    [Test]
    public void PushInput_BuiltInMappings_TakeTheRawPath()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] raw = EmulationAdapterTestSupport.RawReport(0x5A);

        controller.ButtonMappings = VirtualInputMapper.DualSenseDefaultTable;
        controller.PushInput(EmulationAdapterTestSupport.Report(EmulationAdapterTestSupport.NeutralPayload(), raw));
        Assert.That(EmulationAdapterTestSupport.Pull(device, 64), Is.EqualTo(raw));

        controller.ButtonMappings = null;
        controller.PushInput(EmulationAdapterTestSupport.Report(EmulationAdapterTestSupport.NeutralPayload(), raw));
        Assert.That(EmulationAdapterTestSupport.Pull(device, 64), Is.EqualTo(raw));
    }

    [Test]
    public void PushInput_FirstReportBattery_ReachesTheWireOnceAndPushBatteryOverridesIt()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] first = EmulationAdapterTestSupport.NeutralPayload();
        first[PayloadBattery] = 0x28;

        controller.PushInput(EmulationAdapterTestSupport.Report(first));
        Assert.That(EmulationAdapterTestSupport.Pull(device, 64)[OffsetBattery], Is.EqualTo(0x28));

        byte[] later = EmulationAdapterTestSupport.NeutralPayload();
        later[PayloadBattery] = 0x05;
        controller.PushInput(EmulationAdapterTestSupport.Report(later));
        Assert.That(EmulationAdapterTestSupport.Pull(device, 64)[OffsetBattery], Is.EqualTo(0x28),
            "only the first report's battery initializes the meta state");

        controller.PushBattery(new BatteryState(0x19));
        Assert.That(EmulationAdapterTestSupport.Pull(device, 64)[OffsetBattery], Is.EqualTo(0x19));

        controller.PushInput(EmulationAdapterTestSupport.Report(later));
        Assert.That(EmulationAdapterTestSupport.Pull(device, 64)[OffsetBattery], Is.EqualTo(0x19),
            "the synthesised state carries the latest meta battery");
    }

    [Test]
    public void PushBattery_BeforeTheFirstInputReport_IsOverwrittenByThatReport()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());
        controller.PushBattery(new BatteryState(0x19));
        Assert.That(EmulationAdapterTestSupport.Pull(device, 64)[OffsetBattery], Is.EqualTo(0x19),
            "the meta lane applies immediately, like the wrapper's PushMeta");

        byte[] first = EmulationAdapterTestSupport.NeutralPayload();
        first[PayloadBattery] = 0x28;
        controller.PushInput(EmulationAdapterTestSupport.Report(first));

        Assert.That(EmulationAdapterTestSupport.Pull(device, 64)[OffsetBattery], Is.EqualTo(0x28),
            "the first input report overwrites a pre-input battery push, like the wrapper's EnsureInitialMeta");
    }

    [Test]
    public void PushBattery_EveryRawByte_RoundTripsToTheWireByte()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());
        controller.PushInput(EmulationAdapterTestSupport.Report(EmulationAdapterTestSupport.NeutralPayload()));

        for (int raw = 0; raw < 256; raw++)
        {
            controller.PushBattery(new BatteryState((byte)raw));
            Assert.That(EmulationAdapterTestSupport.Pull(device, 64)[OffsetBattery], Is.EqualTo((byte)raw));
        }
    }

    [Test]
    public void PushConnectionStatus_IsANoOp()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[0] = 33;
        controller.PushInput(EmulationAdapterTestSupport.Report(payload));
        byte[] before = EmulationAdapterTestSupport.Pull(device, 64);

        Assert.That(() => controller.PushConnectionStatus(new ConnectionStatus(0xFF)), Throws.Nothing);
        byte[] after = EmulationAdapterTestSupport.Pull(device, 64);

        Assert.Multiple(() =>
        {
            Assert.That(after[OffsetSticks], Is.EqualTo(before[OffsetSticks]));
            Assert.That(after[OffsetBattery], Is.EqualTo(before[OffsetBattery]));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Construction_WrapsTheReadyPersonaAndPushes(bool edge)
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity(), edge);
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[0] = 77;

        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);
        Assert.Multiple(() =>
        {
            Assert.That(controller.Mode, Is.EqualTo(EmulationMode.DualSense));
            Assert.That(controller.DeviceHandle, Is.Null, "an Emulation persona is an object, not a native handle");
            Assert.That(report[OffsetSticks], Is.EqualTo(77));
        });
    }

    [Test]
    public void Dispose_StopsPushesAndOutputDeliveryWithoutRemovingTheDevice()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        EmulationDualSenseController controller = CreateController(device, outputs);
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[0] = 11;
        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        controller.Dispose();
        byte[] ignored = EmulationAdapterTestSupport.NeutralPayload();
        ignored[0] = 99;
        controller.PushInput(EmulationAdapterTestSupport.Report(ignored));
        device.OnOutputReport(CreateOutputWire());

        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);
        Assert.Multiple(() =>
        {
            Assert.That(report[OffsetSticks], Is.EqualTo(11), "a disposed adapter ignores later input pushes");
            Assert.That(outputs.OutputStates, Is.Empty, "a disposed adapter unsubscribes from the persona's output event");
            Assert.That(controller.PushRawInput(EmulationAdapterTestSupport.RawReport(0x77)), Is.False);
            Assert.That(controller.DeviceHandle, Is.Null);
            Assert.That(() => controller.Dispose(), Throws.Nothing);
        });
    }

    private static byte[] CreateOutputWire()
    {
        byte[] payload = new byte[47];
        payload[0] = 0x01;
        byte[] wire = new byte[48];
        wire[0] = 0x02;
        Buffer.BlockCopy(payload, 0, wire, 1, payload.Length);
        return wire;
    }

    /// <summary>
    /// Builds a host output report 0x02 whose valid-flag byte 1 carries (or omits) the mute-light
    /// allowance and whose mute-light mode byte is <paramref name="mode"/> (0 off, 1 on,
    /// 2 breathing).
    /// </summary>
    /// <param name="mode">Mute-light mode the host requests.</param>
    /// <param name="allowed">Whether the mute-light allowance flag is set.</param>
    /// <returns>The id-prefixed wire payload.</returns>
    private static byte[] CreateMuteLightWire(byte mode, bool allowed = true)
    {
        byte[] wire = new byte[48];
        wire[0] = 0x02;
        wire[2] = allowed ? (byte)0x01 : (byte)0x00;
        wire[9] = mode;
        return wire;
    }

    /// <summary>Pushes one neutral physical report and returns the served host-status byte.</summary>
    private static byte ReadServedHostStatus(DualSenseDevice device, EmulationDualSenseController controller)
    {
        controller.PushInput(EmulationAdapterTestSupport.Report(EmulationAdapterTestSupport.NeutralPayload()));
        return EmulationAdapterTestSupport.Pull(device, 64)[OffsetHostStatus];
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    public void HostMuteLight_AllowedMode_IsEchoedOnFollowingInputPushes(byte mode, bool expectedMicMuted)
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());

        device.OnOutputReport(CreateMuteLightWire(mode));

        // The persona never sets the echo field itself: the adapter's cache is the only writer.
        controller.PushInput(EmulationAdapterTestSupport.Report(EmulationAdapterTestSupport.NeutralPayload()));
        byte[] report = EmulationAdapterTestSupport.Pull(device, 64);

        Assert.That((report[OffsetHostStatus] & MicMutedBit) != 0, Is.EqualTo(expectedMicMuted));
    }

    [Test]
    public void HostMuteLight_OffAfterOn_ClearsTheEcho()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());

        device.OnOutputReport(CreateMuteLightWire(1));
        device.OnOutputReport(CreateMuteLightWire(0));

        Assert.That(ReadServedHostStatus(device, controller) & MicMutedBit, Is.Zero);
    }

    [Test]
    public void HostMuteLight_WithoutTheAllowFlag_KeepsTheCachedEcho()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());

        device.OnOutputReport(CreateMuteLightWire(1));
        // A report that does not allow the mute light to change must not disturb the echo.
        device.OnOutputReport(CreateMuteLightWire(0, false));

        Assert.That((ReadServedHostStatus(device, controller) & MicMutedBit) != 0, Is.True);
    }

    [Test]
    public void HostMuteLight_EchoSurvivesLaterInputPushes()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());

        device.OnOutputReport(CreateMuteLightWire(1));

        for (int i = 0; i < 3; i++)
        {
            byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
            payload[0] = (byte)(100 + i);
            controller.PushInput(EmulationAdapterTestSupport.Report(payload));
            Assert.That((EmulationAdapterTestSupport.Pull(device, 64)[OffsetHostStatus] & MicMutedBit) != 0, Is.True,
                $"push {i}");
        }
    }

    [Test]
    public void HostMuteLight_Dispose_ClearsTheCachedEcho()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        EmulationDualSenseController controller = CreateController(device, new RecordingDualSenseOutputs());
        device.OnOutputReport(CreateMuteLightWire(1));
        Assert.That(MuteLightModeField.GetValue(controller), Is.EqualTo((byte)1), "the host report populated the cache");

        controller.Dispose();

        Assert.That(MuteLightModeField.GetValue(controller), Is.EqualTo((byte)0), "Dispose clears the cached echo");
    }
}