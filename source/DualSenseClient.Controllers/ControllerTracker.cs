using DualSenseClient.Logging;

namespace DualSenseClient.Controllers;

/// <summary>
/// Tracks the single controller currently in use by the application.
/// Owns the lifecycle of the selected controller — disposes the previous one
/// on reselection and auto-clears when the active controller is physically disconnected.
/// </summary>
public interface IControllerTracker : IDisposable
{
    /// <summary>
    /// The controller currently in use, or <c>null</c> if none is selected.
    /// </summary>
    IControllerDevice? ActiveController { get; }

    /// <summary>
    /// Selects a new active controller. Any previously selected controller is disposed.
    /// Pass <c>null</c> to clear the selection.
    /// </summary>
    void SelectController(IControllerDevice? controller);

    /// <summary>
    /// Raised when <see cref="ActiveController"/> changes (including when cleared).
    /// May be raised on a background thread when triggered by a disconnect,
    /// so UI subscribers should dispatch to the UI thread.
    /// </summary>
    event EventHandler? ActiveControllerChanged;
}

/// <summary>
/// Default implementation of <see cref="IControllerTracker"/>.
/// Subscribes to <see cref="IControllerScanner.ControllerDisconnected"/> so that
/// the active controller is automatically cleared when the physical device is removed.
/// Controller state is guarded by a lock so that <see cref="SelectController"/> (UI thread)
/// and the disconnect watcher (background thread) can run concurrently without racing.
/// </summary>
public sealed class ControllerTracker : IControllerTracker
{
    /// <summary>
    /// Logger instance
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("ControllerTracker");

    /// <summary>
    /// Scanner used to watch for controller disconnections.
    /// </summary>
    private readonly IControllerScanner _scanner;

    /// <summary>
    /// Guards access to <see cref="_activeController"/> and <see cref="_disposed"/>.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// The currently selected active controller, or <c>null</c> if none is selected.
    /// Only access while holding <see cref="_sync"/>.
    /// </summary>
    private IControllerDevice? _activeController;

    /// <summary>
    /// Whether the service has been disposed.
    /// Only access while holding <see cref="_sync"/>.
    /// </summary>
    private bool _disposed;

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
    public event EventHandler? ActiveControllerChanged;

    /// <summary>
    /// Creates a new <see cref="ControllerTracker"/> that watches for disconnections
    /// via the provided <paramref name="scanner"/>.
    /// </summary>
    public ControllerTracker(IControllerScanner scanner)
    {
        _scanner = scanner;
        _scanner.ControllerDisconnected += OnControllerDisconnected;
    }

    /// <inheritdoc/>
    public void SelectController(IControllerDevice? controller)
    {
        IControllerDevice? toDispose;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (ReferenceEquals(_activeController, controller))
            {
                return;
            }

            string? oldName = _activeController?.Info.ProductName;
            string? newName = controller?.Info.ProductName;
            _log.Debug($"Selecting controller: '{oldName ?? "none"}' -> '{newName ?? "none"}'");

            toDispose = _activeController;
            _activeController = controller;
        }

        toDispose?.Dispose();
        ActiveControllerChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Handles the scanner's <see cref="IControllerScanner.ControllerDisconnected"/> event.
    /// Clears the active controller if it matches the disconnected device.
    /// The path check and clear happen atomically so a concurrent
    /// <see cref="SelectController"/> cannot clear a newly selected controller.
    /// </summary>
    private void OnControllerDisconnected(object? sender, ControllerConnectionEventArgs e)
    {
        IControllerDevice? toDispose;

        lock (_sync)
        {
            IControllerDevice? active = _activeController;
            if (active is null || active.Info.Path != e.Info.Path)
            {
                return;
            }

            _log.Info($"Active controller '{active.Info.ProductName}' disconnected — clearing selection");
            toDispose = active;
            _activeController = null;
        }

        toDispose?.Dispose();
        ActiveControllerChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        IControllerDevice? toDispose;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            _scanner.ControllerDisconnected -= OnControllerDisconnected;
            toDispose = _activeController;
            _activeController = null;
        }

        toDispose?.Dispose();
    }
}