using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// Lightbar mode selected for a profile.
/// </summary>
public enum LightbarMode
{
    /// <summary>A single color from <see cref="LightbarSettings.Red"/>, <see cref="LightbarSettings.Green"/> and <see cref="LightbarSettings.Blue"/>.</summary>
    SingleColor,

    /// <summary>A palette from <see cref="LightbarSettings.Colors"/>.</summary>
    MultiColor,

    /// <summary>Fade between the minimum and maximum breath brightness.</summary>
    Breathing,

    /// <summary>Flash the current color rapidly.</summary>
    Strobe,

    /// <summary>Show the remaining battery charge as a color.</summary>
    Battery,

    /// <summary>React to the audio level using the low, mid and high colors.</summary>
    AudioReactive
}

/// <summary>
/// A single RGB lightbar color.
/// </summary>
public class LightbarColorSetting
{
    /// <summary>
    /// Gets or sets the red channel (0-255).
    /// </summary>
    [JsonPropertyName("red")]
    public byte Red { get; set; }

    /// <summary>
    /// Gets or sets the green channel (0-255).
    /// </summary>
    [JsonPropertyName("green")]
    public byte Green { get; set; }

    /// <summary>
    /// Gets or sets the blue channel (0-255).
    /// </summary>
    [JsonPropertyName("blue")]
    public byte Blue { get; set; }

    /// <summary>
    /// Creates a copy of this color.
    /// </summary>
    /// <returns>A new <see cref="LightbarColorSetting"/> with the same channels.</returns>
    public LightbarColorSetting Clone() =>
        new LightbarColorSetting
        {
            Red = Red,
            Green = Green,
            Blue = Blue
        };
}

/// <summary>
/// Lightbar color preset stored in a <see cref="Profile"/>.
/// </summary>
public class LightbarSettings
{
    /// <summary>
    /// Gets or sets the lightbar red channel (0-255).
    /// </summary>
    [JsonPropertyName("red")]
    public byte Red { get; set; }

    /// <summary>
    /// Gets or sets the lightbar green channel (0-255).
    /// </summary>
    [JsonPropertyName("green")]
    public byte Green { get; set; }

    /// <summary>
    /// Gets or sets the lightbar blue channel (0-255).
    /// </summary>
    [JsonPropertyName("blue")]
    public byte Blue { get; set; } = 255;

    /// <summary>
    /// Gets or sets the lightbar mode.
    /// </summary>
    [JsonPropertyName("mode")]
    public LightbarMode Mode { get; set; } = LightbarMode.SingleColor;

    /// <summary>
    /// Gets or sets the lightbar brightness percentage (0-100).
    /// </summary>
    [JsonPropertyName("brightness")]
    public int Brightness { get; set; } = 100;

    /// <summary>
    /// Gets or sets the effect speed percentage (0-100).
    /// </summary>
    [JsonPropertyName("speed")]
    public int Speed { get; set; } = 50;

    /// <summary>
    /// Gets or sets whether the palette is used in a random order.
    /// </summary>
    [JsonPropertyName("random_color")]
    public bool RandomColor { get; set; }

    /// <summary>
    /// Gets or sets the multi-color palette.
    /// </summary>
    [JsonPropertyName("colors")]
    public List<LightbarColorSetting> Colors { get; set; } = CreateDefaultColors();

    /// <summary>
    /// Gets or sets the minimum brightness percentage of the breathing effect (0-100).
    /// </summary>
    [JsonPropertyName("breath_min")]
    public int BreathMin { get; set; }

    /// <summary>
    /// Gets or sets the maximum brightness percentage of the breathing effect (0-100).
    /// </summary>
    [JsonPropertyName("breath_max")]
    public int BreathMax { get; set; } = 100;

    /// <summary>
    /// Gets or sets the color shown at low audio levels.
    /// </summary>
    [JsonPropertyName("audio_low_color")]
    public LightbarColorSetting AudioLowColor { get; set; } = new LightbarColorSetting
    {
        Red = 0,
        Green = 80,
        Blue = 255
    };

    /// <summary>
    /// Gets or sets the color shown at mid audio levels.
    /// </summary>
    [JsonPropertyName("audio_mid_color")]
    public LightbarColorSetting AudioMidColor { get; set; } = new LightbarColorSetting
    {
        Red = 255,
        Green = 180,
        Blue = 0
    };

    /// <summary>
    /// Gets or sets the color shown at high audio levels.
    /// </summary>
    [JsonPropertyName("audio_high_color")]
    public LightbarColorSetting AudioHighColor { get; set; } = new LightbarColorSetting
    {
        Red = 255,
        Green = 40,
        Blue = 40
    };

    /// <summary>
    /// Gets or sets the audio reactivity sensitivity percentage (0-100).
    /// </summary>
    [JsonPropertyName("audio_sensitivity")]
    public int AudioSensitivity { get; set; } = 50;

    /// <summary>
    /// Gets or sets whether player LEDs show the battery level alongside the lightbar.
    /// </summary>
    [JsonPropertyName("battery_player_leds")]
    public bool BatteryPlayerLeds { get; set; }

    /// <summary>
    /// Creates a new default six-color palette.
    /// </summary>
    /// <returns>A new palette on every call.</returns>
    public static List<LightbarColorSetting> CreateDefaultColors() =>
    [
        new LightbarColorSetting
        {
            Red = 255,
            Green = 40,
            Blue = 40
        },
        new LightbarColorSetting
        {
            Red = 255,
            Green = 140,
            Blue = 0
        },
        new LightbarColorSetting
        {
            Red = 255,
            Green = 220,
            Blue = 0
        },
        new LightbarColorSetting
        {
            Red = 40,
            Green = 220,
            Blue = 80
        },
        new LightbarColorSetting
        {
            Red = 40,
            Green = 120,
            Blue = 255
        },
        new LightbarColorSetting
        {
            Red = 180,
            Green = 60,
            Blue = 255
        }
    ];

    /// <summary>
    /// Creates a deep copy of these lightbar settings.
    /// </summary>
    /// <returns>A new <see cref="LightbarSettings"/> with cloned colors.</returns>
    public LightbarSettings Clone() =>
        new LightbarSettings
        {
            Red = Red,
            Green = Green,
            Blue = Blue,
            Mode = Mode,
            Brightness = Brightness,
            Speed = Speed,
            RandomColor = RandomColor,
            Colors = Colors.Select(c => c.Clone()).ToList(),
            BreathMin = BreathMin,
            BreathMax = BreathMax,
            AudioLowColor = AudioLowColor.Clone(),
            AudioMidColor = AudioMidColor.Clone(),
            AudioHighColor = AudioHighColor.Clone(),
            AudioSensitivity = AudioSensitivity,
            BatteryPlayerLeds = BatteryPlayerLeds
        };
}