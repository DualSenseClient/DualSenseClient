using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

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
}