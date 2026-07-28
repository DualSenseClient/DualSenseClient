using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DualSenseClient.GUI.Converters;

/// <summary>
/// Multi-value converter that formats a <see cref="double"/> using the provided format string.
/// </summary>
/// <remarks>
/// Binding[0] = <see cref="double"/> value, Binding[1] = format string (e.g., "F0", "F2").
/// Used by <see cref="Controls.Cards.SliderCard"/> to display the current slider value.
/// </remarks>
public class DoubleFormatConverter : IMultiValueConverter
{
    /// <summary>
    /// Shared singleton instance of this converter.
    /// </summary>
    public static DoubleFormatConverter Instance { get; } = new DoubleFormatConverter();

    /// <summary>
    /// Formats the first value in the collection using the format string from the second value.
    /// </summary>
    /// <param name="values">A list where index 0 is the <see cref="double"/> value and index 1 is the format string.</param>
    /// <param name="targetType">The target type (unused).</param>
    /// <param name="parameter">An optional parameter (unused).</param>
    /// <param name="culture">The culture used to format the number.</param>
    /// <returns>The formatted string, or an empty string if the input is invalid.</returns>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 1 && values[0] is double doubleValue)
        {
            string? format = values.Count >= 2 ? values[1] as string : "F0";
            return doubleValue.ToString(format ?? "F0", culture);
        }
        return string.Empty;
    }
}