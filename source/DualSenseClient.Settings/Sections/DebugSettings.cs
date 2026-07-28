using System.Text.Json.Serialization;
using DualSenseClient.Logging;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// Settings related to debugging and logging.
/// </summary>
public class DebugSettings
{
    /// <summary>
    /// Gets or sets the minimum logging level for log output.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="LogLevel.Info"/> in release builds and <see cref="LogLevel.Trace"/> in nightly builds.
    /// This value is applied at startup and can be changed at runtime via the Settings page.
    /// </remarks>
    [JsonPropertyName("log_level")]
    public LogLevel LogLevel { get; set; } = LogLevel.Info;
}