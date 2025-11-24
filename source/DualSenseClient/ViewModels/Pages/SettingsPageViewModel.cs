using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Settings;
using DualSenseClient.Core.Settings.Models;
using DualSenseClient.Services;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using Logger = DualSenseClient.Core.Logging.Logger;

namespace DualSenseClient.ViewModels.Pages;

public partial class SettingsPageViewModel : ViewModelBase
{
    public class ThemeItem
    {
        public AppTheme Theme { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }

    public class LogLevelItem
    {
        public LogLevel Level { get; set; } = LogLevel.Info;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    // Properties
    private readonly ISettingsManager _settingsManager;
    private readonly ThemeService _themeService;
    private readonly IHidHideService _hidHideService;
    private readonly IViGEmBusService _viGEmBusService;

    public ObservableCollection<ThemeItem> AvailableThemes { get; } = [];
    [ObservableProperty] private ThemeItem? selectedTheme;

    public ObservableCollection<LogLevelItem> AvailableLogLevels { get; } = [];
    [ObservableProperty] private LogLevelItem? selectedLogLevel;

    [ObservableProperty] private bool closeToTray;

    [ObservableProperty] private bool startMinimized;

    [ObservableProperty] private bool startOnLaunch;

    [ObservableProperty] private bool trayBatteryTracking;

    [ObservableProperty] private bool isViGEMBusInstalled = false;

    [ObservableProperty] private bool isHidHideInstalled = false;

    [ObservableProperty] private bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

    public string ApplicationVersion => _settingsManager.Application.GetVersion();

    // Constructor
    public SettingsPageViewModel(ISettingsManager settingsManager, ThemeService themeService, IHidHideService hidHideService, IViGEmBusService viGEmBusService)
    {
        _settingsManager = settingsManager;
        _themeService = themeService;
        _hidHideService = hidHideService;
#pragma warning disable CA1416 // Platform-specific services are registered based on platform
        _viGEmBusService = viGEmBusService;
#pragma warning restore CA1416

        InitializeThemes();
        InitializeLogLevels();
        InitializeDriverStatus();

        ApplySettings(_settingsManager.Application);
        _settingsManager.SettingsChanged += OnSettingsChanged;
    }

    // Functions
    private void InitializeThemes()
    {
        // Add available themes from the ThemeService
        foreach (AppTheme theme in _themeService.GetAvailableThemes())
        {
            AvailableThemes.Add(new ThemeItem
            {
                Theme = theme,
                DisplayName = theme.ToString()
            });
        }
    }

    private void InitializeLogLevels()
    {
        AvailableLogLevels.Add(new LogLevelItem
        {
            Level = LogLevel.Trace,
            DisplayName = "Trace",
            Description = "Most detailed logging, includes all messages"
        });

        AvailableLogLevels.Add(new LogLevelItem
        {
            Level = LogLevel.Debug,
            DisplayName = "Debug",
            Description = "Detailed debugging information"
        });

        AvailableLogLevels.Add(new LogLevelItem
        {
            Level = LogLevel.Info,
            DisplayName = "Info",
            Description = "General informational messages"
        });

        AvailableLogLevels.Add(new LogLevelItem
        {
            Level = LogLevel.Warn,
            DisplayName = "Warning",
            Description = "Warning messages and recoverable errors"
        });

        AvailableLogLevels.Add(new LogLevelItem
        {
            Level = LogLevel.Error,
            DisplayName = "Error",
            Description = "Error messages only"
        });

        AvailableLogLevels.Add(new LogLevelItem
        {
            Level = LogLevel.Fatal,
            DisplayName = "Fatal",
            Description = "Only critical/fatal errors"
        });

        AvailableLogLevels.Add(new LogLevelItem
        {
            Level = LogLevel.Off,
            DisplayName = "Off",
            Description = "Disable logging completely"
        });
    }

    private void InitializeDriverStatus()
    {
        // Initialize HidHide status (Windows-specific)
#pragma warning disable CA1416 // Platform compatibility is checked above
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            IsHidHideInstalled = _hidHideService.IsInstalled;
            IsViGEMBusInstalled = _viGEmBusService.IsViGEMBusInstalled;
        }
        else
        {
            // On non-Windows platforms, the services will be null implementations returning false
            IsHidHideInstalled = _hidHideService.IsInstalled;
            IsViGEMBusInstalled = _viGEmBusService.IsViGEMBusInstalled;
        }
#pragma warning restore CA1416
    }

    private void ApplySettings(ApplicationSettingsStore settings)
    {
        try
        {
            // Theme settings
            SelectedTheme = AvailableThemes.FirstOrDefault(t => t.Theme == settings.Ui.Theme);

            // Close to tray settings
            CloseToTray = settings.Ui.CloseToTray;

            // Start minimized settings
            StartMinimized = settings.Ui.StartMinimized;

            // Start on launch settings
            StartOnLaunch = settings.Ui.StartOnLaunch;

            // Tray battery tracking settings
            TrayBatteryTracking = settings.Ui.TrayBatteryTracking;

            // Debug settings
            LogLevel logLevel = LogLevelHelper.FromString(settings.Debug.Logger.Level);
            SelectedLogLevel = AvailableLogLevels.FirstOrDefault(level => level.Level == logLevel);

            Logger.Info<SettingsPageViewModel>("Settings loaded");
            Logger.Info<SettingsPageViewModel>($"Theme: {settings.Ui.Theme}");
            Logger.Info<SettingsPageViewModel>($"Close to Tray: {settings.Ui.CloseToTray}");
            Logger.Info<SettingsPageViewModel>($"Start Minimized: {settings.Ui.StartMinimized}");
            Logger.Info<SettingsPageViewModel>($"Start on Launch: {settings.Ui.StartOnLaunch}");
            Logger.Info<SettingsPageViewModel>($"Tray Battery Tracking: {settings.Ui.TrayBatteryTracking}");
            Logger.Info<SettingsPageViewModel>($"Log Level: {settings.Debug.Logger.Level}");
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private void OnSettingsChanged(object? sender, ApplicationSettingsStore settings)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplySettings(settings);
        });
    }

    partial void OnSelectedThemeChanged(ThemeItem? value)
    {
        if (value == null)
        {
            return;
        }
        _settingsManager.Application.Ui.Theme = value.Theme;
        _themeService.SetTheme(value.Theme);
        SaveSettings();
        Logger.Info<SettingsPageViewModel>($"Theme changed to: {value.DisplayName}");
    }

    partial void OnSelectedLogLevelChanged(LogLevelItem? value)
    {
        if (value != null)
        {
            _settingsManager.Application.Debug.Logger.Level = LogLevelHelper.ToString(value.Level);
            Logger.SetLogLevel(value.Level);
            SaveSettings();
            Logger.Info<SettingsPageViewModel>($"Log level changed to: {value.DisplayName}");
        }
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        _settingsManager.Application.Ui.CloseToTray = value;
        SaveSettings();
        Logger.Info<SettingsPageViewModel>($"Close to tray setting changed to: {value}");
    }

    partial void OnStartMinimizedChanged(bool value)
    {
        _settingsManager.Application.Ui.StartMinimized = value;
        SaveSettings();
        Logger.Info<SettingsPageViewModel>($"Start minimized setting changed to: {value}");
    }

    partial void OnStartOnLaunchChanged(bool value)
    {
        _settingsManager.Application.Ui.StartOnLaunch = value;
        SaveSettings();
        Logger.Info<SettingsPageViewModel>($"Start on launch setting changed to: {value}");

        // Also update the system auto-start setting
        try
        {
            IAutoStartService autoStartService = App.Services.GetRequiredService<IAutoStartService>();
            autoStartService.SetAutoStart(value);
            Logger.Info<SettingsPageViewModel>($"System auto-start setting updated to: {value}");
        }
        catch (Exception ex)
        {
            Logger.Error<SettingsPageViewModel>("Failed to update system auto-start setting");
            Logger.LogExceptionDetails<SettingsPageViewModel>(ex);
        }
    }

    partial void OnTrayBatteryTrackingChanged(bool value)
    {
        _settingsManager.Application.Ui.TrayBatteryTracking = value;
        SaveSettings();
        Logger.Info<SettingsPageViewModel>($"Tray battery tracking setting changed to: {value}");
    }

    private void SaveSettings()
    {
        try
        {
            _settingsManager.SaveAll();
            Logger.Info<SettingsPageViewModel>("Settings saved successfully");
        }
        catch (Exception ex)
        {
            Logger.Error<SettingsPageViewModel>("Failed to save settings");
            Logger.LogExceptionDetails<SettingsPageViewModel>(ex);
        }
    }
}