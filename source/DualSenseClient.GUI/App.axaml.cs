using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DualSenseClient.Core.Utilities;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.GUI.Services;
using DualSenseClient.GUI.Views;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using LogLevel = DualSenseClient.Logging.LogLevel;

namespace DualSenseClient.GUI;

public partial class App : Application
{
    /// <summary>
    /// Gets the desktop application lifetime, or <c>null</c> if not running as a desktop app.
    /// </summary>
    /// <remarks>
    /// Provides access to desktop-specific functionality such as the main window and
    /// shutdown modes. Initialized during <see cref="OnFrameworkInitializationCompleted"/>.
    /// </remarks>
    public static readonly IClassicDesktopStyleApplicationLifetime? Desktop = Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    /// <summary>
    /// Gets the main application window, or <c>null</c> if not running as a desktop app.
    /// </summary>
    public static Window? MainWindow => Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;

    /// <summary>
    /// Gets the dependency injection service provider for the application.
    /// </summary>
    /// <remarks>
    /// Configured once at static initialization time via <see cref="ServiceConfigurator.ConfigureServices"/>.
    /// All application services should be resolved from this provider.
    /// </remarks>
    public static IServiceProvider Services { get; private set; } = ServiceConfigurator.ConfigureServices();

    /// <summary>
    /// Whether the application is shutting down via the tray menu's Exit item.
    /// Set so the main window's close handler stops intercepting window closes
    /// and lets the shutdown proceed.
    /// </summary>
    public static bool IsExiting { get; set; }

    /// <summary>
    /// Logger instance for application-level events.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("App");

    /// <summary>
    /// Loads the XAML resources for this application.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Called when the Avalonia framework has completed initialization.
    /// Configures logging, localization, theme, global exception handlers, and creates the main window.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (Desktop is { } desktop)
        {
            SettingsService settingsService = Services.GetRequiredService<SettingsService>();
            _ = settingsService.Settings;

            LogLevel logLevel = settingsService.Settings.Debug.LogLevel;
            DualSenseClientLogger.Configure(logLevel,
                new CompositeLogSink(
                    new ConsoleLogSink(),
                    new FileLogSink(PathResolver.GetFullPath(@"Logs\DualSenseClient.log"))
                )
            );

            // Flush buffered log entries and close the file sink when the app exits.
            desktop.Exit += (_, _) =>
            {
                _log.Info("Closing DualSense Client");
                DualSenseClientLogger.Shutdown();
            };

            BuildInfo.LogStartupBanner(_log);
            RegisterGlobalExceptionHandlers();

            LocalizationService.LoadLanguage(settingsService.Settings.Ui.Language);

            ThemeService themeService = Services.GetRequiredService<ThemeService>();
            themeService.SetTheme(settingsService.Settings.Ui.Theme);

            MainWindow mainWindow = Services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;

            // When "start in tray" is enabled, hide the window once it opens.
            // The lifetime's ShowMainWindow call runs after this method, so the
            // window must be hidden in its Opened handler instead of here.
            if (settingsService.Settings.Ui.StartInTray)
            {
                mainWindow.Opened += HideStartupWindow;
            }

            // Tray icon (created for its side effects: icon, menu, and subscriptions).
            _ = Services.GetRequiredService<TrayIconService>();

            // Special action coordinator (created for its side effects: it attaches the
            // special actions engine to the active controller).
            _ = Services.GetRequiredService<SpecialActionCoordinator>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Hides the main window on its first opening when the "start in tray" setting
    /// is enabled, leaving the application running in the system tray.
    /// </summary>
    private static void HideStartupWindow(object? sender, EventArgs e)
    {
        if (sender is not MainWindow mainWindow)
        {
            return;
        }

        mainWindow.Opened -= HideStartupWindow;
        mainWindow.Hide();
    }

    /// <summary>
    /// Registers global exception handlers for <see cref="TaskScheduler.UnobservedTaskException"/>,
    /// <see cref="AppDomain.UnhandledException"/>, and <see cref="Dispatcher.UIThread.UnhandledException"/>.
    /// All caught exceptions are logged via <see cref="DualSenseClientLogger.LogExceptionDetails"/>.
    /// </summary>
    private void RegisterGlobalExceptionHandlers()
    {
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            _log.Error("Unobserved task exception occurred");
            _log.LogExceptionDetails(args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            bool isTerminating = args.IsTerminating;
            _log.Error($"Unhandled exception occurred in AppDomain (Terminating: {isTerminating})");
            if (args.ExceptionObject is Exception ex)
            {
                _log.LogExceptionDetails(ex);
            }
            else
            {
                _log.Error($"Non-exception object thrown: {args.ExceptionObject.GetType().FullName ?? "null"}");
            }

            // The process is about to die, so the background flush timer can no longer be
            // relied on. Flush all buffered entries and close the file sink before exit.
            if (args.IsTerminating)
            {
                DualSenseClientLogger.Shutdown();
            }
        };

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            args.Handled = true;
            _log.Error("Unhandled exception on UI thread");
            _log.LogExceptionDetails(args.Exception);
        };
    }
}