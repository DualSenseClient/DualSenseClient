using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// Settings for desktop notification popups.
/// </summary>
public class NotificationSettings
{
    /// <summary>
    /// Gets or sets whether desktop notification popups are shown.
    /// </summary>
    /// <remarks>
    /// Master switch for all notification popups (connection, low battery, charge).
    /// Defaults to <c>true</c>.
    /// </remarks>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets where on the screen desktop notification popups appear.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="NotificationPosition.BottomRight"/>.
    /// </remarks>
    [JsonPropertyName("position")]
    public NotificationPosition Position { get; set; } = NotificationPosition.BottomRight;

    /// <summary>
    /// Gets or sets how long in seconds each notification popup stays visible
    /// before auto-closing.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>3</c>. Expected range is 1-30; values are clamped at use time.
    /// </remarks>
    [JsonPropertyName("duration")]
    public int Duration { get; set; } = 3;

    /// <summary>
    /// Gets or sets whether a notification popup is shown when a controller
    /// connects or disconnects.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>true</c>. Requires <see cref="Enabled"/> to be enabled.
    /// </remarks>
    [JsonPropertyName("notifyOnConnection")]
    public bool NotifyOnConnection { get; set; } = true;

    /// <summary>
    /// Gets or sets whether a notification popup is shown when a controller's
    /// battery drops to or below <see cref="LowBatteryThreshold"/>.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>true</c>. Requires <see cref="Enabled"/> to be enabled.
    /// </remarks>
    [JsonPropertyName("notifyOnLowBattery")]
    public bool NotifyOnLowBattery { get; set; } = true;

    /// <summary>
    /// Gets or sets the battery percentage at or below which the low battery
    /// notification fires. Fires once per discharge cycle.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>15</c>. Expected range is 5-50; values are clamped at use time.
    /// </remarks>
    [JsonPropertyName("lowBatteryThreshold")]
    public int LowBatteryThreshold { get; set; } = 15;

    /// <summary>
    /// Gets or sets whether a notification popup is shown when a charging controller
    /// reaches <see cref="ChargeThreshold"/> or reports a completed charge.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>true</c>. Requires <see cref="Enabled"/> to be enabled.
    /// </remarks>
    [JsonPropertyName("notifyOnCharge")]
    public bool NotifyOnCharge { get; set; } = true;

    /// <summary>
    /// Gets or sets the battery percentage at or above which the charge
    /// notification fires while charging. Fires once per charge cycle.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>100</c>. Expected range is 50-100; values are clamped at use time.
    /// </remarks>
    [JsonPropertyName("chargeThreshold")]
    public int ChargeThreshold { get; set; } = 100;
}