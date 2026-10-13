using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace DualSenseClient.GUI.Controls;

/// <summary>
/// A slider with its current value rendered below it, centered. The slider disables
/// layout rounding so the fill line's width does not jitter as the value changes.
/// </summary>
/// <remarks>
/// The value is formatted through <c>DoubleFormatConverter</c> (see
/// <see cref="ValueFormat"/>; defaults to <c>F0</c>). Bind <see cref="Value"/> two-way to
/// the ViewModel property; the default binding mode applies it on every change.
/// </remarks>
public class SliderInput : TemplatedControl
{
    /// <summary>
    /// Minimum value of the slider.
    /// </summary>
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<SliderInput, double>(nameof(Minimum), 0);

    /// <summary>
    /// Maximum value of the slider.
    /// </summary>
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<SliderInput, double>(nameof(Maximum), 100);

    /// <summary>
    /// The current value of the slider.
    /// </summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SliderInput, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Interval between tick marks drawn on the slider track.
    /// </summary>
    public static readonly StyledProperty<double> TickFrequencyProperty =
        AvaloniaProperty.Register<SliderInput, double>(nameof(TickFrequency), 1);

    /// <summary>
    /// Whether the slider value snaps to tick marks. Enabled by default so
    /// integer-backed sliders always land on whole numbers while dragging.
    /// </summary>
    public static readonly StyledProperty<bool> IsSnapToTickEnabledProperty =
        AvaloniaProperty.Register<SliderInput, bool>(nameof(IsSnapToTickEnabled), true);

    /// <summary>
    /// Format string (e.g. <c>F0</c>, <c>F2</c>) used to render the value readout.
    /// </summary>
    public static readonly StyledProperty<string> ValueFormatProperty =
        AvaloniaProperty.Register<SliderInput, string>(nameof(ValueFormat), "F0");

    /// <summary>
    /// Minimum value of the slider.
    /// </summary>
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

    /// <summary>
    /// Maximum value of the slider.
    /// </summary>
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

    /// <summary>
    /// The current value of the slider.
    /// </summary>
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

    /// <summary>
    /// Interval between tick marks drawn on the slider track.
    /// </summary>
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

    /// <summary>
    /// Whether the slider value snaps to tick marks.
    /// </summary>
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

    /// <summary>
    /// Format string (e.g. <c>F0</c>, <c>F2</c>) used to render the value readout.
    /// </summary>
    public string ValueFormat
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
}