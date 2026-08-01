using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DualSenseClient.Controllers.DualSense.Input;

namespace DualSenseClient.GUI.Controls;

/// <summary>
/// Which motion sensor is plotted by a <see cref="MotionGraph"/>.
/// </summary>
public enum MotionGraphAxis
{
    /// <summary>
    /// Gyroscope (angular velocity, 16.384 LSB/dps).
    /// </summary>
    Gyro,

    /// <summary>
    /// Accelerometer (linear acceleration, 8192 LSB/g).
    /// </summary>
    Accel
}

/// <summary>
/// Lightweight real-time line chart that plots the X, Y, and Z axes of the DualSense
/// IMU from a rolling buffer of motion samples. The device motion timestamp drives the
/// horizontal axis, so the window scrolls in time as new samples arrive; each axis is
/// drawn in its own color.
/// </summary>
/// <remarks>
/// <para>
/// The sample list is a mutable buffer that grows in place, so it is supplied from
/// code-behind rather than through a one-way binding (a binding would not re-fire when
/// the same instance is mutated). Callers invalidate the visual after appending samples.
/// </para>
/// </remarks>
public class MotionGraph : Control
{
    /// <summary>
    /// Stroke thickness of the series polylines, in device-independent units.
    /// </summary>
    private const double StrokeThickness = 1.5;

    /// <summary>
    /// The current rolling buffer of motion samples, oldest first.
    /// </summary>
    private IReadOnlyList<MotionState>? _samples;

    /// <summary>
    /// Defines the <see cref="Axis"/> dependency property.
    /// </summary>
    public static readonly StyledProperty<MotionGraphAxis> AxisProperty = AvaloniaProperty.Register<MotionGraph, MotionGraphAxis>(nameof(Axis));

    /// <summary>
    /// Defines the <see cref="SeriesXBrush"/> dependency property. The default is
    /// <c>null</c>; the value is supplied from the theme's custom colors (see
    /// <c>MotionGraphXBrush</c>) via a DynamicResource binding.
    /// </summary>
    public static readonly StyledProperty<IBrush?> SeriesXBrushProperty = AvaloniaProperty.Register<MotionGraph, IBrush?>(nameof(SeriesXBrush));

    /// <summary>
    /// Defines the <see cref="SeriesYBrush"/> dependency property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> SeriesYBrushProperty = AvaloniaProperty.Register<MotionGraph, IBrush?>(nameof(SeriesYBrush));

    /// <summary>
    /// Defines the <see cref="SeriesZBrush"/> dependency property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> SeriesZBrushProperty = AvaloniaProperty.Register<MotionGraph, IBrush?>(nameof(SeriesZBrush));

    /// <summary>
    /// Registers render-affecting properties.
    /// </summary>
    static MotionGraph()
    {
        AffectsRender<MotionGraph>(AxisProperty, SeriesXBrushProperty, SeriesYBrushProperty, SeriesZBrushProperty);
    }

    /// <summary>
    /// Whether this graph plots the gyroscope or the accelerometer axes.
    /// </summary>
    public MotionGraphAxis Axis
    {
        get => GetValue(AxisProperty);
        set => SetValue(AxisProperty, value);
    }

    /// <summary>
    /// Color used for the X axis.
    /// </summary>
    public IBrush? SeriesXBrush
    {
        get => GetValue(SeriesXBrushProperty);
        set => SetValue(SeriesXBrushProperty, value);
    }

    /// <summary>
    /// Color used for the Y axis.
    /// </summary>
    public IBrush? SeriesYBrush
    {
        get => GetValue(SeriesYBrushProperty);
        set => SetValue(SeriesYBrushProperty, value);
    }

    /// <summary>
    /// Color used for the Z axis.
    /// </summary>
    public IBrush? SeriesZBrush
    {
        get => GetValue(SeriesZBrushProperty);
        set => SetValue(SeriesZBrushProperty, value);
    }

    /// <summary>
    /// The rolling buffer of motion samples to plot, oldest first. Assigning the buffer
    /// (or mutating it in place) invalidates the control so it repaints on the next pass.
    /// </summary>
    public IReadOnlyList<MotionState>? Samples
    {
        get => _samples;
        set
        {
            _samples = value;
            InvalidateVisual();
        }
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        IReadOnlyList<MotionState>? samples = _samples;
        if (samples is null || samples.Count < 2)
        {
            DrawZeroLine(context, bounds, valueRange: null);
            return;
        }

        // Inset by half the stroke width so the lines are not clipped at the edges.
        Rect plot = bounds.Deflate(StrokeThickness / 2);

        int count = samples.Count;
        long startTimestamp = samples[0].Timestamp;
        long totalTicks = unchecked((uint)(samples[count - 1].Timestamp - startTimestamp));
        if (totalTicks <= 0)
        {
            totalTicks = 1;
        }

        double min = double.MaxValue;
        double max = double.MinValue;
        for (int i = 0; i < count; i++)
        {
            MinMax(samples[i], ref min, ref max);
        }

        if (max == min)
        {
            min -= 1;
            max += 1;
        }

        DrawZeroLine(context, plot, valueRange: (min, max));

        DrawSeries(context, plot, SeriesXBrush, samples, count, startTimestamp, totalTicks, min, max, axis: 0);
        DrawSeries(context, plot, SeriesYBrush, samples, count, startTimestamp, totalTicks, min, max, axis: 1);
        DrawSeries(context, plot, SeriesZBrush, samples, count, startTimestamp, totalTicks, min, max, axis: 2);
    }

    /// <summary>
    /// Expands the running value range to include this sample's three axis values.
    /// </summary>
    private void MinMax(MotionState sample, ref double min, ref double max)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            double value = GetValue(sample, axis);
            if (value < min) min = value;
            if (value > max) max = value;
        }
    }

    /// <summary>
    /// Reads the gyro or accel value for the given axis (0=X, 1=Y, 2=Z), depending on
    /// <see cref="Axis"/>.
    /// </summary>
    private double GetValue(MotionState sample, int axis)
    {
        if (Axis == MotionGraphAxis.Accel)
        {
            return axis switch
            {
                0 => sample.AccelX,
                1 => sample.AccelY,
                _ => sample.AccelZ
            };
        }

        return axis switch
        {
            0 => sample.GyroX,
            1 => sample.GyroY,
            _ => sample.GyroZ
        };
    }

    /// <summary>
    /// Draws a faint horizontal line at the zero value, or at the vertical center when
    /// no value range is available.
    /// </summary>
    private void DrawZeroLine(DrawingContext context, Rect plot, (double Min, double Max)? valueRange)
    {
        double y;
        if (valueRange is { } range)
        {
            double ratio = (0 - range.Min) / (range.Max - range.Min);
            y = Clamp(plot.Bottom - ratio * plot.Height, plot.Top, plot.Bottom);
        }
        else
        {
            y = plot.Center.Y;
        }

        IBrush? brush = ResolveBrush("TextFillColorSecondaryBrush")
                        ?? ResolveBrush("MotionGraphZeroLineBrush");
        if (brush is null)
        {
            return;
        }

        context.DrawLine(new Pen(brush, 1), new Point(plot.Left, y), new Point(plot.Right, y));
    }

    /// <summary>
    /// Resolves a brush from the active theme, or <c>null</c> when the key is absent.
    /// </summary>
    private static IBrush? ResolveBrush(string key)
    {
        if (Application.Current is not { } app)
        {
            return null;
        }

        return app.TryGetResource(key, app.ActualThemeVariant, out object? resource)
            ? resource as IBrush
            : null;
    }

    /// <summary>
    /// Builds and draws the polyline for a single axis.
    /// </summary>
    private void DrawSeries(
        DrawingContext context,
        Rect plot,
        IBrush? brush,
        IReadOnlyList<MotionState> samples,
        int count,
        long startTimestamp,
        long totalTicks,
        double min,
        double max,
        int axis)
    {
        if (brush is null)
        {
            return;
        }

        StreamGeometry geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            for (int i = 0; i < count; i++)
            {
                MotionState sample = samples[i];
                long tick = unchecked((uint)(sample.Timestamp - startTimestamp));
                double x = plot.Left + (tick / (double)totalTicks) * plot.Width;
                double ratio = (GetValue(sample, axis) - min) / (max - min);
                double y = plot.Bottom - ratio * plot.Height;
                Point point = new Point(x, y);
                if (i == 0)
                {
                    ctx.BeginFigure(point, false);
                }
                else
                {
                    ctx.LineTo(point);
                }
            }

            ctx.EndFigure(false);
        }

        context.DrawGeometry(
            null,
            new Pen(brush, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
            geometry);
    }

    /// <summary>
    /// Clamps a value into the given range.
    /// </summary>
    private static double Clamp(double value, double min, double max)
        => value < min ? min : value > max ? max : value;
}