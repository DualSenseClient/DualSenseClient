using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using FluentAvalonia.UI.Windowing;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.GUI.Services;
using DualSenseClient.GUI.ViewModels;
using DualSenseClient.HidHide;
using DualSenseClient.Settings;

namespace DualSenseClient.GUI.Controls;

/// <summary>
/// Splash screen implementation that displays a progress bar during application startup.
/// Runs initialization steps (e.g. loading persistent data) on a background thread.
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
    /// The view providing the status message and progress bar during startup.
    /// </summary>
    private readonly SplashScreenView _splashScreen;

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
        _splashScreen = new SplashScreenView();
        SplashScreenContent = _splashScreen;
    }

    /// <summary>
    /// Called by <see cref="FAAppWindow"/> to run initialization tasks during the splash screen.
    /// Loads persistent data (e.g. controller profiles) and scans for connected controllers on
    /// a background thread, reporting progress to the <see cref="SplashScreenView"/>.
    /// </summary>
    /// <param name="token">A cancellation token to signal when the splash screen should be cancelled.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task RunTasks(CancellationToken token)
    {
        // Required delay so the window properly shows
        await Task.Delay(10, token);
        _splashScreen.UpdateStatusMessage(LocalizationService.GetText("SplashScreen.LoadingProfiles"));
        App.Services.GetRequiredService<ProfileService>().Load();
        App.Services.GetRequiredService<ControllerInfoService>().Load();

        _splashScreen.UpdateStatusMessage(LocalizationService.GetText("SplashScreen.StartingServices"));

        // Special action coordinator (created for its side effects: it attaches a
        // special actions engine to every tracked controller).
        _ = App.Services.GetRequiredService<SpecialActionCoordinator>();

        // Emulation service (started for its side effects: it creates a virtual
        // controller for every tracked controller whose bound profile enables it).
        App.Services.GetRequiredService<IEmulationService>().Start();

        // Controller hiding: ensure this app stays able to see hidden
        // controllers, e.g. via the HidHide driver whitelist on Windows.
        App.Services.GetRequiredService<IControllerHidingService>().EnsureSelfVisible();

        MainViewModel mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _splashScreen.UpdateStatusMessage(LocalizationService.GetText("SplashScreen.ScanningControllers"));
        await mainViewModel.InitializeScanningAsync(token);
    }
}