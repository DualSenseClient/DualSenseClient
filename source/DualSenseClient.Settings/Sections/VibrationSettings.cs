using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// How the classic rumble motors are encoded in the output report.
/// </summary>
public enum VibrationEncodingMode
{
    /// <summary>
    /// Follow the controller's firmware capability: V2 when supported, V1 otherwise.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Use the original compatible vibration encoding (<c>COMPATIBLE_VIBRATION</c>).
    /// </summary>
    Compatible = 1,

    /// <summary>
    /// Use the improved vibration encoding (<c>COMPATIBLE_VIBRATION2</c>).
    /// </summary>
    Improved = 2
}

/// <summary>
/// Per-controller vibration and haptics options applied to the output reports the app
/// sends to the controller.
/// </summary>
public class VibrationSettings
{
    /// <summary>
    /// Largest rumble-motor power reduction value carried by the low nibble (0-15).
    /// </summary>
    public const int MaxRumblePowerReduction = 15;

    /// <summary>
    /// Largest trigger-motor power reduction value carried by the high nibble (0-10).
    /// </summary>
    public const int MaxTriggerPowerReduction = 10;

    /// <summary>
    /// Gets or sets the classic rumble encoding
    /// (<see cref="VibrationEncodingMode.Auto"/> by default).
    /// </summary>
    [JsonPropertyName("rumble_encoding")]
    public VibrationEncodingMode RumbleEncoding { get; set; } = VibrationEncodingMode.Auto;

    /// <summary>
    /// Gets or sets whether the app enforces <see cref="RumblePowerReduction"/> and
    /// <see cref="TriggerPowerReduction"/> instead of leaving the game's values untouched.
    /// </summary>
    [JsonPropertyName("motor_power_level_enabled")]
    public bool MotorPowerLevelEnabled { get; set; }

    /// <summary>
    /// Gets or sets the rumble-motor power reduction (0-15, low nibble of output byte 36).
    /// </summary>
    [JsonPropertyName("rumble_power_reduction")]
    public int RumblePowerReduction { get; set; }

    /// <summary>
    /// Gets or sets the trigger-motor power reduction (0-10, high nibble of output byte 36).
    /// </summary>
    [JsonPropertyName("trigger_power_reduction")]
    public int TriggerPowerReduction { get; set; }

    /// <summary>
    /// Gets or sets whether the app enforces the haptic low-pass filter. When
    /// <c>false</c>, the game's own filter value passes through unchanged.
    /// </summary>
    [JsonPropertyName("haptic_low_pass_filter")]
    public bool HapticLowPassFilter { get; set; }
}