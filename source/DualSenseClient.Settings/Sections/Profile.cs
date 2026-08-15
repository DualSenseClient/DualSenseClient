using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// A named controller profile storing a lightbar color preset, microphone LED mode,
/// and player LED layout that can be applied to a controller.
/// </summary>
public class Profile
{
    /// <summary>
    /// Gets or sets the unique profile name used to reference it from controller bindings.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Profile";

    /// <summary>
    /// Gets or sets the lightbar color preset.
    /// </summary>
    [JsonPropertyName("lightbar")]
    public LightbarSettings Lightbar { get; set; } = new LightbarSettings();

    /// <summary>
    /// Gets or sets the microphone LED mode.
    /// </summary>
    [JsonPropertyName("mic_led")]
    public MicLedSettings MicLed { get; set; } = new MicLedSettings();

    /// <summary>
    /// Gets or sets the player LED layout.
    /// </summary>
    [JsonPropertyName("player_leds")]
    public PlayerLedSettings PlayerLeds { get; set; } = new PlayerLedSettings();
}