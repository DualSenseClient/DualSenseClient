using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// Microphone LED mode stored in a <see cref="Profile"/>.
/// </summary>
public class MicLedSettings
{
    /// <summary>
    /// Gets or sets the microphone LED mode: <c>0</c> off, <c>1</c> on, <c>2</c> pulse.
    /// </summary>
    [JsonPropertyName("mode")]
    public byte Mode { get; set; }
}