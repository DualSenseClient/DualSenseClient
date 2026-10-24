using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Emulation.Devices.Xbox360;

namespace DualSenseClient.Tests.Controllers.Emulation;

/// <summary>
/// Output-side tests of <see cref="VirtualXbox360Controller"/>: a synthetic 8-byte rumble packet
/// (id 0x00, size 0x08) is delivered to the persona device and must reach
/// <see cref="IDualSenseOutputs.SetVibration"/> with the physical pad's orientation — the large
/// (low-frequency) motor as the left value and the small (high-frequency) motor as the right value.
/// The controller's LED packet is not a rumble command and is ignored.
/// </summary>
[TestFixture]
public sealed class VirtualXbox360ControllerOutputTests
{
    private static byte[] RumblePacket(byte left, byte right) => new byte[]
    {
        0x00, 0x08, 0x00, left, right, 0x00, 0x00, 0x00
    };

    [Test]
    public void HostOutput_RumblePacket_ForwardsTheMotorOrientation()
    {
        Xbox360Device device = new Xbox360Device(new Xbox360Identity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        _ = new VirtualXbox360Controller(device, outputs);

        device.OnOutputReport(RumblePacket(0x11, 0x22));

        Assert.Multiple(() =>
        {
            Assert.That(outputs.Vibrations, Has.Count.EqualTo(1));
            Assert.That(outputs.Vibrations[0], Is.EqualTo(((byte)0x11, (byte)0x22)), "the large motor drives the left output");
            Assert.That(outputs.OutputStates, Is.Empty, "the Xbox 360 path never forwards an output state");
        });
    }

    [Test]
    public void HostOutput_LedPacket_IsIgnored()
    {
        Xbox360Device device = new Xbox360Device(new Xbox360Identity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        _ = new VirtualXbox360Controller(device, outputs);

        device.OnOutputReport(new byte[]
        {
            0x01, 0x03, 0x00
        });

        Assert.That(outputs.Vibrations, Is.Empty);
    }

    [Test]
    public void HostOutput_TruncatedRumblePacket_IsIgnored()
    {
        Xbox360Device device = new Xbox360Device(new Xbox360Identity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        _ = new VirtualXbox360Controller(device, outputs);

        device.OnOutputReport(RumblePacket(0x11, 0x22).AsSpan(0, 7));

        Assert.That(outputs.Vibrations, Is.Empty);
    }
}