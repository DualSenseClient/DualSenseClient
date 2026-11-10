using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Controllers.TouchpadMouse;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Wires the touchpad-mouse engines to the application lifecycle: attaches an engine to
/// every tracked controller whose stored setting enables the feature, suspends the
/// controller's touchpad-triggered special actions while its touchpad is owned by the
/// mouse, and keeps the engines' options in sync with <see cref="ControllerInfoService"/>.
/// Resolved eagerly at startup (see <see cref="Controls.AppSplashScreen"/>) so it runs for
/// the app's lifetime.
/// </summary>
public sealed class TouchpadMouseCoordinator : IDisposable
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("TouchpadMouse");

    /// <summary>
    /// Tracks the connected controllers.
    /// </summary>
    private readonly IControllerTracker _tracker;

    /// <summary>
    /// Stores the per-controller touchpad mouse settings.
    /// </summary>
    private readonly ControllerInfoService _controllerService;

    /// <summary>
    /// Owns the per-controller special action engines whose touchpad actions are suspended.
    /// </summary>
    private readonly SpecialActionEngineRegistry _specialActions;

    /// <summary>
    /// Creates the per-controller mouse injector.
    /// </summary>
    private readonly Func<IMouseInjector> _injectorFactory;

    /// <summary>
    /// Guards <see cref="_engines"/> and <see cref="_disposed"/>. The coordinator is constructed
    /// on the splash thread while tracker and settings events arrive on the UI thread, so the
    /// initial reconcile and later events must not interleave.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// The engine attached to each controller with the feature enabled.
    /// </summary>
    private readonly Dictionary<DualSenseDevice, TouchpadMouseEngine> _engines = new Dictionary<DualSenseDevice, TouchpadMouseEngine>();

    /// <summary>
    /// Whether the coordinator was disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Creates the coordinator and attaches an engine to every tracked controller whose
    /// stored setting enables the feature.
    /// </summary>
    /// <param name="tracker">The controller tracker providing the connected controllers.</param>
    /// <param name="controllerService">Stores the per-controller touchpad mouse settings.</param>
    /// <param name="specialActions">The special action engine registry whose touchpad-triggered actions are suspended.</param>
    /// <param name="injectorFactory">Creates the per-controller mouse injector.</param>
    public TouchpadMouseCoordinator(IControllerTracker tracker, ControllerInfoService controllerService,
        SpecialActionEngineRegistry specialActions, Func<IMouseInjector> injectorFactory)
    {
        _tracker = tracker;
        _controllerService = controllerService;
        _specialActions = specialActions;
        _injectorFactory = injectorFactory;
        _tracker.ControllersChanged += OnControllersChanged;
        _controllerService.ControllersChanged += OnControllersChanged;
        Reconcile();
    }

    /// <summary>
    /// Reconciles the engines when controllers are connected or disconnected, or when the
    /// stored controller info (including the touchpad mouse settings) is saved.
    /// </summary>
    private void OnControllersChanged(object? sender, EventArgs e) => Reconcile();

    /// <summary>
    /// Diffs the tracked controllers against the attached engines, creating, updating, and
    /// removing them as their stored settings require, and suspends touchpad-triggered
    /// special actions for as long as a controller's touchpad mouse is enabled.
    /// </summary>
    private void Reconcile()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            HashSet<DualSenseDevice> current = _tracker.Controllers.OfType<DualSenseDevice>().ToHashSet();

            foreach (DualSenseDevice device in current)
            {
                TouchpadMouseSettings settings = _controllerService.GetTouchpadMouseSettings(device.Key);

                // The null injector accepts nothing, so an unsupported platform must not
                // attach an engine (nor claim the touchpad from special actions).
                bool enabled = TouchpadMouseInjectorFactory.IsSupported && settings.Enabled;

                if (enabled)
                {
                    if (_engines.TryGetValue(device, out TouchpadMouseEngine? engine))
                    {
                        engine.UpdateOptions(CreateOptions(settings));
                    }
                    else
                    {
                        engine = new TouchpadMouseEngine(_injectorFactory(), CreateOptions(settings));
                        engine.Attach(device);
                        _engines.Add(device, engine);
                        _log.Info($"Touchpad mouse enabled for {device.Info.ProductName}");
                    }
                }
                else if (_engines.Remove(device, out TouchpadMouseEngine? disabled))
                {
                    DisposeEngine(disabled);
                    _log.Info($"Touchpad mouse disabled for {device.Info.ProductName}");
                }

                _specialActions.SetTouchpadActionsSuspended(device, enabled);
            }

            foreach (DualSenseDevice stale in _engines.Keys.Where(device => !current.Contains(device)).ToList())
            {
                TouchpadMouseEngine engine = _engines[stale];
                _engines.Remove(stale);
                DisposeEngine(engine);
                _specialActions.SetTouchpadActionsSuspended(stale, false);
            }
        }
    }

    /// <summary>
    /// Maps the stored settings to engine options, clamping hand-edited speeds into the
    /// UI range.
    /// </summary>
    /// <param name="settings">The stored touchpad mouse settings.</param>
    /// <returns>The options used by the engine's subsequent reports.</returns>
    private static TouchpadMouseOptions CreateOptions(TouchpadMouseSettings settings)
    {
        return new TouchpadMouseOptions(
            settings.Enabled,
            Math.Clamp(settings.PointerSpeed, 0.25, 3.0),
            Math.Clamp(settings.ScrollSpeed, 0.25, 3.0),
            settings.NaturalScrolling);
    }

    /// <summary>
    /// Unsubscribes, detaches and disposes every engine, and resumes the touchpad-triggered
    /// special actions of their controllers.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _tracker.ControllersChanged -= OnControllersChanged;
            _controllerService.ControllersChanged -= OnControllersChanged;
            foreach ((DualSenseDevice device, TouchpadMouseEngine engine) in _engines)
            {
                DisposeEngine(engine);
                _specialActions.SetTouchpadActionsSuspended(device, false);
            }

            _engines.Clear();
        }
    }

    /// <summary>
    /// Disposes one engine without letting a failure stop the remaining engines from detaching
    /// or releasing their held buttons.
    /// </summary>
    /// <param name="engine">The engine to dispose.</param>
    private static void DisposeEngine(TouchpadMouseEngine engine)
    {
        try
        {
            engine.Dispose();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to dispose touchpad mouse engine", ex);
        }
    }
}