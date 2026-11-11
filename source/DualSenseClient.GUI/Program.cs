using System;
using Avalonia;
using DualSenseClient.GUI.Services;
using DualSenseClient.Logging;

namespace DualSenseClient.GUI;

/// <summary>
/// Application entry point. Configures Avalonia and starts the desktop lifetime.
/// </summary>
/// <remarks>
/// Do not use any Avalonia or third-party APIs before <see cref="Main"/> is called,
/// as the framework has not been initialized yet.
/// </remarks>
internal sealed class Program
{
    /// <summary>
    /// The application entry point. Configures bootstrap logging, then starts the Avalonia
    /// classic desktop lifetime.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the application.</param>
    [STAThread]
    public static void Main(string[] args)
    {
        LoggingSetup.Initialize();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            DualSenseClientLogger.For("Program")
                .LogExceptionDetails(ex, true, LogLevel.Critical);
            DualSenseClientLogger.Shutdown();
            throw;
        }
    }

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