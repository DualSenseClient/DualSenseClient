using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.Logging;

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
    /// The controller currently shown on this page, or <c>null</c> when none is selected.
    /// </summary>
    public DeviceInfoItem? CurrentDevice { get; private set; }

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

        OnPropertyChanged(nameof(CurrentDevice));
        OnPropertyChanged(nameof(HasDevice));
    }
}