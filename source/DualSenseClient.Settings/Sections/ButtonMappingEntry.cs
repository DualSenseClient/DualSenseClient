using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// A single button remapping rule: one or more physical DualSense buttons (a combo when
/// more than one) mapped to one or more targets on a virtual controller.
/// </summary>
public class ButtonMappingEntry
{
    /// <summary>
    /// Physical source button names (<c>ButtonType</c> member names). A single-key entry
    /// remaps that button; multiple keys define a combo that fires only while all of its
    /// keys are held at once.
    /// </summary>
    [JsonPropertyName("keys")]
    public List<string> Keys { get; set; } = [];

    /// <summary>
    /// Target names in the mode's button set (for example "A", "LeftTrigger", "DPadUp").
    /// Several targets are pressed together (for example "Y" and "B"); "None" disables the
    /// source buttons.
    /// </summary>
    [JsonPropertyName("targets")]
    public List<string> Targets { get; set; } = [];

    /// <summary>
    /// Output style for trigger targets that have both a click flag and an analog byte:
    /// "full" (the default) forces the byte to 255 while active, "click" sets only the
    /// click flag. Ignored where it cannot apply (Xbox 360 triggers are byte-only).
    /// </summary>
    [JsonPropertyName("target_output")]
    public string? TargetOutput { get; set; }

    /// <summary>
    /// Whether member buttons' own single-button outputs are muted while all keys of a
    /// multi-key entry are held (<c>true</c> by default). Ignored by single-key entries.
    /// </summary>
    [JsonPropertyName("suppress_solos")]
    public bool SuppressSolos { get; set; } = true;
}