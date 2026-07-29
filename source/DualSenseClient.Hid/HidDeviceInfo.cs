namespace DualSenseClient.Hid;

/// <summary>
/// Describes a HID device discovered during enumeration.
/// </summary>
public interface IHidDeviceInfo
{
    /// <summary>
    /// Platform device path used to open the device.
    /// </summary>
    string Path { get; }

    /// <summary>
    /// USB vendor ID.
    /// </summary>
    ushort VendorId { get; }

    /// <summary>
    /// USB product ID.
    /// </summary>
    ushort ProductId { get; }

    /// <summary>
    /// Human-readable product name.
    /// </summary>
    string ProductName { get; }

    /// <summary>
    /// Device serial number.
    /// </summary>
    string SerialNumber { get; }

    /// <summary>
    /// Manufacturer string.
    /// </summary>
    string Manufacturer { get; }

    /// <summary>
    /// Interface number on a composite device.
    /// </summary>
    int InterfaceNumber { get; }

    /// <summary>
    /// HID usage page.
    /// </summary>
    ushort UsagePage { get; }

    /// <summary>
    /// HID usage.
    /// </summary>
    HidUsageId Usage { get; }

    /// <summary>
    /// Physical transport (USB, Bluetooth, etc.).
    /// </summary>
    ConnectionType BusType { get; }
}

/// <inheritdoc/>
internal sealed class HidDeviceInfo : IHidDeviceInfo
{
    /// <inheritdoc/>
    public string Path { get; init; } = string.Empty;

    /// <inheritdoc/>
    public ushort VendorId { get; init; }

    /// <inheritdoc/>
    public ushort ProductId { get; init; }

    /// <inheritdoc/>
    public string ProductName { get; init; } = string.Empty;

    /// <inheritdoc/>
    public string SerialNumber { get; init; } = string.Empty;

    /// <inheritdoc/>
    public string Manufacturer { get; init; } = string.Empty;

    /// <inheritdoc/>
    public int InterfaceNumber { get; init; }

    /// <inheritdoc/>
    public ushort UsagePage { get; init; }

    /// <inheritdoc/>
    public HidUsageId Usage { get; init; }

    /// <inheritdoc/>
    public ConnectionType BusType { get; init; }
}