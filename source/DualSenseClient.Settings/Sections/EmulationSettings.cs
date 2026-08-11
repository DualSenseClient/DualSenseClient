using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// The type of virtual controller created for a physical controller's profile.
/// </summary>
public enum EmulationMode
{
    /// <summary>
    /// No virtual controller is created.
    /// </summary>
    Off = 0,

    /// <summary>
    /// A virtual Xbox 360 controller (XInput), the most compatible with Windows games.
    /// </summary>
    Xbox360 = 1,

    /// <summary>
    /// A virtual DualShock 4 controller.
    /// </summary>
    DualShock4 = 2,

    /// <summary>
    /// A virtual DualSense controller, exposing haptics, adaptive trigger and audio
    /// interfaces to the host.
    /// </summary>
    DualSense = 3
}

/// <summary>
/// The virtual controller emulation settings stored in a controller profile.
/// </summary>
public class EmulationSettings
{
    /// <summary>
    /// Gets or sets the virtual controller mode to create for this profile
    /// (<see cref="EmulationMode.Off"/> by default).
    /// </summary>
    [JsonPropertyName("mode")]
    public EmulationMode Mode { get; set; } = EmulationMode.Off;

    /// <summary>
    /// Gets or sets the volume applied to the physical controller's speaker when
    /// forwarding host audio (0-255, same range as the audio player tester).
    /// </summary>
    [JsonPropertyName("forward_volume")]
    public int ForwardVolume { get; set; } = 0x50;

    /// <summary>
    /// Gets or sets the haptic vibration strength when forwarding host audio, as a
    /// percentage (0-200, same range as the audio player tester).
    /// </summary>
    [JsonPropertyName("forward_haptics")]
    public int ForwardHapticStrength { get; set; } = 100;
}