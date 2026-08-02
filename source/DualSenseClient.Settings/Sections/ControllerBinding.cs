using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// Binds a physical controller (identified by its Bluetooth MAC address and/or HID device
/// path) to a named profile. At least one identifier is stored; the MAC address is preferred
/// for lookups, with the device path used as a fallback when the MAC is unavailable.
/// </summary>
public class ControllerBinding
{
    /// <summary>
    /// Gets or sets the controller's Bluetooth MAC address in XX:XX:XX:XX:XX:XX format,
    /// or empty when the MAC is unavailable (e.g. USB-only devices).
    /// </summary>
    [JsonPropertyName("mac_address")]
    public string MacAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the controller's HID device path, or empty when the binding was
    /// created with a MAC address.
    /// </summary>
    [JsonPropertyName("device_path")]
    public string DevicePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the profile applied to this controller on connect.
    /// </summary>
    [JsonPropertyName("profile_name")]
    public string ProfileName { get; set; } = string.Empty;
}