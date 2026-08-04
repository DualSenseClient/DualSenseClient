using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// The root settings class for persistent controller information.
/// Persisted to <c>controllers.json</c> next to the application's <c>config.json</c>.
/// </summary>
public class ControllerInfoSettings
{
    /// <summary>
    /// Gets or sets all known controllers, each identified by MAC address and/or HID
    /// device path with a user-renameable name and the bound profile name.
    /// </summary>
    [JsonPropertyName("controllers")]
    public List<ControllerInfo> Controllers { get; set; } = [];
}