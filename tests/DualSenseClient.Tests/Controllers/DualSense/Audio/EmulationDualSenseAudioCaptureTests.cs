using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.DualSense;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio;

/// <summary>
/// Pins the Emulation DualSense speaker capture: the persona's four-channel S16LE speaker
/// packets are copied out of the event, reduced to their front stereo pair and fed to the
/// PCM sink, the speaker-reset event flushes the sink, a suspended capture discards packets,
/// and disposal unsubscribes both events. The conversion itself is the libVIIPER capture's
/// pure arithmetic, reused rather than duplicated.
/// </summary>
[TestFixture]
public sealed class EmulationDualSenseAudioCaptureTests
{
    /// <summary>Isochronous OUT endpoint address of the speaker path.</summary>
    private const byte AudioOutEndpointAddress = 0x01;

    /// <summary>Interface number of the speaker audio streaming interface (AS OUT).</summary>
    private const byte SpeakerInterfaceNumber = 1;

    /// <summary>Standard SET_INTERFACE request code (USB 2.0, section 9.4.10).</summary>
    private const byte RequestSetInterface = 0x0B;

    /// <summary>Host-to-device standard request addressed to an interface (bmRequestType 0x01).</summary>
    private const byte StandardInterfaceOut = 0x01;

    /// <summary>Bytes of one speaker sample frame: four interleaved 16-bit channels.</summary>
    private const int SpeakerFrameBytes = 8;

    /// <summary>Nominal speaker packet length: 48 one-millisecond frames of four 16-bit channels.</summary>
    private const int SpeakerPacketBytes = 384;

    /// <summary>Frames carried by a nominal speaker packet.</summary>
    private const int SpeakerFrames = SpeakerPacketBytes / SpeakerFrameBytes;

    [Test]
    public void ConvertToStereoFloat_KeepsFrontChannelsAndIgnoresRear()
    {
        // Two 4-channel S16LE frames: [front L, front R, rear L, rear R].
        short[] quad =
        [
            16384, -8192, 4096, 32767,
            0, -16384, -32768, 8192
        ];
        byte[] raw = new byte[quad.Length * sizeof(short)];
        Buffer.BlockCopy(quad, 0, raw, 0, raw.Length);
        float[] stereo = new float[quad.Length / 2];

        EmulationDualSenseAudioCapture.ConvertToStereoFloat(raw, stereo);

        Assert.Multiple(() =>
        {
            Assert.That(stereo[0], Is.EqualTo(16384f / 32768f).Within(1e-6f), "frame 0 front left");
            Assert.That(stereo[1], Is.EqualTo(-8192f / 32768f).Within(1e-6f), "frame 0 front right");
            Assert.That(stereo[2], Is.EqualTo(0f).Within(1e-6f), "frame 1 front left");
            Assert.That(stereo[3], Is.EqualTo(-16384f / 32768f).Within(1e-6f), "frame 1 front right");
        });
    }

    [Test]
    public void OnIsoPacketReceived_QuadPacket_FeedsOneStereoBlock()
    {
        DualSenseDevice device = CreateDevice();
        RecordingSink sink = new RecordingSink();
        using EmulationDualSenseAudioCapture capture = new EmulationDualSenseAudioCapture(device, sink);
        (byte[] packet, float[] expected) = CreateSpeakerPacket(SpeakerFrames);

        device.OnIsoPacketReceived(AudioOutEndpointAddress, packet);

        Assert.Multiple(() =>
        {
            Assert.That(sink.Blocks, Has.Count.EqualTo(1));
            Assert.That(sink.Blocks[0], Has.Length.EqualTo(SpeakerFrames * 2));
            Assert.That(sink.Blocks[0], Is.EqualTo(expected).Within(1e-6f));
        });
    }

    [Test]
    public void OnIsoPacketReceived_CopiesThePacketBeforeReturning()
    {
        DualSenseDevice device = CreateDevice();
        RecordingSink sink = new RecordingSink();
        using EmulationDualSenseAudioCapture capture = new EmulationDualSenseAudioCapture(device, sink);
        (byte[] packet, float[] expected) = CreateSpeakerPacket(SpeakerFrames);

        device.OnIsoPacketReceived(AudioOutEndpointAddress, packet);

        // Overwrite the caller's buffer, then deliver a different packet so the persona reuses
        // and overwrites its own per-device scratch buffer as well: neither may reach the block
        // the sink already received.
        Array.Fill(packet, (byte)0x7F);
        byte[] second = new byte[SpeakerPacketBytes];
        Array.Fill(second, (byte)0x40);
        device.OnIsoPacketReceived(AudioOutEndpointAddress, second);

        Assert.Multiple(() =>
        {
            Assert.That(sink.Blocks, Has.Count.EqualTo(2));
            Assert.That(sink.Blocks[0], Is.EqualTo(expected).Within(1e-6f));
        });
    }

    [Test]
    public void IsSuspended_True_DiscardsPacket()
    {
        DualSenseDevice device = CreateDevice();
        RecordingSink sink = new RecordingSink();
        using EmulationDualSenseAudioCapture capture = new EmulationDualSenseAudioCapture(device, sink);
        (byte[] packet, _) = CreateSpeakerPacket(SpeakerFrames);

        capture.IsSuspended = true;
        device.OnIsoPacketReceived(AudioOutEndpointAddress, packet);
        Assert.That(sink.Blocks, Is.Empty);

        capture.IsSuspended = false;
        device.OnIsoPacketReceived(AudioOutEndpointAddress, packet);
        Assert.That(sink.Blocks, Has.Count.EqualTo(1));
    }

    [Test]
    public void SpeakerResetReceived_FlushesTheSink()
    {
        DualSenseDevice device = CreateDevice();
        RecordingSink sink = new RecordingSink();
        using EmulationDualSenseAudioCapture capture = new EmulationDualSenseAudioCapture(device, sink);

        Assert.That(SendSetInterface(device, SpeakerInterfaceNumber, 1), Is.Zero);

        Assert.That(sink.FlushCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_UnsubscribesAudioAndReset()
    {
        DualSenseDevice device = CreateDevice();
        RecordingSink sink = new RecordingSink();
        EmulationDualSenseAudioCapture capture = new EmulationDualSenseAudioCapture(device, sink);
        (byte[] packet, _) = CreateSpeakerPacket(SpeakerFrames);
        device.OnIsoPacketReceived(AudioOutEndpointAddress, packet);
        Assert.That(SendSetInterface(device, SpeakerInterfaceNumber, 1), Is.Zero);
        int feeds = sink.Blocks.Count;
        int flushes = sink.FlushCount;

        capture.Dispose();

        device.OnIsoPacketReceived(AudioOutEndpointAddress, packet);
        Assert.That(SendSetInterface(device, SpeakerInterfaceNumber, 0), Is.Zero);
        Assert.Multiple(() =>
        {
            Assert.That(sink.Blocks, Has.Count.EqualTo(feeds));
            Assert.That(sink.FlushCount, Is.EqualTo(flushes));
        });
    }

    [Test]
    public void Capture_ImplementsTheSuspendableContract() =>
        Assert.That(typeof(EmulationDualSenseAudioCapture).IsAssignableTo(typeof(IViiperAudioCapture)), Is.True);

    /// <summary>Creates the persona device with the reference identity.</summary>
    private static DualSenseDevice CreateDevice() => new DualSenseDevice(new DualSenseIdentity());

    /// <summary>
    /// Creates a deterministic 384-byte speaker packet whose four interleaved 16-bit channels
    /// carry distinct patterns, together with the expected interleaved stereo float block: only
    /// the front pair (channels 0 and 1) of every frame, scaled by 1/32768.
    /// </summary>
    private static (byte[] Packet, float[] ExpectedStereo) CreateSpeakerPacket(int frames)
    {
        short[] quad = new short[frames * 4];
        float[] stereo = new float[frames * 2];
        for (int f = 0; f < frames; f++)
        {
            short left = (short)(f * 617 % 30000 - 15000);
            short right = (short)(20000 - f * 431);
            quad[f * 4] = left;
            quad[f * 4 + 1] = right;
            quad[f * 4 + 2] = (short)(f * 3 + 1);
            quad[f * 4 + 3] = (short)(-f * 5 - 2);
            stereo[f * 2] = left / 32768f;
            stereo[f * 2 + 1] = right / 32768f;
        }

        byte[] packet = new byte[quad.Length * sizeof(short)];
        Buffer.BlockCopy(quad, 0, packet, 0, packet.Length);
        return (packet, stereo);
    }

    /// <summary>Sends a standard SET_INTERFACE request and returns its result count.</summary>
    private static int SendSetInterface(DualSenseDevice device, byte interfaceNumber, byte alternateSetting)
    {
        UsbSetupPacket setup = new UsbSetupPacket
        {
            RequestType = StandardInterfaceOut,
            Request = RequestSetInterface,
            Value = alternateSetting,
            Index = interfaceNumber,
            Length = 0
        };

        return device.HandleControlRequest(setup, ReadOnlySpan<byte>.Empty, Span<byte>.Empty);
    }

    /// <summary>
    /// Records the blocks it is fed (copied, so later packet reuse cannot reach them) and the
    /// number of flushes.
    /// </summary>
    private sealed class RecordingSink : IAudioPcmSink
    {
        public List<float[]> Blocks { get; } = [];

        public int FlushCount { get; private set; }

        public void FeedPcm(ReadOnlySpan<float> stereo) => Blocks.Add(stereo.ToArray());

        public void Flush() => FlushCount++;
    }
}