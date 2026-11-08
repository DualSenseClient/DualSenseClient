using DualSenseClient.Controllers.DualSense.Audio;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio;

public class DualSenseAudioPlayerTests
{
    [Test]
    public void ToUsbFourChannel_AppliesSoftwareGainToAudioOnly()
    {
        float[] stereo = new float[DualSenseBtAudioPipeline.FramesPerBlock * 2];
        for (int i = 0; i < DualSenseBtAudioPipeline.FramesPerBlock; i++)
        {
            stereo[i * 2] = 0.5f;
            stereo[i * 2 + 1] = -0.5f;
        }

        float[] quad = new float[DualSenseBtAudioPipeline.FramesPerBlock * 4];

        DualSenseAudioPlayer.ToUsbFourChannel(stereo, quad, 0.5f, 1f);

        Assert.Multiple(() =>
        {
            Assert.That(quad[0], Is.EqualTo(0.25f), "the front-left audio channel must be scaled");
            Assert.That(quad[1], Is.EqualTo(-0.25f), "the front-right audio channel must be scaled");
            Assert.That(quad[2], Is.EqualTo(0.5f), "the rear-left haptics channel must stay unscaled");
            Assert.That(quad[3], Is.EqualTo(-0.5f), "the rear-right haptics channel must stay unscaled");
        });
    }
}