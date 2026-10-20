using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio;

/// <summary>
/// Pins the Emulation DualShock 4 speaker capture: the persona's two-channel S16LE 32 kHz speaker
/// packets are copied out of the event, converted to interleaved stereo float, upsampled to the
/// 48 kHz rate the sink consumes, and fed as one block per packet. The resampler is a single
/// instance owned by the capture, so its phase carries across callbacks; the speaker-reset event
/// flushes the sink, a suspended capture discards packets, and disposal unsubscribes both events.
/// </summary>
[TestFixture]
public sealed class EmulationDualShock4AudioCaptureTests
{
    /// <summary>Isochronous OUT endpoint address of the speaker path (AS OUT interface 1, alternate 1).</summary>
    private const byte AudioOutEndpointAddress = 0x01;

    /// <summary>Interface number of the speaker audio streaming interface (AS OUT).</summary>
    private const byte SpeakerInterfaceNumber = 1;

    /// <summary>Standard SET_INTERFACE request code (USB 2.0, section 9.4.10).</summary>
    private const byte RequestSetInterface = 0x0B;

    /// <summary>Host-to-device standard request addressed to an interface (bmRequestType 0x01).</summary>
    private const byte StandardInterfaceOut = 0x01;

    /// <summary>Bytes of one speaker sample frame: two interleaved 16-bit channels at 32 kHz.</summary>
    private const int SpeakerFrameBytes = sizeof(short) * 2;

    /// <summary>Nominal speaker packet length: 32 one-millisecond frames of two 16-bit channels.</summary>
    private const int SpeakerPacketBytes = 128;

    /// <summary>Input frames carried by a nominal speaker packet.</summary>
    private const int SpeakerFrames = SpeakerPacketBytes / SpeakerFrameBytes;

    /// <summary>Output frames a nominal packet produces at 48 kHz (an exact 3:2 ratio).</summary>
    private const int OutputFrames = SpeakerFrames * 3 / 2;

    /// <summary>Interleaved float count of a nominal packet's 48 kHz stereo block.</summary>
    private const int OutputFloats = OutputFrames * 2;

    [Test]
    public void OnIsoPacketReceived_Stereo32kPacket_Feeds48kStereo()
    {
        DualShock4Device device = CreateDevice();
        RecordingSink sink = new RecordingSink();
        using EmulationDualShock4AudioCapture capture = new EmulationDualShock4AudioCapture(device, sink);
        const short amplitude = 8192; // 0.25 full scale
        byte[] packet = CreateConstantPacket(SpeakerFrames, amplitude);

        device.OnIsoPacketReceived(AudioOutEndpointAddress, packet);

        Assert.That(sink.Blocks, Has.Count.EqualTo(1), "one packet must produce exactly one sink block");
        float[] block = sink.Blocks[0];
        Assert.That(block, Has.Length.EqualTo(OutputFloats), "32 input frames at 32 kHz become 48 frames at 48 kHz");
        Assert.Multiple(() =>
        {
            // The resampler anchors its first output frames to the zero-initialised history, so the
            // stream reaches the constant value on the third output frame and stays there.
            Assert.That(block[0], Is.Zero, "the first output frame is the zero history, not a discontinuity");
            Assert.That(block[2], Is.EqualTo(amplitude / 32768f * 2f / 3f).Within(1e-6f),
                "the second output frame interpolates out of the zero history");
            Assert.That(block[4..], Is.All.EqualTo(amplitude / 32768f).Within(1e-6f),
                "a constant input must stay constant once the history is primed");
        });
    }

    [Test]
    public void Upsampler32To48_ChunkBoundariesStayContinuous()
    {
        DualShock4Device device = CreateDevice();
        RecordingSink sink = new RecordingSink();
        using EmulationDualShock4AudioCapture capture = new EmulationDualShock4AudioCapture(device, sink);

        // Two 2-frame chunks sampling the continuous ramp 0, 1, 2, 3. A capture that reset its
        // resampler per callback would restart at the zero history and break the ramp.
        device.OnIsoPacketReceived(AudioOutEndpointAddress, CreateRampChunk(0, 8192));
        device.OnIsoPacketReceived(AudioOutEndpointAddress, CreateRampChunk(16384, 24576));

        Assert.That(sink.Blocks, Has.Count.EqualTo(2), "one block per callback");
        float[] stream = [.. sink.Blocks[0], .. sink.Blocks[1]];
        const int inputFrames = 4;
        Assert.That(stream, Has.Length.EqualTo(6 * 2), "4 input frames at 32 kHz become 6 output frames at 48 kHz");

        for (int frame = 1; frame < stream.Length / 2; frame++)
        {
            Assert.That(stream[frame * 2], Is.GreaterThanOrEqualTo(stream[(frame - 1) * 2] - 1e-6f),
                $"frame {frame}: the resampled ramp must never decrease across the chunk boundary");
        }

        Assert.That(stream.Length / 2, Is.EqualTo(inputFrames * 3 / 2), "the long-run ratio must be exactly 3:2");
    }

    [Test]
    public void IsSuspended_True_DiscardsPacket()
    {
        DualShock4Device device = CreateDevice();
        RecordingSink sink = new RecordingSink();
        using EmulationDualShock4AudioCapture capture = new EmulationDualShock4AudioCapture(device, sink);
        byte[] packet = CreateConstantPacket(SpeakerFrames, 8192);

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
        DualShock4Device device = CreateDevice();
        RecordingSink sink = new RecordingSink();
        using EmulationDualShock4AudioCapture capture = new EmulationDualShock4AudioCapture(device, sink);

        Assert.That(SendSetInterface(device, SpeakerInterfaceNumber, 1), Is.Zero);

        Assert.That(sink.FlushCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_UnsubscribesAudioAndReset()
    {
        DualShock4Device device = CreateDevice();
        RecordingSink sink = new RecordingSink();
        EmulationDualShock4AudioCapture capture = new EmulationDualShock4AudioCapture(device, sink);
        byte[] packet = CreateConstantPacket(SpeakerFrames, 8192);
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
        Assert.That(typeof(EmulationDualShock4AudioCapture).IsAssignableTo(typeof(IViiperAudioCapture)), Is.True);

    /// <summary>Creates the persona device with the reference identity.</summary>
    private static DualShock4Device CreateDevice() => new DualShock4Device(new DualShock4Identity());

    /// <summary>
    /// Creates a packet whose two interleaved channels both carry <paramref name="value"/> for every
    /// one of <paramref name="frames"/> sample frames.
    /// </summary>
    private static byte[] CreateConstantPacket(int frames, short value)
    {
        short[] pcm = new short[frames * 2];
        Array.Fill(pcm, value);
        byte[] packet = new byte[pcm.Length * sizeof(short)];
        Buffer.BlockCopy(pcm, 0, packet, 0, packet.Length);
        return packet;
    }

    /// <summary>
    /// Creates a two-frame packet with the same value on both channels: two successive samples of a
    /// continuous rising ramp.
    /// </summary>
    private static byte[] CreateRampChunk(short first, short second)
    {
        short[] pcm = [first, first, second, second];
        byte[] packet = new byte[pcm.Length * sizeof(short)];
        Buffer.BlockCopy(pcm, 0, packet, 0, packet.Length);
        return packet;
    }

    /// <summary>Sends a standard SET_INTERFACE request and returns its result count.</summary>
    private static int SendSetInterface(DualShock4Device device, byte interfaceNumber, byte alternateSetting)
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