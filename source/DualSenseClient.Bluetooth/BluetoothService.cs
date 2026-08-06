using DualSenseClient.Logging;

namespace DualSenseClient.Bluetooth;

/// <summary>
/// Provides Bluetooth device management on the host platform.
/// Windows uses the Bluetooth radio driver via <see cref="WindowsBluetooth"/>;
/// Linux uses BlueZ over D-Bus via <see cref="LinuxBluetooth"/>. Other platforms
/// are not implemented yet and report failure.
/// </summary>
public static class BluetoothService
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("BluetoothService");

    /// <summary>
    /// Disconnects a classic Bluetooth device by its MAC address (XX:XX:XX:XX:XX:XX).
    /// The device remains paired and can be reconnected later.
    /// </summary>
    /// <param name="macAddress">The MAC address of the device to disconnect.</param>
    /// <returns><c>true</c> if the device was disconnected; otherwise, <c>false</c>.</returns>
    public static bool Disconnect(string macAddress)
    {
        ulong? address = BluetoothAddress.TryParse(macAddress);
        if (address is null)
        {
            _log.Warning($"Invalid Bluetooth MAC address: '{macAddress}'");
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsBluetooth.Disconnect(address.Value);
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxBluetooth.Disconnect(address.Value);
        }

        _log.Warning("Bluetooth disconnect is not supported on this platform yet");
        return false;
    }
}