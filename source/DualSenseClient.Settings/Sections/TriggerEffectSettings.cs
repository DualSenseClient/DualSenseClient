using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// The adaptive trigger effect applied while an auto profile rule matches.
/// Mirrors the modes offered by the output tester.
/// </summary>
public enum AutoProfileTriggerMode
{
    /// <summary>
    /// No resistance; the trigger moves freely.
    /// </summary>
    Off = 0,

    /// <summary>
    /// Trigger resists movement beyond <see cref="TriggerEffectSettings.Start"/>.
    /// </summary>
    Feedback = 1,

    /// <summary>
    /// Resistance between <see cref="TriggerEffectSettings.Start"/> and
    /// <see cref="TriggerEffectSettings.End"/> ("weapon" mode).
    /// </summary>
    Weapon = 2,

    /// <summary>
    /// Vibrating effect at <see cref="TriggerEffectSettings.Frequency"/>.
    /// </summary>
    Vibration = 3,

    /// <summary>
    /// Weapon-like effect with a snap-back force.
    /// </summary>
    Bow = 4,

    /// <summary>
    /// Rhythmic galloping oscillation.
    /// </summary>
    Galloping = 5,

    /// <summary>
    /// Vibration oscillating between two amplitudes.
    /// </summary>
    Machine = 6,

    /// <summary>
    /// Resistance interpolating from <see cref="TriggerEffectSettings.Force"/>
    /// to <see cref="TriggerEffectSettings.EndStrength"/>.
    /// </summary>
    SlopeFeedback = 7,

    /// <summary>
    /// Legacy name for <see cref="Feedback"/>; kept so old rules keep working.
    /// Shares its numeric value (1) and serializes as "Feedback".
    /// </summary>
    [Obsolete("Use Feedback instead.")] Resistance = 1,

    /// <summary>
    /// Legacy name for <see cref="Weapon"/>; kept so old rules keep working.
    /// Shares its numeric value (2) and serializes as "Weapon".
    /// </summary>
    [Obsolete("Use Weapon instead.")] Trigger = 2,

    /// <summary>
    /// Legacy name for <see cref="Vibration"/>; kept so old rules keep working.
    /// Shares its numeric value (3) and serializes as "Vibration".
    /// </summary>
    [Obsolete("Use Vibration instead.")] Automatic = 3
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
    /// Gets or sets the trigger position where the effect begins (0-9).
    /// </summary>
    [JsonPropertyName("start")]
    public int Start { get; set; }

    /// <summary>
    /// Gets or sets the trigger position where the effect ends (0-9; weapon/bow modes cap at 8).
    /// </summary>
    [JsonPropertyName("end")]
    public int End { get; set; } = 8;

    /// <summary>
    /// Gets or sets the resistance/effect force (0-8). For slope mode this is the start strength.
    /// </summary>
    [JsonPropertyName("force")]
    public int Force { get; set; } = 8;

    /// <summary>
    /// Gets or sets the vibration/galloping/machine effect frequency (0-255).
    /// </summary>
    [JsonPropertyName("frequency")]
    public int Frequency { get; set; } = 10;

    /// <summary>
    /// Gets or sets the bow snap-back force (0-8).
    /// </summary>
    [JsonPropertyName("snapForce")]
    public int SnapForce { get; set; } = 8;

    /// <summary>
    /// Gets or sets the galloping first foot position (0-6).
    /// </summary>
    [JsonPropertyName("firstFoot")]
    public int FirstFoot { get; set; }

    /// <summary>
    /// Gets or sets the galloping second foot position (1-7).
    /// </summary>
    [JsonPropertyName("secondFoot")]
    public int SecondFoot { get; set; } = 1;

    /// <summary>
    /// Gets or sets the machine secondary amplitude (0-7). The primary amplitude reuses <see cref="Force"/>.
    /// </summary>
    [JsonPropertyName("amplitudeB")]
    public int AmplitudeB { get; set; } = 4;

    /// <summary>
    /// Gets or sets the machine oscillation period in tenths of a second (0-255).
    /// </summary>
    [JsonPropertyName("period")]
    public int Period { get; set; } = 5;

    /// <summary>
    /// Gets or sets the slope mode end strength (0-8, 0 yields Off).
    /// </summary>
    [JsonPropertyName("endStrength")]
    public int EndStrength { get; set; } = 8;
}