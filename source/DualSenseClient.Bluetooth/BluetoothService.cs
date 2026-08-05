using DualSenseClient.Logging;

namespace DualSenseClient.Bluetooth;

/// <summary>
/// Provides Bluetooth device management on the host platform.
/// </summary>
public interface IBluetoothService
{
    /// <summary>
    /// Disconnects a classic Bluetooth device by its MAC address (XX:XX:XX:XX:XX:XX).
    /// The device remains paired and can be reconnected later.
    /// </summary>
    /// <param name="macAddress">The MAC address of the device to disconnect.</param>
    /// <returns><c>true</c> if the device was disconnected; otherwise, <c>false</c>.</returns>
    bool Disconnect(string macAddress);
}

/// <summary>
/// Default implementation of <see cref="IBluetoothService"/>.
/// Windows uses the Bluetooth radio driver via <see cref="WindowsBluetooth"/>;
/// other platforms are not implemented yet and report failure.
/// </summary>
public sealed class BluetoothService : IBluetoothService
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("BluetoothService");

    /// <inheritdoc/>
    public bool Disconnect(string macAddress)
    {
        ulong? address = BluetoothAddress.TryParse(macAddress);
        if (address is null)
        {
            _log.Warning($"Invalid Bluetooth MAC address: '{macAddress}'");
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            _log.Warning("Bluetooth disconnect is not supported on this platform yet");
            return false;
        }

        return WindowsBluetooth.Disconnect(address.Value);
    }
}