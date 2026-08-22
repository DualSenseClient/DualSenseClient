using System;
using System.Collections.Generic;
using System.Linq;
using DualSenseClient.Logging;

namespace DualSenseClient.Controllers;

/// <summary>
/// Tracks all connected controllers and the single controller currently selected
/// for the application UI. Devices themselves are owned by whoever opened them
/// (the scanner / <c>MainViewModel</c>); this service only keeps references, so
/// selecting a different controller never disposes the previous one.
/// </summary>
public interface IControllerTracker : IDisposable
{
    /// <summary>
    /// The controller currently selected for the application UI, or <c>null</c> when none.
    /// </summary>
    IControllerDevice? ActiveController { get; }

    /// <summary>
    /// A snapshot of all tracked controllers.
    /// </summary>
    IReadOnlyCollection<IControllerDevice> Controllers { get; }

    /// <summary>
    /// Selects a new active controller. The previously selected controller is not
    /// disposed. Pass <c>null</c> to clear the selection.
    /// </summary>
    void SelectController(IControllerDevice? controller);

    /// <summary>
    /// Registers a connected controller, making it available to consumers that manage
    /// per-controller state (emulation, special actions).
    /// </summary>
    void TrackController(IControllerDevice controller);

    /// <summary>
    /// Unregisters a disconnected controller, clearing the active selection when it
    /// was the selected one. The device is not disposed.
    /// </summary>
    void UntrackController(IControllerDevice controller);

    /// <summary>
    /// Raised when <see cref="ActiveController"/> changes (including when cleared).
    /// </summary>
    event EventHandler? ActiveControllerChanged;

    /// <summary>
    /// Raised when a controller is tracked or untracked.
    /// </summary>
    event EventHandler? ControllersChanged;
}

/// <summary>
/// Default implementation of <see cref="IControllerTracker"/>. Holds the set of
/// tracked controllers and the current selection; it never owns devices, so
/// reselecting disposes nothing.
/// </summary>
public sealed class ControllerTracker : IControllerTracker
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("ControllerTracker");

    /// <summary>
    /// Guards access to <see cref="_controllers"/> and <see cref="_activeController"/>.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// All tracked controllers, in connection order.
    /// Only access while holding <see cref="_sync"/>.
    /// </summary>
    private readonly List<IControllerDevice> _controllers = new List<IControllerDevice>();

    /// <summary>
    /// The currently selected controller, or <c>null</c> when none is selected.
    /// Only access while holding <see cref="_sync"/>.
    /// </summary>
    private IControllerDevice? _activeController;

    /// <inheritdoc/>
    public IControllerDevice? ActiveController
    {
        get
        {
            lock (_sync)
            {
                return _activeController;
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<IControllerDevice> Controllers
    {
        get
        {
            lock (_sync)
            {
                return _controllers.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public event EventHandler? ActiveControllerChanged;

    /// <inheritdoc/>
    public event EventHandler? ControllersChanged;

    /// <inheritdoc/>
    public void SelectController(IControllerDevice? controller)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_activeController, controller))
            {
                return;
            }

            string? oldName = _activeController?.Info.ProductName;
            string? newName = controller?.Info.ProductName;
            _activeController = controller;

            // The old controller stays tracked and keeps running; only the selection moves.
            _log.Debug($"Selecting controller: '{oldName ?? "none"}' -> '{newName ?? "none"}'");
        }

        ActiveControllerChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void TrackController(IControllerDevice controller)
    {
        lock (_sync)
        {
            if (_controllers.Any(c => ReferenceEquals(c, controller)))
            {
                return;
            }

            _controllers.Add(controller);
        }

        ControllersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void UntrackController(IControllerDevice controller)
    {
        bool clearedActive;
        bool removed;
        lock (_sync)
        {
            clearedActive = ReferenceEquals(_activeController, controller);
            if (clearedActive)
            {
                _activeController = null;
            }

            removed = _controllers.RemoveAll(c => ReferenceEquals(c, controller)) > 0;
        }

        if (!removed)
        {
            return;
        }

        if (clearedActive)
        {
            ActiveControllerChanged?.Invoke(this, EventArgs.Empty);
        }

        ControllersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Nothing is owned; devices are disposed by their owner.
    }
}