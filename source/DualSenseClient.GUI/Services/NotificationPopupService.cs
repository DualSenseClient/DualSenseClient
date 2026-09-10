using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Events;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.GUI.Views;
using DualSenseClient.Logging;
using DualSenseClient.Settings;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Kinds of desktop notification popups, used to re-check the matching
/// per-type setting before display.
/// </summary>
internal enum NotificationPopupKind
{
    Connection,
    LowBattery,
    Charge
}

/// <summary>
/// A queued desktop notification popup.
/// </summary>
/// <param name="Title">The bold title line.</param>
/// <param name="Message">The descriptive message line.</param>
/// <param name="Duration">How long the popup stays visible, or <c>null</c> for the configured duration.</param>
internal sealed record NotificationPopupItem(string Title, string Message, TimeSpan? Duration = null, string? Url = null);

/// <summary>
/// Shows desktop notification popups in a separate bottom-right window, so they
/// are visible even when the main window is hidden to the tray. Each popup
/// auto-closes and dismisses on click.
/// </summary>
public interface INotificationPopupService
{
    /// <summary>
    /// Shows a controller connected/disconnected popup. No-op when notifications
    /// or connection notifications are disabled.
    /// </summary>
    void ShowConnection(string displayName, string connectionType, bool connected);

    /// <summary>
    /// Shows a low-battery popup. No-op when notifications or low-battery
    /// notifications are disabled.
    /// </summary>
    void ShowLowBattery(string displayName, int percentage);

    /// <summary>
    /// Shows a charge-completed popup. No-op when notifications or charge
    /// notifications are disabled.
    /// </summary>
    void ShowChargeComplete(string displayName, int percentage);

    /// <summary>
    /// Shows a popup with a custom title and message. No-op when notifications
    /// are disabled. Safe to call from any thread.
    /// </summary>
    /// <param name="title">The bold title line.</param>
    /// <param name="message">The descriptive message line.</param>
    /// <param name="duration">How long the popup stays visible, or <c>null</c> for the configured duration.</param>
    /// <param name="url">Optional URL opened when the popup is clicked.</param>
    void ShowMessage(string title, string message, TimeSpan? duration = null, string? url = null);

    /// <summary>
    /// Shows a sticky popup that stays open until <see cref="DismissStickyNotification"/>
    /// or clicked. No-op when notifications are disabled (returns <see cref="Guid.Empty"/>).
    /// Safe to call from any thread.
    /// </summary>
    /// <returns>A handle for <see cref="DismissStickyNotification"/>, or <see cref="Guid.Empty"/> when disabled.</returns>
    Guid ShowStickyNotification(string title, string message);

    /// <summary>
    /// Dismisses a popup shown with <see cref="ShowStickyNotification"/>. No-op for
    /// unknown or already dismissed handles. Safe to call from any thread.
    /// </summary>
    void DismissStickyNotification(Guid id);
}

/// <summary>
/// Implementation of <see cref="INotificationPopupService"/> showing one popup
/// window at a time from a queue.
/// </summary>
public sealed class NotificationPopupService : INotificationPopupService
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("NotificationPopupService");

    /// <summary>
    /// Minimum auto-close delay in seconds.
    /// </summary>
    private const int MinDurationSeconds = 1;

    /// <summary>
    /// Maximum auto-close delay in seconds.
    /// </summary>
    private const int MaxDurationSeconds = 30;

    /// <summary>
    /// Settings service providing the notification toggles and thresholds.
    /// </summary>
    private readonly SettingsService _settingsService;

    /// <summary>
    /// Tracks the active controllers; battery events are subscribed per tracked
    /// <see cref="DualSenseDevice"/>, mirroring <see cref="TrayIconService"/>.
    /// </summary>
    private readonly IControllerTracker _tracker;

    /// <summary>
    /// Service resolving each controller's display name for popup messages.
    /// </summary>
    private readonly ControllerInfoService _controllerInfoService;

    /// <summary>
    /// Guards <see cref="_watched"/> and <see cref="_batteryState"/>.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// Battery-event subscriptions by HID device path.
    /// </summary>
    private readonly Dictionary<string, DualSenseDevice> _watched = new Dictionary<string, DualSenseDevice>(StringComparer.Ordinal);

    /// <summary>
    /// Once-per-cycle notified flags by HID device path. Entries are dropped on
    /// disconnect so a reconnection starts a fresh cycle.
    /// </summary>
    private readonly Dictionary<string, DeviceNotifyState> _batteryState = new Dictionary<string, DeviceNotifyState>(StringComparer.Ordinal);

    /// <summary>
    /// Open sticky popups by handle. A <c>null</c> value means the window is still
    /// being created on the UI thread; dismissing such a handle drops it so the
    /// window closes immediately after showing.
    /// </summary>
    private readonly Dictionary<Guid, NotificationPopupWindow?> _sticky = new Dictionary<Guid, NotificationPopupWindow?>();

    /// <summary>
    /// Pending popups awaiting display.
    /// </summary>
    private readonly ConcurrentQueue<NotificationPopupItem> _queue = new ConcurrentQueue<NotificationPopupItem>();

    /// <summary>
    /// Ensures popups are processed one at a time.
    /// </summary>
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Whether the queue is currently being processed.
    /// </summary>
    private bool _isProcessing;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationPopupService"/> class.
    /// </summary>
    /// <param name="settingsService">Service providing the notification toggles and thresholds.</param>
    /// <param name="tracker">Tracker used to follow tracked controllers for battery changes.</param>
    /// <param name="controllerInfoService">Service resolving controller display names.</param>
    public NotificationPopupService(SettingsService settingsService, IControllerTracker tracker, ControllerInfoService controllerInfoService)
    {
        _settingsService = settingsService;
        _tracker = tracker;
        _controllerInfoService = controllerInfoService;
        _tracker.ControllersChanged += OnControllersChanged;
        ReconcileBatterySubscriptions();
    }

    /// <inheritdoc/>
    public void ShowConnection(string displayName, string connectionType, bool connected)
    {
        if (!IsEnabled(NotificationPopupKind.Connection))
        {
            return;
        }

        string title = LocalizationService.GetText(connected ? "Notification.Connected.Title" : "Notification.Disconnected.Title");
        string message = string.Format(LocalizationService.GetText(connected ? "Notification.Connected.Message" : "Notification.Disconnected.Message"),
            displayName, connectionType);
        Enqueue(new NotificationPopupItem(title, message));
    }

    /// <inheritdoc/>
    public void ShowLowBattery(string displayName, int percentage)
    {
        if (!IsEnabled(NotificationPopupKind.LowBattery))
        {
            return;
        }

        string title = LocalizationService.GetText("Notification.LowBattery.Title");
        string message = string.Format(LocalizationService.GetText("Notification.LowBattery.Message"), displayName, percentage);
        Enqueue(new NotificationPopupItem(title, message));
    }

    /// <inheritdoc/>
    public void ShowChargeComplete(string displayName, int percentage)
    {
        if (!IsEnabled(NotificationPopupKind.Charge))
        {
            return;
        }

        string title = LocalizationService.GetText("Notification.Charged.Title");
        string message = string.Format(LocalizationService.GetText("Notification.Charged.Message"), displayName, percentage);
        Enqueue(new NotificationPopupItem(title, message));
    }

    /// <inheritdoc/>
    public void ShowMessage(string title, string message, TimeSpan? duration = null, string? url = null)
    {
        if (!_settingsService.Settings.Ui.Notifications.Enabled)
        {
            return;
        }

        Enqueue(new NotificationPopupItem(title, message, duration, url));
    }

    /// <inheritdoc/>
    public Guid ShowStickyNotification(string title, string message)
    {
        if (!_settingsService.Settings.Ui.Notifications.Enabled)
        {
            return Guid.Empty;
        }

        Guid id = Guid.NewGuid();
        lock (_sync)
        {
            _sticky[id] = null;
        }

        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            lock (_sync)
            {
                if (!_sticky.ContainsKey(id))
                {
                    return;
                }
            }

            NotificationPopupWindow? popup = null;
            try
            {
                popup = new NotificationPopupWindow();
                popup.SetContent(title, message);
                popup.Placement = _settingsService.Settings.Ui.Notifications.Position;
                Guid captured = id;
                popup.Closed += (_, _) =>
                {
                    lock (_sync)
                    {
                        _sticky.Remove(captured);
                    }
                };
                popup.Show();
            }
            catch (Exception ex)
            {
                _log.Warning($"Failed to show sticky notification popup: {ex.Message}");
            }

            lock (_sync)
            {
                if (!_sticky.ContainsKey(id))
                {
                    // Dismissed while the window was being created.
                    if (popup is not null)
                    {
                        try
                        {
                            popup.Close();
                        }
                        catch (Exception ex)
                        {
                            _log.Warning($"Failed to close sticky notification popup: {ex.Message}");
                        }
                    }
                }
                else if (popup is null)
                {
                    _sticky.Remove(id);
                }
                else
                {
                    _sticky[id] = popup;
                }
            }
        });

        return id;
    }

    /// <inheritdoc/>
    public void DismissStickyNotification(Guid id)
    {
        if (id == Guid.Empty)
        {
            return;
        }

        NotificationPopupWindow? popup;
        lock (_sync)
        {
            if (!_sticky.Remove(id, out popup))
            {
                return;
            }
        }

        if (popup is null)
        {
            // Still being created; the show delegate closes it on arrival.
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (popup.IsVisible)
                {
                    popup.Close();
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Failed to close sticky notification popup: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Whether popups of the given kind are currently enabled (master switch plus
    /// the matching per-type toggle).
    /// </summary>
    private bool IsEnabled(NotificationPopupKind kind)
    {
        if (!_settingsService.Settings.Ui.Notifications.Enabled)
        {
            return false;
        }

        return kind switch
        {
            NotificationPopupKind.Connection => _settingsService.Settings.Ui.Notifications.NotifyOnConnection,
            NotificationPopupKind.LowBattery => _settingsService.Settings.Ui.Notifications.NotifyOnLowBattery,
            NotificationPopupKind.Charge => _settingsService.Settings.Ui.Notifications.NotifyOnCharge,
            _ => false
        };
    }

    /// <summary>
    /// How long each popup stays visible before auto-closing, from settings.
    /// </summary>
    private TimeSpan GetPopupDuration() =>
        TimeSpan.FromSeconds(Math.Clamp(_settingsService.Settings.Ui.Notifications.Duration, MinDurationSeconds, MaxDurationSeconds));

    /// <summary>
    /// Queues a popup and kicks the processing loop. Safe to call from any thread.
    /// </summary>
    private void Enqueue(NotificationPopupItem item)
    {
        _queue.Enqueue(item);
        _ = ProcessQueueAsync();
    }

    /// <summary>
    /// Shows queued popups one at a time.
    /// </summary>
    private async Task ProcessQueueAsync()
    {
        if (_isProcessing || !await _semaphore.WaitAsync(0))
        {
            return;
        }

        try
        {
            _isProcessing = true;
            while (_queue.TryDequeue(out NotificationPopupItem? item))
            {
                await ShowPopupAsync(item.Title, item.Message, item.Duration ?? GetPopupDuration(), item.Url);
            }
        }
        finally
        {
            _isProcessing = false;
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Shows a single popup window, waiting for its auto-close timeout or for the
    /// user to dismiss it by clicking. Window work is marshaled to the UI thread;
    /// failures are logged and skipped so one bad popup never stalls the queue.
    /// </summary>
    private async Task ShowPopupAsync(string title, string message, TimeSpan duration, string? url = null)
    {
        TaskCompletionSource<bool> closedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        NotificationPopupWindow? window = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                NotificationPopupWindow popup = new NotificationPopupWindow();
                popup.SetContent(title, message);
                popup.Placement = _settingsService.Settings.Ui.Notifications.Position;
                if (!string.IsNullOrEmpty(url))
                {
                    popup.ClickAction = () => OpenUrl(url);
                }

                popup.Closed += (_, _) => closedTcs.TrySetResult(true);
                popup.Show();
                return popup;
            }
            catch (Exception ex)
            {
                _log.Warning($"Failed to show notification popup: {ex.Message}");
                closedTcs.TrySetResult(true);
                return null;
            }
        });

        if (window is null)
        {
            return;
        }

        await Task.WhenAny(Task.Delay(duration), closedTcs.Task);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Failed to close notification popup: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Opens a URL in the default browser. Failures are logged and ignored.
    /// </summary>
    internal static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to open URL '{url}'");
            _log.LogExceptionDetails(ex);
        }
    }

    /// <summary>
    /// Re-subscribes battery events after the tracked controller set changed.
    /// Raised on the UI thread. State for disconnected devices is dropped so a
    /// reconnection starts a fresh notify cycle.
    /// </summary>
    private void OnControllersChanged(object? sender, EventArgs e) => ReconcileBatterySubscriptions();

    /// <summary>
    /// Watches every tracked <see cref="DualSenseDevice"/> for battery changes,
    /// evaluating the current level immediately so an already-low or already-charged
    /// controller notifies without waiting for the next report.
    /// </summary>
    private void ReconcileBatterySubscriptions()
    {
        List<DualSenseDevice> added = new List<DualSenseDevice>();
        lock (_sync)
        {
            HashSet<string> current = new HashSet<string>(StringComparer.Ordinal);
            foreach (IControllerDevice controller in _tracker.Controllers)
            {
                if (controller is not DualSenseDevice device)
                {
                    continue;
                }

                current.Add(device.Info.Path);
                if (!_watched.ContainsKey(device.Info.Path))
                {
                    _watched[device.Info.Path] = device;
                    added.Add(device);
                }
            }

            foreach (string path in _watched.Keys.Where(path => !current.Contains(path)).ToList())
            {
                _watched[path].BatteryStateChanged -= OnBatteryStateChanged;
                _watched.Remove(path);
                _batteryState.Remove(path);
            }
        }

        foreach (DualSenseDevice device in added)
        {
            device.BatteryStateChanged += OnBatteryStateChanged;
            if (device.InputReport is { } report)
            {
                EvaluateBattery(device, report.Battery);
            }
        }
    }

    /// <summary>
    /// Evaluates a battery change against the thresholds. Raised from the device
    /// read-loop thread; popup calls are thread-safe.
    /// </summary>
    private void OnBatteryStateChanged(object? sender, BatteryStateEventArgs e)
    {
        if (sender is DualSenseDevice device)
        {
            EvaluateBattery(device, e.CurrentState);
        }
    }

    /// <summary>
    /// Applies the once-per-cycle <see cref="BatteryNotificationPolicy"/> rules for
    /// one device and shows popups for newly reached states.
    /// </summary>
    private void EvaluateBattery(DualSenseDevice device, BatteryState battery)
    {
        int lowThreshold = BatteryNotificationPolicy.ClampLowThreshold(_settingsService.Settings.Ui.Notifications.LowBatteryThreshold);
        int chargeThreshold = BatteryNotificationPolicy.ClampChargeThreshold(_settingsService.Settings.Ui.Notifications.ChargeThreshold);
        int percentage = battery.DisplayPercentage;
        BatteryPowerState powerState = battery.PowerState;

        bool notifyLow = false;
        bool notifyCharge = false;
        lock (_sync)
        {
            if (!_batteryState.TryGetValue(device.Info.Path, out DeviceNotifyState? state))
            {
                state = new DeviceNotifyState();
                _batteryState[device.Info.Path] = state;
            }

            if (BatteryNotificationPolicy.ShouldResetLowNotification(percentage, lowThreshold))
            {
                state.LowNotified = false;
            }

            if (BatteryNotificationPolicy.ShouldResetChargeNotification(powerState))
            {
                state.ChargeNotified = false;
            }

            if (BatteryNotificationPolicy.ShouldNotifyLowBattery(percentage, lowThreshold, state.LowNotified))
            {
                state.LowNotified = true;
                notifyLow = true;
            }

            if (BatteryNotificationPolicy.ShouldNotifyChargeComplete(powerState, percentage, chargeThreshold, state.ChargeNotified))
            {
                state.ChargeNotified = true;
                notifyCharge = true;
            }
        }

        if (!notifyLow && !notifyCharge)
        {
            return;
        }

        string displayName = _controllerInfoService.GetDisplayName(device.PairingInfo?.ClientMac, device.Info.Path, device.Info.ProductName);
        if (notifyLow)
        {
            ShowLowBattery(displayName, percentage);
        }

        if (notifyCharge)
        {
            ShowChargeComplete(displayName, percentage);
        }
    }

    /// <summary>
    /// Once-per-cycle notified flags for one controller.
    /// </summary>
    private sealed class DeviceNotifyState
    {
        /// <summary>
        /// Whether the low-battery popup already fired this discharge cycle.
        /// </summary>
        public bool LowNotified;

        /// <summary>
        /// Whether the charge popup already fired this charge cycle.
        /// </summary>
        public bool ChargeNotified;
    }
}