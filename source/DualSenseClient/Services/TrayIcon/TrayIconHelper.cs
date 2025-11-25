using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DualSenseClient.Core.Logging;
using DualSenseClient.ViewModels;

namespace DualSenseClient.Services.Helpers;

/// <summary>
/// Helper class providing static methods for creating and managing tray icons.
/// </summary>
public static class TrayIconHelper
{
    /// <summary>
    /// Creates a custom tray icon with battery level text overlay based on the controller's battery status.
    /// </summary>
    /// <param name="controller">The controller view model containing battery information</param>
    /// <returns>A window icon with battery percentage displayed</returns>
    public static WindowIcon CreateBatteryIcon(ControllerViewModelBase controller)
    {
        try
        {
            // Create a 16x16 Tray Icon
            PixelSize pixelSize = new PixelSize(16, 16);
            RenderTargetBitmap renderTarget = new RenderTargetBitmap(pixelSize, new Vector(96, 96)); // 96 DPI

            using (DrawingContext context = renderTarget.CreateDrawingContext())
            {
                // Transparent background
                context.FillRectangle(Brushes.Transparent, new Rect(0, 0, 16, 16));

                // Determine the text color based on battery level
                Color textColor;
                if (controller.IsCharging)
                {
                    textColor = Color.FromRgb(0, 123, 255); // Blue for charging
                }
                else
                {
                    float level = Math.Clamp((float)controller.BatteryLevel / 100f, 0f, 1f);

                    byte red = (byte)(255 * (1f - level));
                    byte green = (byte)(255 * level);
                    byte blue = 0;

                    textColor = Color.FromRgb(red, green, blue);
                }

                // Draw the battery level text
                FormattedText text = new FormattedText(
                    controller.BatteryLevel.ToString("F0"),
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    14, // Font size
                    new SolidColorBrush(textColor)
                );

                // Measure the text size manually
                Size textSize = new Size(text.Width, text.Height);
                Point textPosition = new Point(
                    (16 - textSize.Width) / 2,
                    (16 - textSize.Height) / 2 - 1 // Slightly adjust position
                );

                context.DrawText(text, textPosition);
            }

            using MemoryStream memoryStream = new MemoryStream();
            renderTarget.Save(memoryStream);
            memoryStream.Position = 0;
            return new WindowIcon(memoryStream);
        }
        catch (Exception ex)
        {
            Logger.Error<TrayIconService>($"Failed to create battery icon for controller {controller.Name}: {ex.Message}");
            Logger.LogExceptionDetails<TrayIconService>(ex);

            // Return default icon as fallback
            return LoadDefaultIcon();
        }
    }

    /// <summary>
    /// Loads the default application icon for the tray.
    /// </summary>
    /// <returns>The default application tray icon</returns>
    public static WindowIcon LoadDefaultIcon()
    {
        try
        {
            string defaultIconPath = "avares://DualSenseClient/Assets/icon.ico";
            return new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri(defaultIconPath)));
        }
        catch (Exception ex)
        {
            Logger.Error<TrayIconService>($"Failed to load default icon: {ex.Message}");
            Logger.LogExceptionDetails<TrayIconService>(ex);

            // Since we can't return a null icon, we throw to indicate failure
            throw;
        }
    }
}