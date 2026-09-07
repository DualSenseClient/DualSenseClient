namespace DualSenseClient.Hid.Interop;

/// <summary>
/// HID underlying bus types, mirroring the native <c>hid_bus_type</c> enum
/// (available since HIDAPI 0.13.0).
/// </summary>
internal enum HidBusType
{
    /// <summary>
    /// Unknown bus type.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// USB bus.
    /// </summary>
    Usb = 1,

    /// <summary>
    /// Bluetooth or Bluetooth LE bus.
    /// </summary>
    Bluetooth = 2,

    /// <summary>
    /// I2C bus.
    /// </summary>
    I2c = 3,

    /// <summary>
    /// SPI bus.
    /// </summary>
    Spi = 4
}