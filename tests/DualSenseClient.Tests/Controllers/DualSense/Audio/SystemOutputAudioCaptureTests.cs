using DualSenseClient.Controllers.DualSense.Audio;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio;

public class SystemOutputAudioCaptureTests
{
    [Test]
    public void ToStereo_MonoIsDuplicatedToBothChannels()
    {
        float[] mono = [0.25f, -0.5f];
        float[] stereo = new float[mono.Length * 2];

        SystemOutputAudioCapture.ToStereo(mono, 1, stereo);

        Assert.Multiple(() =>
        {
            Assert.That(stereo[0], Is.EqualTo(0.25f).Within(1e-6f), "frame 0 left");
            Assert.That(stereo[1], Is.EqualTo(0.25f).Within(1e-6f), "frame 0 right");
            Assert.That(stereo[2], Is.EqualTo(-0.5f).Within(1e-6f), "frame 1 left");
            Assert.That(stereo[3], Is.EqualTo(-0.5f).Within(1e-6f), "frame 1 right");
        });
    }

    [Test]
    public void ToStereo_SurroundKeepsFrontPair()
    {
        // 5.1 frame: FL, FR, C, LFE, BL, BR — only the front pair is forwarded.
        float[] surround = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f];
        float[] stereo = new float[4];

        SystemOutputAudioCapture.ToStereo(surround, 6, stereo);

        Assert.Multiple(() =>
        {
            Assert.That(stereo[0], Is.EqualTo(0.1f).Within(1e-6f), "frame 0 left");
            Assert.That(stereo[1], Is.EqualTo(0.2f).Within(1e-6f), "frame 0 right");
            Assert.That(stereo[2], Is.Zero, "single frame only");
            Assert.That(stereo[3], Is.Zero, "single frame only");
        });
    }

    [Test]
    public void ToStereo_StereoPassesThrough()
    {
        float[] input = [0.1f, -0.2f, 0.3f, -0.4f];
        float[] stereo = new float[input.Length];

        SystemOutputAudioCapture.ToStereo(input, 2, stereo);

        Assert.That(stereo, Is.EqualTo(input));
    }

    [Test]
    public void LinearResampler_44100To48000ProducesTheExactRatio()
    {
        // 44.1 kHz → 48 kHz consumes 0.91875 source frames per output frame.
        SystemOutputAudioCapture.LinearResampler resampler = new SystemOutputAudioCapture.LinearResampler(44100.0 / 48000.0);
        float[] input = new float[1024 * 2];
        float[] output = new float[(int)(1024 / resampler.Step + 4) * 2];

        int written = resampler.Process(input, output);

        Assert.That(written, Is.EqualTo(1024 / resampler.Step).Within(1), "1024 source frames must yield ~1114 output frames");
    }

    [Test]
    public void LinearResampler_ConstantSignalStaysConstantAcrossChunks()
    {
        SystemOutputAudioCapture.LinearResampler resampler = new SystemOutputAudioCapture.LinearResampler(44100.0 / 48000.0);
        float[] chunk = [0.5f, -0.5f];
        float[] allOutput = new float[64];
        int written = 0;

        // Warm up: the first output frame is anchored to the (zero) initial history, so a
        // first chunk establishes the constant history.
        resampler.Process(chunk, allOutput);
        written = 0;
        for (int i = 0; i < 16; i++)
        {
            int n = resampler.Process(chunk, allOutput.AsSpan(written * 2));
            written += n;
        }

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.GreaterThan(0), "the resampler must produce output");
            for (int f = 1; f < written; f++)
            {
                Assert.That(allOutput[f * 2], Is.EqualTo(0.5f).Within(1e-4f), $"left frame {f}");
                Assert.That(allOutput[f * 2 + 1], Is.EqualTo(-0.5f).Within(1e-4f), $"right frame {f}");
            }
        });
    }

    [Test]
    public void LinearResampler_InterpolatesBetweenSourceFrames()
    {
        // 32 kHz → 48 kHz: each output frame advances two thirds of a source frame, so a
        // ramp is sampled on and between the source frames.
        SystemOutputAudioCapture.LinearResampler resampler = new SystemOutputAudioCapture.LinearResampler(2.0 / 3.0);
        float[] input = new float[8];
        for (int f = 0; f < 4; f++)
        {
            input[f * 2] = f;
            input[f * 2 + 1] = f;
        }

        float[] output = new float[2 * 6];
        int written = resampler.Process(input, output);

        Assert.Multiple(() =>
        {
            // Position p reproduces the source signal at coordinate p - 1 (one frame of
            // history), so the 0,1,2,3 ramp comes out as 0, 0, 1/3, 1, 5/3, 7/3.
            Assert.That(written, Is.EqualTo(6), "4 source frames at two thirds rate yield 6 output frames");
            Assert.That(output[0], Is.Zero.Within(1e-6f), "frame 0 holds the history frame");
            Assert.That(output[2], Is.Zero.Within(1e-6f), "frame 1 interpolates history to source frame 0");
            Assert.That(output[4], Is.EqualTo(1f / 3f).Within(1e-6f), "frame 2 interpolates source frames 0 to 1");
            Assert.That(output[6], Is.EqualTo(1f).Within(1e-6f), "frame 3 sits on source frame 1");
            Assert.That(output[10], Is.EqualTo(7f / 3f).Within(1e-6f), "frame 5 is two thirds past source frame 2");
        });
    }
}