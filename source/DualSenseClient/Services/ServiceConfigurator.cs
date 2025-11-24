using System;
using DualSenseClient.Core.DualSense;
using DualSenseClient.Core.DualSense.Actions;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Settings;
using DualSenseClient.Core.Settings.Models;
using DualSenseClient.ViewModels;
using DualSenseClient.ViewModels.Controls;
using DualSenseClient.ViewModels.Pages;
using DualSenseClient.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DualSenseClient.Services;

public static class ServiceConfigurator
{
    public static IServiceProvider ConfigureServices()
    {
        ServiceCollection services = new ServiceCollection();

        // Core
        services.AddSingleton<ISettingsManager, SettingsManager>();
        services.AddSingleton<DualSenseProfileManager>();
        services.AddSingleton<DualSenseManager>();
        services.AddSingleton<SelectedControllerService>();
        services.AddSingleton<SpecialActionService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<IApplicationSettings, ApplicationSettings>();
        services.AddSingleton<IProfileRenameService, ProfileRenameService>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<IAutoStartService, AutoStartService>();

        // Windows-specific services
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
#pragma warning disable CA1416
            services.AddSingleton<HidHideService>();
            services.AddSingleton<IHidHideService>(sp => sp.GetRequiredService<HidHideService>());
            services.AddSingleton<ViGEmBusService>();
            services.AddSingleton<IViGEmBusService>(sp => sp.GetRequiredService<ViGEmBusService>());
#pragma warning restore CA1416
        }
        else
        {
#pragma warning disable CA1416
            // Register a null implementation or a no-op implementation for non-Windows platforms
            services.AddSingleton<IHidHideService, NullHidHideService>();
            services.AddSingleton<IViGEmBusService, NullViGEmBusService>();
#pragma warning restore CA1416
        }

        services.AddSingleton<ThemeService>(provider =>
        {
            ThemeService themeService = new ThemeService();
            ISettingsManager settingsManager = provider.GetRequiredService<ISettingsManager>();
            try
            {
                ApplicationSettingsStore settings = settingsManager.Application;
                AppTheme savedTheme = settings.Ui.Theme;
                themeService.SetTheme(savedTheme);
                Logger.Info<ServiceProvider>($"Applied saved theme during service initialization: {{savedTheme}}");
            }
            catch (Exception ex)
            {
                Logger.Error<ServiceProvider>($"Failed to apply saved theme: {ex.Message}");
            }
            return themeService;
        });

        // ViewModels
        services.AddSingleton<ControllerSelectorViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HomePageViewModel>();
        services.AddSingleton<MonitorPageViewModel>();
        services.AddSingleton<ProfilePageViewModel>();
        services.AddSingleton<SettingsPageViewModel>();
        services.AddSingleton<DevicesPageViewModel>();
        services.AddSingleton<DebugPageViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();

        IServiceProvider serviceProvider = services.BuildServiceProvider();

        // Initialize the service locator with the services after the provider is built
        ISettingsManager settingsManager = serviceProvider.GetRequiredService<ISettingsManager>();
        string logLevel = settingsManager.Application.Debug.Logger.Level;
        Logger.Debug<App>($"Setting log level from settings: {logLevel}");
        Logger.SetLogLevel(LogLevelHelper.FromString(logLevel));

        // Initialize auto-start setting to match user preference
        IAutoStartService autoStartService = serviceProvider.GetRequiredService<IAutoStartService>();
        bool startOnLaunchSetting = serviceProvider.GetRequiredService<ISettingsManager>().Application.Ui.StartOnLaunch;
        autoStartService.SetAutoStart(startOnLaunchSetting);

        DualSenseManager dualSenseManager = serviceProvider.GetRequiredService<DualSenseManager>();
        DualSenseProfileManager profileManager = serviceProvider.GetRequiredService<DualSenseProfileManager>();
        SpecialActionService specialActionService = serviceProvider.GetRequiredService<SpecialActionService>();

        DualSenseServiceLocator.RegisterSettingsManager(settingsManager);
        DualSenseServiceLocator.RegisterDualSenseManager(dualSenseManager);
        DualSenseServiceLocator.RegisterProfileManager(profileManager);
        DualSenseServiceLocator.RegisterSpecialActionService(specialActionService);

        // Complete profile manager initialization after all services are registered
        profileManager.CompleteInitialization();

        // Complete dualsense manager initialization after all services are registered
        dualSenseManager.CompleteInitialization();

        // Initialize ThemeService
        _ = serviceProvider.GetRequiredService<ThemeService>();

        return serviceProvider;
    }
}