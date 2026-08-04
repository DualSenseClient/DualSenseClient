using System;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Controllers;
using DualSenseClient.GUI.ViewModels;
using DualSenseClient.GUI.ViewModels.Pages;
using DualSenseClient.GUI.Views;
using DualSenseClient.Hid;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Codecs.FFMpeg;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Provides centralized service configuration and registration for the application,
/// managing dependency injection container setup and service lifecycle management.
/// </summary>
public abstract class ServiceConfigurator
{
    /// <summary>
    /// Configures and registers all application services with the dependency injection container.
    /// </summary>
    /// <returns>An IServiceProvider instance with all configured services.</returns>
    public static IServiceProvider ConfigureServices()
    {
        DualSenseClientLogger log = DualSenseClientLogger.For("ServiceConfigurator");
        log.Debug("Registering application services");
        ServiceCollection services = new ServiceCollection();

        // Settings
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ProfileService>();

        // Services
        services.AddSingleton<IMessageBoxService, MessageBoxService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<NavigationService>();

        // Audio engine (SoundFlow/MiniAudio). One shared engine owns the WASAPI context,
        // the device list, and the codec registry; the FFmpeg codec adds decoding for
        // formats beyond the core wav/mp3/flac set.
        services.AddSingleton<AudioEngine>(_ =>
        {
            AudioEngine engine = new MiniAudioEngine();
            engine.RegisterCodecFactory(new FFmpegCodecFactory());
            return engine;
        });

        // Controllers
        services.AddSingleton<IHidDeviceEnumerator, HidDeviceEnumerator>();
        services.AddSingleton<IControllerScanner, ControllerScanner>();
        services.AddSingleton<IControllerTracker, ControllerTracker>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SettingsPageViewModel>();
        services.AddSingleton<DeviceInfoPageViewModel>();
        services.AddSingleton<InputMonitorPageViewModel>();
        services.AddSingleton<ProfilePageViewModel>();
        services.AddSingleton<MainViewModel>();

        // Views
        services.AddSingleton<MainWindow>();

        IServiceProvider provider = services.BuildServiceProvider();
        log.Info("Service provider built");
        return provider;
    }
}