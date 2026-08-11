using DualSenseClient.Controllers.DualSense.Audio;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio;

public class ViiperDualSenseAudioCaptureTests
{
    [Test]
    public void ConvertToStereoFloat_KeepsFrontChannelsAndScales()
    {
        // One 4-channel S16LE frame = [front L, front R, rear L, rear R].
        short[] quad =
        [
            16384, -8192, 4096, 32767,
            0, -16384, -32768, 8192
        ];
        byte[] raw = new byte[quad.Length * sizeof(short)];
        Buffer.BlockCopy(quad, 0, raw, 0, raw.Length);

        float[] stereo = new float[quad.Length / 2];
        ViiperDualSenseAudioCapture.ConvertToStereoFloat(raw, stereo);

        Assert.Multiple(() =>
        {
            Assert.That(stereo[0], Is.EqualTo(16384f / 32768f).Within(1e-6f), "frame 0 front left");
            Assert.That(stereo[1], Is.EqualTo(-8192f / 32768f).Within(1e-6f), "frame 0 front right");
            Assert.That(stereo[2], Is.EqualTo(0f).Within(1e-6f), "frame 1 front left");
            Assert.That(stereo[3], Is.EqualTo(-16384f / 32768f).Within(1e-6f), "frame 1 front right");
        });
    }

    [Test]
    public void ConvertToStereoFloat_IgnoresTheRearHapticsChannels()
    {
        // Extreme rear values must not leak into the forwarded stereo.
        short[] quad =
        [
            -32768, -32768, 32767, 32767,
            32767, 32767, -32768, -32768
        ];
        byte[] raw = new byte[quad.Length * sizeof(short)];
        Buffer.BlockCopy(quad, 0, raw, 0, raw.Length);

        float[] stereo = new float[quad.Length / 2];
        ViiperDualSenseAudioCapture.ConvertToStereoFloat(raw, stereo);

        Assert.Multiple(() =>
        {
            Assert.That(stereo[0], Is.EqualTo(-32768f / 32768f).Within(1e-6f));
            Assert.That(stereo[1], Is.EqualTo(-32768f / 32768f).Within(1e-6f));
            Assert.That(stereo[2], Is.EqualTo(32767f / 32768f).Within(1e-6f));
            Assert.That(stereo[3], Is.EqualTo(32767f / 32768f).Within(1e-6f));
        });
    }
}