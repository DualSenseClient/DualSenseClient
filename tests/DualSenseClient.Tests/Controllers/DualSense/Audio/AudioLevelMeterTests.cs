using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Tests.Emulation.TestSupport;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio;

public class AudioLevelMeterTests
{
    private const double BlockSeconds = 0.016;

    [Test]
    public void Process_RisesTowardPeak()
    {
        AudioLevelMeter meter = new AudioLevelMeter();

        meter.Process([0.8f, -0.8f], BlockSeconds);

        Assert.That(
            meter.Level,
            Is.EqualTo(0.8 * (1 - Math.Exp(-BlockSeconds / 0.06))).Within(1e-4));
    }

    [Test]
    public void Process_RepeatedBlocksReachPeak()
    {
        AudioLevelMeter meter = new AudioLevelMeter();
        float[] block = [0.8f, -0.8f];

        for (int i = 0; i < 1000; i++)
        {
            meter.Process(block, BlockSeconds);
        }

        Assert.That(meter.Level, Is.EqualTo(0.8).Within(1e-4));
    }

    [Test]
    public void Process_SilenceDecays()
    {
        AudioLevelMeter meter = new AudioLevelMeter();
        float[] block = [0.8f, -0.8f];
        for (int i = 0; i < 1000; i++)
        {
            meter.Process(block, BlockSeconds);
        }

        meter.Process(new float[2], 0.2);

        Assert.That(meter.Level, Is.EqualTo(0.8 * Math.Exp(-1)).Within(1e-3));
    }

    [Test]
    public void Reset_ReturnsToZero()
    {
        AudioLevelMeter meter = new AudioLevelMeter();
        meter.Process([0.8f, -0.8f], BlockSeconds);

        meter.Reset();

        Assert.That(meter.Level, Is.Zero);
    }

    [Test]
    public void Level_DecaysToZero_WhenBlocksStopWithoutStop()
    {
        ManualTimeProvider time = new ManualTimeProvider();
        AudioLevelMeter meter = new AudioLevelMeter(time);
        meter.Process([0.8f, -0.8f], BlockSeconds);
        Assert.That(meter.Level, Is.GreaterThan(0f));

        time.Advance(TimeSpan.FromSeconds(0.6));

        Assert.That(meter.Level, Is.Zero);
    }

    [Test]
    public void Process_AfterIdle_DoesNotResumeFromStaleLevel()
    {
        ManualTimeProvider time = new ManualTimeProvider();
        AudioLevelMeter meter = new AudioLevelMeter(time);
        for (int i = 0; i < 1000; i++)
        {
            meter.Process([0.8f, -0.8f], BlockSeconds);
        }

        time.Advance(TimeSpan.FromSeconds(0.6));
        meter.Process(new float[2], BlockSeconds);

        Assert.That(meter.Level, Is.Zero);
    }
}