using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using FluentIcons.Common;

namespace DualSenseClient.GUI.Controls.Cards;

/// <summary>
/// A labeled card combining CardHeader with a TextBox for string input.
/// </summary>
public class TextBoxCard : ContentControl
{
    /// <summary>
    /// The main title text.
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<TextBoxCard, string?>(nameof(Title));

    /// <summary>
    /// Subtitle/description text.
    /// </summary>
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<TextBoxCard, string?>(nameof(Description));

    /// <summary>
    /// Tooltip for the card.
    /// </summary>
    public static readonly StyledProperty<string?> TooltipProperty = AvaloniaProperty.Register<TextBoxCard, string?>(nameof(Tooltip));

    /// <summary>
    /// Fluent icon for the header.
    /// </summary>
    public static readonly StyledProperty<Symbol?> IconProperty = AvaloniaProperty.Register<TextBoxCard, Symbol?>(nameof(Icon));

    /// <summary>
    /// Whether to show accent background behind the icon.
    /// </summary>
    public static readonly StyledProperty<bool> ShowIconBackgroundProperty = AvaloniaProperty.Register<CardHeader, bool>(
        nameof(ShowIconBackground),
        defaultValue: false);

    /// <summary>
    /// The text content (two-way).
    /// </summary>
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<TextBoxCard, string?>(
        nameof(Text),
        defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Minimum width of the TextBox.
    /// </summary>
    public static readonly StyledProperty<double> TextBoxMinWidthProperty = AvaloniaProperty.Register<TextBoxCard, double>(
        nameof(TextBoxMinWidth),
        160.0);

    /// <summary>
    /// Maximum width of the TextBox.
    /// </summary>
    public static readonly StyledProperty<double> TextBoxMaxWidthProperty = AvaloniaProperty.Register<TextBoxCard, double>(
        nameof(TextBoxMaxWidth),
        160.0);

    /// <summary>
    /// Maximum number of characters the TextBox accepts. <c>0</c> means no limit.
    /// </summary>
    public static readonly StyledProperty<int> MaxLengthProperty = AvaloniaProperty.Register<TextBoxCard, int>(
        nameof(MaxLength),
        0);

    /// <inheritdoc cref="TitleProperty"/>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <inheritdoc cref="DescriptionProperty"/>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <inheritdoc cref="TooltipProperty"/>
    public string? Tooltip
    {
        get => GetValue(TooltipProperty);
        set => SetValue(TooltipProperty, value);
    }

    /// <inheritdoc cref="IconProperty"/>
    public Symbol? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <inheritdoc cref="ShowIconBackgroundProperty"/>
    public bool ShowIconBackground
    {
        get => GetValue(ShowIconBackgroundProperty);
        set => SetValue(ShowIconBackgroundProperty, value);
    }

    /// <inheritdoc cref="TextProperty"/>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <inheritdoc cref="TextBoxMinWidthProperty"/>
    public double TextBoxMinWidth
    {
        get => GetValue(TextBoxMinWidthProperty);
        set => SetValue(TextBoxMinWidthProperty, value);
    }

    /// <inheritdoc cref="TextBoxMaxWidthProperty"/>
    public double TextBoxMaxWidth
    {
        get => GetValue(TextBoxMaxWidthProperty);
        set => SetValue(TextBoxMaxWidthProperty, value);
    }

    /// <inheritdoc cref="MaxLengthProperty"/>
    public int MaxLength
    {
        get => GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }
}