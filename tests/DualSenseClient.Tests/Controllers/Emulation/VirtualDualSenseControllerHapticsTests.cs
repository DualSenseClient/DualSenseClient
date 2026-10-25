using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Emulation.Devices.DualSense;
using DualSenseClient.Emulation.Devices.DualShock4;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.Emulation;

/// <summary>
/// Pins the adapter's realtime-haptics lane and the persona access points. The persona raises
/// <see cref="DualSenseDevice.RealtimeHapticsReceived"/> with a view over a reusable scratch buffer,
/// so the adapter must copy the rear voice-coil pair to an array before invoking its own
/// subscribers; disposal unsubscribes the lane. Both adapters also expose the persona they wrap so
/// the service can build the speaker captures from it.
/// </summary>
[TestFixture]
public sealed class VirtualDualSenseControllerHapticsTests
{
    /// <summary>Isochronous OUT endpoint address of the speaker path.</summary>
    private const byte AudioOutEndpointAddress = 0x01;

    /// <summary>Bytes of one speaker sample frame: four interleaved 16-bit channels.</summary>
    private const int SpeakerFrameBytes = 8;

    /// <summary>Nominal speaker packet length: 48 one-millisecond frames of four 16-bit channels.</summary>
    private const int SpeakerPacketBytes = 384;

    /// <summary>Frames carried by a nominal speaker packet.</summary>
    private const int SpeakerFrames = SpeakerPacketBytes / SpeakerFrameBytes;

    /// <summary>Bytes of one extracted haptics frame: channels 2 and 3 of a speaker frame.</summary>
    private const int HapticsFrameBytes = 4;

    /// <summary>Offset of the rear-channel pair inside one speaker frame.</summary>
    private const int HapticsOffsetInSpeakerFrame = 4;

    [Test]
    public void OnIsoPacketReceived_QuadPacket_RaisesHapticsCopy()
    {
        DualSenseDevice device = CreateDevice();
        using VirtualDualSenseController controller = CreateController(device);
        byte[] packet = CreateSpeakerPacket(1);
        byte[]? received = null;
        controller.RealtimeHapticsReceived += payload => received = payload;

        device.OnIsoPacketReceived(AudioOutEndpointAddress, packet);

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.Not.Null, "the adapter did not raise the realtime lane");
            Assert.That(received, Has.Length.EqualTo(SpeakerFrames * HapticsFrameBytes), "one rear pair per whole frame");
            Assert.That(received, Is.EqualTo(ExtractHaptics(packet)), "channels 2 and 3 of every frame");
        });
    }

    [Test]
    public void OnIsoPacketReceived_SubscriberMutationDoesNotChangePersona()
    {
        DualSenseDevice device = CreateDevice();
        using VirtualDualSenseController controller = CreateController(device);
        List<byte[]> received = [];
        controller.RealtimeHapticsReceived += payload => received.Add(payload);

        device.OnIsoPacketReceived(AudioOutEndpointAddress, CreateSpeakerPacket(1));
        Assert.That(received, Has.Count.EqualTo(1));
        received[0].AsSpan().Fill(0x7F);

        byte[] second = CreateSpeakerPacket(2);
        device.OnIsoPacketReceived(AudioOutEndpointAddress, second);

        Assert.Multiple(() =>
        {
            Assert.That(received, Has.Count.EqualTo(2));
            Assert.That(received[1], Is.EqualTo(ExtractHaptics(second)), "a later packet carries the adapter's own copy");
            Assert.That(received[0], Has.All.EqualTo((byte)0x7F), "the earlier payload was a view over reused persona memory");
        });
    }

    [Test]
    public void Dispose_UnsubscribesRealtimeHaptics()
    {
        DualSenseDevice device = CreateDevice();
        VirtualDualSenseController controller = CreateController(device);
        int calls = 0;
        controller.RealtimeHapticsReceived += _ => calls++;
        byte[] packet = CreateSpeakerPacket(1);
        device.OnIsoPacketReceived(AudioOutEndpointAddress, packet);
        Assert.That(calls, Is.EqualTo(1));

        controller.Dispose();
        device.OnIsoPacketReceived(AudioOutEndpointAddress, packet);

        Assert.That(calls, Is.EqualTo(1), "the disposed adapter still received the lane");
    }

    [Test]
    public void ApplyForwardingSettings_GameHapticsDisabled_DropsTheRealtimeLane()
    {
        DualSenseDevice device = CreateDevice();
        using VirtualDualSenseController controller = CreateController(device);
        int calls = 0;
        controller.RealtimeHapticsReceived += _ => calls++;
        controller.ApplyForwardingSettings(new ForwardSettings
        {
            HapticsEnabled = false
        });

        device.OnIsoPacketReceived(AudioOutEndpointAddress, CreateSpeakerPacket(1));

        Assert.That(calls, Is.Zero, "the game's realtime haptics must not reach the audio forwarder");

        controller.ApplyForwardingSettings(new ForwardSettings
        {
            HapticsEnabled = true
        });
        device.OnIsoPacketReceived(AudioOutEndpointAddress, CreateSpeakerPacket(1));

        Assert.That(calls, Is.EqualTo(1), "re-enabling the channel must resume the lane");
    }

    [Test]
    public void Persona_ExposesTheUnderlyingDevice()
    {
        DualSenseDevice dualSense = CreateDevice();
        DualShock4Device dualShock4 = new DualShock4Device(new DualShock4Identity());
        using VirtualDualSenseController dualSenseController = CreateController(dualSense);
        using VirtualDualShock4Controller dualShock4Controller = new VirtualDualShock4Controller(dualShock4, new RecordingDualSenseOutputs());

        Assert.Multiple(() =>
        {
            Assert.That(dualSenseController.Persona, Is.SameAs(dualSense), "DualSense persona");
            Assert.That(dualShock4Controller.Persona, Is.SameAs(dualShock4), "DualShock 4 persona");
        });
    }

    /// <summary>Creates the persona device with the reference identity.</summary>
    private static DualSenseDevice CreateDevice() => new DualSenseDevice(new DualSenseIdentity());

    /// <summary>Creates the adapter under test around <paramref name="device"/>.</summary>
    private static VirtualDualSenseController CreateController(DualSenseDevice device) =>
        new VirtualDualSenseController(device, new RecordingDualSenseOutputs(), false);

    /// <summary>
    /// Creates a deterministic 384-byte speaker packet whose four interleaved 16-bit channels carry
    /// distinct patterns; <paramref name="variant"/> selects a different pattern for the same layout,
    /// so two packets differ byte for byte.
    /// </summary>
    private static byte[] CreateSpeakerPacket(short variant)
    {
        short[] quad = new short[SpeakerFrames * 4];
        for (int frame = 0; frame < SpeakerFrames; frame++)
        {
            quad[frame * 4] = (short)(variant * 1000 + frame);
            quad[frame * 4 + 1] = (short)(variant * 2000 - frame);
            quad[frame * 4 + 2] = (short)(variant * 3000 + frame * 3);
            quad[frame * 4 + 3] = (short)(variant * 4000 - frame * 5);
        }

        byte[] packet = new byte[quad.Length * sizeof(short)];
        Buffer.BlockCopy(quad, 0, packet, 0, packet.Length);
        return packet;
    }

    /// <summary>Extracts channels 2 and 3 of every whole speaker frame, as the persona does.</summary>
    private static byte[] ExtractHaptics(byte[] packet)
    {
        byte[] haptics = new byte[SpeakerFrames * HapticsFrameBytes];
        for (int frame = 0; frame < SpeakerFrames; frame++)
        {
            Buffer.BlockCopy(
                packet,
                frame * SpeakerFrameBytes + HapticsOffsetInSpeakerFrame,
                haptics,
                frame * HapticsFrameBytes,
                HapticsFrameBytes);
        }

        return haptics;
    }
}