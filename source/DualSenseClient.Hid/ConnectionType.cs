namespace DualSenseClient.Hid;

/// <summary>
/// Identifies the physical transport a HID device is connected over.
/// </summary>
public enum ConnectionType
{
    /// <summary>
    /// The bus type could not be determined.
    /// </summary>
    Unknown,

    /// <summary>
    /// USB connection.
    /// </summary>
    Usb,

    /// <summary>
    /// Bluetooth connection.
    /// </summary>
    Bluetooth
}