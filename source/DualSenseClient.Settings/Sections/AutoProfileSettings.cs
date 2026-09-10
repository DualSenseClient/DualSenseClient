using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// The root settings class for foreground-app auto profiles.
/// Persisted to <c>auto_profiles.json</c> next to the application's <c>config.json</c>.
/// </summary>
public class AutoProfileSettings
{
    /// <summary>
    /// Gets or sets whether foreground-app switching is active.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the auto profile rules in priority order (first match wins).
    /// </summary>
    [JsonPropertyName("rules")]
    public List<AutoProfileRule> Rules { get; set; } = [];
}