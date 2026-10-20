using System.Buffers.Binary;
using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Emulation.Core;

/// <summary>
/// Tests of the UAC1 audio function: the feature-unit mute and volume state machine and the
/// bounded microphone PCM byte ring. The reference values are the ones the reference firmwares
/// expose, expressed in 1/256 dB Q8 units.
/// </summary>
[TestFixture]
public sealed class UsbAudioFunctionTests
{
    private const byte ClassInterfaceOut = 0x21;
    private const byte ClassInterfaceIn = 0xA1;
    private const byte SetCur = 0x01;
    private const byte SetMin = 0x02;
    private const byte GetCur = 0x81;
    private const byte GetMin = 0x82;
    private const byte GetMax = 0x83;
    private const byte GetRes = 0x84;
    private const byte GetStat = 0xFF;
    private const byte MuteSelector = 0x01;
    private const byte VolumeSelector = 0x02;
    private const byte SpeakerFeatureUnit = 2;
    private const byte MicrophoneFeatureUnit = 5;

    [Test]
    public void AudioFunctionFormat_StaticValues_MatchBindingDecisions()
    {
        AudioFunctionFormat dualSense = AudioFunctionFormat.DualSense;
        Assert.Multiple(() =>
        {
            Assert.That(AudioFunctionFormat.SpeakerFeatureUnitId, Is.EqualTo((byte)2));
            Assert.That(AudioFunctionFormat.MicrophoneFeatureUnitId, Is.EqualTo((byte)5));
            Assert.That(AudioFunctionFormat.MicrophoneQueueCapacityFrames, Is.EqualTo(16));

            Assert.That(dualSense.SpeakerChannels, Is.EqualTo(4));
            Assert.That(dualSense.SpeakerSampleRateHz, Is.EqualTo(48000));
            Assert.That(dualSense.MicrophoneChannels, Is.EqualTo(2));
            Assert.That(dualSense.MicrophoneSampleRateHz, Is.EqualTo(48000));
            Assert.That(dualSense.SpeakerFrameBytes, Is.EqualTo(8));
            Assert.That(dualSense.MicrophoneFrameBytes, Is.EqualTo(192));

            // Reference: speaker 0 dB default, -100..0 dB, 1 dB steps; microphone
            // +48 dB default, 0..+48 dB, 122/256 dB steps.
            Assert.That(dualSense.SpeakerVolumeDefaultQ8, Is.EqualTo(0));
            Assert.That(dualSense.SpeakerVolumeMinQ8, Is.EqualTo(-25600));
            Assert.That(dualSense.SpeakerVolumeMaxQ8, Is.EqualTo(0));
            Assert.That(dualSense.SpeakerVolumeResolutionQ8, Is.EqualTo(256));
            Assert.That(dualSense.MicrophoneVolumeDefaultQ8, Is.EqualTo(12288));
            Assert.That(dualSense.MicrophoneVolumeMinQ8, Is.EqualTo(0));
            Assert.That(dualSense.MicrophoneVolumeMaxQ8, Is.EqualTo(12288));
            Assert.That(dualSense.MicrophoneVolumeResolutionQ8, Is.EqualTo(122));

            AudioFunctionFormat dualShock4 = AudioFunctionFormat.DualShock4;
            Assert.That(dualShock4.SpeakerChannels, Is.EqualTo(2));
            Assert.That(dualShock4.SpeakerSampleRateHz, Is.EqualTo(32000));
            Assert.That(dualShock4.MicrophoneChannels, Is.EqualTo(1));
            Assert.That(dualShock4.MicrophoneSampleRateHz, Is.EqualTo(16000));
            Assert.That(dualShock4.SpeakerFrameBytes, Is.EqualTo(4));
            Assert.That(dualShock4.MicrophoneFrameBytes, Is.EqualTo(32));

            // Reference: speaker -1 dB default (config volume 100), -73..-1 dB,
            // 1 dB steps; microphone 0 dB default, -23.25..+24 dB, 0.75 dB steps.
            Assert.That(dualShock4.SpeakerVolumeDefaultQ8, Is.EqualTo(-256));
            Assert.That(dualShock4.SpeakerVolumeMinQ8, Is.EqualTo(-18688));
            Assert.That(dualShock4.SpeakerVolumeMaxQ8, Is.EqualTo(-256));
            Assert.That(dualShock4.SpeakerVolumeResolutionQ8, Is.EqualTo(256));
            Assert.That(dualShock4.MicrophoneVolumeDefaultQ8, Is.EqualTo(0));
            Assert.That(dualShock4.MicrophoneVolumeMinQ8, Is.EqualTo(-5952));
            Assert.That(dualShock4.MicrophoneVolumeMaxQ8, Is.EqualTo(6144));
            Assert.That(dualShock4.MicrophoneVolumeResolutionQ8, Is.EqualTo(192));
        });
    }

    [Test]
    public void HandleClassRequest_SetSpeakerMute_StoresAndGetReturnsIt()
    {
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualSense);

        Assert.That(SetMute(audio, SpeakerFeatureUnit, 1), Is.Zero);
        Assert.That(audio.SpeakerMuted, Is.True);
        Assert.That(audio.MicrophoneMuted, Is.False);
        Assert.That(ReadMute(audio, SpeakerFeatureUnit, out byte muted), Is.EqualTo(1));
        Assert.That(muted, Is.EqualTo((byte)1));

        Assert.That(SetMute(audio, SpeakerFeatureUnit, 0), Is.Zero);
        Assert.That(audio.SpeakerMuted, Is.False);
        Assert.That(ReadMute(audio, SpeakerFeatureUnit, out muted), Is.EqualTo(1));
        Assert.That(muted, Is.Zero);
    }

    [Test]
    public void HandleClassRequest_SetMicrophoneMute_StoresAndGetReturnsIt()
    {
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualSense);

        Assert.That(SetMute(audio, MicrophoneFeatureUnit, 1), Is.Zero);
        Assert.That(audio.MicrophoneMuted, Is.True);
        Assert.That(audio.SpeakerMuted, Is.False);
        Assert.That(ReadMute(audio, MicrophoneFeatureUnit, out byte muted), Is.EqualTo(1));
        Assert.That(muted, Is.EqualTo((byte)1));

        Assert.That(SetMute(audio, MicrophoneFeatureUnit, 0), Is.Zero);
        Assert.That(audio.MicrophoneMuted, Is.False);
        Assert.That(ReadMute(audio, MicrophoneFeatureUnit, out muted), Is.EqualTo(1));
        Assert.That(muted, Is.Zero);
    }

    [Test]
    public void HandleClassRequest_GetCurMuteBeforeSet_ReturnsUnmutedDefaults()
    {
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualSense);

        Assert.That(audio.SpeakerMuted, Is.False);
        Assert.That(audio.MicrophoneMuted, Is.False);
        Assert.That(ReadMute(audio, SpeakerFeatureUnit, out byte speakerMute), Is.EqualTo(1));
        Assert.That(speakerMute, Is.Zero);
        Assert.That(ReadMute(audio, MicrophoneFeatureUnit, out byte microphoneMute), Is.EqualTo(1));
        Assert.That(microphoneMute, Is.Zero);
    }

    [Test]
    public void HandleClassRequest_SetSpeakerVolume_StoresQ8AndGetReturnsIt()
    {
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualSense);

        Assert.That(SetVolume(audio, SpeakerFeatureUnit, -2560), Is.Zero);
        Assert.That(audio.SpeakerVolumeQ8, Is.EqualTo(-2560));
        Assert.That(GetVolume(audio, SpeakerFeatureUnit, GetCur), Is.EqualTo(-2560));

        // The value is a signed Q8 quantity: a positive value must survive as well.
        Assert.That(SetVolume(audio, SpeakerFeatureUnit, 1234), Is.Zero);
        Assert.That(audio.SpeakerVolumeQ8, Is.EqualTo(1234));
        Assert.That(GetVolume(audio, SpeakerFeatureUnit, GetCur), Is.EqualTo(1234));
    }

    [Test]
    public void HandleClassRequest_SetMicrophoneVolume_RoundTripsQ8()
    {
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualSense);
        Assert.That(audio.MicrophoneVolumeQ8, Is.EqualTo(12288)); // reference startup value: +48 dB

        Assert.That(SetVolume(audio, MicrophoneFeatureUnit, -1234), Is.Zero);
        Assert.That(audio.MicrophoneVolumeQ8, Is.EqualTo(-1234));
        Assert.That(GetVolume(audio, MicrophoneFeatureUnit, GetCur), Is.EqualTo(-1234));
        Assert.That(audio.SpeakerVolumeQ8, Is.EqualTo(0));
        Assert.That(audio.MicrophoneMuted, Is.False);
    }

    [Test]
    public void HandleClassRequest_GetCurVolumeBeforeSet_ReturnsFormatDefaults()
    {
        UsbAudioFunction dualSense = new UsbAudioFunction(AudioFunctionFormat.DualSense);
        Assert.Multiple(() =>
        {
            Assert.That(GetVolume(dualSense, SpeakerFeatureUnit, GetCur), Is.EqualTo(0));
            Assert.That(GetVolume(dualSense, MicrophoneFeatureUnit, GetCur), Is.EqualTo(12288));
        });

        UsbAudioFunction dualShock4 = new UsbAudioFunction(AudioFunctionFormat.DualShock4);
        Assert.Multiple(() =>
        {
            Assert.That(GetVolume(dualShock4, SpeakerFeatureUnit, GetCur), Is.EqualTo(-256));
            Assert.That(GetVolume(dualShock4, MicrophoneFeatureUnit, GetCur), Is.EqualTo(0));
        });
    }

    [Test]
    public void HandleClassRequest_GetMinMaxRes_MatchesReferenceValues()
    {
        AssertVolumeRange(AudioFunctionFormat.DualSense, SpeakerFeatureUnit, -25600, 0, 256);
        AssertVolumeRange(AudioFunctionFormat.DualSense, MicrophoneFeatureUnit, 0, 12288, 122);
        AssertVolumeRange(AudioFunctionFormat.DualShock4, SpeakerFeatureUnit, -18688, -256, 256);
        AssertVolumeRange(AudioFunctionFormat.DualShock4, MicrophoneFeatureUnit, -5952, 6144, 192);

        // The parameter blocks are little-endian Q8 values, byte-for-byte the reference's.
        UsbAudioFunction dualSense = new UsbAudioFunction(AudioFunctionFormat.DualSense);
        Assert.That(ReadVolumeBlock(dualSense, SpeakerFeatureUnit, GetMin), Is.EqualTo(new byte[]
        {
            0x00, 0x9c
        }));
        Assert.That(ReadVolumeBlock(dualSense, MicrophoneFeatureUnit, GetRes), Is.EqualTo(new byte[]
        {
            0x7a, 0x00
        }));

        UsbAudioFunction dualShock4 = new UsbAudioFunction(AudioFunctionFormat.DualShock4);
        Assert.That(ReadVolumeBlock(dualShock4, SpeakerFeatureUnit, GetMin), Is.EqualTo(new byte[]
        {
            0x00, 0xb7
        }));
        Assert.That(ReadVolumeBlock(dualShock4, MicrophoneFeatureUnit, GetMin), Is.EqualTo(new byte[]
        {
            0xc0, 0xe8
        }));
        Assert.That(ReadVolumeBlock(dualShock4, MicrophoneFeatureUnit, GetRes), Is.EqualTo(new byte[]
        {
            0xc0, 0x00
        }));
    }

    [Test]
    public void HandleClassRequest_UnsupportedSelectorOrLength_Stalls()
    {
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualSense);
        byte[] oneByte = [0x01];
        byte[] twoBytes = new byte[2];

        // GET_STAT (mute and volume) and the range codes on the mute selector are not supported.
        AssertStalls(audio, CreateSetup(ClassInterfaceIn, GetStat, SpeakerFeatureUnit, MuteSelector, 1), ReadOnlySpan<byte>.Empty, 1);
        AssertStalls(audio, CreateSetup(ClassInterfaceIn, GetStat, SpeakerFeatureUnit, VolumeSelector, 2), ReadOnlySpan<byte>.Empty, 2);
        AssertStalls(audio, CreateSetup(ClassInterfaceIn, GetMin, SpeakerFeatureUnit, MuteSelector, 1), ReadOnlySpan<byte>.Empty, 1);
        AssertStalls(audio, CreateSetup(ClassInterfaceIn, GetMax, MicrophoneFeatureUnit, MuteSelector, 1), ReadOnlySpan<byte>.Empty, 1);
        AssertStalls(audio, CreateSetup(ClassInterfaceIn, GetRes, MicrophoneFeatureUnit, MuteSelector, 1), ReadOnlySpan<byte>.Empty, 1);

        // The data-stage length has to match the parameter block.
        AssertStalls(audio, CreateSetup(ClassInterfaceOut, SetCur, SpeakerFeatureUnit, MuteSelector, 2), twoBytes, 2);
        AssertStalls(audio, CreateSetup(ClassInterfaceOut, SetCur, SpeakerFeatureUnit, VolumeSelector, 1), oneByte, 2);
        AssertStalls(audio, CreateSetup(ClassInterfaceOut, SetCur, SpeakerFeatureUnit, VolumeSelector, 2), oneByte, 2);
        AssertStalls(audio, CreateSetup(ClassInterfaceOut, SetCur, SpeakerFeatureUnit, MuteSelector, 1), ReadOnlySpan<byte>.Empty, 2);
        AssertStalls(audio, CreateSetup(ClassInterfaceIn, GetCur, SpeakerFeatureUnit, MuteSelector, 2), ReadOnlySpan<byte>.Empty, 2);
        AssertStalls(audio, CreateSetup(ClassInterfaceIn, GetCur, SpeakerFeatureUnit, VolumeSelector, 1), ReadOnlySpan<byte>.Empty, 2);
        AssertStalls(audio, CreateSetup(ClassInterfaceIn, GetMin, SpeakerFeatureUnit, VolumeSelector, 0), ReadOnlySpan<byte>.Empty, 2);

        // Unknown selector, unknown entity and SET_MIN (known but unsupported) stall.
        AssertStalls(audio, CreateSetup(ClassInterfaceIn, GetCur, SpeakerFeatureUnit, 0x03, 1), ReadOnlySpan<byte>.Empty, 2);
        AssertStalls(audio, CreateSetup(ClassInterfaceIn, GetCur, 3, MuteSelector, 1), ReadOnlySpan<byte>.Empty, 1);
        AssertStalls(audio, CreateSetup(ClassInterfaceOut, SetMin, SpeakerFeatureUnit, MuteSelector, 1), oneByte, 2);

        // The direction bit has to agree with the request code.
        AssertStalls(audio, CreateSetup(ClassInterfaceOut, GetCur, SpeakerFeatureUnit, MuteSelector, 1), ReadOnlySpan<byte>.Empty, 1);
        AssertStalls(audio, CreateSetup(ClassInterfaceIn, SetCur, SpeakerFeatureUnit, MuteSelector, 1), oneByte, 1);

        // Only class requests addressed to an interface are served.
        AssertStalls(audio, CreateSetup(0x41, GetCur, SpeakerFeatureUnit, MuteSelector, 1), ReadOnlySpan<byte>.Empty, 1);
        AssertStalls(audio, CreateSetup(0x01, GetCur, SpeakerFeatureUnit, MuteSelector, 1), ReadOnlySpan<byte>.Empty, 1);
        AssertStalls(audio, CreateSetup(0xA2, GetCur, SpeakerFeatureUnit, MuteSelector, 1), ReadOnlySpan<byte>.Empty, 1);

        // A response buffer smaller than the parameter block cannot be served either.
        AssertStalls(audio, CreateSetup(ClassInterfaceIn, GetCur, SpeakerFeatureUnit, VolumeSelector, 2), ReadOnlySpan<byte>.Empty, 1);

        // No rejected request touched the state.
        Assert.That(audio.SpeakerMuted, Is.False);
        Assert.That(audio.MicrophoneMuted, Is.False);
        Assert.That(audio.SpeakerVolumeQ8, Is.EqualTo(0));
        Assert.That(audio.MicrophoneVolumeQ8, Is.EqualTo(12288));
    }

    [Test]
    public void EnqueueMicrophonePcm_WrongLength_Throws()
    {
        UsbAudioFunction dualSense = new UsbAudioFunction(AudioFunctionFormat.DualSense);
        Assert.That(() => dualSense.EnqueueMicrophonePcm(new byte[3]), Throws.TypeOf<ArgumentException>());
        Assert.That(() => dualSense.EnqueueMicrophonePcm(new byte[5]), Throws.TypeOf<ArgumentException>());
        Assert.That(() => dualSense.EnqueueMicrophonePcm(ReadOnlySpan<byte>.Empty), Throws.TypeOf<ArgumentException>());

        UsbAudioFunction dualShock4 = new UsbAudioFunction(AudioFunctionFormat.DualShock4);
        Assert.That(() => dualShock4.EnqueueMicrophonePcm(new byte[1]), Throws.TypeOf<ArgumentException>());
        Assert.That(() => dualShock4.EnqueueMicrophonePcm(new byte[33]), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void EnqueueMicrophonePcm_QueueFull_DropsOldestAndLogsOnce()
    {
        RecordingLogger logger = new RecordingLogger();
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualSense, logger);
        byte[] frame = new byte[192];
        int capacityFrames = AudioFunctionFormat.MicrophoneQueueCapacityFrames;
        for (int i = 0; i < capacityFrames; i++)
        {
            frame.AsSpan().Fill((byte)i);
            audio.EnqueueMicrophonePcm(frame);
        }

        Assert.That(logger.Entries, Is.Empty);

        for (int i = capacityFrames; i < capacityFrames + 3; i++)
        {
            frame.AsSpan().Fill((byte)i);
            audio.EnqueueMicrophonePcm(frame);
        }

        IReadOnlyList<RecordedLogEntry> entries = logger.Entries;
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Level, Is.EqualTo(EmulationLogLevel.Warning));

        byte[] destination = new byte[capacityFrames * 192];
        Assert.That(audio.ReadMicrophonePcm(destination), Is.EqualTo(destination.Length));
        Assert.That(destination[0], Is.EqualTo((byte)3));
        Assert.That(destination[^1], Is.EqualTo((byte)(capacityFrames + 2)));

        // Draining the ring ends the burst; the next overflow warns once more.
        frame.AsSpan().Fill(0x20);
        for (int i = 0; i < capacityFrames + 4; i++)
        {
            audio.EnqueueMicrophonePcm(frame);
        }

        Assert.That(logger.Entries, Has.Count.EqualTo(2));
    }

    [Test]
    public void ReadMicrophonePcm_EmptyQueue_ZeroFillsNominalBytes()
    {
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualSense);
        byte[] destination = new byte[196];
        destination.AsSpan().Fill(0xAA);

        Assert.That(audio.ReadMicrophonePcm(destination), Is.EqualTo(192));
        Assert.That(destination, Has.All.EqualTo((byte)0));

        UsbAudioFunction dualShock4 = new UsbAudioFunction(AudioFunctionFormat.DualShock4);
        byte[] dualShock4Destination = new byte[34];
        dualShock4Destination.AsSpan().Fill(0xAA);
        Assert.That(dualShock4.ReadMicrophonePcm(dualShock4Destination), Is.EqualTo(32));
        Assert.That(dualShock4Destination, Has.All.EqualTo((byte)0));
    }

    [Test]
    public void ReadMicrophonePcm_EmptyQueueDestinationSmallerThanFrame_ReturnsDestinationLength()
    {
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualSense);
        byte[] destination = new byte[2];
        destination.AsSpan().Fill(0xAA);

        Assert.That(audio.ReadMicrophonePcm(destination), Is.EqualTo(2));
        Assert.That(destination, Has.All.EqualTo((byte)0));
    }

    [Test]
    public void ReadMicrophonePcm_FrameQueued_CopiesFrameAndReturnsFrameBytes()
    {
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualSense);
        byte[] frame = CreateSequence(192, 0);
        audio.EnqueueMicrophonePcm(frame);
        byte[] destination = new byte[192];
        destination.AsSpan().Fill(0xAA);

        Assert.That(audio.ReadMicrophonePcm(destination), Is.EqualTo(192));
        Assert.That(destination, Is.EqualTo(frame));

        // The frame is consumed: the next read is a nominal zero packet again.
        Assert.That(audio.ReadMicrophonePcm(destination), Is.EqualTo(192));
        Assert.That(destination, Has.All.EqualTo((byte)0));
    }

    [Test]
    public void ReadMicrophonePcm_DestinationLargerThanFrame_ReturnsFrameBytesAndShortPacket()
    {
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualSense);
        byte[] frame = CreateSequence(192, 7);
        audio.EnqueueMicrophonePcm(frame);
        byte[] destination = new byte[196];
        destination.AsSpan().Fill(0xAA);

        Assert.That(audio.ReadMicrophonePcm(destination), Is.EqualTo(192));
        Assert.That(destination.AsSpan(0, 192).ToArray(), Is.EqualTo(frame));
        Assert.That(destination.AsSpan(192).ToArray(), Has.All.EqualTo((byte)0xAA));
    }

    [Test]
    public void ReadMicrophonePcm_ShortReadThenFullRead_PreservesByteStream()
    {
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualSense);
        byte[] first = CreateSequence(192, 0);
        byte[] second = CreateSequence(192, 100);
        audio.EnqueueMicrophonePcm(first);
        audio.EnqueueMicrophonePcm(second);

        byte[] shortPacket = new byte[188];
        Assert.That(audio.ReadMicrophonePcm(shortPacket), Is.EqualTo(188));
        Assert.That(shortPacket, Is.EqualTo(first.AsSpan(0, 188).ToArray()));

        // The four bytes left of the first frame lead the next packet: no byte is lost at the seam.
        byte[] fullPacket = new byte[192];
        Assert.That(audio.ReadMicrophonePcm(fullPacket), Is.EqualTo(192));
        byte[] expected = new byte[192];
        first.AsSpan(188, 4).CopyTo(expected);
        second.AsSpan(0, 188).CopyTo(expected.AsSpan(4));
        Assert.That(fullPacket, Is.EqualTo(expected));

        byte[] tail = new byte[4];
        Assert.That(audio.ReadMicrophonePcm(tail), Is.EqualTo(4));
        Assert.That(tail, Is.EqualTo(second.AsSpan(188, 4).ToArray()));
    }

    [Test]
    public void ReadMicrophonePcm_RingWrap_PreservesByteStream()
    {
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualShock4);
        byte[] frame = new byte[AudioFunctionFormat.DualShock4.MicrophoneFrameBytes];
        for (int i = 0; i < AudioFunctionFormat.MicrophoneQueueCapacityFrames; i++)
        {
            frame.AsSpan().Fill((byte)(i + 1));
            audio.EnqueueMicrophonePcm(frame);
        }

        // Leave the last 12 bytes of frame 16 in the ring at the end of the 512-byte buffer.
        byte[] consumed = new byte[500];
        Assert.That(audio.ReadMicrophonePcm(consumed), Is.EqualTo(500));

        // Fifteen fresh frames fill the ring back up; the next read straddles the wrap point.
        for (int i = AudioFunctionFormat.MicrophoneQueueCapacityFrames; i < 31; i++)
        {
            frame.AsSpan().Fill((byte)(i + 1));
            audio.EnqueueMicrophonePcm(frame);
        }

        byte[] wrapped = new byte[100];
        Assert.That(audio.ReadMicrophonePcm(wrapped), Is.EqualTo(100));
        Assert.That(wrapped.AsSpan(0, 12).ToArray(), Has.All.EqualTo((byte)16));
        Assert.That(wrapped.AsSpan(12, 32).ToArray(), Has.All.EqualTo((byte)17));
        Assert.That(wrapped.AsSpan(44, 32).ToArray(), Has.All.EqualTo((byte)18));
        Assert.That(wrapped.AsSpan(76, 24).ToArray(), Has.All.EqualTo((byte)19));

        byte[] rest = new byte[392];
        Assert.That(audio.ReadMicrophonePcm(rest), Is.EqualTo(392));
        Assert.That(rest.AsSpan(0, 8).ToArray(), Has.All.EqualTo((byte)19));
        Assert.That(rest[^1], Is.EqualTo((byte)31));
    }

    [Test]
    public void EnqueueMicrophonePcm_PartialDrainThenFullDrainOrClear_ResetsWarningBurst()
    {
        RecordingLogger logger = new RecordingLogger();
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualSense, logger);
        byte[] frame = new byte[AudioFunctionFormat.DualSense.MicrophoneFrameBytes];
        int capacityFrames = AudioFunctionFormat.MicrophoneQueueCapacityFrames;
        for (int i = 0; i < capacityFrames; i++)
        {
            audio.EnqueueMicrophonePcm(frame);
        }

        audio.EnqueueMicrophonePcm(frame);
        Assert.That(logger.Entries, Has.Count.EqualTo(1));

        // A partial drain does not end the burst: the next overflow still logs nothing new.
        byte[] oneFrame = new byte[AudioFunctionFormat.DualSense.MicrophoneFrameBytes];
        Assert.That(audio.ReadMicrophonePcm(oneFrame), Is.EqualTo(oneFrame.Length));
        audio.EnqueueMicrophonePcm(frame);
        audio.EnqueueMicrophonePcm(frame);
        Assert.That(logger.Entries, Has.Count.EqualTo(1));

        // Draining the ring to zero ends the burst; the next overflow warns once more.
        byte[] remaining = new byte[capacityFrames * frame.Length];
        Assert.That(audio.ReadMicrophonePcm(remaining), Is.EqualTo(remaining.Length));
        for (int i = 0; i <= capacityFrames; i++)
        {
            audio.EnqueueMicrophonePcm(frame);
        }

        Assert.That(logger.Entries, Has.Count.EqualTo(2));

        // Clearing the full ring also ends a burst; the next overflow warns once more.
        audio.ClearMicrophonePcm();
        for (int i = 0; i <= capacityFrames; i++)
        {
            audio.EnqueueMicrophonePcm(frame);
        }

        Assert.That(logger.Entries, Has.Count.EqualTo(3));
    }

    [Test]
    public void ClearMicrophonePcm_DropsQueuedFrames()
    {
        UsbAudioFunction audio = new UsbAudioFunction(AudioFunctionFormat.DualShock4);
        audio.EnqueueMicrophonePcm(CreateSequence(32, 0));
        audio.EnqueueMicrophonePcm(CreateSequence(32, 50));
        audio.ClearMicrophonePcm();

        byte[] destination = new byte[34];
        destination.AsSpan().Fill(0xAA);
        Assert.That(audio.ReadMicrophonePcm(destination), Is.EqualTo(32));
        Assert.That(destination, Has.All.EqualTo((byte)0));
    }

    private static UsbSetupPacket CreateSetup(byte requestType, byte request, byte entityId, byte selector, ushort length)
    {
        return new UsbSetupPacket
        {
            RequestType = requestType,
            Request = request,
            Value = (ushort)(selector << 8),
            Index = (ushort)(entityId << 8),
            Length = length
        };
    }

    private static int SetMute(UsbAudioFunction audio, byte entityId, byte value)
    {
        UsbSetupPacket setup = CreateSetup(ClassInterfaceOut, SetCur, entityId, MuteSelector, 1);
        return audio.HandleClassRequest(setup, [value], Span<byte>.Empty);
    }

    private static int ReadMute(UsbAudioFunction audio, byte entityId, out byte value)
    {
        byte[] response = new byte[1];
        UsbSetupPacket setup = CreateSetup(ClassInterfaceIn, GetCur, entityId, MuteSelector, 1);
        int count = audio.HandleClassRequest(setup, ReadOnlySpan<byte>.Empty, response);
        value = response[0];
        return count;
    }

    private static int SetVolume(UsbAudioFunction audio, byte entityId, short volumeQ8)
    {
        byte[] data = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(data, volumeQ8);
        UsbSetupPacket setup = CreateSetup(ClassInterfaceOut, SetCur, entityId, VolumeSelector, 2);
        return audio.HandleClassRequest(setup, data, Span<byte>.Empty);
    }

    private static short GetVolume(UsbAudioFunction audio, byte entityId, byte request)
    {
        byte[] block = ReadVolumeBlock(audio, entityId, request);
        return BinaryPrimitives.ReadInt16LittleEndian(block);
    }

    private static byte[] ReadVolumeBlock(UsbAudioFunction audio, byte entityId, byte request)
    {
        byte[] response = new byte[2];
        UsbSetupPacket setup = CreateSetup(ClassInterfaceIn, request, entityId, VolumeSelector, 2);
        int count = audio.HandleClassRequest(setup, ReadOnlySpan<byte>.Empty, response);
        Assert.That(count, Is.EqualTo(2));
        return response;
    }

    private static byte[] CreateSequence(int length, int start)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = (byte)((start + i) & 0xFF);
        }

        return bytes;
    }

    private static void AssertStalls(UsbAudioFunction audio, UsbSetupPacket setup, ReadOnlySpan<byte> outData, int responseLength)
    {
        byte[] response = new byte[responseLength];
        response.AsSpan().Fill(0xAA);
        int count = audio.HandleClassRequest(setup, outData, response);
        Assert.That(count, Is.EqualTo(-1));
        Assert.That(response, Has.All.EqualTo((byte)0xAA));
    }

    private static void AssertVolumeRange(AudioFunctionFormat format, byte entityId, short minQ8, short maxQ8, short resolutionQ8)
    {
        UsbAudioFunction audio = new UsbAudioFunction(format);
        Assert.Multiple(() =>
        {
            Assert.That(GetVolume(audio, entityId, GetMin), Is.EqualTo(minQ8));
            Assert.That(GetVolume(audio, entityId, GetMax), Is.EqualTo(maxQ8));
            Assert.That(GetVolume(audio, entityId, GetRes), Is.EqualTo(resolutionQ8));
        });
    }
}