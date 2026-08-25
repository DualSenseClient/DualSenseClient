using DualSenseClient.Hid;

namespace DualSenseClient.Controllers;

/// <summary>
/// Event data for controller connection and disconnection events.
/// Carries the raw device info, the resolved controller type, and (on connect) the live controller.
/// </summary>
public sealed class ControllerConnectionEventArgs : EventArgs
{
    /// <summary>
    /// Whether the controller connected or disconnected.
    /// </summary>
    public DeviceChangeType ChangeType { get; }

    /// <summary>
    /// Information about the underlying HID device that connected or disconnected.
    /// Always available regardless of connection state.
    /// </summary>
    public IHidDeviceInfo Info { get; }

    /// <summary>
    /// The resolved controller type.
    /// </summary>
    public ControllerType Type { get; }

    /// <summary>
    /// The physical transport (USB or Bluetooth).
    /// </summary>
    public ConnectionType ConnectionType
    {
        get
        {
            return Info.BusType;
        }
    }

    /// <summary>
    /// The live controller instance, or <c>null</c> on disconnect when the device is no longer openable.
    /// </summary>
    public IControllerDevice? Controller { get; }

    /// <summary>
    /// Creates a new <see cref="ControllerConnectionEventArgs"/> instance.
    /// </summary>
    /// <param name="changeType">Whether the controller connected or disconnected.</param>
    /// <param name="info">Information about the underlying HID device.</param>
    /// <param name="type">The resolved controller type.</param>
    /// <param name="controller">
    /// The live controller (non-null on connect); <c>null</c> on disconnect when the device has been removed.
    /// </param>
    public ControllerConnectionEventArgs(
        DeviceChangeType changeType,
        IHidDeviceInfo info,
        ControllerType type,
        IControllerDevice? controller)
    {
        ChangeType = changeType;
        Info = info;
        Type = type;
        Controller = controller;
    }
}