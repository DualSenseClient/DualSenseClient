using System;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Bluetooth;
using DualSenseClient.Controllers;
using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Core.Foreground;
using DualSenseClient.GUI.ViewModels;
using DualSenseClient.GUI.ViewModels.Pages;
using DualSenseClient.GUI.Views;
using DualSenseClient.Hid;
using DualSenseClient.HidHide;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Emulation;
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
        services.AddSingleton<ControllerInfoService>();
        services.AddSingleton<SpecialActionService>();
        services.AddSingleton<AutoProfileService>();

        // Services
        services.AddSingleton<IMessageBoxService, MessageBoxService>();
        services.AddSingleton<INotificationPopupService, NotificationPopupService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<SpecialActionCoordinator>();
        services.AddSingleton<AutoProfileCoordinator>();
        services.AddSingleton<ControllerIllustrationService>();

        // Audio engine (SoundFlow/MiniAudio). One shared engine owns the WASAPI context,
        // the device list, and the codec registry; the FFmpeg codec adds decoding for
        // formats beyond the core wav/mp3/flac set.
        services.AddSingleton<AudioEngine>(_ =>
        {
            AudioEngine engine = new MiniAudioEngine();
            engine.RegisterCodecFactory(new FFmpegCodecFactory());
            return engine;
        });

        // Per-application audio: enumerates the apps currently producing audio and
        // resolves a stored executable path to a live process for process-loopback capture.
        services.AddSingleton<AudioApplicationService>();

        // Controllers
        services.AddSingleton<IHidDeviceEnumerator, HidDeviceEnumerator>();
        services.AddSingleton<IControllerScanner, ControllerScanner>();
        services.AddSingleton<IControllerTracker, ControllerTracker>();
        services.AddSingleton<IEmulationControllerFactory, EmulationControllerFactory>();

        // Emulation engine: one server for the engine's lifetime. The factory builds a fresh server
        // (never a reused one) with the app-logger bridge for the engine's own logs and the
        // default attach strategy (native usbip-win2 VHCI with the usbip.exe CLI as fallback).
        // TryAttach passes the bound endpoint (address and port) to the attach strategy, so
        // nothing else assumes the default port.
        services.AddSingleton<Func<EmulationServer>>(_ => () => new EmulationServer(new EmulationServerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 3242),
            Logger = new EmulationLoggerAdapter()
        }));
        services.AddSingleton<IEmulationService, EmulationService>();
        services.AddSingleton<SpecialActionEngineRegistry>();
        services.AddSingleton<IControllerHidingService, HidHideService>();

        // Platform
        services.AddSingleton<IForegroundAppProvider, WindowsForegroundAppProvider>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SettingsPageViewModel>();
        services.AddSingleton<DeviceInfoPageViewModel>();
        services.AddSingleton<CustomizationPageViewModel>();
        services.AddSingleton<EmulationPageViewModel>();
        services.AddSingleton<AutoProfilePageViewModel>();
        services.AddSingleton<MainViewModel>();

        // Views
        services.AddSingleton<MainWindow>();

        IServiceProvider provider = services.BuildServiceProvider();
        log.Info("Service provider built");
        return provider;
    }
}