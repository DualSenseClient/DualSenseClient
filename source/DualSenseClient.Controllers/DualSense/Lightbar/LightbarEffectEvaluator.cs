using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.DualSense.Lightbar;

/// <summary>
/// Inputs for one lightbar effect evaluation.
/// </summary>
public readonly struct LightbarEffectContext
{
    /// <summary>
    /// Initializes a new effect context with default values.
    /// </summary>
    public LightbarEffectContext()
    {
    }

    /// <summary>
    /// Gets the lightbar settings to evaluate.
    /// </summary>
    public required LightbarSettings Settings { get; init; }

    /// <summary>
    /// Gets how long the effect has been running, in seconds.
    /// </summary>
    public double ElapsedSeconds { get; init; }

    /// <summary>
    /// Gets the current battery state, when available.
    /// </summary>
    public BatteryState? Battery { get; init; }

    /// <summary>
    /// Gets the last computed battery band (0-9).
    /// </summary>
    public int LastBatteryBand { get; init; } = 5;

    /// <summary>
    /// Gets the current audio level (0-1).
    /// </summary>
    public float AudioLevel { get; init; }

    /// <summary>
    /// Gets whether the controller supports all five player LEDs.
    /// </summary>
    public bool FullPlayerLedSupport { get; init; }

    /// <summary>
    /// Gets the seed used by randomized effects.
    /// </summary>
    public int RandomSeed { get; init; }
}

/// <summary>
/// One evaluated lightbar frame.
/// </summary>
/// <param name="Red">Red channel (0-255).</param>
/// <param name="Green">Green channel (0-255).</param>
/// <param name="Blue">Blue channel (0-255).</param>
/// <param name="PlayerLeds">Player LED mask, or null when the effect leaves them alone.</param>
public readonly record struct LightbarEffectFrame(
    byte Red,
    byte Green,
    byte Blue,
    PlayerLedMask? PlayerLeds);

/// <summary>
/// Result of one lightbar evaluation.
/// </summary>
public readonly struct LightbarEffectResult
{
    /// <summary>
    /// Gets the evaluated frame.
    /// </summary>
    public LightbarEffectFrame Frame { get; init; }

    /// <summary>
    /// Gets the battery band (0-9) the battery effect should show.
    /// </summary>
    public int BatteryBand { get; init; }
}

/// <summary>
/// Pure lightbar effect evaluation shared by every controller.
/// </summary>
public static class LightbarEffectEvaluator
{
    private static readonly BatteryLevelColor[] BatteryColors =
        SpecialActionEffect.DefaultBatteryColors;

    /// <summary>
    /// Evaluates the configured effect at the given elapsed time.
    /// </summary>
    /// <param name="context">The effect inputs.</param>
    /// <returns>The frame to show and the current battery band.</returns>
    public static LightbarEffectResult Evaluate(in LightbarEffectContext context)
    {
        int band = BatteryBand(context.Battery, context.LastBatteryBand);

        LightbarEffectFrame frame = context.Settings.Mode switch
        {
            LightbarMode.MultiColor => EvaluateMultiColor(in context),
            LightbarMode.Breathing => EvaluateBreathing(in context),
            LightbarMode.Strobe => EvaluateStrobe(in context),
            LightbarMode.Battery => EvaluateBattery(in context, band),
            LightbarMode.AudioReactive => EvaluateAudioReactive(in context),
            _ => EvaluateBase(context.Settings)
        };

        return new LightbarEffectResult
        {
            Frame = frame,
            BatteryBand = band
        };
    }

    /// <summary>
    /// Maps a battery state to a charge band.
    /// </summary>
    /// <param name="battery">The current battery state, when available.</param>
    /// <param name="lastBand">The last known band, kept when the percentage is unknown.</param>
    /// <returns>The charge band (0-9).</returns>
    public static int BatteryBand(BatteryState? battery, int lastBand)
    {
        int? percentage = battery?.Percentage;

        if (percentage is null)
        {
            return Math.Clamp(lastBand, 0, 9);
        }

        return Math.Clamp((int)Math.Floor(percentage.Value / 10.0), 0, 9);
    }

    /// <summary>
    /// Scales a color by a brightness percentage.
    /// </summary>
    /// <param name="red">Red channel (0-255).</param>
    /// <param name="green">Green channel (0-255).</param>
    /// <param name="blue">Blue channel (0-255).</param>
    /// <param name="brightness">Brightness percentage, clamped to 0-100.</param>
    /// <returns>The scaled color.</returns>
    public static (byte Red, byte Green, byte Blue) ApplyBrightness(
        byte red,
        byte green,
        byte blue,
        int brightness)
    {
        int percent = Math.Clamp(brightness, 0, 100);

        return (ScaleChannel(red, percent), ScaleChannel(green, percent), ScaleChannel(blue, percent));
    }

    private static LightbarEffectFrame EvaluateBase(LightbarSettings settings)
    {
        (byte red, byte green, byte blue) =
            ApplyBrightness(settings.Red, settings.Green, settings.Blue, settings.Brightness);

        return new LightbarEffectFrame(red, green, blue, null);
    }

    private static LightbarEffectFrame EvaluateMultiColor(in LightbarEffectContext context)
    {
        LightbarSettings settings = context.Settings;
        List<LightbarColorSetting> palette = settings.Colors;

        if (palette is not { Count: > 0 })
        {
            return EvaluateBase(settings);
        }

        if (palette.Count == 1)
        {
            LightbarColorSetting only = palette[0];
            (byte onlyRed, byte onlyGreen, byte onlyBlue) =
                ApplyBrightness(only.Red, only.Green, only.Blue, settings.Brightness);

            return new LightbarEffectFrame(onlyRed, onlyGreen, onlyBlue, null);
        }

        double loopSeconds = 20 - Math.Clamp(settings.Speed, 0, 100) / 100.0 * 18;
        double segment = loopSeconds / palette.Count;
        // Fractional step position keeps exact loop boundaries at t = 0.
        double position = context.ElapsedSeconds / segment;
        int index = (int)Math.Floor(position);
        double t = position - index;

        int from = settings.RandomColor
            ? index == 0 ? 0 : RandomIndex(context.RandomSeed, index - 1, palette.Count)
            : index % palette.Count;

        int to = settings.RandomColor
            ? RandomIndex(context.RandomSeed, index, palette.Count)
            : (index + 1) % palette.Count;

        LightbarColorSetting start = palette[from];
        LightbarColorSetting end = palette[to];

        (byte red, byte green, byte blue) = ApplyBrightness(
            Blend(start.Red, end.Red),
            Blend(start.Green, end.Green),
            Blend(start.Blue, end.Blue),
            settings.Brightness);

        return new LightbarEffectFrame(red, green, blue, null);

        byte Blend(byte startChannel, byte endChannel)
        {
            return (byte)Math.Round(
                startChannel + (endChannel - startChannel) * t,
                MidpointRounding.AwayFromZero);
        }
    }

    private static LightbarEffectFrame EvaluateBreathing(in LightbarEffectContext context)
    {
        LightbarSettings settings = context.Settings;
        double period = 10 - Math.Clamp(settings.Speed, 0, 100) / 100.0 * 9;
        double phase = context.ElapsedSeconds % period / period;

        int min = Math.Clamp(settings.BreathMin, 0, 100);
        int max = Math.Clamp(settings.BreathMax, 0, 100);
        min = Math.Min(min, max);

        double intensity = min + (max - min) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * phase));
        double percent = intensity * Math.Clamp(settings.Brightness, 0, 100) / 100;

        (byte red, byte green, byte blue) = settings.RandomColor
            ? FromHsv(Hue(context.RandomSeed, (int)Math.Floor(context.ElapsedSeconds / period)))
            : (settings.Red, settings.Green, settings.Blue);

        return new LightbarEffectFrame(
            ScaleChannel(red, percent),
            ScaleChannel(green, percent),
            ScaleChannel(blue, percent),
            null);
    }

    private static LightbarEffectFrame EvaluateBattery(in LightbarEffectContext context, int band)
    {
        LightbarSettings settings = context.Settings;
        bool charging = context.Battery?.PowerState == BatteryPowerState.Charging;
        bool animate = charging && settings.BatteryChargingAnimation;

        BatteryLevelColor from;
        BatteryLevelColor to;
        double blend;

        if (animate)
        {
            // One full cycle takes 10 s at speed 0 and 1 s at speed 100; each
            // step crossfades into the next level color instead of switching.
            double cycleSeconds = 10 - Math.Clamp(settings.Speed, 0, 100) / 100.0 * 9;
            double stepSeconds = cycleSeconds / BatteryColors.Length;
            double position = context.ElapsedSeconds / stepSeconds;
            int step = (int)Math.Floor(position);
            blend = position - step;
            from = BatteryColors[step % BatteryColors.Length];
            to = BatteryColors[(step + 1) % BatteryColors.Length];
        }
        else
        {
            from = BatteryColors[band];
            to = from;
            blend = 0;
        }

        (byte red, byte green, byte blue) = ApplyBrightness(
            Blend(from.Red, to.Red),
            Blend(from.Green, to.Green),
            Blend(from.Blue, to.Blue),
            settings.Brightness);

        PlayerLedMask? playerLeds = null;

        if (context.FullPlayerLedSupport)
        {
            if (charging && settings.BatteryPlayerLeds)
            {
                // One full chase takes 2 s at speed 0 and 0.5 s at speed 100.
                double stepSeconds = (2 - Math.Clamp(settings.BatteryPlayerLedSpeed, 0, 100) / 100.0 * 1.5) / 5;
                playerLeds = (PlayerLedMask)(1 << ((int)(context.ElapsedSeconds / stepSeconds) % 5));
            }
            else if (settings.BatteryLevelPlayerLeds && context.Battery?.Percentage is int percentage)
            {
                playerLeds = (PlayerLedMask)SpecialActionEngine.BatteryPercentageToPlayerLedMask(percentage);
            }
        }

        return new LightbarEffectFrame(red, green, blue, playerLeds);

        byte Blend(byte start, byte end)
        {
            return (byte)Math.Round(start + (end - start) * blend, MidpointRounding.AwayFromZero);
        }
    }

    private static LightbarEffectFrame EvaluateStrobe(in LightbarEffectContext context)
    {
        LightbarSettings settings = context.Settings;
        double frequency = 0.5 + Math.Clamp(settings.Speed, 0, 100) / 100.0 * 4.5;

        if (context.ElapsedSeconds * frequency % 1.0 >= 0.5)
        {
            return new LightbarEffectFrame(0, 0, 0, null);
        }

        (byte red, byte green, byte blue) = ApplyBrightness(
            settings.Red, settings.Green, settings.Blue, settings.Brightness);

        return new LightbarEffectFrame(red, green, blue, null);
    }

    private static LightbarEffectFrame EvaluateAudioReactive(in LightbarEffectContext context)
    {
        LightbarSettings settings = context.Settings;
        double gain = 0.5 + Math.Clamp(settings.AudioSensitivity, 0, 100) / 100.0 * 3.5;
        float level = Math.Clamp(context.AudioLevel * (float)gain, 0f, 1f);

        LightbarColorSetting start;
        LightbarColorSetting end;
        float t;

        if (level <= 0.5f)
        {
            start = settings.AudioLowColor;
            end = settings.AudioMidColor;
            t = level / 0.5f;
        }
        else
        {
            start = settings.AudioMidColor;
            end = settings.AudioHighColor;
            t = (level - 0.5f) / 0.5f;
        }

        (byte red, byte green, byte blue) = ApplyBrightness(
            Blend(start.Red, end.Red),
            Blend(start.Green, end.Green),
            Blend(start.Blue, end.Blue),
            settings.Brightness);

        PlayerLedMask? playerLeds = null;

        if (context.FullPlayerLedSupport && settings.AudioPlayerLeds)
        {
            int litLeds = level >= 0.8f ? 5
                : level >= 0.6f ? 4
                : level >= 0.4f ? 3
                : level >= 0.2f ? 2
                : 1;
            playerLeds = (PlayerLedMask)((1 << litLeds) - 1);
        }

        return new LightbarEffectFrame(red, green, blue, playerLeds);

        byte Blend(byte startChannel, byte endChannel)
        {
            return (byte)Math.Round(
                startChannel + (endChannel - startChannel) * t,
                MidpointRounding.AwayFromZero);
        }
    }

    private static int RandomIndex(int seed, int index, int count) => (int)(Hash(seed, index) % count);

    private static uint Hash(int seed, int index)
    {
        uint x = (uint)seed + (uint)index * 0x9E3779B9;
        x ^= x >> 16;
        x *= 0x7FEB352D;
        x ^= x >> 15;
        x *= 0x846CA68B;
        x ^= x >> 16;

        return x;
    }

    private static double Hue(int seed, int index) => Hash(seed, index) / (double)uint.MaxValue;

    // Full-spectrum color (saturation 1, value 1), so one channel is always 255.
    private static (byte Red, byte Green, byte Blue) FromHsv(double hue)
    {
        double sector = hue * 6;
        int index = (int)Math.Floor(sector) % 6;
        double fraction = sector - Math.Floor(sector);
        byte rising = (byte)Math.Round(255 * fraction, MidpointRounding.AwayFromZero);
        byte falling = (byte)(255 - rising);

        return index switch
        {
            0 => (255, rising, 0),
            1 => (falling, 255, 0),
            2 => (0, 255, rising),
            3 => (0, falling, 255),
            4 => (rising, 0, 255),
            _ => (255, 0, falling)
        };
    }

    private static byte ScaleChannel(byte channel, double percent) => (byte)Math.Round(channel * percent / 100.0, MidpointRounding.AwayFromZero);
}