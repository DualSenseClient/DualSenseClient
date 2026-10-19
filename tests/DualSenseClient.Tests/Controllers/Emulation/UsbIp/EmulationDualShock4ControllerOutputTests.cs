using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Emulation.UsbIp;
using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Tests.Controllers.Emulation.UsbIp;

/// <summary>
/// Output-side tests of <see cref="EmulationDualShock4Controller"/>: a synthetic host output report
/// 0x05 is delivered to the persona device and the adapter must honor the host's update flags. A
/// report updates only the fields its flags enable: rumble goes to
/// <see cref="IDualSenseOutputs.SetVibration"/> and the lightbar is rebuilt into the same payload
/// the libVIIPER wrapper sends, while fields the report does not update retain their cached values.
/// </summary>
[TestFixture]
public sealed class EmulationDualShock4ControllerOutputTests
{
    /// <summary>Report id of the host output report 0x05.</summary>
    private const byte OutputReportId = 0x05;

    /// <summary>Builds the id-prefixed 32-byte host transfer from the decoded fields.</summary>
    private static byte[] Wire(byte flags, byte rightMotor, byte leftMotor, byte red, byte green, byte blue, byte flashOn, byte flashOff)
    {
        byte[] wire = new byte[32];
        wire[0] = OutputReportId;
        wire[1] = flags;
        wire[4] = rightMotor;
        wire[5] = leftMotor;
        wire[6] = red;
        wire[7] = green;
        wire[8] = blue;
        wire[9] = flashOn;
        wire[10] = flashOff;
        return wire;
    }

    [Test]
    public void HostOutput_AllUpdateFlags_UsesTheHostRumbleAndLightbar()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        EmulationDualShock4Controller controller = new EmulationDualShock4Controller(device, outputs);

        device.OnOutputReport(Wire(0x07, 0x22, 0x33, 0x01, 0x02, 0x03, 0x07, 0x08));

        byte[] expected = new byte[SetStateData.PayloadSize];
        expected[1] = (byte)ValidFlags.AllowLedColor;
        expected[2] = 0x22; // right (small) motor
        expected[3] = 0x33; // left (large) motor
        expected[38] = (byte)ValidFlags.AllowColorFadeAnim;
        expected[41] = 0x02;
        expected[44] = 0x01;
        expected[45] = 0x02;
        expected[46] = 0x03;
        Assert.Multiple(() =>
        {
            Assert.That(outputs.Vibrations, Has.Count.EqualTo(1));
            Assert.That(outputs.Vibrations[0], Is.EqualTo(((byte)0x33, (byte)0x22)), "large motor first, small motor second");
            Assert.That(outputs.OutputStates, Has.Count.EqualTo(1));
            Assert.That(outputs.OutputStates[0].AsSpan().ToArray(), Is.EqualTo(expected));
            Assert.That(controller.DeviceHandle, Is.Null);
        });
    }

    [Test]
    public void HostOutput_NoUpdateFlags_ChangesNothing()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        EmulationDualShock4Controller controller = new EmulationDualShock4Controller(device, outputs);
        int events = 0;
        controller.OutputStateReceived += _ => events++;

        device.OnOutputReport(Wire(0x00, 0x11, 0x22, 0x04, 0x05, 0x06, 0x00, 0x00));

        Assert.Multiple(() =>
        {
            Assert.That(outputs.Vibrations, Is.Empty);
            Assert.That(outputs.OutputStates, Is.Empty);
            Assert.That(events, Is.Zero);
        });
    }

    [Test]
    public void HostOutput_RumbleOnly_KeepsTheCachedLightbarColor()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        _ = new EmulationDualShock4Controller(device, outputs);

        device.OnOutputReport(Wire(0x02, 0x00, 0x00, 0x0A, 0x0B, 0x0C, 0x00, 0x00));
        device.OnOutputReport(Wire(0x01, 0x22, 0x33, 0x00, 0x00, 0x00, 0x00, 0x00));

        Assert.That(outputs.Vibrations, Has.Count.EqualTo(1));
        Assert.That(outputs.Vibrations[0], Is.EqualTo(((byte)0x33, (byte)0x22)));
        Assert.That(outputs.OutputStates, Has.Count.EqualTo(2));
        SetStateData second = outputs.OutputStates[1];
        Assert.Multiple(() =>
        {
            Assert.That(second.RumbleLeft, Is.EqualTo(0x33));
            Assert.That(second.RumbleRight, Is.EqualTo(0x22));
            Assert.That(second.LedRed, Is.EqualTo(0x0A));
            Assert.That(second.LedGreen, Is.EqualTo(0x0B));
            Assert.That(second.LedBlue, Is.EqualTo(0x0C));
            Assert.That(second.ValidFlag1, Is.EqualTo(ValidFlags.None));
            Assert.That(second.ValidFlag2, Is.EqualTo(ValidFlags.None));
            Assert.That(second.LightFadeAnimation, Is.Zero);
        });
    }

    [Test]
    public void HostOutput_LightbarOnly_KeepsTheCachedRumble()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        _ = new EmulationDualShock4Controller(device, outputs);

        device.OnOutputReport(Wire(0x01, 0x22, 0x33, 0x00, 0x00, 0x00, 0x00, 0x00));
        device.OnOutputReport(Wire(0x02, 0x00, 0x00, 0x01, 0x02, 0x03, 0x00, 0x00));

        Assert.That(outputs.Vibrations, Has.Count.EqualTo(1));
        Assert.That(outputs.Vibrations[0], Is.EqualTo(((byte)0x33, (byte)0x22)));
        Assert.That(outputs.OutputStates, Has.Count.EqualTo(2));
        SetStateData second = outputs.OutputStates[1];
        Assert.Multiple(() =>
        {
            Assert.That(second.RumbleLeft, Is.EqualTo(0x33));
            Assert.That(second.RumbleRight, Is.EqualTo(0x22));
            Assert.That(second.LedRed, Is.EqualTo(0x01));
            Assert.That(second.LedGreen, Is.EqualTo(0x02));
            Assert.That(second.LedBlue, Is.EqualTo(0x03));
            Assert.That(second.ValidFlag1, Is.EqualTo(ValidFlags.AllowLedColor));
            Assert.That(second.ValidFlag2 & ValidFlags.AllowColorFadeAnim, Is.Not.EqualTo(ValidFlags.None));
            Assert.That(second.LightFadeAnimation, Is.EqualTo(0x02));
        });
    }

    [Test]
    public void HostOutput_FlashOnly_ChangesNothing()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        EmulationDualShock4Controller controller = new EmulationDualShock4Controller(device, outputs);
        int events = 0;
        controller.OutputStateReceived += _ => events++;

        device.OnOutputReport(Wire(0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0A, 0x14));

        Assert.Multiple(() =>
        {
            Assert.That(outputs.Vibrations, Is.Empty);
            Assert.That(outputs.OutputStates, Is.Empty);
            Assert.That(events, Is.Zero);
        });
    }

    [Test]
    public void HostOutput_ForwardsToTheOutputsLaneBeforeRaisingTheEventWithTheSamePayload()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        EmulationDualShock4Controller controller = new EmulationDualShock4Controller(device, outputs);
        SetStateData received = default;

        controller.OutputStateReceived += payload =>
        {
            Assert.That(outputs.OutputStates, Has.Count.EqualTo(1), "the outputs lane must be fed before the event is raised");
            received = payload;
        };

        device.OnOutputReport(Wire(0x07, 0x22, 0x33, 0x01, 0x02, 0x03, 0x00, 0x00));

        Assert.That(received.AsSpan().ToArray(), Is.EqualTo(outputs.OutputStates[0].AsSpan().ToArray()));
    }

    [TestCase(0x00)]
    [TestCase(0x06)]
    public void HostOutput_UnknownReportId_RaisesNothing(byte reportId)
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        _ = new EmulationDualShock4Controller(device, outputs);
        byte[] wire = Wire(0x07, 0x22, 0x33, 1, 2, 3, 0, 0);
        wire[0] = reportId;

        device.OnOutputReport(wire);

        Assert.Multiple(() =>
        {
            Assert.That(outputs.Vibrations, Is.Empty);
            Assert.That(outputs.OutputStates, Is.Empty);
        });
    }

    [Test]
    public void HostOutput_TruncatedPayload_RaisesNothing()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        _ = new EmulationDualShock4Controller(device, outputs);

        device.OnOutputReport(Wire(0x07, 0x22, 0x33, 1, 2, 3, 0, 0).AsSpan(0, 10));

        Assert.That(outputs.Vibrations, Is.Empty);
    }
}