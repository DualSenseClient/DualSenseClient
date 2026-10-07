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
/// The per-mode hardware variants presented by the virtual controller. Only the entry
/// matching the active emulation mode is used; the other keeps its default.
/// </summary>
public class VariantSettings
{
    /// <summary>
    /// Gets or sets the DualSense hardware variant of the virtual device when the
    /// emulation mode is <see cref="EmulationMode.DualSense"/>
    /// (<see cref="DualSenseVariant.Standard"/> by default).
    /// </summary>
    [JsonPropertyName("dualsense")]
    public DualSenseVariant DualSense { get; set; } = DualSenseVariant.Standard;

    /// <summary>
    /// Gets or sets the DualShock 4 hardware generation of the virtual device when the
    /// emulation mode is <see cref="EmulationMode.DualShock4"/>
    /// (<see cref="DualShock4Variant.V2"/> by default, matching the libVIIPER default).
    /// </summary>
    [JsonPropertyName("dualshock4")]
    public DualShock4Variant DualShock4 { get; set; } = DualShock4Variant.V2;
}

/// <summary>
/// The audio source forwarded to the physical controller while a virtual device is
/// active.
/// </summary>
public enum EmulationAudioSource
{
    /// <summary>
    /// The audio the host renders to the virtual controller's own audio endpoint (the
    /// game must select the virtual controller as its output).
    /// </summary>
    ControllerEndpoint = 0,

    /// <summary>
    /// A loopback capture of the system's default output device, so any audio the host
    /// plays drives the controller. Routed directly to the physical controller and needs
    /// no virtual device.
    /// </summary>
    SystemOutput = 1,

    /// <summary>
    /// A loopback capture of a single application's audio (its process tree), so only
    /// that app drives the controller. Routed directly to the physical controller and
    /// needs no virtual device. Windows 10 build 20348 (Windows 11) or later only.
    /// </summary>
    Application = 2
}

/// <summary>
/// How the application captured when <see cref="EmulationAudioSource.Application"/> is
/// selected is picked in the UI: from the applications currently playing audio, or by
/// browsing to an executable file. It is only a picker preference: what is captured is
/// still described by <see cref="ForwardSettings.AudioApp"/>.
/// </summary>
public enum EmulationAudioAppMode
{
    /// <summary>
    /// Choose from the applications that currently hold an audio session on the default
    /// output device (the default, and what this setting was originally limited to).
    /// </summary>
    PlayingApps = 0,

    /// <summary>
    /// Choose with the platform file picker, so an executable that is not currently
    /// playing audio can be captured too.
    /// </summary>
    ExecutableFile = 1
}

/// <summary>
/// The host-audio forwarding options applied to the physical controller while a
/// virtual device is active.
/// </summary>
public class ForwardSettings
{
    /// <summary>
    /// Gets or sets the physical controller output used when forwarding host audio
    /// (<see cref="EmulationAudioOutput.Speaker"/> by default).
    /// </summary>
    [JsonPropertyName("audio_output")]
    public EmulationAudioOutput AudioOutput { get; set; } = EmulationAudioOutput.Speaker;

    /// <summary>
    /// Gets or sets the volume applied to the physical controller's speaker when
    /// forwarding host audio (0-255, same range as the audio player tester).
    /// </summary>
    [JsonPropertyName("volume")]
    public int Volume { get; set; } = 0x50;

    /// <summary>
    /// Gets or sets the haptic vibration strength when forwarding host audio, as a
    /// percentage (0-200, same range as the audio player tester).
    /// </summary>
    [JsonPropertyName("haptics")]
    public int Haptics { get; set; } = 100;

    /// <summary>
    /// Gets or sets whether the game's own haptics are ignored in favor of the
    /// audio-derived haptics (which always honor the haptic strength). Only affects
    /// DualSense emulation over Bluetooth; USB output never carries game haptics.
    /// </summary>
    [JsonPropertyName("haptics_audio_only")]
    public bool ForceAudioHaptics { get; set; }

    /// <summary>
    /// Gets or sets whether the forwarded audio is captured from the system's default
    /// output device (loopback) instead of the virtual controller's own audio endpoint,
    /// so any audio the host plays drives the controller without the game having to
    /// select the virtual controller as its output. The system-output source is routed
    /// directly to the physical controller and needs no virtual device.
    /// </summary>
    [JsonPropertyName("audio_from_output")]
    public bool AudioFromSystemOutput
    {
        get
        {
            return AudioSource == EmulationAudioSource.SystemOutput;
        }
        set
        {
            // Only ever upgrades to the system output: a stored file that carries both
            // fields must not let the legacy false value clobber the newer source, since
            // JSON property order is not guaranteed.
            if (value)
            {
                AudioSource = EmulationAudioSource.SystemOutput;
            }
        }
    }

    /// <summary>
    /// Gets or sets the audio source forwarded to the physical controller
    /// (<see cref="EmulationAudioSource.ControllerEndpoint"/> by default).
    /// </summary>
    [JsonPropertyName("audio_source")]
    public EmulationAudioSource AudioSource { get; set; } = EmulationAudioSource.ControllerEndpoint;

    /// <summary>
    /// Gets or sets the executable path of the application captured when
    /// <see cref="AudioSource"/> is <see cref="EmulationAudioSource.Application"/>
    /// (empty by default). The path (not a process id) is stored so it survives app
    /// restarts.
    /// </summary>
    [JsonPropertyName("audio_app")]
    public string AudioApp { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how <see cref="AudioApp"/> is picked in the UI
    /// (<see cref="EmulationAudioAppMode.PlayingApps"/> by default): from the list of
    /// applications currently playing audio, or with the file picker. Purely a picker
    /// preference; it never changes which executable is captured.
    /// </summary>
    [JsonPropertyName("audio_app_mode")]
    public EmulationAudioAppMode AudioAppMode { get; set; } = EmulationAudioAppMode.PlayingApps;
}

/// <summary>
/// The button remapping rules per emulation mode. A <c>null</c> list means the mode
/// uses its built-in default mapping.
/// </summary>
public class MappingsSettings
{
    /// <summary>
    /// Gets or sets the button remapping rules for Xbox 360 emulation, or <c>null</c> to
    /// use the built-in default mapping.
    /// </summary>
    [JsonPropertyName("xbox360")]
    public List<ButtonMappingEntry>? Xbox360 { get; set; }

    /// <summary>
    /// Gets or sets the button remapping rules for DualShock 4 emulation, or <c>null</c>
    /// to use the built-in default mapping.
    /// </summary>
    [JsonPropertyName("dualshock4")]
    public List<ButtonMappingEntry>? DualShock4 { get; set; }

    /// <summary>
    /// Gets or sets the button remapping rules for DualSense emulation, or <c>null</c> to
    /// use the built-in default mapping.
    /// </summary>
    [JsonPropertyName("dualsense")]
    public List<ButtonMappingEntry>? DualSense { get; set; }
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
    /// Gets or sets the per-mode hardware variants of the virtual device.
    /// </summary>
    [JsonPropertyName("variant")]
    public VariantSettings Variant { get; set; } = new VariantSettings();

    /// <summary>
    /// Gets or sets the host-audio forwarding options of the physical controller.
    /// </summary>
    [JsonPropertyName("forward")]
    public ForwardSettings Forward { get; set; } = new ForwardSettings();

    /// <summary>
    /// Gets or sets the per-mode button remapping rules.
    /// </summary>
    [JsonPropertyName("mappings")]
    public MappingsSettings Mappings { get; set; } = new MappingsSettings();
}