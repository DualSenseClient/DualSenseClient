using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// Player LED layout stored in a <see cref="Profile"/>.
/// </summary>
/// <remarks>
/// The layout is persisted as a raw byte mask (bit 0 = LED 1 through bit 4 = LED 5) so the
/// Settings project does not need to depend on the protocol-level
/// <c>PlayerLedMask</c> enum defined in the Controllers project. The applying side casts
/// this byte back to that enum.
/// </remarks>
public class PlayerLedSettings
{
    /// <summary>
    /// Gets or sets the player LED mask: bit 0 = LED 1 (leftmost) through bit 4 = LED 5 (rightmost).
    /// </summary>
    [JsonPropertyName("mask")]
    public byte Mask { get; set; }
}