using System.Text.Json.Serialization;

namespace DualSenseClient.Core.Settings.Models;

public class TrackpadMouseSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("sensitivity")]
    public double Sensitivity { get; set; } = 1.0;

    [JsonPropertyName("invertX")]
    public bool InvertX { get; set; } = false;

    [JsonPropertyName("invertY")]
    public bool InvertY { get; set; } = false;
}