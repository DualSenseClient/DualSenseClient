using DualSenseClient.Controllers.DualSense.Audio;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio;

public class DualSenseBtAudioPipelineTests
{
    private static readonly Random _random = new Random(42);

    /// <summary>
    /// Builds a stereo block with the given constant value on both channels.
    /// </summary>
    private static short[] ConstantBlock(short value)
    {
        short[] block = new short[DualSenseBtAudioPipeline.FramesPerBlock * 2];
        Array.Fill(block, value);
        return block;
    }

    /// <summary>
    /// Builds a stereo block with a linear ramp 0..<paramref name="max"/> on the left
    /// channel and a mirrored ramp on the right channel.
    /// </summary>
    private static short[] RampBlock(short max)
    {
        short[] block = new short[DualSenseBtAudioPipeline.FramesPerBlock * 2];
        for (int i = 0; i < DualSenseBtAudioPipeline.FramesPerBlock; i++)
        {
            block[i * 2] = (short)(max * i / (DualSenseBtAudioPipeline.FramesPerBlock - 1));
            block[i * 2 + 1] = (short)(max * (DualSenseBtAudioPipeline.FramesPerBlock - 1 - i) / (DualSenseBtAudioPipeline.FramesPerBlock - 1));
        }
        return block;
    }

    [Test]
    public void EncodeOpus_ProducesFixedSizeFrame()
    {
        using DualSenseBtAudioPipeline pipeline = new DualSenseBtAudioPipeline();
        short[] block = ConstantBlock(0);
        byte[] frame = new byte[DualSenseBtAudioPipeline.OpusBytes];

        pipeline.EncodeOpus(block, frame);

        Assert.Multiple(() =>
        {
            Assert.That(frame, Has.Length.EqualTo(200));
            Assert.That(frame.Any(b => b != 0), Is.True, "even a silence block must produce a real Opus payload");
        });
    }

    [Test]
    public void EncodeOpus_ShortFrameIsRejected()
    {
        using DualSenseBtAudioPipeline pipeline = new DualSenseBtAudioPipeline();
        short[] block = ConstantBlock(0);

        // The lane requires exactly 200 bytes; a smaller buffer must not slip through
        // even when the encoder can fit its payload into it.
        Assert.Throws<InvalidOperationException>(() => pipeline.EncodeOpus(block, new byte[100]));
    }

    [Test]
    public void EncodeOpus_ResetEncoderStillProducesValidFrames()
    {
        using DualSenseBtAudioPipeline pipeline = new DualSenseBtAudioPipeline();
        byte[] frame = new byte[DualSenseBtAudioPipeline.OpusBytes];

        pipeline.EncodeOpus(ConstantBlock(1000), frame);
        pipeline.ResetEncoder();
        pipeline.EncodeOpus(ConstantBlock(-1000), frame);

        Assert.That(frame, Has.Length.EqualTo(200));
    }

    [Test]
    public void ConvertToPcm16_MapsFloatRangeToShortRange()
    {
        float[] source = [-1f, 0f, 0.5f, 1f];
        short[] target = new short[4];

        DualSenseBtAudioPipeline.ConvertToPcm16(source, target);

        Assert.Multiple(() =>
        {
            Assert.That(target[0], Is.EqualTo((short)(-1f * 32767f)));
            Assert.That(target[1], Is.EqualTo(0));
            Assert.That(target[2], Is.EqualTo((short)(0.5 * 32767)));
            Assert.That(target[3], Is.EqualTo(short.MaxValue));
        });
    }

    [Test]
    public void ConvertToPcm16_ClampsOutOfRangeInput()
    {
        float[] source = [-2f, 2f];
        short[] target = new short[2];

        DualSenseBtAudioPipeline.ConvertToPcm16(source, target);

        Assert.Multiple(() =>
        {
            Assert.That(target[0], Is.EqualTo(short.MinValue));
            Assert.That(target[1], Is.EqualTo(short.MaxValue));
        });
    }

    [Test]
    public void ToHapticsPcm_SilenceProducesZeroHaptics()
    {
        byte[] haptics = new byte[DualSenseBtAudioPipeline.HapticsBytes];

        DualSenseBtAudioPipeline.ToHapticsPcm(ConstantBlock(0), haptics, 1f);

        Assert.That(haptics.All(b => b == 0), Is.True);
        Assert.That(haptics, Has.Length.EqualTo(64));
    }

    [Test]
    public void ToHapticsPcm_ConstantSignalScalesWithStrength()
    {
        byte[] strong = new byte[DualSenseBtAudioPipeline.HapticsBytes];
        byte[] weak = new byte[DualSenseBtAudioPipeline.HapticsBytes];

        DualSenseBtAudioPipeline.ToHapticsPcm(ConstantBlock(10000), strong, 1f);
        DualSenseBtAudioPipeline.ToHapticsPcm(ConstantBlock(10000), weak, 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(strong[0], Is.GreaterThan(0), "a positive constant signal must produce non-zero haptics");
            Assert.That(weak[0], Is.LessThanOrEqualTo(strong[0]));
            Assert.That(weak[0] * 2, Is.GreaterThanOrEqualTo(strong[0] - 1));
        });
    }

    [Test]
    public void ToHapticsPcm_StereoChannelsAreInterleaved()
    {
        // Left channel constant, right channel silence: odd (right) samples must be 0.
        short[] block = new short[DualSenseBtAudioPipeline.FramesPerBlock * 2];
        for (int i = 0; i < DualSenseBtAudioPipeline.FramesPerBlock; i++)
        {
            block[i * 2] = 8000;
            block[i * 2 + 1] = 0;
        }

        byte[] haptics = new byte[DualSenseBtAudioPipeline.HapticsBytes];
        DualSenseBtAudioPipeline.ToHapticsPcm(block, haptics, 1f);

        for (int i = 0; i < DualSenseBtAudioPipeline.HapticsFrames; i++)
        {
            Assert.That(haptics[i * 2], Is.GreaterThan(0), $"left sample {i} must be non-zero");
            Assert.That(haptics[i * 2 + 1], Is.EqualTo(0), $"right sample {i} must be zero");
        }
    }

    [Test]
    public void ToHapticsPcm_IsDeterministic()
    {
        short[] block = RampBlock(20000);
        byte[] first = new byte[DualSenseBtAudioPipeline.HapticsBytes];
        byte[] second = new byte[DualSenseBtAudioPipeline.HapticsBytes];

        DualSenseBtAudioPipeline.ToHapticsPcm(block, first, 0.75f);
        DualSenseBtAudioPipeline.ToHapticsPcm(block, second, 0.75f);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void ResampleToOpusBlock_Produces480Frames()
    {
        short[] target = new short[DualSenseBtAudioPipeline.OpusFrameSamples * 2];

        DualSenseBtAudioPipeline.ResampleToOpusBlock(ConstantBlock(500), target);

        Assert.That(target, Has.Length.EqualTo(960));
        Assert.That(target.All(s => s == 500), Is.True);
    }

    [Test]
    public void ResampleToOpusBlock_EndpointsMatchSource()
    {
        short[] source = RampBlock(30000);
        short[] target = new short[DualSenseBtAudioPipeline.OpusFrameSamples * 2];

        DualSenseBtAudioPipeline.ResampleToOpusBlock(source, target);

        Assert.Multiple(() =>
        {
            Assert.That(target[0], Is.EqualTo(source[0]));
            Assert.That(target[DualSenseBtAudioPipeline.OpusFrameSamples * 2 - 2], Is.EqualTo(source[DualSenseBtAudioPipeline.FramesPerBlock * 2 - 2]));
            Assert.That(target[1], Is.EqualTo(source[1]));
            Assert.That(target[DualSenseBtAudioPipeline.OpusFrameSamples * 2 - 1], Is.EqualTo(source[DualSenseBtAudioPipeline.FramesPerBlock * 2 - 1]));
        });
    }

    [Test]
    public void ResampleToOpusBlock_LinearRampInterpolatesExactly()
    {
        // A linear ramp resampled by linear interpolation must land on the source line:
        // output[i] == source[floor(i*step)] + (i*step - floor(i*step)) * source[ceil(i*step)].
        short[] source = RampBlock(30000);
        short[] target = new short[DualSenseBtAudioPipeline.OpusFrameSamples * 2];

        DualSenseBtAudioPipeline.ResampleToOpusBlock(source, target);

        const double step = (DualSenseBtAudioPipeline.FramesPerBlock - 1) / (double)(DualSenseBtAudioPipeline.OpusFrameSamples - 1);
        for (int i = 0; i < DualSenseBtAudioPipeline.OpusFrameSamples - 1; i++)
        {
            double src = i * step;
            int idx = (int)src;
            int nxt = Math.Min(idx + 1, DualSenseBtAudioPipeline.FramesPerBlock - 1);
            double frac = src - idx;
            short expectedLeft = (short)(source[idx * 2] + (source[nxt * 2] - source[idx * 2]) * frac);
            short expectedRight = (short)(source[idx * 2 + 1] + (source[nxt * 2 + 1] - source[idx * 2 + 1]) * frac);
            Assert.That(target[i * 2], Is.EqualTo(expectedLeft), $"left sample {i}");
            Assert.That(target[i * 2 + 1], Is.EqualTo(expectedRight), $"right sample {i}");
        }
    }
}
