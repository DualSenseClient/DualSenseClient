using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using DualSenseClient.Core.DualSense;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Settings;
using DualSenseClient.Core.Settings.Models;
using DualSenseClient.Helpers;
using DualSenseClient.ViewModels;

namespace DualSenseClient.Services.Helpers;

/// <summary>
/// Builder class responsible for creating and managing the system tray context menu.
/// </summary>
public class TrayMenuBuilder
{
    private readonly SelectedControllerService _selectedControllerService;
    private readonly DualSenseProfileManager _profileManager;
    private readonly ISettingsManager _settingsManager;
    private readonly IHidHideService? _hidHideService;
    private readonly TrayIconViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayMenuBuilder"/> class.
    /// </summary>
    /// <param name="selectedControllerService">Service for managing selected controllers</param>
    /// <param name="profileManager">Manager for handling controller profiles</param>
    /// <param name="settingsManager">Manager for application settings</param>
    /// <param name="viewModel">ViewModel for tray icon functionality</param>
    /// <param name="hidHideService">Optional service for HidHide functionality (Windows only)</param>
    public TrayMenuBuilder(
        SelectedControllerService selectedControllerService,
        DualSenseProfileManager profileManager,
        ISettingsManager settingsManager,
        TrayIconViewModel viewModel,
        IHidHideService? hidHideService = null)
    {
        _selectedControllerService = selectedControllerService;
        _profileManager = profileManager;
        _settingsManager = settingsManager;
        _viewModel = viewModel;
        _hidHideService = hidHideService;
    }

    public NativeMenu BuildMainMenu()
    {
        NativeMenu nativeMenu = [];

        try
        {
            // Add main application items
            AddMainApplicationItems(nativeMenu);

            // Add controller items if there are any controllers
            AddControllerItems(nativeMenu);

            // Add exit item
            AddExitItem(nativeMenu);
        }
        catch (Exception ex)
        {
            Logger.Error<TrayMenuBuilder>($"Failed to build main menu: {ex.Message}");
            Logger.LogExceptionDetails<TrayMenuBuilder>(ex);
        }

        return nativeMenu;
    }

    private void AddMainApplicationItems(NativeMenu menu)
    {
        try
        {
            NativeMenuItem showItem = new NativeMenuItem("Show")
            {
                Command = _viewModel.ShowMainWindowCommand
            };
            menu.Items.Add(showItem);
        }
        catch (Exception ex)
        {
            Logger.Error<TrayMenuBuilder>($"Failed to add main application items: {ex.Message}");
            Logger.LogExceptionDetails<TrayMenuBuilder>(ex);
        }
    }

    private void AddControllerItems(NativeMenu menu)
    {
        try
        {
            if (!_selectedControllerService.AvailableControllers.Any())
            {
                return;
            }

            menu.Items.Add(new NativeMenuItemSeparator());

            foreach (ControllerViewModelBase controller in _selectedControllerService.AvailableControllers)
            {
                NativeMenu controllerMenu = BuildControllerMenu(controller);
                NativeMenuItem controllerParentItem = new NativeMenuItem($"{controller.Name} - {controller.BatteryText}")
                {
                    Menu = controllerMenu
                };
                menu.Items.Add(controllerParentItem);
            }
        }
        catch (Exception ex)
        {
            Logger.Error<TrayMenuBuilder>($"Failed to add controller items: {ex.Message}");
            Logger.LogExceptionDetails<TrayMenuBuilder>(ex);
        }
    }

    private NativeMenu BuildControllerMenu(ControllerViewModelBase controller)
    {
        NativeMenu controllerMenu = new NativeMenu();

        try
        {
            // Add controller selection item
            AddControllerSelectionItem(controllerMenu, controller);

            // Add profile submenu
            AddProfileSubmenu(controllerMenu, controller);

            // Add HidHide submenu for Windows platforms
            AddHidHideSubmenu(controllerMenu, controller);

            // Add disconnect option for Bluetooth controllers
            AddDisconnectOption(controllerMenu, controller);
        }
        catch (Exception ex)
        {
            Logger.Error<TrayMenuBuilder>($"Failed to build controller menu for {controller.Name}: {ex.Message}");
            Logger.LogExceptionDetails<TrayMenuBuilder>(ex);
        }

        return controllerMenu;
    }

    private void AddControllerSelectionItem(NativeMenu controllerMenu, ControllerViewModelBase controller)
    {
        try
        {
            NativeMenuItem selectControllerItem = new NativeMenuItem("Select")
            {
                Command = new RelayCommand(() => SelectController(controller))
            };
            controllerMenu.Add(selectControllerItem);
        }
        catch (Exception ex)
        {
            Logger.Error<TrayMenuBuilder>($"Failed to add controller selection item for {controller.Name}: {ex.Message}");
            Logger.LogExceptionDetails<TrayMenuBuilder>(ex);
        }
    }

    private void AddProfileSubmenu(NativeMenu controllerMenu, ControllerViewModelBase controller)
    {
        NativeMenu profileSubMenu = new NativeMenu();

        try
        {
            var profiles = _profileManager.GetAllProfiles().Values.OrderBy(p => p.Name);
            foreach (ControllerProfile profile in profiles)
            {
                NativeMenuItem profileItem = CreateProfileMenuItem(profile, controller);
                profileSubMenu.Add(profileItem);
            }

            NativeMenuItem profilesItem = new NativeMenuItem("Profiles")
            {
                Menu = profileSubMenu
            };
            controllerMenu.Add(new NativeMenuItemSeparator());
            controllerMenu.Add(profilesItem);
        }
        catch (Exception ex)
        {
            Logger.Error<TrayMenuBuilder>($"Failed to add profile submenu for {controller.Name}: {ex.Message}");
            Logger.LogExceptionDetails<TrayMenuBuilder>(ex);
        }
    }

    private NativeMenuItem CreateProfileMenuItem(ControllerProfile profile, ControllerViewModelBase controller)
    {
        return new NativeMenuItem(profile.Name)
        {
            Command = new RelayCommand(() =>
            {
                ApplyProfileToController(controller, profile);
            })
        };
    }

    private void ApplyProfileToController(ControllerViewModelBase controller, ControllerProfile profile)
    {
        try
        {
            // Apply the selected profile to the controller
            _profileManager.AssignProfileToController(controller.ControllerId, profile.Id);
            // Apply the profile settings to the controller immediately
            ControllerProfile? profileToApply = _profileManager.GetProfile(profile.Id);
            if (profileToApply == null)
            {
                return;
            }
            _profileManager.ApplyProfileToController(controller.Controller, profileToApply);

            // Trigger the profile changed event to notify UI elements
            _profileManager.TriggerProfileChanged(controller.ControllerId, profileToApply);
        }
        catch (Exception ex)
        {
            Logger.Error<TrayMenuBuilder>($"Failed to apply profile '{profile.Name}' to controller '{controller.Name}': {ex.Message}");
            Logger.LogExceptionDetails<TrayMenuBuilder>(ex);
        }
    }

    private void AddHidHideSubmenu(NativeMenu controllerMenu, ControllerViewModelBase controller)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            if (_hidHideService == null || !_hidHideService.IsInstalled)
            {
                // Optionally log that HidHide is not available
                return;
            }

            NativeMenu hidHideSubMenu = new NativeMenu();

            // Add hide controller option
            NativeMenuItem hideItem = new NativeMenuItem("Hide Controller")
            {
                Command = new RelayCommand(() =>
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        HideController(controller);
                    }
                }),
                IsEnabled = _hidHideService.IsReady // Only enabled if HidHide is ready (installed + running as admin)
            };
            hidHideSubMenu.Add(hideItem);

            // Add unhide controller option
            NativeMenuItem unhideItem = new NativeMenuItem("Unhide Controller")
            {
                Command = new RelayCommand(() =>
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        UnhideController(controller);
                    }
                }),
                IsEnabled = _hidHideService.IsReady // Only enabled if HidHide is ready (installed + running as admin)
            };
            hidHideSubMenu.Add(unhideItem);

            // Add HidHide submenu item
            NativeMenuItem hidHideItem = new NativeMenuItem("HidHide")
            {
                Menu = hidHideSubMenu,
                IsEnabled = _hidHideService.IsReady // Only enabled if HidHide is ready
            };
            controllerMenu.Add(new NativeMenuItemSeparator());
            controllerMenu.Add(hidHideItem);
        }
        catch (Exception ex)
        {
            Logger.Error<TrayMenuBuilder>($"Failed to add HidHide submenu for {controller.Name}: {ex.Message}");
            Logger.LogExceptionDetails<TrayMenuBuilder>(ex);
        }
    }

    private void HideController(ControllerViewModelBase controller)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }
        
        if (_hidHideService != null)
        {
            HidHideHelper.HideController(_hidHideService, controller);
        }
    }

    private void UnhideController(ControllerViewModelBase controller)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }
        
        if (_hidHideService != null)
        {
            HidHideHelper.UnhideController(_hidHideService, controller);
        }
    }

    private void AddDisconnectOption(NativeMenu controllerMenu, ControllerViewModelBase controller)
    {
        try
        {
            if (controller.ConnectionType == "Bluetooth")
            {
                NativeMenuItem disconnectItem = new NativeMenuItem("Disconnect")
                {
                    Command = new RelayCommand(() => _viewModel.DisconnectControllerCommand.Execute(controller))
                };
                controllerMenu.Add(disconnectItem);
            }
        }
        catch (Exception ex)
        {
            Logger.Error<TrayMenuBuilder>($"Failed to add disconnect option for {controller.Name}: {ex.Message}");
            Logger.LogExceptionDetails<TrayMenuBuilder>(ex);
        }
    }

    private void AddExitItem(NativeMenu menu)
    {
        try
        {
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
            Logger.Error<TrayMenuBuilder>($"Failed to add exit item: {ex.Message}");
            Logger.LogExceptionDetails<TrayMenuBuilder>(ex);
        }
    }

    private void SelectController(ControllerViewModelBase controller)
    {
        try
        {
            _selectedControllerService.SelectedController = controller;
            _viewModel.ShowMainWindowCommand.Execute(null);
        }
        catch (Exception ex)
        {
            Logger.Error<TrayMenuBuilder>($"Failed to select controller '{controller.Name}': {ex.Message}");
            Logger.LogExceptionDetails<TrayMenuBuilder>(ex);
        }
    }
}