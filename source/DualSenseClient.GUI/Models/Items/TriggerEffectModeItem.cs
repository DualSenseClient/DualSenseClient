using DualSenseClient.Controllers.DualSense.Triggers;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// Display item for the adaptive trigger mode picker. Wraps a <see cref="TriggerEffectType"/>
/// with its localized display name.
/// </summary>
public sealed class TriggerEffectModeItem
{
    /// <summary>
    /// Creates a new picker item.
    /// </summary>
    /// <param name="value">The trigger effect mode this item represents.</param>
    /// <param name="name">Localized display name.</param>
    public TriggerEffectModeItem(TriggerEffectType value, string name)
    {
        Value = value;
        Name = name;
    }

    /// <summary>
    /// The trigger effect mode this item represents.
    /// </summary>
    public TriggerEffectType Value { get; }

    /// <summary>
    /// Localized display name.
    /// </summary>
    public string Name { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}