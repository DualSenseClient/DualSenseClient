using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.Logging;
using DualSenseClient.Settings;

namespace DualSenseClient.GUI.ViewModels.Pages;

/// <summary>
/// ViewModel for the device info page. Displays firmware and hardware information
/// for the controller currently selected in the title bar combobox.
/// </summary>
/// <remarks>
/// <para>
/// Resolves <see cref="MainViewModel"/> from the DI container and mirrors its
/// <see cref="MainViewModel.SelectedItem"/>, so the page always shows the controller
/// that is active in the shell. Navigating away and back creates a fresh page instance
/// (<c>CacheSize=0</c>), which re-subscribes to selection changes.
/// </para>
/// <para>
/// Firmware info is read once when the controller connects; <see cref="RefreshCommand"/>
/// re-reads it on demand (e.g. to retry a read that failed at connect time).
/// </para>
/// </remarks>
public partial class DeviceInfoPageViewModel : ObservableObject
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("DeviceInfoPage");

    /// <summary>
    /// The shell ViewModel owning the controller selection.
    /// </summary>
    private readonly MainViewModel _mainViewModel;

    /// <summary>
    /// Service storing persistent controller info, used to read and rename the custom
    /// display name of the selected controller.
    /// </summary>
    private readonly ControllerInfoService _controllerService;

    /// <summary>
    /// The controller currently shown on this page, or <c>null</c> when none is selected.
    /// </summary>
    public DeviceInfoItem? CurrentDevice { get; private set; }

    /// <summary>
    /// The user-visible controller name (custom name, or product name when none was set).
    /// Editable: assigning a new name renames the controller in
    /// <see cref="ControllerInfoService"/> and persists it.
    /// </summary>
    public string ControllerName
    {
        get => _controllerName;
        set
        {
            string trimmed = value?.Trim() ?? string.Empty;
            if (trimmed.Length > ControllerInfoService.MaxNameLength)
            {
                trimmed = trimmed[..ControllerInfoService.MaxNameLength];
            }

            if (string.Equals(trimmed, _controllerName, StringComparison.OrdinalIgnoreCase))
            {
                OnPropertyChanged();
                return;
            }

            _controllerName = trimmed;
            if (CurrentDevice is not null && !string.IsNullOrEmpty(trimmed))
            {
                bool renamed = _controllerService.RenameController(CurrentMac, CurrentDevicePath, trimmed);
                if (renamed)
                {
                    _log.Info($"Renamed controller '{CurrentMac}' to '{trimmed}'");
                }
                else
                {
                    _controllerName = _controllerService.GetDisplayName(CurrentMac, CurrentDevicePath, CurrentDevice.DisplayName);
                }
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Backing field for <see cref="ControllerName"/>.
    /// </summary>
    private string _controllerName = string.Empty;

    /// <summary>
    /// The Bluetooth MAC address of the selected controller, or empty when unavailable.
    /// </summary>
    private string CurrentMac => CurrentDevice?.Controller.PairingInfo?.ClientMac ?? string.Empty;

    /// <summary>
    /// The HID device path of the selected controller.
    /// </summary>
    private string CurrentDevicePath => CurrentDevice?.Controller.Device.Info.Path ?? string.Empty;

    /// <summary>
    /// Tracks the previous item so its event subscriptions are released on replacement.
    /// </summary>
    private DeviceInfoItem? _previousItem;

    /// <summary>
    /// Whether a controller is selected and its info can be displayed.
    /// </summary>
    public bool HasDevice => CurrentDevice is not null;

    /// <summary>
    /// Creates the page ViewModel and subscribes to the shell's controller selection.
    /// </summary>
    public DeviceInfoPageViewModel()
    {
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _controllerService = App.Services.GetRequiredService<ControllerInfoService>();
        _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        UpdateDevice();
    }

    /// <summary>
    /// Re-reads the selected controller's firmware info report and refreshes the display.
    /// </summary>
    [RelayCommand]
    private void Refresh()
    {
        ControllerItem? selected = _mainViewModel.SelectedItem;
        if (selected is null)
        {
            return;
        }

        selected.FirmwareInfo = FeatureReader.ReadFirmwareInfo(selected.Device);
        if (selected.FirmwareInfo is null)
        {
            _log.Warning("Firmware info refresh returned no data");
        }

        UpdateDevice();
    }

    /// <summary>
    /// Tracks the shell's controller selection.
    /// </summary>
    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedItem))
        {
            UpdateDevice();
        }
    }

    /// <summary>
    /// Rebuilds <see cref="CurrentDevice"/> from the shell's selected controller.
    /// Releases the previous item's event subscriptions before replacing it.
    /// </summary>
    private void UpdateDevice()
    {
        _previousItem?.Dispose();

        ControllerItem? selected = _mainViewModel.SelectedItem;
        CurrentDevice = selected is not null ? new DeviceInfoItem(selected) : null;
        _previousItem = CurrentDevice;
        _controllerName = selected is null
            ? string.Empty
            : _controllerService.GetDisplayName(CurrentMac, CurrentDevicePath, selected.DisplayName);

        OnPropertyChanged(nameof(CurrentDevice));
        OnPropertyChanged(nameof(HasDevice));
        OnPropertyChanged(nameof(ControllerName));
    }
}