using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// A temporary emulation override layered over a controller's stored settings, used by
/// Auto-profile rules. A <c>null</c> field leaves that setting at its stored value.
/// </summary>
/// <param name="Mode">The temporary emulation mode.</param>
/// <param name="DualSenseVariant">The temporary DualSense variant.</param>
/// <param name="DualShock4Variant">The temporary DualShock 4 variant.</param>
/// <param name="AudioSource">The temporary forwarded-audio source.</param>
/// <param name="AudioApp">The temporary captured application.</param>
public sealed record EmulationOverride(
    EmulationMode? Mode = null,
    DualSenseVariant? DualSenseVariant = null,
    DualShock4Variant? DualShock4Variant = null,
    EmulationAudioSource? AudioSource = null,
    string? AudioApp = null)
{
    /// <summary>
    /// Whether the override leaves every setting at its stored value.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            return Mode is null && DualSenseVariant is null && DualShock4Variant is null
                   && AudioSource is null && AudioApp is null;
        }
    }
}