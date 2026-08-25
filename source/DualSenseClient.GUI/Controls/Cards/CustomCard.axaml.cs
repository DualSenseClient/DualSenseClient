using Avalonia;
using Avalonia.Controls;

namespace DualSenseClient.GUI.Controls.Cards;

/// <summary>
/// Base card container with rounded border and theme-aware background/stroke colors.
/// </summary>
public class CustomCard : ContentControl
{
    /// <summary>
    /// Tooltip shown on hover over the entire card.
    /// </summary>
    public static readonly StyledProperty<string?> TooltipProperty = AvaloniaProperty.Register<CustomCard, string?>(nameof(Tooltip));

    /// <summary>
    /// Gets or sets the tooltip shown on hover over the entire card.
    /// </summary>
    public string? Tooltip
    {
        get
        {
            return GetValue(TooltipProperty);
        }
        set
        {
            SetValue(TooltipProperty, value);
        }
    }
}