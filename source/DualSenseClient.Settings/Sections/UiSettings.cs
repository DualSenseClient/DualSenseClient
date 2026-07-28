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
}