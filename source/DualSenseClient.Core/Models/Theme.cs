namespace DualSenseClient.Core.Models;

/// <summary>
/// Defines the application themes supported by the app.
/// </summary>
public enum Theme
{
    /// <summary>
    /// Follows the operating system's theme preference.
    /// </summary>
    System,

    /// <summary>
    /// Light theme with light backgrounds and dark text.
    /// </summary>
    Light,

    /// <summary>
    /// Dark theme with dark backgrounds and light text.
    /// </summary>
    Dark,

    /// <summary>
    /// AMOLED theme with true-black backgrounds that switch off OLED pixels.
    /// </summary>
    Amoled,

    /// <summary>
    /// PlayStation theme with deep blue-tinted surfaces and a PlayStation blue accent,
    /// in the style of the PS5 home screen.
    /// </summary>
    Playstation

    // Add new themes here
}