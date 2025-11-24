using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSenseClient.Core.Logging;
using DualSenseClient.Services;

namespace DualSenseClient.ViewModels.Controls;

public partial class ControllerSelectorViewModel : ViewModelBase
{
    private readonly SelectedControllerService _selectedControllerService;
    private bool _isUpdating;

    public ObservableCollection<ControllerListItemViewModel> Controllers { get; } = new();

    [ObservableProperty] private bool _hasControllers;

    public ControllerSelectorViewModel(SelectedControllerService selectedControllerService)
    {
        Logger.Debug<ControllerSelectorViewModel>("Creating ControllerSelectorViewModel");

        _selectedControllerService = selectedControllerService;

        // Subscribe to changes
        _selectedControllerService.PropertyChanged += OnServicePropertyChanged;
        _selectedControllerService.AvailableControllers.CollectionChanged += OnControllersCollectionChanged;

        // Initialize
        UpdateControllersList();

        Logger.Debug<ControllerSelectorViewModel>("ControllerSelectorViewModel created successfully");
    }

    private void OnServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedControllerService.SelectedController))
        {
            Logger.Trace<ControllerSelectorViewModel>("Selected controller changed in service");
            UpdateSelectedStates();
        }
    }

    private void OnControllersCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        Logger.Debug<ControllerSelectorViewModel>("Controllers collection changed");
        UpdateControllersList();
    }

    private void UpdateControllersList()
    {
        Logger.Trace<ControllerSelectorViewModel>("Updating controllers list");
        _isUpdating = true;

        // Dispose old items
        foreach (ControllerListItemViewModel item in Controllers)
        {
            item.Dispose();
        }

        // Clear and rebuild
        Controllers.Clear();

        foreach (var controller in _selectedControllerService.AvailableControllers)
        {
            ControllerListItemViewModel item = new ControllerListItemViewModel(
                controller,
                controller == _selectedControllerService.SelectedController,
                _selectedControllerService);
            Controllers.Add(item);
        }

        HasControllers = Controllers.Count > 0;
        Logger.Debug<ControllerSelectorViewModel>($"Controllers list updated: {Controllers.Count} controller(s)");

        _isUpdating = false;
    }

    private void UpdateSelectedStates()
    {
        if (_isUpdating)
        {
            Logger.Trace<ControllerSelectorViewModel>("Skipping selection update (currently updating list)");
            return;
        }

        Logger.Trace<ControllerSelectorViewModel>("Updating selected states");

        foreach (ControllerListItemViewModel item in Controllers)
        {
            item.IsSelected = item.Controller == _selectedControllerService.SelectedController;
        }
    }

    [RelayCommand]
    private void SelectController(ControllerListItemViewModel item)
    {
        if (item != null)
        {
            Logger.Info<ControllerSelectorViewModel>($"Selecting controller: {item.Name}");
            _selectedControllerService.SelectController(item.Controller);
        }
        else
        {
            Logger.Warning<ControllerSelectorViewModel>("SelectController called with null item");
        }
    }
}

/// <summary>
/// Wrapper ViewModel for individual controller items in the list
/// </summary>
public partial class ControllerListItemViewModel : ObservableObject
{
    private readonly SelectedControllerService _selectedControllerService;
    private readonly HidHideService? _hidHideService;

    public ControllerViewModelBase Controller { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _editingName = string.Empty;

    // Proxy properties for easier binding
    public string Name => Controller.Name;
    public string ConnectionType => Controller.ConnectionType;
    public string ConnectionIcon => Controller.ConnectionIcon;
    public string MacAddress => Controller.MacAddress;
    public string BatteryText => Controller.BatteryText;
    public string BatteryIcon => Controller.BatteryIcon;
    public string ChargingIcon => Controller.ChargingIcon;
    public bool IsCharging => Controller.IsCharging;
    public double BatteryLevel => Controller.BatteryLevel;
    public bool IsBluetooth => Controller.ConnectionType == "Bluetooth";

    // Windows-specific properties
    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [SupportedOSPlatform("windows")]
    private bool IsHidHideSupported => IsWindows && _hidHideService?.IsInstalled == true;

    [SupportedOSPlatform("windows")]
    public bool IsHidHideReady => IsWindows && _hidHideService?.IsReady == true;

    public ControllerListItemViewModel(ControllerViewModelBase controller, bool isSelected, SelectedControllerService selectedControllerService)
    {
        Logger.Trace<ControllerListItemViewModel>($"Creating ControllerListItemViewModel for: {controller.Name}");

        Controller = controller;
        IsSelected = isSelected;
        _selectedControllerService = selectedControllerService;
        EditingName = controller.Name;

        // Initialize HidHide service on Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                _hidHideService = App.Services.GetService(typeof(HidHideService)) as HidHideService;
            }
            catch
            {
                // HidHide service might not be registered
                _hidHideService = null;
            }
        }

        // Subscribe to controller property changes to update proxy properties
        Controller.PropertyChanged += OnControllerPropertyChanged;
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Update relevant property
        switch (e.PropertyName)
        {
            case nameof(ControllerViewModelBase.Name):
                Logger.Trace<ControllerListItemViewModel>($"Controller name changed: {Controller.Name}");
                OnPropertyChanged(nameof(Name));
                EditingName = Controller.Name; // Keep editing name in sync
                break;
            case nameof(ControllerViewModelBase.BatteryText):
                OnPropertyChanged(nameof(BatteryText));
                break;
            case nameof(ControllerViewModelBase.BatteryIcon):
                OnPropertyChanged(nameof(BatteryIcon));
                break;
            case nameof(ControllerViewModelBase.ChargingIcon):
                OnPropertyChanged(nameof(ChargingIcon));
                break;
            case nameof(ControllerViewModelBase.IsCharging):
                OnPropertyChanged(nameof(IsCharging));
                break;
            case nameof(ControllerViewModelBase.BatteryLevel):
                OnPropertyChanged(nameof(BatteryLevel));
                break;
        }
    }

    [RelayCommand]
    private void StartRenaming()
    {
        Logger.Debug<ControllerListItemViewModel>($"Starting rename for controller: {Name}");
        EditingName = Controller.Name;
        IsRenaming = true;
    }

    [RelayCommand]
    private void SaveName()
    {
        if (string.IsNullOrWhiteSpace(EditingName))
        {
            Logger.Warning<ControllerListItemViewModel>("Cannot save empty controller name");
            CancelRenaming();
            return;
        }

        if (EditingName != Controller.Name)
        {
            Logger.Info<ControllerListItemViewModel>($"Renaming controller from '{Controller.Name}' to '{EditingName}'");
            _selectedControllerService.UpdateControllerName(Controller.ControllerId, EditingName);
        }
        else
        {
            Logger.Debug<ControllerListItemViewModel>("Name unchanged, cancelling rename");
        }

        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRenaming()
    {
        Logger.Debug<ControllerListItemViewModel>("Cancelled renaming");
        EditingName = Controller.Name;
        IsRenaming = false;
    }

    [RelayCommand]
    private async Task CopyMacAddress()
    {
        try
        {
            if (App.MainWindow?.Clipboard != null)
            {
                await App.MainWindow.Clipboard.SetTextAsync(MacAddress);
                Logger.Info<ControllerListItemViewModel>($"MAC address copied to clipboard: {MacAddress}");
            }
            else
            {
                Logger.Warning<ControllerListItemViewModel>("Cannot copy MAC address: Clipboard not available");
            }
        }
        catch (Exception ex)
        {
            Logger.Error<ControllerListItemViewModel>("Failed to copy MAC address");
            Logger.LogExceptionDetails<ControllerListItemViewModel>(ex, includeEnvironmentInfo: false);
        }
    }

    [RelayCommand]
    private void DisconnectController()
    {
        if (Controller.ConnectionType == "Bluetooth")
        {
            Logger.Info<ControllerListItemViewModel>($"Attempting to disconnect Bluetooth controller: {Name}");
            bool success = Controller.Controller.DisconnectBluetooth();

            if (success)
            {
                Logger.Info<ControllerListItemViewModel>($"Successfully disconnected Bluetooth controller: {Name}");
            }
            else
            {
                Logger.Warning<ControllerListItemViewModel>($"Failed to disconnect Bluetooth controller: {Name}");
            }
        }
        else
        {
            Logger.Warning<ControllerListItemViewModel>($"Cannot disconnect controller via Bluetooth: {Name} is connected via {Controller.ConnectionType}");
        }
    }

    [RelayCommand]
    [SupportedOSPlatform("windows")]
    private void HideController()
    {
        if (!IsHidHideSupported || _hidHideService == null)
        {
            Logger.Warning<ControllerListItemViewModel>("HidHide is not supported or not available on this system");
            return;
        }

        // Check if HidHide is ready (installed and running as admin)
        if (!_hidHideService.IsReady)
        {
            if (!_hidHideService.IsInstalled)
            {
                Logger.Warning<ControllerListItemViewModel>("HidHide is not installed");
            }
            else if (!_hidHideService.IsRunningAsAdmin())
            {
                Logger.Warning<ControllerListItemViewModel>("Application is not running as Administrator - HidHide requires elevated privileges");
            }
            return;
        }

        // Try to get the device instance ID from the controller
        string? deviceInstanceId = GetDeviceInstanceId();
        if (string.IsNullOrEmpty(deviceInstanceId))
        {
            Logger.Warning<ControllerListItemViewModel>("Could not get device instance ID for controller");
            return;
        }

        Logger.Info<ControllerListItemViewModel>($"Hiding controller with device ID: {deviceInstanceId}");
        bool success = _hidHideService.HideDevice(deviceInstanceId);

        if (success)
        {
            Logger.Info<ControllerListItemViewModel>($"Successfully hid controller: {Name}");
            // Optionally activate cloaking if not already active
            if (!_hidHideService.IsCloakingActive())
            {
                _hidHideService.SetCloakingState(true);
            }
        }
        else
        {
            Logger.Warning<ControllerListItemViewModel>($"Failed to hide controller: {Name}");
        }
    }

    [RelayCommand]
    [SupportedOSPlatform("windows")]
    private void UnhideController()
    {
        if (!IsHidHideSupported || _hidHideService == null)
        {
            Logger.Warning<ControllerListItemViewModel>("HidHide is not supported or not available on this system");
            return;
        }

        // Check if HidHide is ready (installed and running as admin)
        if (!_hidHideService.IsReady)
        {
            if (!_hidHideService.IsInstalled)
            {
                Logger.Warning<ControllerListItemViewModel>("HidHide is not installed");
            }
            else if (!_hidHideService.IsRunningAsAdmin())
            {
                Logger.Warning<ControllerListItemViewModel>("Application is not running as Administrator - HidHide requires elevated privileges");
            }
            return;
        }

        // Try to get the device instance ID from the controller
        string? deviceInstanceId = GetDeviceInstanceId();
        if (string.IsNullOrEmpty(deviceInstanceId))
        {
            Logger.Warning<ControllerListItemViewModel>("Could not get device instance ID for controller");
            return;
        }

        Logger.Info<ControllerListItemViewModel>($"Unhiding controller with device ID: {deviceInstanceId}");
        bool success = _hidHideService.UnhideDevice(deviceInstanceId);

        if (success)
        {
            Logger.Info<ControllerListItemViewModel>($"Successfully unhid controller: {Name}");
        }
        else
        {
            Logger.Warning<ControllerListItemViewModel>($"Failed to unhide controller: {Name}");
        }
    }

    [SupportedOSPlatform("windows")]
    private string? GetDeviceInstanceId()
    {
        // If HidHide service is not available, return null
        if (_hidHideService == null)
        {
            return null;
        }

        // Get the MAC address from the controller
        var controllerType = Controller.Controller.GetType();
        var macAddressProperty = controllerType.GetProperty("MacAddress");
        if (macAddressProperty != null)
        {
            string? macAddress = macAddressProperty.GetValue(Controller.Controller) as string;
            if (!string.IsNullOrEmpty(macAddress))
            {
                // Use the HidHideService to find the device instance ID by MAC address
                return _hidHideService.FindDeviceInstanceIdByMacAddress(macAddress);
            }
        }

        return null;
    }

    public void Dispose()
    {
        Logger.Trace<ControllerListItemViewModel>($"Disposing ControllerListItemViewModel for: {Name}");
        Controller.PropertyChanged -= OnControllerPropertyChanged;
    }
}