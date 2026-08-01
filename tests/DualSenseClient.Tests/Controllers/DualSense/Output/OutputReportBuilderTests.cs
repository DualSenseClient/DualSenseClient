using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.DualSense.Triggers;

namespace DualSenseClient.Tests.Controllers.DualSense.Output;

public class OutputReportBuilderTests
{
    private static byte[] RawPayload(SetStateData payload)
    {
        byte[] raw = new byte[SetStateData.PayloadSize];
        payload.CopyTo(raw, 0);
        return raw;
    }

    private static void AssertHasFlag(ValidFlags flags, ValidFlags expected) =>
        Assert.That(flags & expected, Is.EqualTo(expected), $"expected flag {expected}");

    private static void AssertNotHasFlag(ValidFlags flags, ValidFlags unexpected) =>
        Assert.That(flags & unexpected, Is.EqualTo(ValidFlags.None), $"unexpected flag {unexpected}");

    [Test]
    public void DefaultBuild_DerivesBaseFlags()
    {
        SetStateData payload = new OutputReportBuilder().Build();
        byte[] raw = RawPayload(payload);

        Assert.Multiple(() =>
        {
            Assert.That(payload.ValidFlag0, Is.EqualTo(ValidFlags.EnableRumbleEmulation));
            Assert.That(payload.ValidFlag1, Is.EqualTo(ValidFlags.AllowMuteLight | ValidFlags.AllowLedColor));
            Assert.That(payload.ValidFlag2,
                Is.EqualTo(ValidFlags.AllowBrightnessChange | ValidFlags.AllowColorFadeAnim));
            Assert.That(raw[43], Is.EqualTo((byte)PlayerLedMask.None));
            Assert.That(raw[44], Is.EqualTo(0));
            Assert.That(raw[45], Is.EqualTo(0));
            Assert.That(raw[46], Is.EqualTo(0));
        });
    }

    [Test]
    public void Rumble_WritesMotorsAndKeepsRumbleFlag()
    {
        SetStateData payload = new OutputReportBuilder
        {
            RumbleRight = 100,
            RumbleLeft = 50
        }.Build();
        byte[] raw = RawPayload(payload);

        Assert.Multiple(() =>
        {
            Assert.That(raw[2], Is.EqualTo(100));
            Assert.That(raw[3], Is.EqualTo(50));
            AssertHasFlag(payload.ValidFlag0, ValidFlags.EnableRumbleEmulation);
        });
    }

    [Test]
    public void R2TriggerEffect_EnablesAllowRightTriggerFfb()
    {
        SetStateData payload = new OutputReportBuilder
        {
            R2TriggerEffect = TriggerEffectBuilder.Resistance(40, 230)
        }.Build();
        byte[] raw = RawPayload(payload);

        Assert.Multiple(() =>
        {
            AssertHasFlag(payload.ValidFlag0, ValidFlags.AllowRightTriggerFfb);
            AssertNotHasFlag(payload.ValidFlag0, ValidFlags.AllowLeftTriggerFfb);
            Assert.That(raw[10], Is.EqualTo((byte)TriggerEffectType.Resistance));
            Assert.That(raw[11], Is.EqualTo(40));
        });
    }

    [Test]
    public void L2TriggerEffect_EnablesAllowLeftTriggerFfb()
    {
        SetStateData payload = new OutputReportBuilder
        {
            L2TriggerEffect = TriggerEffectBuilder.Trigger(15, 100, 255)
        }.Build();

        AssertHasFlag(payload.ValidFlag0, ValidFlags.AllowLeftTriggerFfb);
    }

    [Test]
    public void PlayerLeds_EnablePlayerIndicatorFlag()
    {
        SetStateData payload = new OutputReportBuilder
        {
            PlayerLeds = PlayerLedMask.Led1 | PlayerLedMask.Led5
        }.Build();
        byte[] raw = RawPayload(payload);

        Assert.Multiple(() =>
        {
            AssertHasFlag(payload.ValidFlag1, ValidFlags.AllowPlayerIndicators);
            Assert.That(raw[43], Is.EqualTo(0x11));
        });
    }

    [Test]
    public void Rgb_WritesLightbarBytes()
    {
        SetStateData payload = new OutputReportBuilder
        {
            LedRed = 0x10,
            LedGreen = 0x20,
            LedBlue = 0x30
        }.Build();
        byte[] raw = RawPayload(payload);

        Assert.Multiple(() =>
        {
            Assert.That(raw[44], Is.EqualTo(0x10));
            Assert.That(raw[45], Is.EqualTo(0x20));
            Assert.That(raw[46], Is.EqualTo(0x30));
            AssertHasFlag(payload.ValidFlag1, ValidFlags.AllowLedColor);
        });
    }

    [Test]
    public void Build_CanBeFramedForUsb()
    {
        SetStateData payload = new OutputReportBuilder
        {
            RumbleRight = 200,
            PlayerLeds = PlayerLedMask.All,
            LedBlue = 0xFF
        }.Build();

        OutputReport report = OutputReport.ForUsb(payload);

        Assert.Multiple(() =>
        {
            Assert.That(report.Length, Is.EqualTo(48));
            Assert.That(report.Raw[0], Is.EqualTo(0x02));
            Assert.That(report.Raw[1 + 2], Is.EqualTo(200));
            Assert.That(report.Raw[1 + 43], Is.EqualTo((byte)PlayerLedMask.All));
            Assert.That(report.Raw[1 + 46], Is.EqualTo(0xFF));
        });
    }
}