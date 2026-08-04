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
                    new MinimumLevelFilterSink(new ConsoleLogSink(), () => DualSenseClientLogger.MinimumLevel),
                    new FileLogSink(PathResolver.GetFullPath(@"Logs\DualSenseClient.log"))
                )
            );

            BuildInfo.LogStartupBanner(_log);
            RegisterGlobalExceptionHandlers();

            LocalizationService.LoadLanguage(settingsService.Settings.Ui.Language);

            ThemeService themeService = Services.GetRequiredService<ThemeService>();
            themeService.SetTheme(settingsService.Settings.Ui.Theme);

            MainWindow mainWindow = Services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
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
        };

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            args.Handled = true;
            _log.Error("Unhandled exception on UI thread");
            _log.LogExceptionDetails(args.Exception);
        };
    }
}