using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.Templates;
using FluentIcons.Common;

namespace DualSenseClient.GUI.Controls.Cards;

/// <summary>
/// A labeled card combining CardHeader with a ComboBox for selecting from a list.
/// </summary>
public class ComboBoxCard : ContentControl
{
    /// <summary>
    /// The main title text.
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<ComboBoxCard, string?>(nameof(Title));

    /// <summary>
    /// Subtitle/description text.
    /// </summary>
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<ComboBoxCard, string?>(nameof(Description));

    /// <summary>
    /// Tooltip for the card.
    /// </summary>
    public static readonly StyledProperty<string?> TooltipProperty = AvaloniaProperty.Register<ComboBoxCard, string?>(nameof(Tooltip));

    /// <summary>
    /// Fluent icon for the header.
    /// </summary>
    public static readonly StyledProperty<Symbol?> IconProperty = AvaloniaProperty.Register<ComboBoxCard, Symbol?>(nameof(Icon));

    /// <summary>
    /// Whether to show accent background behind the icon.
    /// </summary>
    public static readonly StyledProperty<bool> ShowIconBackgroundProperty = AvaloniaProperty.Register<CardHeader, bool>(
        nameof(ShowIconBackground),
        defaultValue: false);

    /// <summary>
    /// Item source for the ComboBox.
    /// </summary>
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty = AvaloniaProperty.Register<ComboBoxCard, IEnumerable?>(nameof(ItemsSource));

    /// <summary>
    /// Currently selected item (two-way).
    /// </summary>
    public static readonly StyledProperty<object?> SelectedItemProperty = AvaloniaProperty.Register<ComboBoxCard, object?>(
        nameof(SelectedItem),
        defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Currently selected index (two-way, default -1).
    /// </summary>
    public static readonly StyledProperty<int> SelectedIndexProperty = AvaloniaProperty.Register<ComboBoxCard, int>(
        nameof(SelectedIndex),
        defaultValue: -1,
        defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Optional data template for each item.
    /// </summary>
    public static readonly StyledProperty<DataTemplate?> ItemTemplateProperty = AvaloniaProperty.Register<ComboBoxCard, DataTemplate?>(nameof(ItemTemplate));

    /// <summary>
    /// Minimum width of the ComboBox control.
    /// </summary>
    public static readonly StyledProperty<double> ComboBoxMinWidthProperty = AvaloniaProperty.Register<ComboBoxCard, double>(
        nameof(ComboBoxMinWidth),
        160.0);

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

    /// <inheritdoc cref="ItemsSourceProperty"/>
    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <inheritdoc cref="SelectedItemProperty"/>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <inheritdoc cref="SelectedIndexProperty"/>
    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <inheritdoc cref="ItemTemplateProperty"/>
    public DataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <inheritdoc cref="ComboBoxMinWidthProperty"/>
    public double ComboBoxMinWidth
    {
        get => GetValue(ComboBoxMinWidthProperty);
        set => SetValue(ComboBoxMinWidthProperty, value);
    }
}