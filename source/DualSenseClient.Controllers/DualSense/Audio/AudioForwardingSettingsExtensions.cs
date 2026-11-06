using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Convenience overloads that normalize persisted settings before applying them to the
/// audio forwarder. Production callers pass a <see cref="ForwardingSnapshot"/>.
/// </summary>
public static class AudioForwardingSettingsExtensions
{
    /// <summary>
    /// Applies persisted forwarding settings by normalizing them first.
    /// </summary>
    /// <param name="forwarder">The audio forwarder.</param>
    /// <param name="settings">The persisted settings.</param>
    public static void ApplyForwardingSettings(this DualSenseAudioForwarder forwarder, ForwardSettings settings)
    {
        ArgumentNullException.ThrowIfNull(forwarder);
        forwarder.ApplyForwardingSettings(ForwardingSnapshot.From(settings));
    }
}