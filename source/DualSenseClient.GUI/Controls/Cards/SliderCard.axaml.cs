using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using FluentIcons.Common;

namespace DualSenseClient.GUI.Controls.Cards;

/// <summary>
/// A labeled card combining CardHeader with a Slider and formatted value readout.
/// </summary>
public class SliderCard : ContentControl
{
    /// <summary>
    /// The main title text.
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<SliderCard, string?>(nameof(Title));

    /// <summary>
    /// Subtitle/description text.
    /// </summary>
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<SliderCard, string?>(nameof(Description));

    /// <summary>
    /// Tooltip for the card.
    /// </summary>
    public static readonly StyledProperty<string?> TooltipProperty = AvaloniaProperty.Register<SliderCard, string?>(nameof(Tooltip));

    /// <summary>
    /// Fluent icon for the header.
    /// </summary>
    public static readonly StyledProperty<Symbol?> IconProperty = AvaloniaProperty.Register<SliderCard, Symbol?>(nameof(Icon));

    /// <summary>
    /// Whether to show accent background behind the icon.
    /// </summary>
    public static readonly StyledProperty<bool> ShowIconBackgroundProperty = AvaloniaProperty.Register<CardHeader, bool>(
        nameof(ShowIconBackground),
        false);

    /// <summary>
    /// Minimum slider value.
    /// </summary>
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<SliderCard, double>(nameof(Minimum), 0);

    /// <summary>
    /// Maximum slider value.
    /// </summary>
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<SliderCard, double>(nameof(Maximum), 100);

    /// <summary>
    /// Current slider value (two-way).
    /// </summary>
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<SliderCard, double>(
        nameof(Value),
        defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Frequency of tick marks.
    /// </summary>
    public static readonly StyledProperty<double> TickFrequencyProperty = AvaloniaProperty.Register<SliderCard, double>(nameof(TickFrequency), 1);

    /// <summary>
    /// Whether the slider snaps to tick marks.
    /// </summary>
    public static readonly StyledProperty<bool> IsSnapToTickEnabledProperty = AvaloniaProperty.Register<SliderCard, bool>(nameof(IsSnapToTickEnabled));

    /// <summary>
    /// Placement of tick marks along the slider track.
    /// </summary>
    public static readonly StyledProperty<TickPlacement> TickPlacementProperty = AvaloniaProperty.Register<SliderCard, TickPlacement>(
        nameof(TickPlacement),
        TickPlacement.None);

    /// <summary>
    /// Whether to enable layout rounding on the inner slider.
    /// </summary>
    public new static readonly StyledProperty<bool> UseLayoutRoundingProperty = AvaloniaProperty.Register<SliderCard, bool>(
        nameof(UseLayoutRounding));

    /// <summary>
    /// Minimum width of the slider area.
    /// </summary>
    public static readonly StyledProperty<double> SliderMinWidthProperty = AvaloniaProperty.Register<SliderCard, double>(
        nameof(SliderMinWidth),
        220.0);

    /// <summary>
    /// Format string for the value readout (e.g. "F0", "F2").
    /// </summary>
    public static readonly StyledProperty<string?> ValueFormatProperty = AvaloniaProperty.Register<SliderCard, string?>(
        nameof(ValueFormat),
        "F0");

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
    public double Maximum
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

    /// <inheritdoc cref="TickFrequencyProperty"/>
    public double TickFrequency
    {
        get
        {
            return GetValue(TickFrequencyProperty);
        }
        set
        {
            SetValue(TickFrequencyProperty, value);
        }
    }

    /// <inheritdoc cref="IsSnapToTickEnabledProperty"/>
    public bool IsSnapToTickEnabled
    {
        get
        {
            return GetValue(IsSnapToTickEnabledProperty);
        }
        set
        {
            SetValue(IsSnapToTickEnabledProperty, value);
        }
    }

    /// <inheritdoc cref="TickPlacementProperty"/>
    public TickPlacement TickPlacement
    {
        get
        {
            return GetValue(TickPlacementProperty);
        }
        set
        {
            SetValue(TickPlacementProperty, value);
        }
    }

    /// <inheritdoc cref="UseLayoutRoundingProperty"/>
    public new bool UseLayoutRounding
    {
        get
        {
            return GetValue(UseLayoutRoundingProperty);
        }
        set
        {
            SetValue(UseLayoutRoundingProperty, value);
        }
    }

    /// <inheritdoc cref="SliderMinWidthProperty"/>
    public double SliderMinWidth
    {
        get
        {
            return GetValue(SliderMinWidthProperty);
        }
        set
        {
            SetValue(SliderMinWidthProperty, value);
        }
    }

    /// <inheritdoc cref="ValueFormatProperty"/>
    public string? ValueFormat
    {
        get
        {
            return GetValue(ValueFormatProperty);
        }
        set
        {
            SetValue(ValueFormatProperty, value);
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