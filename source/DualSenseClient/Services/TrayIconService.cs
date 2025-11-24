using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
using DualSenseClient.Core.DualSense;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Settings;
using DualSenseClient.Core.Settings.Models;
using DualSenseClient.ViewModels;

namespace DualSenseClient.Services;

public class TrayIconService : IDisposable
{
    private TrayIcon? _trayIcon;
    private readonly SelectedControllerService _selectedControllerService;
    private readonly DualSenseProfileManager _profileManager;
    private readonly ISettingsManager _settingsManager;
    private readonly List<ControllerViewModelBase> _controllers = new();
    private readonly TrayIconViewModel _viewModel;
    private bool _disposed = false;

    public TrayIconService(SelectedControllerService selectedControllerService, DualSenseProfileManager profileManager, ISettingsManager settingsManager)
    {
        _selectedControllerService = selectedControllerService;
        _profileManager = profileManager;
        _settingsManager = settingsManager;
        _viewModel = new TrayIconViewModel(ShowMainWindow, _selectedControllerService);

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

    private void OnSettingsChanged(object? sender, ApplicationSettingsStore settings)
    {
        if (_disposed)
        {
            return;
        }

        // Refresh tray icon when the battery tracking setting changes
        UpdateTrayIcon();
    }

    public void Initialize()
    {
        Logger.Info<TrayIconService>("Initializing tray icon service");

        try
        {
            _trayIcon = new TrayIcon();

            // Set the default icon
            string iconPath = "avares://DualSenseClient/Assets/icon.ico";
            _trayIcon.Icon = new WindowIcon(AssetLoader.Open(new Uri(iconPath)));

            // Update controllers list and set initial icon
            UpdateControllers();

            // Create context menu
            _trayIcon.Menu = CreateContextMenu();

            // TODO: Add direct left-click event or double-click to show the app

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

    private NativeMenu CreateContextMenu()
    {
        NativeMenu nativeMenu = new NativeMenu();

        // Show main window item
        NativeMenuItem showItem = new NativeMenuItem("Show")
        {
            Command = _viewModel.ShowMainWindowCommand
        };
        nativeMenu.Items.Add(showItem);

        // Add controller items if there are any controllers
        if (_controllers.Count != 0)
        {
            nativeMenu.Items.Add(new NativeMenuItemSeparator());

            foreach (ControllerViewModelBase controller in _controllers)
            {
                // Create submenu for each controller
                NativeMenu controllerSubMenu = new NativeMenu();

                // Main controller selection item
                NativeMenuItem selectControllerItem = new NativeMenuItem("Select")
                {
                    Command = new RelayCommand(() => SelectController(controller))
                };
                controllerSubMenu.Add(selectControllerItem);

                // Add profile submenu for the controller (for all controllers)
                NativeMenu profileSubMenu = new NativeMenu();

                // Add profile items
                foreach (ControllerProfile profile in _profileManager.GetAllProfiles().Values.OrderBy(p => p.Name))
                {
                    NativeMenuItem profileItem = new NativeMenuItem(profile.Name)
                    {
                        Command = new RelayCommand(() =>
                        {
                            // Apply the selected profile to the controller
                            _profileManager.AssignProfileToController(controller.ControllerId, profile.Id);
                            // Apply the profile settings to the controller immediately
                            ControllerProfile? profileToApply = _profileManager.GetProfile(profile.Id);
                            if (profileToApply != null)
                            {
                                _profileManager.ApplyProfileToController(controller.Controller, profileToApply);

                                // Trigger the profile changed event to notify UI elements
                                _profileManager.TriggerProfileChanged(controller.ControllerId, profileToApply);
                            }
                        })
                    };
                    profileSubMenu.Add(profileItem);
                }

                // Add profile submenu item
                NativeMenuItem profilesItem = new NativeMenuItem("Profiles")
                {
                    Menu = profileSubMenu
                };
                controllerSubMenu.Add(new NativeMenuItemSeparator());
                controllerSubMenu.Add(profilesItem);

                // Add disconnect option only if controller is connected via Bluetooth
                if (controller.ConnectionType == "Bluetooth")
                {
                    NativeMenuItem disconnectItem = new NativeMenuItem("Disconnect")
                    {
                        Command = new RelayCommand(() => _viewModel.DisconnectControllerCommand.Execute(controller))
                    };
                    controllerSubMenu.Add(disconnectItem);
                }

                // Add submenu as a parent item
                NativeMenuItem controllerParentItem = new NativeMenuItem($"{controller.Name} - {controller.BatteryText}")
                {
                    Menu = controllerSubMenu
                };
                nativeMenu.Items.Add(controllerParentItem);
            }
        }

        // Separator before exit
        nativeMenu.Items.Add(new NativeMenuItemSeparator());

        // Exit item
        NativeMenuItem exitItem = new NativeMenuItem("Exit")
        {
            Command = _viewModel.ExitApplicationCommand
        };
        nativeMenu.Items.Add(exitItem);

        return nativeMenu;
    }

    private void SelectController(ControllerViewModelBase controller)
    {
        _selectedControllerService.SelectedController = controller;
        ShowMainWindow();
    }

    // Method to update the context menu dynamically
    private void UpdateContextMenu()
    {
        if (_trayIcon?.Menu is not { } menu)
        {
            return;
        }
        try
        {
            // Clear and rebuild the menu items
            menu.Items.Clear();

            // Show main window item
            NativeMenuItem showItem = new NativeMenuItem("Show")
            {
                Command = _viewModel.ShowMainWindowCommand
            };
            menu.Items.Add(showItem);

            // Add controller items if there are any controllers
            if (_controllers.Count != 0)
            {
                menu.Items.Add(new NativeMenuItemSeparator());

                foreach (ControllerViewModelBase controller in _controllers)
                {
                    // Create submenu for each controller
                    NativeMenu controllerSubMenu = new NativeMenu();

                    // Main controller selection item
                    NativeMenuItem selectControllerItem = new NativeMenuItem("Select")
                    {
                        Command = new RelayCommand(() => SelectController(controller))
                    };
                    controllerSubMenu.Add(selectControllerItem);

                    // Add profile submenu for the controller (for all controllers)
                    NativeMenu profileSubMenu = new NativeMenu();

                    // Add profile items
                    foreach (ControllerProfile profile in _profileManager.GetAllProfiles().Values.OrderBy(p => p.Name))
                    {
                        NativeMenuItem profileItem = new NativeMenuItem(profile.Name)
                        {
                            Command = new RelayCommand(() =>
                            {
                                // Apply the selected profile to the controller
                                _profileManager.AssignProfileToController(controller.ControllerId, profile.Id);
                                // Apply the profile settings to the controller immediately
                                ControllerProfile? profileToApply = _profileManager.GetProfile(profile.Id);
                                if (profileToApply != null)
                                {
                                    _profileManager.ApplyProfileToController(controller.Controller, profileToApply);

                                    // Trigger the profile changed event to notify UI elements
                                    _profileManager.TriggerProfileChanged(controller.ControllerId, profileToApply);
                                }
                            })
                        };
                        profileSubMenu.Add(profileItem);
                    }

                    // Add profile submenu item
                    NativeMenuItem profilesItem = new NativeMenuItem("Profiles")
                    {
                        Menu = profileSubMenu
                    };
                    controllerSubMenu.Add(new NativeMenuItemSeparator());
                    controllerSubMenu.Add(profilesItem);

                    // Add disconnect option only if controller is connected via Bluetooth
                    if (controller.ConnectionType == "Bluetooth")
                    {
                        NativeMenuItem disconnectItem = new NativeMenuItem("Disconnect")
                        {
                            Command = new RelayCommand(() => _viewModel.DisconnectControllerCommand.Execute(controller))
                        };
                        controllerSubMenu.Add(disconnectItem);
                    }

                    // Add submenu as a parent item
                    NativeMenuItem controllerParentItem = new NativeMenuItem($"{controller.Name} - {controller.BatteryText}")
                    {
                        Menu = controllerSubMenu
                    };
                    menu.Items.Add(controllerParentItem);
                }
            }

            // Separator before exit
            menu.Items.Add(new NativeMenuItemSeparator());

            // Exit item
            NativeMenuItem exitItem = new NativeMenuItem("Exit")
            {
                Command = _viewModel.ExitApplicationCommand
            };
            menu.Items.Add(exitItem);
        }
        catch (Exception ex)
        {
            Logger.Error<TrayIconService>($"Failed to update context menu: {ex.Message}");
        }
    }

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
                    _trayIcon.Icon = GetIconFromBatteryLevel(selectedController);
                }
                else
                {
                    // Use default icon if battery tracking is disabled
                    string defaultIconPath = "avares://DualSenseClient/Assets/icon.ico";
                    _trayIcon.Icon = new WindowIcon(AssetLoader.Open(new Uri(defaultIconPath)));
                }
            }
            catch (Exception ex)
            {
                Logger.Error<TrayIconService>($"Failed to update tray icon: {ex.Message}");

                // Fallback to default icon
                try
                {
                    string defaultIconPath = "avares://DualSenseClient/Assets/icon.ico";
                    _trayIcon.Icon = new WindowIcon(AssetLoader.Open(new Uri(defaultIconPath)));
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
                string defaultIconPath = "avares://DualSenseClient/Assets/icon.ico";
                _trayIcon.Icon = new WindowIcon(AssetLoader.Open(new Uri(defaultIconPath)));
                _trayIcon.ToolTipText = "DualSense Client";
            }
            catch (Exception ex)
            {
                Logger.Error<TrayIconService>($"Failed to reset tray icon: {ex.Message}");
            }
        }
    }

    private WindowIcon GetIconFromBatteryLevel(ControllerViewModelBase controller)
    {
        // Create a 16x16 Tray Icon
        PixelSize pixelSize = new PixelSize(16, 16);
        RenderTargetBitmap renderTarget = new RenderTargetBitmap(pixelSize, new Vector(96, 96)); // 96 DPI

        using (DrawingContext context = renderTarget.CreateDrawingContext())
        {
            // Transparent background
            context.FillRectangle(Brushes.Transparent, new Rect(0, 0, 16, 16));

            // Determine the text color based on battery level/charging status
            Color textColor;
            if (controller.IsCharging)
            {
                textColor = Color.FromRgb(0, 123, 255); // Blue for charging
            }
            else
            {
                float level = Math.Clamp((float)controller.BatteryLevel / 100f, 0f, 1f);

                byte red = (byte)(255 * (1f - level));
                byte green = (byte)(255 * level);
                byte blue = 0;

                textColor = Color.FromRgb(red, green, blue);
            }

            // Draw the battery level text
            FormattedText text = new FormattedText(
                controller.BatteryLevel.ToString("F0"),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                14, // Font size
                new SolidColorBrush(textColor)
            );

            // Measure the text size manually
            Size textSize = new Size(text.Width, text.Height);
            Point textPosition = new Point(
                (16 - textSize.Width) / 2,
                (16 - textSize.Height) / 2 - 1 // Slightly adjust position
            );

            context.DrawText(text, textPosition);
        }

        using MemoryStream memoryStream = new MemoryStream();
        renderTarget.Save(memoryStream);
        memoryStream.Position = 0;
        return new WindowIcon(memoryStream);
    }

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

    public bool ShouldCloseToTray()
    {
        return _settingsManager.Application.Ui.CloseToTray;
    }

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