using DualSenseClient.Controllers.DualSense.Triggers;

namespace DualSenseClient.Tests.Controllers.DualSense.Triggers;

public class TriggerEffectBuilderTests
{
    [Test]
    public void Off_ProducesAllZeroBlock()
    {
        TriggerEffectBlock block = TriggerEffectBuilder.Off();

        Assert.Multiple(() =>
        {
            Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Off));
            Assert.That(block.Parameters.ToArray(), Is.All.EqualTo(0));
        });
    }

    [Test]
    public void Resistance_SetsModeAndStartForce()
    {
        TriggerEffectBlock block = TriggerEffectBuilder.Resistance(40, 230);

        Assert.Multiple(() =>
        {
            Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Resistance));
            Assert.That(block.Parameters[0], Is.EqualTo(40));
            Assert.That(block.Parameters[1], Is.EqualTo(230));
            Assert.That(block.Parameters[2], Is.EqualTo(0));
        });
    }

    [Test]
    public void Trigger_SetsModeAndStartEndForce()
    {
        TriggerEffectBlock block = TriggerEffectBuilder.Trigger(15, 100, 255);

        Assert.Multiple(() =>
        {
            Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Trigger));
            Assert.That(block.Parameters[0], Is.EqualTo(15));
            Assert.That(block.Parameters[1], Is.EqualTo(100));
            Assert.That(block.Parameters[2], Is.EqualTo(255));
            Assert.That(block.Parameters[3], Is.EqualTo(0));
        });
    }

    [Test]
    public void Automatic_FrequencyIsFirstParameter()
    {
        // §5.1: mode 0x06 reads frequency, force, start position in that order.
        TriggerEffectBlock block = TriggerEffectBuilder.Automatic(10, 255, 20);

        Assert.Multiple(() =>
        {
            Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Automatic));
            Assert.That(block.Parameters[0], Is.EqualTo(10));
            Assert.That(block.Parameters[1], Is.EqualTo(255));
            Assert.That(block.Parameters[2], Is.EqualTo(20));
            Assert.That(block.Parameters[3], Is.EqualTo(0));
        });
    }

    [Test]
    public void Blocks_AreExactlyElevenBytes()
    {
        TriggerEffectBlock block = TriggerEffectBuilder.Resistance(40, 230);

        Assert.That(block.Parameters.Length, Is.EqualTo(10));
    }
}
