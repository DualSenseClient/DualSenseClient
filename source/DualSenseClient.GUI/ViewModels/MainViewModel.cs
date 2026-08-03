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
    /// Profile service used to apply each controller's bound profile when it connects.
    /// </summary>
    private readonly ProfileService _profileService;

    /// <summary>
    /// Devices opened by this ViewModel that are not currently owned by the tracker.
    /// Devices are removed from this list when they are handed to the tracker as the active controller.
    /// </summary>
    private readonly List<IControllerDevice> _ownedDevices = new List<IControllerDevice>();

    /// <summary>
    /// Controllers discovered while scanning, shown in the title bar combobox.
    /// </summary>
    public ObservableCollection<ControllerItem> Controllers { get; } = new ObservableCollection<ControllerItem>();

    /// <summary>
    /// The controller selected in the title bar combobox.
    /// </summary>
    [ObservableProperty] private ControllerItem? _selectedItem;

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
    /// <param name="profileService">Profile service used to apply bound profiles on connect.</param>
    public MainViewModel(IControllerScanner scanner, IControllerTracker tracker, INotificationService notifications, ProfileService profileService)
    {
        _scanner = scanner;
        _tracker = tracker;
        _notifications = notifications;
        _profileService = profileService;
        _tracker.ActiveControllerChanged += OnActiveControllerChanged;
        _scanner.ControllerConnected += OnControllerConnected;
        _scanner.ControllerDisconnected += OnControllerDisconnected;
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
                    Controllers.Add(new ControllerItem(controller));
                    _ownedDevices.Add(controller);
                    ApplyBoundProfile(controller);
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
    /// Stops watching, disposes the owned controllers, and clears the selection.
    /// </summary>
    private void StopScanning()
    {
        _log.Debug("Stopping controller scanning");

        _scanner.StopWatching();

        foreach (IControllerDevice device in _ownedDevices)
        {
            device.Dispose();
        }
        _ownedDevices.Clear();
        Controllers.Clear();

        _tracker.SelectController(null);
        IsScanning = false;
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
        Profile? profile = _profileService.GetProfileForControllerOrDefault(mac, path);
        if (profile is null)
        {
            return;
        }

        _log.Info($"Applying profile '{profile.Name}' to {device.Info.ProductName}");
        device.ApplyProfile(profile);
    }

    /// <summary>
    /// Forwards a combobox selection change to the tracker.
    /// Ownership of the selected device transfers to the tracker, which disposes it on reselection.
    /// </summary>
    /// <param name="value">The newly selected controller item, or <c>null</c>.</param>
    partial void OnSelectedItemChanged(ControllerItem? value)
    {
        if (_isUpdatingSelected)
        {
            return;
        }

        _isUpdatingSelected = true;
        if (value is not null)
        {
            _ownedDevices.Remove(value.Device);
            _tracker.SelectController(value.Device);
        }
        else
        {
            _tracker.SelectController(null);
        }
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

            Controllers.Add(new ControllerItem(controller));
            _ownedDevices.Add(controller);
            ApplyBoundProfile(controller);
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
                    if (_ownedDevices.Remove(device))
                    {
                        device.Dispose();
                    }
                    Controllers.RemoveAt(i);
                    _notifications.ShowWarning($"{e.Info.BusType} controller disconnected: {e.Info.ProductName}", 3);
                    _log.Info($"Controller disconnected: {e.Info.ProductName}");
                    break;
                }
            }
        });
    }

    /// <summary>
    /// Unsubscribes from events and disposes the owned controllers.
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

        foreach (IControllerDevice device in _ownedDevices)
        {
            device.Dispose();
        }
        _ownedDevices.Clear();
    }
}