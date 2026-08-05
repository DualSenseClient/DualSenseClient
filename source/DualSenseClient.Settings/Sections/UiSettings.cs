using System.Text.Json.Serialization;
using DualSenseClient.Core.Models;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// Settings for the user interface, including language and theme.
/// </summary>
public class UiSettings
{
    /// <summary>
    /// Gets or sets the language code used by the application UI (e.g., "en", "de").
    /// </summary>
    /// <remarks>
    /// Defaults to <c>"en"</c> (English). Changing this at runtime reloads the localization
    /// overlay via <see cref="DualSenseClient.GUI.Services.LocalizationService.LoadLanguage"/>.
    /// </remarks>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "en";

    /// <summary>
    /// Gets or sets the theme applied to the application UI.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="Theme.System"/>. Changing this at runtime swaps the active
    /// resource dictionary via <see cref="DualSenseClient.GUI.Services.ThemeService"/>.
    /// </remarks>
    [JsonPropertyName("theme")]
    public Theme Theme { get; set; } = Theme.System;

    /// <summary>
    /// Gets or sets whether closing the main window hides it to the system tray
    /// instead of exiting the application.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>true</c>. The application can always be exited from the tray menu.
    /// </remarks>
    [JsonPropertyName("closeToTray")]
    public bool CloseToTray { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the application starts with its main window hidden
    /// in the system tray instead of showing it on launch.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>false</c>. The window can be restored from the tray menu.
    /// </remarks>
    [JsonPropertyName("startInTray")]
    public bool StartInTray { get; set; }

    /// <summary>
    /// Gets or sets whether the tray icon shows the selected controller's battery
    /// percentage instead of the application icon.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>true</c>.
    /// </remarks>
    [JsonPropertyName("showBatteryPercentage")]
    public bool ShowBatteryPercentage { get; set; } = true;
}