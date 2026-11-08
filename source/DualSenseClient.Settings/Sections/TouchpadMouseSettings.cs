using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// The touchpad-as-a-mouse settings stored for a controller. When enabled, touchpad
/// movement drives the Windows pointer and two-finger gestures scroll
/// (Windows only).
/// </summary>
public class TouchpadMouseSettings
{
    /// <summary>
    /// Gets or sets whether touchpad movement controls the pointer (off by default).
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the pointer speed multiplier (UI range 0.25–3.0, 1.0 by default).
    /// </summary>
    [JsonPropertyName("pointer_speed")]
    public double PointerSpeed { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the scroll speed multiplier (UI range 0.25–3.0, 1.0 by default).
    /// </summary>
    [JsonPropertyName("scroll_speed")]
    public double ScrollSpeed { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets whether scrolling follows the natural (touch) direction
    /// (off by default).
    /// </summary>
    [JsonPropertyName("natural_scrolling")]
    public bool NaturalScrolling { get; set; }
}