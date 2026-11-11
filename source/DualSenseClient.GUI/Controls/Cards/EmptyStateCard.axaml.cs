using Avalonia;
using Avalonia.Controls.Primitives;
using FluentIcons.Common;

namespace DualSenseClient.GUI.Controls.Cards;

/// <summary>
/// Card that displays an icon, title, and description, used for empty states
/// such as when no controller is selected.
/// </summary>
public class EmptyStateCard : TemplatedControl
{
    /// <summary>
    /// The Fluent icon shown above the title.
    /// </summary>
    public static readonly StyledProperty<Icon> IconProperty = AvaloniaProperty.Register<EmptyStateCard, Icon>(nameof(Icon), Icon.XboxController);

    /// <summary>
    /// The main title text displayed in SemiBold.
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<EmptyStateCard, string?>(nameof(Title));

    /// <summary>
    /// Subtitle/description shown below the title in a smaller font.
    /// </summary>
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<EmptyStateCard, string?>(nameof(Description));

    /// <summary>
    /// Gets or sets the Fluent icon shown above the title.
    /// </summary>
    public Icon Icon
    {
        get
        {
            return GetValue(IconProperty);
        }
        set
        {
            SetValue(IconProperty, value);
        }
    }

    /// <summary>
    /// Gets or sets the main title text displayed in SemiBold.
    /// </summary>
    public string? Title
    {
        get
        {
            return GetValue(TitleProperty);
        }
        set
        {
            SetValue(TitleProperty, value);
        }
    }

    /// <summary>
    /// Gets or sets the subtitle/description shown below the title in a smaller font.
    /// </summary>
    public string? Description
    {
        get
        {
            return GetValue(DescriptionProperty);
        }
        set
        {
            SetValue(DescriptionProperty, value);
        }
    }
}