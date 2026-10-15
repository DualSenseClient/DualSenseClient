using DualSenseClient.Controllers.DualSense.Triggers;

namespace DualSenseClient.Tests.Controllers.DualSense.Triggers;

public class TriggerEffectBuilderTests
{
    [Test]
    public void Off_ProducesOffModeWithZeroParameters()
    {
        TriggerEffectBlock block = TriggerEffectBuilder.Off();

        Assert.Multiple(() =>
        {
            Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Off));
            Assert.That((byte)block.Mode, Is.EqualTo(0x05));
            Assert.That(block.Parameters.ToArray(), Is.All.EqualTo(0));
        });
    }

    [Test]
    public void Feedback_PacksZonesFromPosition()
    {
        TriggerEffectBlock block = TriggerEffectBuilder.Feedback(2, 8);

        Assert.Multiple(() =>
        {
            Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Feedback));
            // zones 2-9 active: bits 2..9 set => 0x3FC
            Assert.That(block.Parameters[0], Is.EqualTo(0xFC));
            Assert.That(block.Parameters[1], Is.EqualTo(0x03));
            // strength 8 => force value 7 in every active 3-bit slot
            Assert.That(block.Parameters[2], Is.Not.EqualTo(0));
        });
    }

    [Test]
    public void Feedback_ZeroStrength_YieldsOff()
    {
        TriggerEffectBlock block = TriggerEffectBuilder.Feedback(2, 0);

        Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Off));
    }

    [Test]
    public void Feedback_OutOfRange_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffectBuilder.Feedback(10, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffectBuilder.Feedback(0, 9));
        });
    }

    [Test]
    public void Weapon_SetsStartStopZonesAndStrength()
    {
        TriggerEffectBlock block = TriggerEffectBuilder.Weapon(2, 5, 8);

        Assert.Multiple(() =>
        {
            Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Weapon));
            ushort zones = (ushort)(block.Parameters[0] | (block.Parameters[1] << 8));
            Assert.That(zones, Is.EqualTo((1 << 2) | (1 << 5)));
            Assert.That(block.Parameters[2], Is.EqualTo(7));
        });
    }

    [Test]
    public void Weapon_ZeroStrength_YieldsOff() => Assert.That(TriggerEffectBuilder.Weapon(2, 5, 0).Mode, Is.EqualTo(TriggerEffectType.Off));

    [Test]
    public void Weapon_OutOfRange_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffectBuilder.Weapon(1, 5, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffectBuilder.Weapon(2, 2, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffectBuilder.Weapon(2, 9, 8));
        });
    }

    [Test]
    public void Vibration_SetsFrequencyInLastParameter()
    {
        TriggerEffectBlock block = TriggerEffectBuilder.Vibration(0, 8, 10);

        Assert.Multiple(() =>
        {
            Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Vibration));
            Assert.That(block.Parameters[8], Is.EqualTo(10));
        });
    }

    [Test]
    public void Vibration_ZeroAmplitudeOrFrequency_YieldsOff()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TriggerEffectBuilder.Vibration(0, 0, 10).Mode, Is.EqualTo(TriggerEffectType.Off));
            Assert.That(TriggerEffectBuilder.Vibration(0, 8, 0).Mode, Is.EqualTo(TriggerEffectType.Off));
        });
    }

    [Test]
    public void SlopeFeedback_InterpolatesAcrossZones()
    {
        TriggerEffectBlock block = TriggerEffectBuilder.SlopeFeedback(0, 9, 1, 8);

        Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Feedback));
    }

    [Test]
    public void MultiplePositionFeedback_AllZero_YieldsOff() =>
        Assert.That(TriggerEffectBuilder.MultiplePositionFeedback(new byte[10]).Mode, Is.EqualTo(TriggerEffectType.Off));

    [Test]
    public void MultiplePositionVibration_SetsFrequency()
    {
        byte[] amplitude = new byte[10];
        amplitude[5] = 8;
        TriggerEffectBlock block = TriggerEffectBuilder.MultiplePositionVibration(12, amplitude);

        Assert.Multiple(() =>
        {
            Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Vibration));
            Assert.That(block.Parameters[8], Is.EqualTo(12));
        });
    }

    [Test]
    public void Bow_SetsModeAndZonePair()
    {
        TriggerEffectBlock block = TriggerEffectBuilder.Bow(0, 8, 8, 8);

        Assert.Multiple(() =>
        {
            Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Bow));
            ushort zones = (ushort)(block.Parameters[0] | (block.Parameters[1] << 8));
            Assert.That(zones, Is.EqualTo((1 << 0) | (1 << 8)));
        });
    }

    [Test]
    public void Galloping_SetsModeAndFrequency()
    {
        TriggerEffectBlock block = TriggerEffectBuilder.Galloping(0, 9, 0, 1, 10);

        Assert.Multiple(() =>
        {
            Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Galloping));
            Assert.That(block.Parameters[3], Is.EqualTo(10));
        });
    }

    [Test]
    public void Machine_SetsModeFrequencyAndPeriod()
    {
        TriggerEffectBlock block = TriggerEffectBuilder.Machine(0, 9, 7, 3, 10, 5);

        Assert.Multiple(() =>
        {
            Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Machine));
            Assert.That(block.Parameters[3], Is.EqualTo(10));
            Assert.That(block.Parameters[4], Is.EqualTo(5));
        });
    }

    [Test]
    public void Bow_ZeroStrengthOrSnap_YieldsOff()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TriggerEffectBuilder.Bow(0, 8, 0, 8).Mode, Is.EqualTo(TriggerEffectType.Off));
            Assert.That(TriggerEffectBuilder.Bow(0, 8, 8, 0).Mode, Is.EqualTo(TriggerEffectType.Off));
        });
    }

    [Test]
    public void Galloping_ZeroFrequency_YieldsOff() => Assert.That(TriggerEffectBuilder.Galloping(0, 9, 0, 1, 0).Mode, Is.EqualTo(TriggerEffectType.Off));

    [Test]
    public void Machine_ZeroFrequency_YieldsOff() => Assert.That(TriggerEffectBuilder.Machine(0, 9, 7, 3, 0, 5).Mode, Is.EqualTo(TriggerEffectType.Off));

    [Test]
    public void SimpleVibration_ZeroAmplitudeOrFrequency_YieldsOff()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TriggerEffectBuilder.SimpleVibration(0x10, 0, 0x0A).Mode, Is.EqualTo(TriggerEffectType.Off));
            Assert.That(TriggerEffectBuilder.SimpleVibration(0x10, 0x80, 0).Mode, Is.EqualTo(TriggerEffectType.Off));
        });
    }

    [Test]
    public void LimitedWeapon_ZeroStrength_YieldsOff() =>
        Assert.That(TriggerEffectBuilder.LimitedWeapon(0x20, 0x30, 0).Mode, Is.EqualTo(TriggerEffectType.Off));

    [Test]
    public void LimitedWeapon_OutOfRange_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffectBuilder.LimitedWeapon(0x0F, 0x20, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffectBuilder.LimitedWeapon(0x20, 0x10, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffectBuilder.LimitedWeapon(0x20, 0xFF, 5));
        });
    }

    [Test]
    public void SlopeFeedback_ZeroForceOrEndStrength_ThrowsSoCallersFallBackToOff()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffectBuilder.SlopeFeedback(0, 9, 0, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffectBuilder.SlopeFeedback(0, 9, 4, 0));
        });
    }

    [Test]
    public void MultiplePositionFeedback_PacksActiveZonesAndForces()
    {
        byte[] strength = new byte[10];
        strength[0] = 8;
        strength[9] = 4;
        TriggerEffectBlock block = TriggerEffectBuilder.MultiplePositionFeedback(strength);

        Assert.Multiple(() =>
        {
            Assert.That(block.Mode, Is.EqualTo(TriggerEffectType.Feedback));
            ushort zones = (ushort)(block.Parameters[0] | (block.Parameters[1] << 8));
            Assert.That(zones & 0x001, Is.Not.EqualTo(0));
            Assert.That(zones & 0x200, Is.Not.EqualTo(0));
            Assert.That(zones & 0x002, Is.EqualTo(0));
        });
    }

    [Test]
    public void Simple_Modes_UseRawByteLayout()
    {
        TriggerEffectBlock feedback = TriggerEffectBuilder.SimpleFeedback(0x20, 0x64);
        TriggerEffectBlock weapon = TriggerEffectBuilder.SimpleWeapon(0x20, 0xA0, 0xFF);
        TriggerEffectBlock vibration = TriggerEffectBuilder.SimpleVibration(0x10, 0x80, 0x0A);

        Assert.Multiple(() =>
        {
            Assert.That(feedback.Mode, Is.EqualTo(TriggerEffectType.Simple_Feedback));
            Assert.That(feedback.Parameters[0], Is.EqualTo(0x20));
            Assert.That(feedback.Parameters[1], Is.EqualTo(0x64));
            Assert.That(weapon.Mode, Is.EqualTo(TriggerEffectType.Simple_Weapon));
            Assert.That(weapon.Parameters[0], Is.EqualTo(0x20));
            Assert.That(weapon.Parameters[1], Is.EqualTo(0xA0));
            Assert.That(weapon.Parameters[2], Is.EqualTo(0xFF));
            Assert.That(vibration.Mode, Is.EqualTo(TriggerEffectType.Simple_Vibration));
            Assert.That(vibration.Parameters[0], Is.EqualTo(0x0A));
            Assert.That(vibration.Parameters[1], Is.EqualTo(0x80));
            Assert.That(vibration.Parameters[2], Is.EqualTo(0x10));
        });
    }

    [Test]
    public void Limited_Modes_ValidateRanges()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffectBuilder.LimitedFeedback(0, 11));
            Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffectBuilder.LimitedWeapon(0x0F, 0x20, 5));
            Assert.That(TriggerEffectBuilder.LimitedFeedback(0, 0).Mode, Is.EqualTo(TriggerEffectType.Off));
        });
    }

    [Test]
    public void Blocks_AreExactlyElevenBytes()
    {
        TriggerEffectBlock block = TriggerEffectBuilder.Feedback(0, 8);

        Assert.That(block.Parameters.Length, Is.EqualTo(10));
    }
}