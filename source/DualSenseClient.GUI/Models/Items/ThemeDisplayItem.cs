using DualSenseClient.Core.Models;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// Represents a theme option for display in the UI, pairing a localized display name
/// with its corresponding <see cref="Theme"/> enum value.
/// </summary>
public class ThemeDisplayItem
{
    /// <summary>
    /// Gets or sets the localized display name shown to the user in theme dropdowns.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="Theme"/> value associated with this display item.
    /// </summary>
    public Theme ThemeValue { get; set; }

    /// <summary>
    /// Returns the display name for this theme option.
    /// </summary>
    /// <returns>The <see cref="DisplayName"/> of this <see cref="ThemeDisplayItem"/>.</returns>
    public override string ToString() => DisplayName;
}