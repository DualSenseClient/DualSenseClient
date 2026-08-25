using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using FluentIcons.Common;

namespace DualSenseClient.GUI.Controls.Cards;

/// <summary>
/// A labeled card combining CardHeader with a FluentAvalonia FANumberBox for numeric input.
/// </summary>
public class NumberBoxCard : ContentControl
{
    /// <summary>
    /// The main title text.
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<NumberBoxCard, string?>(nameof(Title));

    /// <summary>
    /// Subtitle/description text.
    /// </summary>
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<NumberBoxCard, string?>(nameof(Description));

    /// <summary>
    /// Tooltip for the card.
    /// </summary>
    public static readonly StyledProperty<string?> TooltipProperty = AvaloniaProperty.Register<NumberBoxCard, string?>(nameof(Tooltip));

    /// <summary>
    /// Fluent icon for the header.
    /// </summary>
    public static readonly StyledProperty<Symbol?> IconProperty = AvaloniaProperty.Register<NumberBoxCard, Symbol?>(nameof(Icon));

    /// <summary>
    /// Whether to show accent background behind the icon.
    /// </summary>
    public static readonly StyledProperty<bool> ShowIconBackgroundProperty = AvaloniaProperty.Register<NumberBoxCard, bool>(
        nameof(ShowIconBackground),
        false);

    /// <summary>
    /// Minimum allowable value.
    /// </summary>
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<NumberBoxCard, double>(nameof(Minimum), double.MinValue);

    /// <summary>
    /// Maximum allowable value.
    /// </summary>
    public static readonly StyledProperty<double?> MaximumProperty = AvaloniaProperty.Register<NumberBoxCard, double?>(nameof(Maximum), double.MaxValue);

    /// <summary>
    /// Current numeric value (two-way).
    /// </summary>
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<NumberBoxCard, double>(
        nameof(Value),
        defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Maximum width of the NumberBox control.
    /// </summary>
    public static readonly StyledProperty<double> NumberBoxMaxWidthProperty = AvaloniaProperty.Register<NumberBoxCard, double>(
        nameof(NumberBoxMaxWidth),
        180.0);

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

    /// <inheritdoc cref="MinimumProperty"/>
    public double Minimum
    {
        get
        {
            return GetValue(MinimumProperty);
        }
        set
        {
            SetValue(MinimumProperty, value);
        }
    }

    /// <inheritdoc cref="MaximumProperty"/>
    public double? Maximum
    {
        get
        {
            return GetValue(MaximumProperty);
        }
        set
        {
            SetValue(MaximumProperty, value);
        }
    }

    /// <inheritdoc cref="ValueProperty"/>
    public double Value
    {
        get
        {
            return GetValue(ValueProperty);
        }
        set
        {
            SetValue(ValueProperty, value);
        }
    }

    /// <inheritdoc cref="NumberBoxMaxWidthProperty"/>
    public double NumberBoxMaxWidth
    {
        get
        {
            return GetValue(NumberBoxMaxWidthProperty);
        }
        set
        {
            SetValue(NumberBoxMaxWidthProperty, value);
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
}