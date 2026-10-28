using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Lightbar;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.DualSense.Lightbar;

public class LightbarEffectEvaluatorTests
{
    [Test]
    public void SingleColor_AppliesBrightness()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.SingleColor, baseColor: (200, 100, 50), brightness: 50));

        AssertFrame(result, 100, 50, 25);
        Assert.That(result.Frame.PlayerLeds, Is.Null);
    }

    [Test]
    public void SingleColor_BrightnessZero_IsBlack()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.SingleColor, baseColor: (200, 100, 50), brightness: 0));

        AssertFrame(result, 0, 0, 0);
    }

    [Test]
    public void MultiColor_AtSegmentStart_ShowsPaletteEntry()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.MultiColor, 0));

        AssertFrame(result, 255, 40, 40);
    }

    [Test]
    public void MultiColor_HalfSegment_BlendsTwoEntries()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.MultiColor, 20.0 / 12));

        AssertFrame(result, 255, 90, 20);
    }

    [Test]
    public void MultiColor_NextSegmentStart_ShowsNextEntry()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.MultiColor, 20.0 / 6));

        AssertFrame(result, 255, 140, 0);
    }

    [Test]
    public void MultiColor_WrapsAfterFullLoop()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.MultiColor, 20));

        AssertFrame(result, 255, 40, 40);
    }

    [Test]
    public void MultiColor_Speed100_UsesTwoSecondLoop()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.MultiColor, 1.0 / 3, speed: 100));

        AssertFrame(result, 255, 140, 0);
    }

    [Test]
    public void MultiColor_SingleEntry_IsConstant()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.MultiColor, 7, colors: [(10, 20, 30)]));

        AssertFrame(result, 10, 20, 30);
    }

    [Test]
    public void MultiColor_EmptyPalette_FallsBackToBaseColor()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.MultiColor, 7, baseColor: (9, 8, 7), colors: []));

        AssertFrame(result, 9, 8, 7);
    }

    [Test]
    public void MultiColor_Random_IsDeterministicWithinStep()
    {
        LightbarEffectContext context = Build(
            LightbarMode.MultiColor,
            20.0 / 6 * 1.25,
            randomColor: true,
            seed: 1234);

        LightbarEffectFrame first = LightbarEffectEvaluator.Evaluate(in context).Frame;
        LightbarEffectFrame second = LightbarEffectEvaluator.Evaluate(in context).Frame;

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void MultiColor_BrightnessScalesBlendedColor()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.MultiColor, 0, 50));

        AssertFrame(result, 128, 20, 20);
    }

    [Test]
    public void Breathing_PhaseZero_IsMinimum()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.Breathing, baseColor: (200, 100, 50), breathMin: 20, breathMax: 80));

        AssertFrame(result, 40, 20, 10);
        Assert.That(result.Frame.PlayerLeds, Is.Null);
    }

    [Test]
    public void Breathing_HalfPeriod_IsMaximum()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(
                LightbarMode.Breathing,
                5,
                speed: 0,
                baseColor: (200, 100, 50),
                breathMin: 20,
                breathMax: 80));

        AssertFrame(result, 160, 80, 40);
    }

    [Test]
    public void Breathing_AppliesBrightness()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(
                LightbarMode.Breathing,
                5,
                50,
                baseColor: (200, 100, 50),
                breathMin: 20,
                breathMax: 80));

        AssertFrame(result, 80, 40, 20);
    }

    [Test]
    public void Breathing_Speed100_UsesOneSecondPeriod()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(
                LightbarMode.Breathing,
                0.5,
                speed: 100,
                baseColor: (200, 100, 50),
                breathMin: 100,
                breathMax: 100));

        AssertFrame(result, 200, 100, 50);
    }

    [Test]
    public void Breathing_RandomColor_ChangesPerBreath()
    {
        LightbarEffectFrame first = LightbarEffectEvaluator
            .Evaluate(Build(
                LightbarMode.Breathing,
                2.5,
                speed: 0,
                randomColor: true,
                baseColor: (200, 100, 50),
                breathMin: 100,
                breathMax: 100,
                seed: 1234))
            .Frame;

        LightbarEffectFrame second = LightbarEffectEvaluator
            .Evaluate(Build(
                LightbarMode.Breathing,
                12.5,
                speed: 0,
                randomColor: true,
                baseColor: (200, 100, 50),
                breathMin: 100,
                breathMax: 100,
                seed: 1234))
            .Frame;

        Assert.That(second, Is.Not.EqualTo(first));
        Assert.That(new[]
        {
            first.Red, first.Green, first.Blue
        }, Does.Contain((byte)255));
        Assert.That(new[]
        {
            second.Red, second.Green, second.Blue
        }, Does.Contain((byte)255));
    }

    [Test]
    public void Strobe_AlternatesAtHalfHertz()
    {
        AssertFrame(EvaluateStrobe(0), 200, 100, 50);
        AssertFrame(EvaluateStrobe(0.5), 200, 100, 50);
        AssertFrame(EvaluateStrobe(1.0), 0, 0, 0);
        AssertFrame(EvaluateStrobe(1.5), 0, 0, 0);
    }

    [Test]
    public void Strobe_Speed100_UsesFiveHertz()
    {
        AssertFrame(EvaluateStrobe(0.05, 100), 200, 100, 50);
        AssertFrame(EvaluateStrobe(0.1, 100), 0, 0, 0);
        AssertFrame(EvaluateStrobe(0.2, 100), 200, 100, 50);
    }

    [Test]
    public void Strobe_AppliesBrightness() => AssertFrame(EvaluateStrobe(0, brightness: 50), 100, 50, 25);

    [Test]
    public void Battery_MapsPercentageToBand()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.Battery, battery: new BatteryState(0x07)));

        AssertFrame(result, 110, 220, 60);
        Assert.That(result.BatteryBand, Is.EqualTo(7));
    }

    [Test]
    public void Battery_ChargingComplete_UsesTopBand()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.Battery, battery: new BatteryState(0x2A)));

        AssertFrame(result, 40, 180, 110);
        Assert.That(result.BatteryBand, Is.EqualTo(9));
    }

    [Test]
    public void Battery_UnknownPercentage_KeepsLastBand()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.Battery, battery: new BatteryState(0xF0), lastBatteryBand: 3));

        AssertFrame(result, 255, 160, 30);
        Assert.That(result.BatteryBand, Is.EqualTo(3));
    }

    [Test]
    public void Battery_NoStateYet_StartsAtBandFive()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.Battery, lastBatteryBand: 5));

        AssertFrame(result, 255, 230, 40);
        Assert.That(result.BatteryBand, Is.EqualTo(5));
    }

    [Test]
    public void Battery_Charging_CyclesAllTenBands()
    {
        BatteryState charging = new BatteryState(0x17);

        AssertFrame(EvaluateBattery(0, charging), 255, 60, 60);
        AssertFrame(EvaluateBattery(0.5, charging), 255, 90, 50);
        AssertFrame(EvaluateBattery(4.5, charging), 40, 180, 110);
        AssertFrame(EvaluateBattery(5.0, charging), 255, 60, 60);
    }

    [Test]
    public void Battery_ChargingStops_ReturnsToLevelColor()
    {
        AssertFrame(EvaluateBattery(0.5, new BatteryState(0x17)), 255, 90, 50);
        AssertFrame(EvaluateBattery(0.5, new BatteryState(0x07)), 110, 220, 60);
    }

    [Test]
    public void Battery_PlayerLedChase_OnlyWhenEnabledAndSupported()
    {
        Assert.That(
            EvaluateBatteryFrame(0, new BatteryState(0x17), true, true).PlayerLeds,
            Is.EqualTo(PlayerLedMask.Led1));
        Assert.That(
            EvaluateBatteryFrame(0, new BatteryState(0x17), false, true).PlayerLeds,
            Is.Null);
        Assert.That(
            EvaluateBatteryFrame(0, new BatteryState(0x17), true, false).PlayerLeds,
            Is.Null);
        Assert.That(
            EvaluateBatteryFrame(0, new BatteryState(0x07), true, true).PlayerLeds,
            Is.Null);
    }

    [Test]
    public void Battery_ChaseAdvancesEvery250ms()
    {
        BatteryState charging = new BatteryState(0x17);

        Assert.That(EvaluateBatteryFrame(0, charging, true, true).PlayerLeds, Is.EqualTo(PlayerLedMask.Led1));
        Assert.That(EvaluateBatteryFrame(0.25, charging, true, true).PlayerLeds, Is.EqualTo(PlayerLedMask.Led2));
        Assert.That(EvaluateBatteryFrame(0.5, charging, true, true).PlayerLeds, Is.EqualTo(PlayerLedMask.Led3));
        Assert.That(EvaluateBatteryFrame(0.75, charging, true, true).PlayerLeds, Is.EqualTo(PlayerLedMask.Led4));
        Assert.That(EvaluateBatteryFrame(1.0, charging, true, true).PlayerLeds, Is.EqualTo(PlayerLedMask.Led5));
        Assert.That(EvaluateBatteryFrame(1.25, charging, true, true).PlayerLeds, Is.EqualTo(PlayerLedMask.Led1));
    }

    [Test]
    public void Battery_AppliesBrightness()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.Battery, brightness: 50, battery: new BatteryState(0x00)));

        AssertFrame(result, 128, 30, 30);
    }

    [Test]
    public void AudioReactive_LevelZero_ShowsQuietColor()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.AudioReactive, audioLevel: 0, audioSensitivity: 100));

        AssertFrame(result, 0, 80, 255);
        Assert.That(result.Frame.PlayerLeds, Is.Null);
    }

    [Test]
    public void AudioReactive_MidLevel_ShowsMediumColor()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.AudioReactive, audioLevel: 0.125f, audioSensitivity: 100));

        AssertFrame(result, 255, 180, 0);
    }

    [Test]
    public void AudioReactive_FullLevel_ShowsLoudColor()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.AudioReactive, audioLevel: 0.25f, audioSensitivity: 100));

        AssertFrame(result, 255, 40, 40);
    }

    [Test]
    public void AudioReactive_InterpolatesBetweenStops()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.AudioReactive, audioLevel: 0.0625f, audioSensitivity: 100));

        AssertFrame(result, 128, 130, 128);
    }

    [Test]
    public void AudioReactive_SensitivityScalesLevel()
    {
        AssertFrame(
            LightbarEffectEvaluator.Evaluate(
                Build(LightbarMode.AudioReactive, audioLevel: 0.1f, audioSensitivity: 0)),
            26,
            90,
            230);

        AssertFrame(
            LightbarEffectEvaluator.Evaluate(
                Build(LightbarMode.AudioReactive, audioLevel: 0.1f, audioSensitivity: 100)),
            204,
            160,
            51);
    }

    [Test]
    public void AudioReactive_AppliesBrightness()
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(LightbarMode.AudioReactive, brightness: 50, audioLevel: 0, audioSensitivity: 100));

        AssertFrame(result, 0, 40, 128);
    }

    private static LightbarEffectFrame EvaluateBatteryFrame(
        double elapsed,
        BatteryState battery,
        bool batteryPlayerLeds = false,
        bool fullPlayerLedSupport = false) =>
        LightbarEffectEvaluator
            .Evaluate(Build(
                LightbarMode.Battery,
                elapsed,
                battery: battery,
                batteryPlayerLeds: batteryPlayerLeds,
                fullPlayerLedSupport: fullPlayerLedSupport))
            .Frame;

    private static LightbarEffectResult EvaluateBattery(double elapsed, BatteryState battery) =>
        LightbarEffectEvaluator.Evaluate(Build(LightbarMode.Battery, elapsed, battery: battery));

    private static LightbarEffectResult EvaluateStrobe(
        double elapsed,
        int speed = 0,
        int brightness = 100)
    {
        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            Build(
                LightbarMode.Strobe,
                elapsed,
                brightness,
                speed,
                baseColor: (200, 100, 50)));

        Assert.That(result.Frame.PlayerLeds, Is.Null);

        return result;
    }

    private static LightbarEffectContext Build(
        LightbarMode mode,
        double elapsed = 0,
        int brightness = 100,
        int speed = 0,
        bool randomColor = false,
        (byte Red, byte Green, byte Blue) baseColor = default,
        IReadOnlyList<(byte Red, byte Green, byte Blue)>? colors = null,
        int seed = 0,
        int breathMin = 0,
        int breathMax = 100,
        BatteryState? battery = null,
        int lastBatteryBand = 5,
        bool batteryPlayerLeds = false,
        bool fullPlayerLedSupport = false,
        float audioLevel = 0,
        int audioSensitivity = 50)
    {
        LightbarSettings settings = new LightbarSettings
        {
            Mode = mode,
            Red = baseColor.Red,
            Green = baseColor.Green,
            Blue = baseColor.Blue,
            Brightness = brightness,
            Speed = speed,
            RandomColor = randomColor,
            BreathMin = breathMin,
            BreathMax = breathMax,
            BatteryPlayerLeds = batteryPlayerLeds,
            AudioSensitivity = audioSensitivity
        };

        if (colors is not null)
        {
            settings.Colors = colors
                .Select(c => new LightbarColorSetting
                {
                    Red = c.Red,
                    Green = c.Green,
                    Blue = c.Blue
                })
                .ToList();
        }

        return new LightbarEffectContext
        {
            Settings = settings,
            ElapsedSeconds = elapsed,
            RandomSeed = seed,
            Battery = battery,
            LastBatteryBand = lastBatteryBand,
            FullPlayerLedSupport = fullPlayerLedSupport,
            AudioLevel = audioLevel
        };
    }

    private static void AssertFrame(LightbarEffectResult result, byte red, byte green, byte blue) =>
        Assert.That(result.Frame, Is.EqualTo(new LightbarEffectFrame(red, green, blue, null)));
}