using DualSenseClient.Hid;
using DualSenseClient.Logging;

namespace DualSenseClient.Controllers;

/// <summary>
/// Discovers and watches for known controllers (DualSense, etc.) using the HID device enumerator.
/// Provides both a one-shot <see cref="Scan"/> and a push-based event model via <see cref="StartWatching"/>.
/// </summary>
public interface IControllerScanner : IDisposable
{
    /// <summary>
    /// Performs a one-shot enumeration of currently connected controllers.
    /// Each returned <see cref="IControllerDevice"/> is freshly opened and owned by the caller.
    /// </summary>
    IReadOnlyList<IControllerDevice> Scan();

    /// <summary>
    /// Raised when a known controller connects.
    /// The event args carry a live, opened <see cref="IControllerDevice"/> the subscriber owns.
    /// </summary>
    event EventHandler<ControllerConnectionEventArgs>? ControllerConnected;

    /// <summary>
    /// Raised when a known controller disconnects.
    /// The event args carry the device info and type; the <see cref="ControllerConnectionEventArgs.Controller"/>
    /// property is <c>null</c> because the device has been removed.
    /// </summary>
    event EventHandler<ControllerConnectionEventArgs>? ControllerDisconnected;

    /// <summary>
    /// Starts a background timer that polls for controller connection changes.
    /// </summary>
    /// <param name="intervalMs">Polling interval in milliseconds (default 1000).</param>
    void StartWatching(int intervalMs = 1000);

    /// <summary>
    /// Stops the background watcher timer and unsubscribes from enumerator events.
    /// </summary>
    void StopWatching();
}

/// <summary>
/// Default implementation of <see cref="IControllerScanner"/>.
/// Filters devices through <see cref="ControllerFactory.KnownDeviceIds"/> so that
/// only recognized controllers are surfaced to consumers.
/// </summary>
public sealed class ControllerScanner : IControllerScanner
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("ControllerScanner");

    /// <summary>
    /// The HID enumerator used to discover and open controller devices.
    /// </summary>
    private readonly IHidDeviceEnumerator _enumerator;

    /// <summary>
    /// Whether the scanner is currently watching for connection changes.
    /// </summary>
    private bool _watching;

    /// <summary>
    /// Creates a new <see cref="ControllerScanner"/> backed by the given HID enumerator.
    /// </summary>
    public ControllerScanner(IHidDeviceEnumerator enumerator) => _enumerator = enumerator;

    /// <inheritdoc/>
    public IReadOnlyList<IControllerDevice> Scan()
    {
        List<IControllerDevice> controllers = new List<IControllerDevice>();
        foreach (IHidDeviceInfo info in _enumerator.Enumerate(ControllerFactory.KnownDeviceIds))
        {
            IControllerDevice? controller = ControllerFactory.Create(_enumerator, info);
            if (controller is not null)
            {
                controllers.Add(controller);
            }
        }
        _log.Info($"Scan found {controllers.Count} connected controller(s)");
        return controllers;
    }

    /// <inheritdoc/>
    public event EventHandler<ControllerConnectionEventArgs>? ControllerConnected;

    /// <inheritdoc/>
    public event EventHandler<ControllerConnectionEventArgs>? ControllerDisconnected;

    /// <inheritdoc/>
    public void StartWatching(int intervalMs = 1000)
    {
        if (_watching)
        {
            return;
        }
        _log.Debug($"Starting controller watcher (interval={intervalMs}ms)");
        _enumerator.DeviceConnected += OnDeviceConnected;
        _enumerator.DeviceDisconnected += OnDeviceDisconnected;
        _enumerator.StartWatching(intervalMs);
        _watching = true;
    }

    /// <inheritdoc/>
    public void StopWatching()
    {
        if (!_watching)
        {
            return;
        }
        _log.Debug("Stopping controller watcher");
        _enumerator.DeviceConnected -= OnDeviceConnected;
        _enumerator.DeviceDisconnected -= OnDeviceDisconnected;
        _enumerator.StopWatching();
        _watching = false;
    }

    /// <inheritdoc/>
    public void Dispose() => StopWatching();

    /// <summary>
    /// Handles <see cref="IHidDeviceEnumerator.DeviceConnected"/>.
    /// Tries to create a controller wrapper for the newly connected device;
    /// fires <see cref="ControllerConnected"/> only for recognized controllers.
    /// </summary>
    private void OnDeviceConnected(object? sender, DeviceConnectionEventArgs e)
    {
        IControllerDevice? controller = ControllerFactory.Create(_enumerator, e.Device);
        if (controller is null)
        {
            return;
        }
        _log.Info($"Controller connected: {e.Device.ProductName} ({controller.ControllerType}, bus={controller.ConnectionType})");
        ControllerConnected?.Invoke(this,
            new ControllerConnectionEventArgs(DeviceChangeType.Connected, e.Device, controller.ControllerType, controller));
    }

    /// <summary>
    /// Handles <see cref="IHidDeviceEnumerator.DeviceDisconnected"/>.
    /// Fires <see cref="ControllerDisconnected"/> only for recognized controllers;
    /// the <see cref="ControllerConnectionEventArgs.Controller"/> property is <c>null</c>
    /// because the device has already been removed.
    /// </summary>
    private void OnDeviceDisconnected(object? sender, DeviceConnectionEventArgs e)
    {
        ControllerType type = ControllerFactory.GetType(e.Device);
        if (type == ControllerType.Unknown)
        {
            return;
        }
        _log.Info($"Controller disconnected: {e.Device.ProductName} ({type})");
        ControllerDisconnected?.Invoke(this,
            new ControllerConnectionEventArgs(DeviceChangeType.Disconnected, e.Device, type, null));
    }
}