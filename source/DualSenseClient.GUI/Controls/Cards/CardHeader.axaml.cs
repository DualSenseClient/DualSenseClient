using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using FluentIcons.Common;

namespace DualSenseClient.GUI.Controls.Cards;

/// <summary>
/// Reusable card header displaying an icon, title, description, and optional action content.
/// </summary>
public class CardHeader : TemplatedControl
{
    /// <summary>
    /// The main title text displayed in SemiBold.
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<CardHeader, string?>(nameof(Title));

    /// <summary>
    /// Subtitle/description shown below the title in a smaller font.
    /// </summary>
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<CardHeader, string?>(nameof(Description));

    /// <summary>
    /// Tooltip for the entire header.
    /// </summary>
    public static readonly StyledProperty<string?> TooltipProperty = AvaloniaProperty.Register<CardHeader, string?>(nameof(Tooltip));

    /// <summary>
    /// Fluent icon symbol displayed in an accent-colored border.
    /// </summary>
    public static readonly StyledProperty<Symbol?> IconProperty = AvaloniaProperty.Register<CardHeader, Symbol?>(nameof(Icon));

    /// <summary>
    /// When false, the icon border background becomes transparent.
    /// </summary>
    public static readonly StyledProperty<bool> ShowIconBackgroundProperty = AvaloniaProperty.Register<CardHeader, bool>(
        nameof(ShowIconBackground),
        false);

    /// <summary>
    /// Custom content displayed on the right side of the header.
    /// </summary>
    public static readonly StyledProperty<object?> ActionContentProperty = AvaloniaProperty.Register<CardHeader, object?>(nameof(ActionContent));

    /// <summary>
    /// Data template for the ActionContent.
    /// </summary>
    public static readonly StyledProperty<IDataTemplate?> ActionContentTemplateProperty =
        AvaloniaProperty.Register<CardHeader, IDataTemplate?>(nameof(ActionContentTemplate));

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

    /// <summary>
    /// Gets or sets the tooltip for the entire header.
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

    /// <summary>
    /// Gets or sets the Fluent icon symbol displayed in an accent-colored border.
    /// </summary>
    public Symbol? Icon
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
    /// Gets or sets a value indicating whether the icon border background is shown.
    /// When <c>false</c>, the icon border background becomes transparent.
    /// </summary>
    public bool ShowIconBackground
    {
        get
        {
            return GetValue(ShowIconBackgroundProperty);
        }
        set
        {
            SetValue(ShowIconBackgroundProperty, value);
        }
    }

    /// <summary>
    /// Gets or sets the custom content displayed on the right side of the header.
    /// </summary>
    public object? ActionContent
    {
        get
        {
            return GetValue(ActionContentProperty);
        }
        set
        {
            SetValue(ActionContentProperty, value);
        }
    }

    /// <summary>
    /// Gets or sets the data template for the <see cref="ActionContent"/>.
    /// </summary>
    public IDataTemplate? ActionContentTemplate
    {
        get
        {
            return GetValue(ActionContentTemplateProperty);
        }
        set
        {
            SetValue(ActionContentTemplateProperty, value);
        }
    }
}