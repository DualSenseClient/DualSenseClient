using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Windowing;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Core.Utilities;
using DualSenseClient.GUI.Services;
using DualSenseClient.GUI.ViewModels;
using DualSenseClient.GUI.ViewModels.Pages;
using DualSenseClient.GUI.Views;
using DualSenseClient.HidHide;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.Controls;

/// <summary>
/// Splash screen implementation that displays a progress bar during application startup.
/// Runs initialization steps (e.g. loading persistent data) on a background thread.
/// </summary>
internal class AppSplashScreen : IFAApplicationSplashScreen
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("AppSplashScreen");

    /// <summary>
    /// The name of the application to display during the splash screen
    /// </summary>
    public string AppName
    {
        get
        {
            return null!;
        }
    }

    /// <summary>
    /// The desired image to be shown during the splash screen
    /// </summary>
    public IImage AppIcon
    {
        get
        {
            return null!;
        }
    }

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
    public int MinimumShowTime
    {
        get
        {
            return 0;
        }
    }

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

        // Build the main shell content here rather than in the window's XAML, so the
        // window opens (and this splash screen appears) before that work runs. Queued
        // at background priority so splash progress updates render first, and awaited
        // below so everything is complete before the splash transition fades it in.
        DispatcherOperation uiPreload = Dispatcher.UIThread.InvokeAsync(() =>
        {
            MainWindow mainWindow = App.Services.GetRequiredService<MainWindow>();
            mainWindow.LoadMainContent();

            // Tray icon (created for its side effects: icon, menu, and subscriptions).
            // Resolved here instead of during app initialization so its setup cost also
            // lands behind the splash screen; it needs the UI thread for its timer.
            _ = App.Services.GetRequiredService<TrayIconService>();
        }, DispatcherPriority.Background);

        _splashScreen.UpdateStatusMessage(LocalizationService.GetText("SplashScreen.LoadingProfiles"));

        // RunTasks runs on the UI thread (FAAppWindow awaits it from OnOpened), so
        // every blocking step below is pushed to a worker thread to keep the splash
        // progress bar animating. Profiles and controller info load first because the
        // services started afterwards read them.
        ProfileService profileService = App.Services.GetRequiredService<ProfileService>();
        ControllerInfoService controllerInfoService = App.Services.GetRequiredService<ControllerInfoService>();
        await Task.Run(() =>
        {
            profileService.Load();
            controllerInfoService.Load();
            // Drop the backup left by an in-app update (best-effort, never throws).
            UpdateInstaller.CleanupOld(Environment.ProcessPath);
        }, token);

        _splashScreen.UpdateStatusMessage(LocalizationService.GetText("SplashScreen.StartingServices"));
        await Task.Run(() =>
        {
            // Special action coordinator (created for its side effects: it attaches a
            // special actions engine to every tracked controller).
            _ = App.Services.GetRequiredService<SpecialActionCoordinator>();

            // Emulation service (started for its side effects: it creates a virtual
            // controller for every tracked controller whose bound profile enables it).
            App.Services.GetRequiredService<IEmulationService>().Start();

            // Controller hiding: ensure this app stays able to see hidden
            // controllers, e.g. via the HidHide driver whitelist on Windows.
            App.Services.GetRequiredService<IControllerHidingService>().EnsureSelfVisible();

            // Pre-decode the default controller illustration assets so their decode
            // cost doesn't land on the UI thread when the first page renders them.
            ControllerIllustrationService illustrations = App.Services.GetRequiredService<ControllerIllustrationService>();
            illustrations.GetSkins();
            illustrations.GetSkinImage(ControllerIllustrationService.DefaultSkin);
            illustrations.GetMonitorBase(ControllerIllustrationService.DefaultSkin);
        }, token);

        MainViewModel mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _splashScreen.UpdateStatusMessage(LocalizationService.GetText("SplashScreen.ScanningControllers"));
        await mainViewModel.InitializeScanningAsync(token);

        _splashScreen.UpdateStatusMessage(LocalizationService.GetText("SplashScreen.CheckingUpdates"));
        await CheckForUpdatesDailyAsync();

        await uiPreload;
    }

    /// <summary>
    /// Checks GitHub for updates at most once per calendar day (like the daily log rotation),
    /// when enabled in settings. A found update arms the release button and shows a clickable popup.
    /// Never throws; failures are logged and skipped so startup is unaffected.
    /// </summary>
    private static async Task CheckForUpdatesDailyAsync()
    {
        try
        {
            SettingsService settingsService = App.Services.GetRequiredService<SettingsService>();
            UpdateSettings updates = settingsService.Settings.Update;
            if (!updates.AutomaticCheck || updates.LastUpdateCheck == DateOnly.FromDateTime(DateTime.Today))
            {
                return;
            }

            updates.LastUpdateCheck = DateOnly.FromDateTime(DateTime.Today);
            settingsService.SaveSettings();

            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            UpdateChecker.ReleaseInfo? latest = await UpdateChecker.GetLatestAsync(updates.NightlyVersion, cts.Token);
            if (latest is null
                || !UpdateChecker.IsNewer(latest.Tag, AppInfo.Version, AppInfo.CommitShaShort, updates.NightlyVersion))
            {
                return;
            }

            App.Services.GetRequiredService<SettingsPageViewModel>().SetPendingUpdate(latest);

            App.Services.GetRequiredService<INotificationPopupService>().ShowMessage(
                LocalizationService.GetText("Notification.Update.Title"),
                string.Format(LocalizationService.GetText("Notification.Update.Message"), latest.Tag),
                url: latest.Url);
        }
        catch (Exception ex)
        {
            _log.Warning("Daily update check failed, skipping so startup is unaffected");
            _log.LogExceptionDetails(ex);
        }
    }
}