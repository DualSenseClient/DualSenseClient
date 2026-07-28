using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace DualSenseClient.GUI.Converters;

/// <summary>
/// Returns <c>true</c> if the bound value is not <c>null</c>.
/// Used to conditionally hide UI elements (e.g., empty icons and descriptions in cards).
/// </summary>
public class NotNullConverter : IValueConverter
{
    /// <summary>
    /// Shared singleton instance of this converter.
    /// </summary>
    public static NotNullConverter Instance { get; } = new NotNullConverter();

    /// <summary>
    /// Converts the bound value to a boolean indicating whether it is not <c>null</c>.
    /// </summary>
    /// <param name="value">The value to check for null.</param>
    /// <param name="targetType">The target type (unused).</param>
    /// <param name="parameter">An optional parameter (unused).</param>
    /// <param name="culture">The culture to use (unused).</param>
    /// <returns><c>true</c> if <paramref name="value"/> is not <c>null</c>; otherwise, <c>false</c>.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null;
    }

    /// <summary>
    /// Not supported. Returns <see cref="AvaloniaProperty.UnsetValue"/>.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}