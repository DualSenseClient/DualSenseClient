namespace DualSenseClient.Hid;

/// <summary>
/// Indicates whether a HID device was connected or disconnected.
/// </summary>
public enum DeviceChangeType
{
    /// <summary>
    /// A HID device was connected.
    /// </summary>
    Connected,

    /// <summary>
    /// A HID device was disconnected.
    /// </summary>
    Disconnected
}