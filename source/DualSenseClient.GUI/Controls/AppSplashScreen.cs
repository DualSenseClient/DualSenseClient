using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using FluentAvalonia.UI.Windowing;

namespace DualSenseClient.GUI.Controls;

/// <summary>
/// Splash screen implementation that displays a progress bar during application startup.
/// Runs simulated initialization steps to demonstrate the splash screen pattern.
/// </summary>
internal class AppSplashScreen : IFAApplicationSplashScreen
{
    /// <summary>
    /// The name of the application to display during the splash screen
    /// </summary>
    public string AppName => null!;

    /// <summary>
    /// The desired image to be shown during the splash screen
    /// </summary>
    public IImage AppIcon => null!;

    /// <summary>
    /// Custom content to be shown during the splash screen. Uses a <see cref="SplashScreenView"/> with a progress bar.
    /// </summary>
    public object SplashScreenContent { get; }

    /// <summary>
    /// Specifies the minimum showtime (in milliseconds) for the splash screen.
    /// Set to 0 to allow the splash to transition as soon as <see cref="RunTasks"/> completes.
    /// </summary>
    public int MinimumShowTime => 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppSplashScreen"/> class
    /// </summary>
    public AppSplashScreen()
    {
        SplashScreenContent = new SplashScreenView();
    }

    /// <summary>
    /// Called by <see cref="FAAppWindow"/> to run initialization tasks during the splash screen.
    /// Runs simulated loading steps on a background thread and reports progress to the <see cref="SplashScreenView"/>.
    /// </summary>
    /// <param name="token">A cancellation token to signal when the splash screen should be cancelled.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task RunTasks(CancellationToken token)
    {
        await Task.Run(async () =>
        {
            // TODO: All stuff that needs to be done before usage needs to be done here
            await Task.Delay(2000, token);
        }, token);
    }
}