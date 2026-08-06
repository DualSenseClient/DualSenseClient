using System;
using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;
using SoundFlow.Abstracts;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Wires the special actions engine to the application lifecycle: attaches it to the
/// tracker's active controller and keeps its configuration in sync with
/// <see cref="SpecialActionService"/>. Resolved eagerly at startup (see
/// <see cref="App.OnFrameworkInitializationCompleted"/>) so it runs for the app's lifetime.
/// </summary>
public sealed class SpecialActionCoordinator : IDisposable
{
    /// <summary>
    /// Tracks the currently active controller.
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
    /// The engine that matches combinations and executes actions.
    /// </summary>
    private readonly SpecialActionEngine _engine = new SpecialActionEngine();

    /// <summary>
    /// Creates the coordinator, attaches the engine to the current active controller, and
    /// loads the current configuration.
    /// </summary>
    /// <param name="tracker">The controller tracker providing the active controller.</param>
    /// <param name="service">The special action settings service.</param>
    /// <param name="controllerService">Resolves the profile bound to each controller.</param>
    /// <param name="profileService">Stores the controller profiles.</param>
    /// <param name="audioEngine">The shared audio engine used to decode sound files.</param>
    public SpecialActionCoordinator(IControllerTracker tracker, SpecialActionService service, ControllerInfoService controllerService, ProfileService profileService, AudioEngine audioEngine)
    {
        _tracker = tracker;
        _service = service;
        _controllerService = controllerService;
        _profileService = profileService;
        _audioEngine = audioEngine;
        _tracker.ActiveControllerChanged += OnActiveControllerChanged;
        _service.SpecialActionsChanged += OnSpecialActionsChanged;

        _engine.ProfileProvider = ResolveProfile;
        _engine.SoundPlayerFactory = device => new DualSenseSpecialActionSoundPlayer(device, _audioEngine);
        _engine.UpdateActions(_service.Settings.Actions);
        AttachCurrentController();
    }

    /// <summary>
    /// (Re-)attaches the engine to the tracker's active controller when it changes.
    /// May fire on a background thread (tracker disconnect events); attaching only
    /// subscribes to device events and resets local state, so no UI marshaling is needed.
    /// </summary>
    private void OnActiveControllerChanged(object? sender, EventArgs e)
    {
        AttachCurrentController();
    }

    /// <summary>
    /// Pushes the latest configuration into the engine when the user edits special actions.
    /// </summary>
    private void OnSpecialActionsChanged(object? sender, EventArgs e)
    {
        _engine.UpdateActions(_service.Settings.Actions);
    }

    /// <summary>
    /// Attaches the engine to the tracker's active controller, or detaches when none.
    /// </summary>
    private void AttachCurrentController()
    {
        if (_tracker.ActiveController is DualSenseDevice device)
        {
            _engine.Attach(device);
        }
        else
        {
            _engine.Detach();
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
    /// Unsubscribes and detaches the engine.
    /// </summary>
    public void Dispose()
    {
        _tracker.ActiveControllerChanged -= OnActiveControllerChanged;
        _service.SpecialActionsChanged -= OnSpecialActionsChanged;
        _engine.Dispose();
    }
}