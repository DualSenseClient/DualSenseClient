using DualSenseClient.Controllers.DualSense.Audio;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio;

public class ViiperDualShock4AudioCaptureTests
{
    [Test]
    public void ConvertToStereoFloat_ScalesBothChannels()
    {
        // One 2-channel S16LE frame = [left, right].
        short[] pcm =
        [
            16384, -8192,
            0, 32767
        ];
        byte[] raw = new byte[pcm.Length * sizeof(short)];
        Buffer.BlockCopy(pcm, 0, raw, 0, raw.Length);

        float[] stereo = new float[pcm.Length];
        ViiperDualShock4AudioCapture.ConvertToStereoFloat(raw, stereo);

        Assert.Multiple(() =>
        {
            Assert.That(stereo[0], Is.EqualTo(16384f / 32768f).Within(1e-6f), "frame 0 left");
            Assert.That(stereo[1], Is.EqualTo(-8192f / 32768f).Within(1e-6f), "frame 0 right");
            Assert.That(stereo[2], Is.EqualTo(0f).Within(1e-6f), "frame 1 left");
            Assert.That(stereo[3], Is.EqualTo(32767f / 32768f).Within(1e-6f), "frame 1 right");
        });
    }

    [Test]
    public void Upsampler32To48_EveryTwoInputFramesYieldThreeOutputFrames()
    {
        ViiperDualShock4AudioCapture.Upsampler32To48 upsampler = new ViiperDualShock4AudioCapture.Upsampler32To48();

        float[] input = [0f, 0f, 1f, 1f];
        float[] output = new float[((int)(input.Length / 2 * 1.5) + 2) * 2];

        int written = upsampler.Process(input, output);

        Assert.That(written, Is.EqualTo(3), "2 input frames must produce 3 output frames (3:2 ratio)");
    }

    [Test]
    public void Upsampler32To48_ConstantSignalStaysConstantAcrossChunks()
    {
        ViiperDualShock4AudioCapture.Upsampler32To48 upsampler = new ViiperDualShock4AudioCapture.Upsampler32To48();
        float[] chunk = [0.5f, 0.5f, 0.5f, 0.5f];
        float[] output = new float[8];
        float[] allOutput = new float[24];
        int written = 0;

        // Warm up: the first output sample is anchored to the (zero) initial history,
        // so a first chunk establishes the constant history.
        upsampler.Process(chunk, output);
        for (int i = 0; i < 4; i++)
        {
            int n = upsampler.Process(chunk, output);
            output.AsSpan(0, n * 2).CopyTo(allOutput.AsSpan(written * 2));
            written += n;
        }

        Assert.That(written, Is.EqualTo(12), "4 × 2 input frames must produce 12 output frames");
        for (int i = 0; i < written * 2; i++)
        {
            Assert.That(allOutput[i], Is.EqualTo(0.5f).Within(1e-6f), "a constant signal must not be distorted");
        }
    }

    [Test]
    public void Upsampler32To48_RampIsContinuousAcrossChunkBoundaries()
    {
        ViiperDualShock4AudioCapture.Upsampler32To48 upsampler = new ViiperDualShock4AudioCapture.Upsampler32To48();
        float[] output = new float[16];
        float[] allOutput = new float[32];
        int written = 0;

        // Overlapping 2-frame ramp chunks: [0,1], [1,2], [2,3], [3,4] sample the
        // continuous ramp 0..4, so the resampled stream must never decrease.
        for (int chunk = 0; chunk < 4; chunk++)
        {
            float[] input = [chunk, chunk, chunk + 1f, chunk + 1f];
            int n = upsampler.Process(input, output);
            output.AsSpan(0, n * 2).CopyTo(allOutput.AsSpan(written * 2));
            written += n;
        }

        Assert.That(written, Is.EqualTo(12), "4 × 2 input frames must produce 12 output frames");
        for (int i = 1; i < written; i++)
        {
            Assert.That(allOutput[i * 2], Is.GreaterThanOrEqualTo(allOutput[(i - 1) * 2] - 1e-6f),
                "the resampled ramp must never decrease, so no discontinuity at chunk boundaries");
        }

        Assert.That(allOutput[0], Is.EqualTo(0f).Within(1e-6f), "the stream must start at the first input sample");
        Assert.That(allOutput[written * 2 - 2], Is.EqualTo(4f - 2f / 3f).Within(1e-6f),
            "the last output lands 2/3 of a frame before the final input sample; the ramp value 4 carries into the next chunk's first output");
    }

    [Test]
    public void Upsampler32To48_LongRunProducesExactOneAndAHalfRatio()
    {
        ViiperDualShock4AudioCapture.Upsampler32To48 upsampler = new ViiperDualShock4AudioCapture.Upsampler32To48();
        const int frames = 1000;
        float[] input = new float[frames * 2];
        Array.Fill(input, 0.25f);
        float[] output = new float[((int)(frames * 1.5) + 2) * 2];

        int written = upsampler.Process(input, output);

        Assert.That(written, Is.EqualTo(frames * 3 / 2), "1000 input frames must produce exactly 1500 output frames");
    }
}