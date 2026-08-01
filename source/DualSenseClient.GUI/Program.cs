using Avalonia;
using System;

namespace DualSenseClient.GUI;

/// <summary>
/// Application entry point. Configures Avalonia and starts the desktop lifetime.
/// </summary>
/// <remarks>
/// Do not use any Avalonia or third-party APIs before <see cref="Main"/> is called,
/// as the framework has not been initialized yet.
/// </remarks>
sealed class Program
{
    /// <summary>
    /// The application entry point. Configures the Avalonia app builder and starts the classic desktop lifetime.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the application.</param>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Configures the Avalonia application builder with platform detection, developer tools (in debug mode),
    /// Inter font, and trace logging.
    /// </summary>
    /// <returns>The configured <see cref="AppBuilder"/> instance.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}