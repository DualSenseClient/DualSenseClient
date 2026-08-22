using System;
using System.Collections.Generic;
using System.Linq;
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
    public SpecialActionCoordinator(IControllerTracker tracker, SpecialActionService service, ControllerInfoService controllerService,
        ProfileService profileService, AudioEngine audioEngine, SpecialActionEngineRegistry engines)
    {
        _tracker = tracker;
        _service = service;
        _controllerService = controllerService;
        _profileService = profileService;
        _audioEngine = audioEngine;
        _engines = engines;
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
    private void OnControllersChanged(object? sender, EventArgs e)
    {
        ReconcileControllers();
    }

    /// <summary>
    /// Pushes the latest configuration into every engine when the user edits special actions.
    /// </summary>
    private void OnSpecialActionsChanged(object? sender, EventArgs e)
    {
        _engines.UpdateActions(_service.Settings.Actions);
    }

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
                _engines.GetOrCreate(device).Attach(device);
            }
        }

        foreach (DualSenseDevice device in _attached.Where(device => !current.Contains(device)).ToList())
        {
            _attached.Remove(device);
            _engines.Remove(device);
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
        _engines.Dispose();
    }
}