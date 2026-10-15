using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// Display item for the adaptive trigger mode picker. Wraps an
/// <see cref="AutoProfileTriggerMode"/> with its localized display name.
/// The output tester and auto profiles share the same picker modes so both
/// build through the same <see cref="Controllers.DualSense.Triggers.TriggerEffectBuilder"/> methods.
/// </summary>
public sealed class TriggerEffectModeItem
{
    /// <summary>
    /// Creates a new picker item.
    /// </summary>
    /// <param name="value">The trigger effect mode this item represents.</param>
    /// <param name="name">Localized display name.</param>
    public TriggerEffectModeItem(AutoProfileTriggerMode value, string name)
    {
        Value = value;
        Name = name;
    }

    /// <summary>
    /// The trigger effect mode this item represents.
    /// </summary>
    public AutoProfileTriggerMode Value { get; }

    /// <summary>
    /// Localized display name.
    /// </summary>
    public string Name { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}