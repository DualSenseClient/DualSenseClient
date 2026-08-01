using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.DualSense.Triggers;

namespace DualSenseClient.Tests.Controllers.DualSense.Output;

public class SetStateDataTests
{
    [Test]
    public void DefaultConstructor_ProducesFortySevenZeroBytes()
    {
        SetStateData payload = new SetStateData();
        byte[] raw = new byte[SetStateData.PayloadSize];
        payload.CopyTo(raw, 0);

        Assert.Multiple(() =>
        {
            Assert.That(raw, Is.All.EqualTo(0));
            Assert.That(raw, Has.Length.EqualTo(47));
        });
    }

    [Test]
    public void Properties_WriteToExpectedOffsets()
    {
        SetStateData payload = new SetStateData
        {
            ValidFlag0 = ValidFlags.EnableRumbleEmulation | ValidFlags.AllowLeftTriggerFfb,
            ValidFlag1 = ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators,
            RumbleRight = 0xAA,
            RumbleLeft = 0xBB,
            HeadphoneVolume = 0x01,
            SpeakerVolume = 0x02,
            MicVolume = 0x03,
            AudioControl = AudioControl.OutputPathSpeaker,
            MuteLedMode = 0x01,
            PowerSaveControl = 0x02,
            HostTimestamp = 0x11223344,
            MotorPowerReduction = 0x34,
            AudioControl2 = 0x56,
            ValidFlag2 = ValidFlags.AllowBrightnessChange,
            HapticLowPassFilter = 0x01,
            LightFadeAnimation = 0x02,
            LightBrightness = 0x01,
            PlayerLeds = PlayerLedMask.Led1 | PlayerLedMask.Led5,
            LedRed = 0x10,
            LedGreen = 0x20,
            LedBlue = 0x30
        };

        byte[] raw = new byte[SetStateData.PayloadSize];
        payload.CopyTo(raw, 0);

        Assert.Multiple(() =>
        {
            Assert.That(raw[0], Is.EqualTo((byte)(ValidFlags.EnableRumbleEmulation | ValidFlags.AllowLeftTriggerFfb)));
            Assert.That(raw[1], Is.EqualTo((byte)(ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators)));
            Assert.That(raw[2], Is.EqualTo(0xAA));
            Assert.That(raw[3], Is.EqualTo(0xBB));
            Assert.That(raw[4], Is.EqualTo(0x01));
            Assert.That(raw[5], Is.EqualTo(0x02));
            Assert.That(raw[6], Is.EqualTo(0x03));
            Assert.That(raw[7], Is.EqualTo((byte)AudioControl.OutputPathSpeaker));
            Assert.That(raw[8], Is.EqualTo(0x01));
            Assert.That(raw[9], Is.EqualTo(0x02));
            Assert.That(raw[32], Is.EqualTo(0x44));
            Assert.That(raw[33], Is.EqualTo(0x33));
            Assert.That(raw[34], Is.EqualTo(0x22));
            Assert.That(raw[35], Is.EqualTo(0x11));
            Assert.That(raw[36], Is.EqualTo(0x34));
            Assert.That(raw[37], Is.EqualTo(0x56));
            Assert.That(raw[38], Is.EqualTo((byte)ValidFlags.AllowBrightnessChange));
            Assert.That(raw[39], Is.EqualTo(0x01));
            Assert.That(raw[40], Is.EqualTo(0));
            Assert.That(raw[41], Is.EqualTo(0x02));
            Assert.That(raw[42], Is.EqualTo(0x01));
            Assert.That(raw[43], Is.EqualTo((byte)(PlayerLedMask.Led1 | PlayerLedMask.Led5)));
            Assert.That(raw[44], Is.EqualTo(0x10));
            Assert.That(raw[45], Is.EqualTo(0x20));
            Assert.That(raw[46], Is.EqualTo(0x30));
        });
    }

    [Test]
    public void HostTimestamp_IsLittleEndian()
    {
        SetStateData payload = new SetStateData { HostTimestamp = 0x01020304 };
        byte[] raw = new byte[SetStateData.PayloadSize];
        payload.CopyTo(raw, 0);

        Assert.Multiple(() =>
        {
            Assert.That(raw[32], Is.EqualTo(0x04));
            Assert.That(raw[33], Is.EqualTo(0x03));
            Assert.That(raw[34], Is.EqualTo(0x02));
            Assert.That(raw[35], Is.EqualTo(0x01));
        });
    }

    [Test]
    public void TriggerEffectBlocks_ArePlacedAtCorrectOffsets()
    {
        SetStateData payload = new SetStateData
        {
            R2TriggerEffect = TriggerEffectBuilder.Resistance(40, 230),
            L2TriggerEffect = TriggerEffectBuilder.Automatic(10, 255, 20)
        };
        byte[] raw = new byte[SetStateData.PayloadSize];
        payload.CopyTo(raw, 0);

        Assert.Multiple(() =>
        {
            Assert.That(raw[10], Is.EqualTo((byte)TriggerEffectType.Resistance));
            Assert.That(raw[11], Is.EqualTo(40));
            Assert.That(raw[12], Is.EqualTo(230));
            Assert.That(raw[21], Is.EqualTo((byte)TriggerEffectType.Automatic));
            Assert.That(raw[22], Is.EqualTo(10));
            Assert.That(raw[23], Is.EqualTo(255));
            Assert.That(raw[24], Is.EqualTo(20));
            Assert.That(raw[20], Is.EqualTo(0));
            Assert.That(raw[31], Is.EqualTo(0));
        });
    }

    [Test]
    public void WrapConstructor_ReadsPayloadFromOffset()
    {
        byte[] buffer = new byte[50];
        buffer[3] = 0x02;
        buffer[44] = 0xAB;

        SetStateData payload = new SetStateData(buffer, 3);
        byte[] raw = new byte[SetStateData.PayloadSize];
        payload.CopyTo(raw, 0);

        Assert.Multiple(() =>
        {
            Assert.That(payload.ValidFlag0, Is.EqualTo((ValidFlags)0x02));
            Assert.That(raw[41], Is.EqualTo(0xAB));
        });
    }
}