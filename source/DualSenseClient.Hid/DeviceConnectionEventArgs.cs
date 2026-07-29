namespace DualSenseClient.Hid;

/// <summary>
/// Event data for device connection and disconnection events.
/// </summary>
public class DeviceConnectionEventArgs : EventArgs
{
    /// <summary>
    /// Whether the device connected or disconnected.
    /// </summary>
    public DeviceChangeType ChangeType { get; }

    /// <summary>
    /// Information about the device that connected or disconnected.
    /// </summary>
    public IHidDeviceInfo Device { get; }

    /// <summary>
    /// Creates a new <see cref="DeviceConnectionEventArgs"/> instance.
    /// </summary>
    /// <param name="changeType">Whether the device connected or disconnected.</param>
    /// <param name="device">Information about the device.</param>
    public DeviceConnectionEventArgs(DeviceChangeType changeType, IHidDeviceInfo device)
    {
        ChangeType = changeType;
        Device = device;
    }
}