using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using FluentIcons.Common;

namespace DualSenseClient.GUI.Controls.Cards;

/// <summary>
/// A labeled card combining CardHeader with a ToggleSwitch for boolean settings.
/// </summary>
public class ToggleSwitchCard : ContentControl
{
    /// <summary>
    /// The main title text.
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<ToggleSwitchCard, string?>(nameof(Title));

    /// <summary>
    /// Subtitle/description text.
    /// </summary>
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<ToggleSwitchCard, string?>(nameof(Description));

    /// <summary>
    /// Tooltip for the card.
    /// </summary>
    public static readonly StyledProperty<string?> TooltipProperty = AvaloniaProperty.Register<ToggleSwitchCard, string?>(nameof(Tooltip));

    /// <summary>
    /// Fluent icon for the header.
    /// </summary>
    public static readonly StyledProperty<Symbol?> IconProperty = AvaloniaProperty.Register<ToggleSwitchCard, Symbol?>(nameof(Icon));

    /// <summary>
    /// Whether to show accent background behind the icon.
    /// </summary>
    public static readonly StyledProperty<bool> ShowIconBackgroundProperty = AvaloniaProperty.Register<CardHeader, bool>(
        nameof(ShowIconBackground),
        false);

    /// <summary>
    /// Whether the toggle is checked (two-way).
    /// </summary>
    public static readonly StyledProperty<bool> IsCheckedProperty = AvaloniaProperty.Register<ToggleSwitchCard, bool>(
        nameof(IsChecked),
        defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Command executed when the toggle is switched.
    /// </summary>
    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<ToggleSwitchCard, ICommand?>(nameof(Command));

    /// <inheritdoc cref="TitleProperty"/>
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

    /// <inheritdoc cref="DescriptionProperty"/>
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

    /// <inheritdoc cref="TooltipProperty"/>
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

    /// <inheritdoc cref="IconProperty"/>
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

    /// <inheritdoc cref="ShowIconBackgroundProperty"/>
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

    /// <inheritdoc cref="IsCheckedProperty"/>
    public bool IsChecked
    {
        get
        {
            return GetValue(IsCheckedProperty);
        }
        set
        {
            SetValue(IsCheckedProperty, value);
        }
    }

    /// <inheritdoc cref="CommandProperty"/>
    public ICommand? Command
    {
        get
        {
            return GetValue(CommandProperty);
        }
        set
        {
            SetValue(CommandProperty, value);
        }
    }
}