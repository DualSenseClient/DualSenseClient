namespace DualSenseClient.Hid.Interop;

/// <summary>
/// Maps native HIDAPI values to managed <see cref="Hid"/> domain types.
/// </summary>
internal static class HidObjectMapper
{
    /// <summary>
    /// Maps a native bus type to the managed connection type.
    /// Unrecognized bus types map to <see cref="ConnectionType.Unknown"/>.
    /// </summary>
    /// <param name="busType">Native bus type.</param>
    /// <returns>The managed connection type.</returns>
    public static ConnectionType ToConnectionType(HidBusType busType)
    {
        return busType switch
        {
            HidBusType.Usb => ConnectionType.Usb,
            HidBusType.Bluetooth => ConnectionType.Bluetooth,
            _ => ConnectionType.Unknown
        };
    }
}