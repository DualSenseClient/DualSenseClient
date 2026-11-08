using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Convenience overloads that normalize persisted settings before applying them to a
/// virtual controller. Production callers pass a <see cref="ForwardingSnapshot"/>.
/// </summary>
public static class ForwardingSettingsExtensions
{
    /// <summary>
    /// Applies persisted forwarding settings by normalizing them first.
    /// </summary>
    /// <param name="controller">The virtual controller.</param>
    /// <param name="settings">The persisted settings.</param>
    public static void ApplyForwardingSettings(this IVirtualController controller, ForwardSettings settings)
    {
        ArgumentNullException.ThrowIfNull(controller);
        controller.ApplyForwardingSettings(ForwardingSnapshot.From(settings));
    }
}