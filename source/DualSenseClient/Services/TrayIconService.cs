using System;
using System.Collections.Generic;
using Avalonia.Controls;
using DualSenseClient.Core.DualSense;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Settings;
using DualSenseClient.Helpers.TrayIcon;
using DualSenseClient.ViewModels;

namespace DualSenseClient.Services;

/// <summary>
/// Service responsible for managing the application's system tray icon,
/// including context menu, battery-level icons, and controller management.
/// </summary>
public class TrayIconService : IDisposable
{
    private Avalonia.Controls.TrayIcon? _trayIcon;
    private readonly SelectedControllerService _selectedControllerService;
    private readonly DualSenseProfileManager _profileManager;
    private readonly ISettingsManager _settingsManager;
    private readonly IHidHideService? _hidHideService;
    private readonly List<ControllerViewModelBase> _controllers = new List<ControllerViewModelBase>();
    private readonly TrayIconViewModel _viewModel;
    private readonly TrayMenuBuilder _menuBuilder;
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayIconService"/> class.
    /// </summary>
    /// <param name="selectedControllerService">Service for managing selected controllers</param>
    /// <param name="profileManager">Manager for handling controller profiles</param>
    /// <param name="settingsManager">Manager for application settings</param>
    /// <param name="hidHideService">Optional service for HidHide functionality (Windows only)</param>
    public TrayIconService(SelectedControllerService selectedControllerService, DualSenseProfileManager profileManager, ISettingsManager settingsManager, IHidHideService? hidHideService = null)
    {
        _selectedControllerService = selectedControllerService;
        _profileManager = profileManager;
        _settingsManager = settingsManager;
        _hidHideService = hidHideService;
        _viewModel = new TrayIconViewModel(ShowMainWindow, _selectedControllerService);

        // Initialize the menu builder with dependencies
        _menuBuilder = new TrayMenuBuilder(
            _selectedControllerService,
            _profileManager,
            _settingsManager,
            _viewModel,
            _hidHideService);

        // Subscribe to available controllers collection changes
        _selectedControllerService.AvailableControllers.CollectionChanged += (_, _) =>
        {
            UpdateControllers();
        };

        // Subscribe to controller changes (battery percentage going down/up, rename...)
        _selectedControllerService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedControllerService.SelectedController))
            {
                UpdateTrayIcon();
            }
        };

        // Subscribe to settings changes to update tray
        _settingsManager.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>
    /// Callback method called when application settings are changed.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="settings">Updated settings</param>
    private void OnSettingsChanged(object? sender, ApplicationSettingsStore settings)
    {
        if (_disposed)
        {
            return;
        }

        // Refresh tray icon when the battery tracking setting changes
        UpdateTrayIcon();
    }

    /// <summary>
    /// Initializes the tray icon service, setting up the tray icon, context menu, and event handlers.
    /// </summary>
    public void Initialize()
    {
        Logger.Info<TrayIconService>("Initializing tray icon service");

        try
        {
            _trayIcon = new Avalonia.Controls.TrayIcon();

            // Set the default icon
            _trayIcon.Icon = TrayIconHelper.LoadDefaultIcon();

            // Update controllers list and set initial icon
            UpdateControllers();

            // Create context menu
            _trayIcon.Menu = _menuBuilder.BuildMainMenu();

            // Add left-click event to show the main window
            _trayIcon.Clicked += (_, _) =>
            {
                ShowMainWindow();
            };

            // Show the tray icon
            _trayIcon.IsVisible = true;

            Logger.Info<TrayIconService>("Tray icon service initialized successfully");
        }
        catch (Exception ex)
        {
            Logger.Error<TrayIconService>($"Failed to initialize tray icon: {ex.Message}");
            Logger.LogExceptionDetails<TrayIconService>(ex);
        }
    }

    // Method to update the context menu dynamically
    private void UpdateContextMenu()
    {
        if (_trayIcon == null)
        {
            return;
        }
        try
        {
            // Create a completely new menu and assign it to the tray icon
            // This ensures the context menu remains properly associated with the tray icon
            NativeMenu newMenu = _menuBuilder.BuildMainMenu();
            _trayIcon.Menu = newMenu;
        }
        catch (Exception ex)
        {
            Logger.Error<TrayIconService>($"Failed to update context menu: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the internal list of controllers and refreshes the tray icon and context menu.
    /// This method handles subscribing/unsubscribing from controller property change events.
    /// </summary>
    private void UpdateControllers()
    {
        if (_disposed)
        {
            return;
        }

        // Clear current controllers and unsubscribe from events
        foreach (ControllerViewModelBase controller in _controllers)
        {
            controller.PropertyChanged -= OnControllerPropertyChanged;
        }
        _controllers.Clear();

        // Add all controllers from the service
        foreach (ControllerViewModelBase controller in _selectedControllerService.AvailableControllers)
        {
            _controllers.Add(controller);
            // Subscribe to controller property changes (Battery percentage, name change..)
            controller.PropertyChanged += OnControllerPropertyChanged;
        }

        UpdateTrayIcon();
        UpdateContextMenu();
    }

    /// <summary>
    /// Handles property change events from controller view models to update the tray icon and menu.
    /// This method is triggered when controller properties such as battery level, charging status,
    /// battery icon, battery text, or name change.
    /// </summary>
    /// <param name="sender">The controller view model that triggered the property change</param>
    /// <param name="e">Event arguments containing the name of the changed property</param>
    private void OnControllerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (e.PropertyName is nameof(ControllerViewModelBase.BatteryLevel) or nameof(ControllerViewModelBase.IsCharging) ||
            e.PropertyName == nameof(ControllerViewModelBase.BatteryIcon) ||
            e.PropertyName == nameof(ControllerViewModelBase.BatteryText) ||
            e.PropertyName == nameof(ControllerViewModelBase.Name))
        {
            // Update context menu to reflect new battery information
            UpdateTrayIcon();
            UpdateContextMenu();
        }
    }

    /// <summary>
    /// Updates the tray icon based on the currently selected controller's battery level.
    /// If tray battery tracking is enabled, creates a dynamic icon with battery percentage.
    /// Otherwise, uses the default application icon.
    /// </summary>
    private void UpdateTrayIcon()
    {
        if (_trayIcon == null || _disposed)
        {
            return;
        }

        ControllerViewModelBase? selectedController = _selectedControllerService.SelectedController;

        if (selectedController != null)
        {
            // Controller selected
            try
            {
                // Check if tray battery tracking is enabled
                if (_settingsManager.Application.Ui.TrayBatteryTracking)
                {
                    // Update tray icon based on battery level of selected controller
                    _trayIcon.Icon = TrayIconHelper.CreateBatteryIcon(selectedController);
                }
                else
                {
                    // Use default icon if battery tracking is disabled
                    _trayIcon.Icon = TrayIconHelper.LoadDefaultIcon();
                }
            }
            catch (Exception ex)
            {
                Logger.Error<TrayIconService>($"Failed to update tray icon: {ex.Message}");

                // Fallback to default icon
                try
                {
                    _trayIcon.Icon = TrayIconHelper.LoadDefaultIcon();
                    _trayIcon.ToolTipText = "DualSense Client";
                }
                catch
                {
                    /* Ignore - can't do much if fallback fails */
                }
            }
        }
        else
        {
            // No controller selected, show default icon
            try
            {
                _trayIcon.Icon = TrayIconHelper.LoadDefaultIcon();
                _trayIcon.ToolTipText = "DualSense Client";
            }
            catch (Exception ex)
            {
                Logger.Error<TrayIconService>($"Failed to reset tray icon: {ex.Message}");
            }
        }
    }


    /// <summary>
    /// Shows the main application window, restoring it if it was minimized.
    /// </summary>
    public void ShowMainWindow()
    {
        try
        {
            if (App.Desktop?.MainWindow == null)
            {
                return;
            }
            Window? mainWindow = App.Desktop.MainWindow;

            if (mainWindow == null)
            {
                return;
            }
            if (mainWindow.WindowState == WindowState.Minimized)
            {
                mainWindow.WindowState = WindowState.Normal;
            }

            mainWindow.Show();
            mainWindow.Activate();
            mainWindow.Focus();
        }
        catch (Exception ex)
        {
            Logger.Error<TrayIconService>($"Failed to show main window: {ex.Message}");
        }
    }

    /// <summary>
    /// Hides the main application window if the close-to-tray setting is enabled.
    /// </summary>
    public void HideMainWindow()
    {
        try
        {
            if (App.Desktop?.MainWindow == null)
            {
                return;
            }
            Window? mainWindow = App.Desktop.MainWindow;

            if (mainWindow == null)
            {
                return;
            }
            // Only hide if the close-to-tray setting is enabled
            if (_settingsManager.Application.Ui.CloseToTray)
            {
                mainWindow.Hide();
            }
        }
        catch (Exception ex)
        {
            Logger.Error<TrayIconService>($"Failed to hide main window: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if the application should close to tray based on user settings.
    /// </summary>
    /// <returns>True if the close-to-tray feature is enabled, false otherwise</returns>
    public bool ShouldCloseToTray()
    {
        return _settingsManager.Application.Ui.CloseToTray;
    }

    /// <summary>
    /// Shuts down the application.
    /// </summary>
    private void ExitApplication()
    {
        try
        {
            if (App.Desktop != null)
            {
                App.Desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Logger.Error<TrayIconService>($"Failed to exit application: {ex.Message}");
        }
    }

    /// <summary>
    /// Disposes of resources used by the tray icon service, including removing event subscriptions
    /// and disposing of the tray icon.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            // Unsubscribe from controller events
            foreach (ControllerViewModelBase controller in _controllers)
            {
                controller.PropertyChanged -= OnControllerPropertyChanged;
            }

            _controllers.Clear();

            // Hide and dispose tray icon
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            Logger.Info<TrayIconService>("Tray icon service disposed successfully");
        }
        catch (Exception ex)
        {
            Logger.Error<TrayIconService>($"Error during disposal: {ex.Message}");
        }
    }
}