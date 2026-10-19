using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Controllers.Emulation.UsbIp;
using DualSenseClient.Emulation.Devices.DualSense;
using DualSenseClient.VIIPER.DualSense;

namespace DualSenseClient.Tests.Controllers.Emulation.UsbIp;

/// <summary>
/// Output-side tests of <see cref="EmulationDualSenseController"/>: a synthetic host output report
/// 0x02 is delivered to the persona device, the adapter rebuilds the physical 47-byte payload and
/// the result is compared byte-for-byte with the libVIIPER path's
/// <see cref="VirtualDualSenseController.BuildOutputPayload(DSOutputState, bool)"/> for the same
/// wire payload and both rumble-mode encodings. The ordering of the outputs-lane call and the
/// <see cref="EmulationDualSenseController.OutputStateReceived"/> event is pinned too.
/// </summary>
[TestFixture]
public sealed class EmulationDualSenseControllerOutputTests
{
    /// <summary>Report id of the host output report 0x02.</summary>
    private const byte OutputReportId = 0x02;

    /// <summary>
    /// Builds the libVIIPER output state from a 47-byte wire payload the same way the existing
    /// <c>VirtualDualSenseControllerOutputTests</c> fixture does.
    /// </summary>
    private static DSOutputState ViiperOutput(byte[] payload)
    {
        Assert.That(payload.Length, Is.EqualTo(47));
        byte[] right = new byte[11];
        byte[] left = new byte[11];
        Buffer.BlockCopy(payload, 10, right, 0, 11);
        Buffer.BlockCopy(payload, 21, left, 0, 11);
        return new DSOutputState
        {
            Flags0 = payload[0],
            Flags1 = payload[1],
            RumbleSmall = payload[2],
            RumbleLarge = payload[3],
            VolumeHeadphones = payload[4],
            VolumeSpeaker = payload[5],
            VolumeMic = payload[6],
            AudioControl = payload[7],
            MuteLightMode = payload[8],
            MuteControl = payload[9],
            TriggerRight = right,
            TriggerLeft = left,
            HostTimestamp = (uint)(payload[32] | (payload[33] << 8) | (payload[34] << 16) | (payload[35] << 24)),
            MotorPower = payload[36],
            AudioControl2 = payload[37],
            Flags3 = payload[38],
            HapticFilter = payload[39],
            UnkByte = payload[40],
            LightFade = payload[41],
            LightBrightness = payload[42],
            PlayerLeds = payload[43],
            LedRed = payload[44],
            LedGreen = payload[45],
            LedBlue = payload[46]
        };
    }

    /// <summary>Wraps a 47-byte payload into the id-prefixed 48-byte host transfer.</summary>
    private static byte[] Wire(byte[] payload)
    {
        byte[] wire = new byte[48];
        wire[0] = OutputReportId;
        Buffer.BlockCopy(payload, 0, wire, 1, payload.Length);
        return wire;
    }

    /// <summary>
    /// A rich game write: v1 rumble at distinct magnitudes, volumes, audio control, mute LED,
    /// both trigger blocks, timestamp, motor power reduction, brightness/fade flags and lightbar.
    /// </summary>
    private static byte[] RichGamePayload()
    {
        byte[] p = new byte[47];
        p[0] = 0x3F; // v1 rumble | haptics select | R2 FFB | L2 FFB | headphone volume | speaker volume
        p[2] = 0xAA;
        p[3] = 0xBB;
        p[4] = 0x11;
        p[5] = 0x22;
        p[6] = 0x33;
        p[7] = 0x01;
        p[8] = 0x01;
        p[9] = 0x44;
        p[10] = 0x25;
        p[11] = 0x10;
        p[12] = 0x20;
        p[13] = 0x30;
        p[21] = 0x21;
        p[22] = 0x40;
        p[23] = 0x50;
        p[24] = 0x60;
        p[32] = 0xEF; // host timestamp 0xDEADBEEF
        p[33] = 0xBE;
        p[34] = 0xAD;
        p[35] = 0xDE;
        p[36] = 0x55;
        p[37] = 0x77;
        p[38] = 0x03; // brightness change | color fade animation
        p[39] = 0x01;
        p[41] = 0x01;
        p[42] = 0x02;
        p[43] = 0x1F;
        p[44] = 0xC0;
        p[45] = 0xFF;
        p[46] = 0xEE;
        return p;
    }

    [TestCase(true)]
    [TestCase(false)]
    public void HostOutput_WirePayload_ProducesTheSamePhysicalPayloadAsTheViiperWrapper(bool vibrationV2)
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        EmulationDualSenseController controller = new EmulationDualSenseController(device, outputs, vibrationV2);
        byte[] payload = RichGamePayload();

        device.OnOutputReport(Wire(payload));

        SetStateData expected = VirtualDualSenseController.BuildOutputPayload(ViiperOutput(payload), vibrationV2);
        Assert.That(outputs.OutputStates, Has.Count.EqualTo(1));
        Assert.That(outputs.OutputStates[0].AsSpan().ToArray(), Is.EqualTo(expected.AsSpan().ToArray()));
        Assert.That(controller.DeviceHandle, Is.Null);
    }

    [Test]
    public void HostOutput_ForwardsToTheOutputsLaneBeforeRaisingTheEventWithTheSamePayload()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        EmulationDualSenseController controller = new EmulationDualSenseController(device, outputs, true);
        SetStateData received = default;

        controller.OutputStateReceived += payload =>
        {
            Assert.That(outputs.OutputStates, Has.Count.EqualTo(1), "the outputs lane must be fed before the event is raised");
            received = payload;
        };

        device.OnOutputReport(Wire(RichGamePayload()));

        Assert.That(received.AsSpan().ToArray(), Is.EqualTo(outputs.OutputStates[0].AsSpan().ToArray()));
    }

    [Test]
    public void HostOutput_V1SelectorOnV2Pad_TranslatesTheSelector()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        EmulationDualSenseController controller = new EmulationDualSenseController(device, outputs, true);

        device.OnOutputReport(Wire(RichGamePayload()));

        SetStateData payload = outputs.OutputStates[0];
        Assert.Multiple(() =>
        {
            Assert.That((byte)payload.ValidFlag0 & 0x01, Is.Zero, "the v1 compatibility bit must be cleared for a v2 pad");
            Assert.That((byte)payload.ValidFlag0 & 0x02, Is.EqualTo(0x02), "haptics select must stay set");
            Assert.That((byte)payload.ValidFlag2 & 0x04, Is.EqualTo(0x04), "the v2 selector must be set for a v2 pad");
            Assert.That((byte)payload.ValidFlag0 & 0x0C, Is.EqualTo(0x0C), "the trigger allow bits must be preserved");
            Assert.That(payload.RumbleRight, Is.EqualTo(0xAA));
            Assert.That(payload.RumbleLeft, Is.EqualTo(0xBB));
            Assert.That(payload.HostTimestamp, Is.EqualTo(0xDEADBEEFu));
            Assert.That((PlayerLedMask)payload.PlayerLeds, Is.EqualTo((PlayerLedMask)0x1F));
            Assert.That(payload.LedRed, Is.EqualTo(0xC0));
            Assert.That(payload.LedGreen, Is.EqualTo(0xFF));
            Assert.That(payload.LedBlue, Is.EqualTo(0xEE));
        });
    }

    [Test]
    public void HostOutput_WithoutRumble_ClearsTheSelectorSoThePadRetainsItsMotors()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        EmulationDualSenseController controller = new EmulationDualSenseController(device, outputs, true);
        byte[] payload = new byte[47];
        payload[0] = 0x0C; // trigger-only write: no rumble selector
        payload[2] = 0x7F; // stale motor bytes the pad must ignore
        payload[3] = 0x7F;

        device.OnOutputReport(Wire(payload));

        SetStateData sent = outputs.OutputStates[0];
        Assert.Multiple(() =>
        {
            Assert.That((byte)sent.ValidFlag0 & 0x03, Is.Zero);
            Assert.That((byte)sent.ValidFlag2 & 0x04, Is.Zero);
            Assert.That(sent.RumbleRight, Is.EqualTo(0x7F));
            Assert.That(sent.RumbleLeft, Is.EqualTo(0x7F));
        });
    }

    [Test]
    public void HostOutput_FieldsTheEngineDoesNotDecode_AreServedAsZero()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        EmulationDualSenseController controller = new EmulationDualSenseController(device, outputs, false);
        byte[] payload = RichGamePayload();
        payload[40] = 0xAB; // reference unknown byte, not exposed by DualSenseOutputReport
        payload[38] |= 0xF0; // undefined high nibble of valid-flag byte 2

        device.OnOutputReport(Wire(payload));

        DSOutputState decoded = ViiperOutput(payload);
        decoded.UnkByte = 0;
        decoded.Flags3 = (byte)(payload[38] & 0x0F);
        SetStateData expected = VirtualDualSenseController.BuildOutputPayload(decoded, false);
        SetStateData sent = outputs.OutputStates[0];
        Assert.Multiple(() =>
        {
            Assert.That(sent.AsSpan()[40], Is.Zero);
            Assert.That((byte)sent.ValidFlag2 & 0xF0, Is.Zero);
            Assert.That(sent.AsSpan().ToArray(), Is.EqualTo(expected.AsSpan().ToArray()));
        });
    }

    [TestCase(0x02, 0x00)]
    [TestCase(0x03, 0x01)]
    public void HostOutput_HapticFilterUndefinedBits_AreDroppedToTheDefinedBit(byte wireValue, byte servedValue)
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        _ = new EmulationDualSenseController(device, outputs, false);
        byte[] payload = RichGamePayload();
        payload[39] = wireValue; // the defined low-pass-filter bit is bit 0; the other bits have no decoded accessor

        device.OnOutputReport(Wire(payload));

        DSOutputState decoded = ViiperOutput(payload);
        decoded.HapticFilter = servedValue; // the decoded report exposes only bit 0, so only the defined bit can ride
        SetStateData expected = VirtualDualSenseController.BuildOutputPayload(decoded, false);
        SetStateData sent = outputs.OutputStates[0];
        Assert.Multiple(() =>
        {
            Assert.That(sent.AsSpan()[39], Is.EqualTo(servedValue));
            Assert.That(sent.AsSpan().ToArray(), Is.EqualTo(expected.AsSpan().ToArray()));
        });
    }

    [TestCase(0x00)]
    [TestCase(0x03)]
    public void HostOutput_UnknownReportId_RaisesNothing(byte reportId)
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        _ = new EmulationDualSenseController(device, outputs, false);
        byte[] wire = Wire(RichGamePayload());
        wire[0] = reportId;

        device.OnOutputReport(wire);

        Assert.That(outputs.OutputStates, Is.Empty);
    }

    [Test]
    public void HostOutput_TruncatedPayload_RaisesNothing()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        _ = new EmulationDualSenseController(device, outputs, false);

        device.OnOutputReport(Wire(RichGamePayload()).AsSpan(0, 47));

        Assert.That(outputs.OutputStates, Is.Empty);
    }
}