using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// The adaptive trigger effect applied while an auto profile rule matches.
/// Mirrors the modes offered by the output tester; only the simple modes are supported.
/// </summary>
public enum AutoProfileTriggerMode
{
    /// <summary>
    /// No resistance; the trigger moves freely.
    /// </summary>
    Off = 0,

    /// <summary>
    /// Constant resistance beginning at <see cref="TriggerEffectSettings.Start"/>.
    /// </summary>
    Resistance = 1,

    /// <summary>
    /// Resistance between <see cref="TriggerEffectSettings.Start"/> and
    /// <see cref="TriggerEffectSettings.End"/> ("weapon" mode).
    /// </summary>
    Trigger = 2,

    /// <summary>
    /// Vibrating/automatic effect at <see cref="TriggerEffectSettings.Frequency"/>.
    /// </summary>
    Automatic = 3
}

/// <summary>
/// A custom adaptive trigger effect stored on an auto profile rule.
/// A <c>null</c> rule slot leaves the trigger unchanged; a setting with
/// <see cref="AutoProfileTriggerMode.Off"/> explicitly clears the effect.
/// </summary>
public class TriggerEffectSettings
{
    /// <summary>
    /// Gets or sets the effect mode (<see cref="AutoProfileTriggerMode.Off"/> by default).
    /// </summary>
    [JsonPropertyName("mode")]
    public AutoProfileTriggerMode Mode { get; set; } = AutoProfileTriggerMode.Off;

    /// <summary>
    /// Gets or sets the trigger position where the effect begins (0-255).
    /// </summary>
    [JsonPropertyName("start")]
    public int Start { get; set; }

    /// <summary>
    /// Gets or sets the trigger position where the effect ends (0-255, weapon mode only).
    /// </summary>
    [JsonPropertyName("end")]
    public int End { get; set; } = 255;

    /// <summary>
    /// Gets or sets the resistance/effect force (0-255).
    /// </summary>
    [JsonPropertyName("force")]
    public int Force { get; set; } = 100;

    /// <summary>
    /// Gets or sets the automatic mode effect frequency (0-15).
    /// </summary>
    [JsonPropertyName("frequency")]
    public int Frequency { get; set; } = 5;
}