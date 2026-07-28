using System.Text.Json.Serialization;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Settings;

/// <summary>
/// The root settings class for the application.
/// Add settings sections as properties to extend this class.
/// </summary>
/// <remarks>
/// <para>
/// Each settings section should be a public class with a parameterless constructor and
/// properties decorated with <see cref="JsonPropertyNameAttribute"/> to control JSON key names.
/// </para>
/// <para>
/// Settings are serialized to and deserialized from a JSON file at runtime using
/// <see cref="LenientJsonDeserializer"/> for error-tolerant loading.
/// </para>
/// </remarks>
public class Settings
{
    /// <summary>
    /// Gets or sets the debug and logging settings for the application.
    /// </summary>
    [JsonPropertyName("debug")]
    public DebugSettings Debug { get; set; } = new DebugSettings();

    /// <summary>
    /// Gets or sets the UI settings, including language and theme preferences.
    /// </summary>
    [JsonPropertyName("ui")]
    public UiSettings Ui { get; set; } = new UiSettings();
}