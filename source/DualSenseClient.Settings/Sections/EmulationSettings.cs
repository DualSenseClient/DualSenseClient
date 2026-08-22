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
/// The DualSense hardware variant presented by a virtual DualSense device.
/// </summary>
public enum DualSenseVariant
{
    /// <summary>
    /// The standard DualSense.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// The DualSense Edge.
    /// </summary>
    Edge = 1
}

/// <summary>
/// The DualShock 4 hardware generation presented by a virtual DualShock 4 device.
/// </summary>
public enum DualShock4Variant
{
    /// <summary>
    /// The first-generation DualShock 4 (CUH-ZCT1, USB PID <c>0x05C4</c>).
    /// </summary>
    V1 = 0,

    /// <summary>
    /// The second-generation DualShock 4 (CUH-ZCT2, USB PID <c>0x09CC</c>),
    /// matching the libVIIPER default.
    /// </summary>
    V2 = 1
}

/// <summary>
/// The physical controller output used when forwarding host audio.
/// </summary>
public enum EmulationAudioOutput
{
    /// <summary>
    /// The controller's internal speaker.
    /// </summary>
    Speaker = 0,

    /// <summary>
    /// A headset plugged into the controller's jack.
    /// </summary>
    Headset = 1
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
    /// Gets or sets the DualSense hardware variant of the virtual device when
    /// <see cref="Mode"/> is <see cref="EmulationMode.DualSense"/>
    /// (<see cref="DualSenseVariant.Standard"/> by default).
    /// </summary>
    [JsonPropertyName("device_type")]
    public DualSenseVariant DeviceType { get; set; } = DualSenseVariant.Standard;

    /// <summary>
    /// Gets or sets the DualShock 4 hardware generation of the virtual device when
    /// <see cref="Mode"/> is <see cref="EmulationMode.DualShock4"/>
    /// (<see cref="DualShock4Variant.V2"/> by default, matching the libVIIPER default).
    /// </summary>
    [JsonPropertyName("ds4_variant")]
    public DualShock4Variant Ds4Variant { get; set; } = DualShock4Variant.V2;

    /// <summary>
    /// Gets or sets the physical controller output used when forwarding host audio
    /// (<see cref="EmulationAudioOutput.Speaker"/> by default).
    /// </summary>
    [JsonPropertyName("forward_audio_output")]
    public EmulationAudioOutput ForwardAudioOutput { get; set; } = EmulationAudioOutput.Speaker;

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

    /// <summary>
    /// Gets or sets the button remapping rules for Xbox 360 emulation, or <c>null</c> to
    /// use the built-in default mapping.
    /// </summary>
    [JsonPropertyName("xbox360_button_mappings")]
    public List<ButtonMappingEntry>? Xbox360ButtonMappings { get; set; }

    /// <summary>
    /// Gets or sets the button remapping rules for DualShock 4 emulation, or <c>null</c>
    /// to use the built-in default mapping.
    /// </summary>
    [JsonPropertyName("ds4_button_mappings")]
    public List<ButtonMappingEntry>? DualShock4ButtonMappings { get; set; }

    /// <summary>
    /// Gets or sets the button remapping rules for DualSense emulation, or <c>null</c> to
    /// use the built-in default mapping.
    /// </summary>
    [JsonPropertyName("dualsense_button_mappings")]
    public List<ButtonMappingEntry>? DualSenseButtonMappings { get; set; }
}