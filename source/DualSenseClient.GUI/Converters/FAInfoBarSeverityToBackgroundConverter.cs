using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using FluentAvalonia.UI.Controls;

namespace DualSenseClient.GUI.Converters;

/// <summary>
/// Converts an <see cref="FAInfoBarSeverity"/> to a theme-aware background brush
/// using FluentAvalonia's SystemFillColor background brushes.
/// </summary>
public sealed class FAInfoBarSeverityToBackgroundConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FAInfoBarSeverity severity)
        {
            return null;
        }

        string key = severity switch
        {
            FAInfoBarSeverity.Success => "SystemFillColorSuccessBackgroundBrush",
            FAInfoBarSeverity.Warning => "SystemFillColorCautionBackgroundBrush",
            FAInfoBarSeverity.Error => "SystemFillColorCriticalBackgroundBrush",
            _ => "SystemFillColorNeutralBackgroundBrush"
        };

        if (Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out object? resource))
        {
            return resource;
        }

        return null;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}