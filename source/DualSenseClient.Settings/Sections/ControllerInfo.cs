using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// Stores persistent information about a physical controller: a user-renameable display
/// name, its Bluetooth MAC address and/or HID device path for identification, and the name
/// of the profile applied to it on connect.
/// </summary>
public class ControllerInfo
{
    /// <summary>
    /// Gets or sets the user-visible controller name. Defaults to the product name and can
    /// be changed by the user (see the device info page); empty means "use the product name".
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the controller's Bluetooth MAC address in XX:XX:XX:XX:XX:XX format,
    /// or empty when the MAC is unavailable (e.g. USB-only devices).
    /// </summary>
    [JsonPropertyName("mac_address")]
    public string MacAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the controller's HID device path, or empty when the entry was
    /// created with a MAC address.
    /// </summary>
    [JsonPropertyName("device_path")]
    public string DevicePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the profile applied to this controller on connect,
    /// or empty when the controller uses the default profile.
    /// </summary>
    [JsonPropertyName("profile_name")]
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the controller illustration skin shown on the device info page,
    /// or empty when the controller uses the default (first available) skin.
    /// </summary>
    [JsonPropertyName("skin")]
    public string Skin { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the virtual controller emulation settings stored for this
    /// controller (the emulation section of the device info page). Defaults to
    /// emulation off.
    /// </summary>
    [JsonPropertyName("emulation")]
    public EmulationSettings Emulation { get; set; } = new EmulationSettings();
}