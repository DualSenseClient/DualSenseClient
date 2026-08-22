using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.GUI.Services;
using DualSenseClient.Hid;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.ViewModels;

/// <summary>
/// ViewModel for the main shell view (<see cref="Views.MainView"/>).
/// Owns the controller scanning lifecycle, the list of connected controllers shown in the
/// title bar combobox, and the selection that drives <see cref="IControllerTracker"/>.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("MainViewModel");

    /// <summary>
    /// Scanner used to discover connected controllers and watch for connection changes.
    /// </summary>
    private readonly IControllerScanner _scanner;

    /// <summary>
    /// Tracks the currently selected controller.
    /// </summary>
    private readonly IControllerTracker _tracker;

    /// <summary>
    /// Notification service used to surface connect/disconnect events.
    /// </summary>
    private readonly INotificationService _notifications;

    /// <summary>
    /// Profile service used to look up profiles by name.
    /// </summary>
    private readonly ProfileService _profileService;

    /// <summary>
    /// Service storing persistent controller info (custom name, MAC address, device path,
    /// and bound profile), used to register controllers and apply their bound profiles.
    /// </summary>
    private readonly ControllerInfoService _controllerService;

    /// <summary>
    /// Controllers discovered while scanning, shown in the title bar combobox.
    /// </summary>
    public ObservableCollection<ControllerItem> Controllers { get; } = new ObservableCollection<ControllerItem>();

    /// <summary>
    /// The controller selected in the title bar combobox.
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanDisconnectController))]
    private ControllerItem? _selectedItem;

    /// <summary>
    /// Whether the disconnect button should be shown: the selected controller
    /// is connected over Bluetooth.
    /// </summary>
    public bool CanDisconnectController => SelectedItem?.Device.ConnectionType == ConnectionType.Bluetooth;

    /// <summary>
    /// Whether the scanner is currently watching for controller connection changes.
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ScanButtonToolTip))]
    private bool _isScanning;

    /// <summary>
    /// Tooltip for the scan toggle button, describing the next action.
    /// </summary>
    public string ScanButtonToolTip => IsScanning
        ? LocalizationService.GetText("MainWindow.ScanDevices.Stop")
        : LocalizationService.GetText("MainWindow.ScanDevices.Start");

    /// <summary>
    /// Guards against feedback loops between the combobox selection and the tracker.
    /// </summary>
    private bool _isUpdatingSelected;

    /// <summary>
    /// Guards against overlapping scans, e.g. a double-click on the scan toggle.
    /// </summary>
    private bool _isScanningInProgress;

    /// <summary>
    /// Creates a new <see cref="MainViewModel"/> wired to the scanner, tracker, and notification service.
    /// </summary>
    /// <param name="scanner">Scanner used to discover and watch controllers.</param>
    /// <param name="tracker">Tracker that owns the selected controller.</param>
    /// <param name="notifications">Notification service for connect/disconnect events.</param>
    /// <param name="profileService">Profile service used to look up profiles by name.</param>
    /// <param name="controllerService">Service storing persistent controller info and profile bindings.</param>
    public MainViewModel(IControllerScanner scanner, IControllerTracker tracker, INotificationService notifications, ProfileService profileService,
        ControllerInfoService controllerService)
    {
        _scanner = scanner;
        _tracker = tracker;
        _notifications = notifications;
        _profileService = profileService;
        _controllerService = controllerService;
        _tracker.ActiveControllerChanged += OnActiveControllerChanged;
        _scanner.ControllerConnected += OnControllerConnected;
        _scanner.ControllerDisconnected += OnControllerDisconnected;
        _controllerService.ControllersChanged += OnControllersChanged;
    }

    /// <summary>
    /// Starts or stops controller scanning depending on the current state.
    /// </summary>
    [RelayCommand]
    private async Task ToggleScanning()
    {
        if (!IsScanning)
        {
            await InitializeScanningAsync(CancellationToken.None);
        }
        else
        {
            StopScanning();
        }
    }

    /// <summary>
    /// Manually disconnects the selected Bluetooth controller. The device stays paired;
    /// the watcher removes it from the controller list once the connection drops.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectController()
    {
        IControllerDevice? device = SelectedItem?.Device;
        if (device is null)
        {
            return;
        }

        bool disconnected = await Task.Run(device.DisconnectController);
        if (disconnected)
        {
            _notifications.ShowSuccess(string.Format(LocalizationService.GetText("MainWindow.DisconnectController.Success"), device.Info.ProductName), 3);
        }
        else
        {
            _notifications.ShowWarning(string.Format(LocalizationService.GetText("MainWindow.DisconnectController.Failed"), device.Info.ProductName), 3);
        }
    }

    /// <summary>
    /// Performs an initial scan of connected controllers and starts watching for connection changes.
    /// The HID enumeration (device opens and feature report reads) runs on a background thread;
    /// controller list updates are marshaled to the UI thread.
    /// </summary>
    /// <param name="token">A cancellation token to cancel the scan.</param>
    public async Task InitializeScanningAsync(CancellationToken token)
    {
        if (_isScanningInProgress)
        {
            return;
        }

        _log.Debug("Starting controller scanning");
        _isScanningInProgress = true;
        try
        {
            IReadOnlyList<IControllerDevice> devices = await Task.Run(() => _scanner.Scan(), token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested)
                {
                    foreach (IControllerDevice device in devices)
                    {
                        device.Dispose();
                    }

                    return;
                }

                foreach (IControllerDevice controller in devices)
                {
                    AddController(controller);
                }

                _log.Info($"Found {Controllers.Count} controller(s) on initial scan");

                string? activePath = _tracker.ActiveController?.Info.Path;
                if (activePath is not null)
                {
                    foreach (ControllerItem item in Controllers)
                    {
                        if (item.Device.Info.Path == activePath)
                        {
                            SelectedItem = item;
                            break;
                        }
                    }
                }

                // Make the first scanned controller the active one when nothing is selected yet.
                if (SelectedItem is null && Controllers.Count > 0)
                {
                    SelectedItem = Controllers[0];
                }

                _scanner.StartWatching();
                IsScanning = true;
            });
        }
        finally
        {
            _isScanningInProgress = false;
        }
    }

    /// <summary>
    /// Stops watching, disposes the tracked controllers, and clears the selection.
    /// </summary>
    private void StopScanning()
    {
        _log.Debug("Stopping controller scanning");

        _scanner.StopWatching();

        foreach (ControllerItem item in Controllers)
        {
            _tracker.UntrackController(item.Device);
            item.Device.Dispose();
        }

        Controllers.Clear();

        _tracker.SelectController(null);
        IsScanning = false;
    }

    /// <summary>
    /// Registers a connected controller with the controller info service (so it can be
    /// renamed and assigned a profile), adds it to the list under its stored display name,
    /// tracks it with the tracker, and applies its bound profile. Called from the UI thread.
    /// </summary>
    /// <param name="controller">The controller that just connected.</param>
    private void AddController(IControllerDevice controller)
    {
        string? mac = (controller as DualSenseDevice)?.PairingInfo?.ClientMac;
        string path = controller.Info.Path;
        _controllerService.RegisterController(mac, path, controller.Info.ProductName);
        string displayName = _controllerService.GetDisplayName(mac, path, controller.Info.ProductName);

        Controllers.Add(new ControllerItem(controller, displayName));
        _tracker.TrackController(controller);
        ApplyBoundProfile(controller);
    }

    /// <summary>
    /// Looks up the profile used by a connected controller (the profile bound to its MAC
    /// address, falling back to its HID device path, then to the default profile) and
    /// applies it. Profiles are applied here rather than in the controller device itself.
    /// </summary>
    /// <param name="controller">The controller that just connected.</param>
    private void ApplyBoundProfile(IControllerDevice controller)
    {
        if (controller is not DualSenseDevice device)
        {
            return;
        }

        string? mac = device.PairingInfo?.ClientMac;
        string? path = device.Info.Path;
        string? profileName = _controllerService.GetBoundProfileName(mac, path) ?? ProfileService.DefaultProfileName;
        Profile? profile = _profileService.GetProfile(profileName);
        if (profile is null)
        {
            return;
        }

        _log.Info($"Applying profile '{profile.Name}' to {device.Info.ProductName}");
        device.ApplyProfile(profile);
    }

    /// <summary>
    /// Forwards a combobox selection change to the tracker. The previously selected
    /// device stays tracked; only the selection moves.
    /// </summary>
    /// <param name="value">The newly selected controller item, or <c>null</c>.</param>
    partial void OnSelectedItemChanged(ControllerItem? value)
    {
        if (_isUpdatingSelected)
        {
            return;
        }

        _isUpdatingSelected = true;
        _tracker.SelectController(value?.Device);
        _isUpdatingSelected = false;
    }

    /// <summary>
    /// Adds a newly connected controller to the list and surfaces a notification.
    /// Dispatched to the UI thread because the scanner raises events from a background thread.
    /// </summary>
    private void OnControllerConnected(object? sender, ControllerConnectionEventArgs e)
    {
        if (e.Controller is null)
        {
            return;
        }

        IControllerDevice controller = e.Controller;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsScanning)
            {
                controller.Dispose();
                return;
            }

            AddController(controller);
            _notifications.ShowSuccess($"{controller.ConnectionType} controller connected: {controller.Info.ProductName}", 3);
            _log.Info($"Controller connected: {controller.Info.ProductName}");

            SelectedItem ??= Controllers[^1];
            // Fix for Steam applying their own color profile when the device connects
            // TODO: Find a better solution
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                ApplyBoundProfile(controller);
            });
        });
    }

    /// <summary>
    /// Synchronizes the combobox selection with the tracker's active controller.
    /// Dispatched to the UI thread because the tracker may raise the event from a background thread.
    /// </summary>
    private void OnActiveControllerChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IControllerDevice? active = _tracker.ActiveController;
            if (active is null)
            {
                _isUpdatingSelected = true;
                SelectedItem = null;
                _isUpdatingSelected = false;
                return;
            }

            foreach (ControllerItem item in Controllers)
            {
                if (item.Device.Info.Path == active.Info.Path)
                {
                    _isUpdatingSelected = true;
                    SelectedItem = item;
                    _isUpdatingSelected = false;
                    return;
                }
            }
        });
    }

    /// <summary>
    /// Removes a disconnected controller from the list and surfaces a notification.
    /// Dispatched to the UI thread because the scanner raises events from a background thread.
    /// </summary>
    private void OnControllerDisconnected(object? sender, ControllerConnectionEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsScanning)
            {
                return;
            }

            for (int i = Controllers.Count - 1; i >= 0; i--)
            {
                if (Controllers[i].Device.Info.Path == e.Info.Path)
                {
                    IControllerDevice device = Controllers[i].Device;
                    _tracker.UntrackController(device);
                    device.Dispose();
                    Controllers.RemoveAt(i);
                    _notifications.ShowWarning($"{e.Info.BusType} controller disconnected: {e.Info.ProductName}", 3);
                    _log.Info($"Controller disconnected: {e.Info.ProductName}");
                    break;
                }
            }
        });
    }

    /// <summary>
    /// Refreshes every listed controller's display name when controller info changes
    /// (e.g. the user renames a controller on the device info page). Raised on the UI
    /// thread because all controller info saves originate from UI ViewModels.
    /// </summary>
    private void OnControllersChanged(object? sender, EventArgs e)
    {
        foreach (ControllerItem item in Controllers)
        {
            string? mac = (item.Device as DualSenseDevice)?.PairingInfo?.ClientMac;
            item.DisplayName = _controllerService.GetDisplayName(mac, item.Device.Info.Path, item.Device.Info.ProductName);
        }
    }

    /// <summary>
    /// Unsubscribes from events, untracks and disposes the owned controllers, and
    /// clears the selection.
    /// </summary>
    public void Dispose()
    {
        if (IsScanning)
        {
            _scanner.StopWatching();
        }

        _tracker.ActiveControllerChanged -= OnActiveControllerChanged;
        _scanner.ControllerConnected -= OnControllerConnected;
        _scanner.ControllerDisconnected -= OnControllerDisconnected;
        _controllerService.ControllersChanged -= OnControllersChanged;

        foreach (ControllerItem item in Controllers)
        {
            _tracker.UntrackController(item.Device);
            item.Device.Dispose();
        }

        Controllers.Clear();

        _tracker.SelectController(null);
    }
}