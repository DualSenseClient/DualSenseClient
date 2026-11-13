using CommunityToolkit.Mvvm.ComponentModel;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// Display item for the rumble encoding picker. Wraps a
/// <see cref="VibrationEncodingMode"/> with its localized display name.
/// </summary>
public sealed partial class VibrationModeItem : ObservableObject
{
    /// <summary>
    /// Whether the item can be selected. Improved (V2) is disabled when the selected
    /// controller's firmware does not support it.
    /// </summary>
    [ObservableProperty] private bool _isEnabled;

    /// <summary>
    /// Creates a new picker item.
    /// </summary>
    /// <param name="value">The encoding mode this item represents.</param>
    /// <param name="name">Localized display name.</param>
    /// <param name="isEnabled">Whether the item can be selected.</param>
    public VibrationModeItem(VibrationEncodingMode value, string name, bool isEnabled = true)
    {
        Value = value;
        Name = name;
        _isEnabled = isEnabled;
    }

    /// <summary>
    /// The encoding mode this item represents.
    /// </summary>
    public VibrationEncodingMode Value { get; }

    /// <summary>
    /// Localized display name.
    /// </summary>
    public string Name { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}