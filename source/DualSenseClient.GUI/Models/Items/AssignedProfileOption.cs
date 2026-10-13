using Avalonia.Media;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// A profile entry in the assigned profile selector: its name and lightbar
/// color swatch.
/// </summary>
public sealed class AssignedProfileOption
{
    /// <summary>
    /// Gets the profile name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the brush for the profile's lightbar color swatch.
    /// </summary>
    public IBrush LightbarBrush { get; }

    /// <summary>
    /// Creates a new assigned profile option.
    /// </summary>
    /// <param name="name">The profile name.</param>
    /// <param name="red">The lightbar red channel (0-255).</param>
    /// <param name="green">The lightbar green channel (0-255).</param>
    /// <param name="blue">The lightbar blue channel (0-255).</param>
    public AssignedProfileOption(string name, byte red, byte green, byte blue)
    {
        Name = name;
        LightbarBrush = new SolidColorBrush(Color.FromRgb(red, green, blue));
    }
}