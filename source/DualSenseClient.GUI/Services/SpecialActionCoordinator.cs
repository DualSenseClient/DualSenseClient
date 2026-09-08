using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;
using SoundFlow.Abstracts;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Wires the special actions engine to the application lifecycle: attaches an engine to
/// every tracked controller and keeps the engines' configuration in sync with
/// <see cref="SpecialActionService"/>. Resolved eagerly at startup (see
/// <see cref="App.OnFrameworkInitializationCompleted"/>) so it runs for the app's lifetime.
/// </summary>
public sealed class SpecialActionCoordinator : IDisposable
{
    /// <summary>
    /// Tracks the connected controllers.
    /// </summary>
    private readonly IControllerTracker _tracker;

    /// <summary>
    /// Stores the special action configuration.
    /// </summary>
    private readonly SpecialActionService _service;

    /// <summary>
    /// Resolves the profile bound to a controller, used to revert while-held light actions.
    /// </summary>
    private readonly ControllerInfoService _controllerService;

    /// <summary>
    /// Stores the controller profiles.
    /// </summary>
    private readonly ProfileService _profileService;

    /// <summary>
    /// The shared audio engine used to decode special action sound files.
    /// </summary>
    private readonly AudioEngine _audioEngine;

    /// <summary>
    /// Owns the per-controller special action engines, shared with the emulation output path.
    /// </summary>
    private readonly SpecialActionEngineRegistry _engines;

    /// <summary>
    /// Shows battery notifications raised by special actions.
    /// </summary>
    private readonly INotificationPopupService _popups;

    /// <summary>
    /// Guards <see cref="_subscribed"/> and <see cref="_stickyPopups"/>. Engine events arrive
    /// from controller read-loop and timer threads.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// Engines with battery notification subscriptions, by controller.
    /// </summary>
    private readonly Dictionary<DualSenseDevice, SpecialActionEngine> _subscribed = new Dictionary<DualSenseDevice, SpecialActionEngine>();

    /// <summary>
    /// Open sticky battery notifications by special action id.
    /// </summary>
    private readonly Dictionary<Guid, Guid> _stickyPopups = new Dictionary<Guid, Guid>();

    /// <summary>
    /// The controllers currently attached to an engine.
    /// </summary>
    private readonly HashSet<DualSenseDevice> _attached = new HashSet<DualSenseDevice>();

    /// <summary>
    /// Creates the coordinator, attaches the engine to every tracked controller, and
    /// loads the current configuration.
    /// </summary>
    /// <param name="tracker">The controller tracker providing the connected controllers.</param>
    /// <param name="service">The special action settings service.</param>
    /// <param name="controllerService">Resolves the profile bound to each controller.</param>
    /// <param name="profileService">Stores the controller profiles.</param>
    /// <param name="audioEngine">The shared audio engine used to decode sound files.</param>
    /// <param name="engines">The per-controller engine registry, also used by the emulation output path.</param>
    /// <param name="popups">Shows battery notifications raised by special actions.</param>
    public SpecialActionCoordinator(IControllerTracker tracker, SpecialActionService service, ControllerInfoService controllerService,
        ProfileService profileService, AudioEngine audioEngine, SpecialActionEngineRegistry engines, INotificationPopupService popups)
    {
        _tracker = tracker;
        _service = service;
        _controllerService = controllerService;
        _profileService = profileService;
        _audioEngine = audioEngine;
        _engines = engines;
        _popups = popups;
        _tracker.ControllersChanged += OnControllersChanged;
        _service.SpecialActionsChanged += OnSpecialActionsChanged;

        _engines.ProfileProvider = ResolveProfile;
        _engines.SoundPlayerFactory = device => new DualSenseSpecialActionSoundPlayer(device, _audioEngine);
        _engines.UpdateActions(_service.Settings.Actions);
        ReconcileControllers();
    }

    /// <summary>
    /// Attaches an engine to every tracked controller and removes the engines of
    /// controllers that were untracked. Raised on the UI thread (tracker events).
    /// </summary>
    private void OnControllersChanged(object? sender, EventArgs e) => ReconcileControllers();

    /// <summary>
    /// Pushes the latest configuration into every engine when the user edits special actions.
    /// </summary>
    private void OnSpecialActionsChanged(object? sender, EventArgs e) => _engines.UpdateActions(_service.Settings.Actions);

    /// <summary>
    /// Diffs the tracked controllers against the attached set, creating and removing
    /// engines as needed.
    /// </summary>
    private void ReconcileControllers()
    {
        HashSet<DualSenseDevice> current = _tracker.Controllers.OfType<DualSenseDevice>().ToHashSet();

        foreach (DualSenseDevice device in current)
        {
            if (_attached.Add(device))
            {
                SpecialActionEngine engine = _engines.GetOrCreate(device);
                engine.Attach(device);
                engine.BatteryNotificationShown += OnBatteryNotificationShown;
                engine.BatteryNotificationDismissed += OnBatteryNotificationDismissed;
                lock (_sync)
                {
                    _subscribed[device] = engine;
                }
            }
        }

        foreach (DualSenseDevice device in _attached.Where(device => !current.Contains(device)).ToList())
        {
            _attached.Remove(device);
            // Removing disposes the engine, which dismisses its sticky notifications
            // while still subscribed; unsubscribe afterwards.
            _engines.Remove(device);
            lock (_sync)
            {
                if (_subscribed.Remove(device, out SpecialActionEngine? engine))
                {
                    engine.BatteryNotificationShown -= OnBatteryNotificationShown;
                    engine.BatteryNotificationDismissed -= OnBatteryNotificationDismissed;
                }
            }
        }
    }

    /// <summary>
    /// Shows a battery notification popup for a fired show-battery-level effect: sticky
    /// while its action stays active, transient otherwise. May run on a controller
    /// read-loop or engine timer thread.
    /// </summary>
    private void OnBatteryNotificationShown(object? sender, SpecialActionBatteryNotificationShownEventArgs e)
    {
        string displayName = _controllerService.GetDisplayName(e.Device.PairingInfo?.ClientMac, e.Device.Info.Path, e.Device.Info.ProductName);
        string title = LocalizationService.GetText("Notification.Battery.Title");
        string message = string.Format(LocalizationService.GetText("Notification.Battery.Message"), displayName, e.Percentage);

        lock (_sync)
        {
            if (_stickyPopups.Remove(e.ActionId, out Guid previous))
            {
                _popups.DismissStickyNotification(previous);
            }

            if (e.Sticky)
            {
                _stickyPopups[e.ActionId] = _popups.ShowStickyNotification(title, message);
            }
            else
            {
                TimeSpan? duration = e.DurationMs > 0 ? TimeSpan.FromMilliseconds(e.DurationMs) : null;
                _popups.ShowMessage(title, message, duration);
            }
        }
    }

    /// <summary>
    /// Dismisses a sticky battery notification whose action ended. May run on a
    /// controller read-loop or engine timer thread.
    /// </summary>
    private void OnBatteryNotificationDismissed(object? sender, SpecialActionBatteryNotificationDismissedEventArgs e)
    {
        lock (_sync)
        {
            if (_stickyPopups.Remove(e.ActionId, out Guid stickyId))
            {
                _popups.DismissStickyNotification(stickyId);
            }
        }
    }

    /// <summary>
    /// Resolves the profile bound to a controller (or the default profile when unbound),
    /// which the engine re-applies to revert while-held light actions. Runs on the
    /// controller's read-loop thread or the engine's timer thread.
    /// </summary>
    private Profile? ResolveProfile(DualSenseDevice device)
    {
        string? mac = device.PairingInfo?.ClientMac;
        string? path = device.Info.Path;
        string? profileName = _controllerService.GetBoundProfileName(mac, path) ?? ProfileService.DefaultProfileName;
        return _profileService.GetProfile(profileName);
    }

    /// <summary>
    /// Unsubscribes and disposes the per-controller engines.
    /// </summary>
    public void Dispose()
    {
        _tracker.ControllersChanged -= OnControllersChanged;
        _service.SpecialActionsChanged -= OnSpecialActionsChanged;
        lock (_sync)
        {
            foreach ((DualSenseDevice _, SpecialActionEngine engine) in _subscribed)
            {
                engine.BatteryNotificationShown -= OnBatteryNotificationShown;
                engine.BatteryNotificationDismissed -= OnBatteryNotificationDismissed;
            }

            _subscribed.Clear();
        }

        _engines.Dispose();
    }
}