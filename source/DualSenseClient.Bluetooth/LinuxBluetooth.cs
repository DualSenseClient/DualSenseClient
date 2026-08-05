using DualSenseClient.Logging;
using Tmds.DBus;

namespace DualSenseClient.Bluetooth;

/// <summary>
/// Linux implementation of <see cref="IBluetoothService"/>.
/// Talks to BlueZ (org.bluez) over the system D-Bus: finds the <c>org.bluez.Device1</c>
/// object whose address matches the controller and calls its <c>Disconnect</c> method,
/// which drops the ACL link without unpairing the device.
/// </summary>
internal static class LinuxBluetooth
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("LinuxBluetooth");

    /// <summary>
    /// The BlueZ D-Bus service name.
    /// </summary>
    private const string BluezService = "org.bluez";

    /// <summary>
    /// The interface exposing <c>Disconnect</c> on remote device objects.
    /// </summary>
    private const string Device1Interface = "org.bluez.Device1";

    /// <summary>
    /// Disconnects the device with the given address. The device is looked up by its
    /// <c>Address</c> property so the BlueZ object path (which differs per adapter)
    /// does not need to be known in advance.
    /// </summary>
    /// <param name="address">The 48-bit Bluetooth address of the device to disconnect.</param>
    /// <returns><c>true</c> if the device was disconnected; otherwise, <c>false</c>.</returns>
    public static bool Disconnect(ulong address)
    {
        string mac = FormatMacAddress(address);
        try
        {
            Connection connection = Connection.System;
            IObjectManager objectManager = connection.CreateProxy<IObjectManager>(BluezService, "/");
            IDictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>> objects =
                objectManager.GetManagedObjectsAsync().GetAwaiter().GetResult();

            foreach (KeyValuePair<ObjectPath, IDictionary<string, IDictionary<string, object>>> entry in objects)
            {
                if (!entry.Value.TryGetValue(Device1Interface, out IDictionary<string, object>? properties)
                    || !properties.TryGetValue("Address", out object? value)
                    || value is not string deviceMac)
                {
                    continue;
                }

                if (!string.Equals(deviceMac, mac, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                IDevice1 device = connection.CreateProxy<IDevice1>(BluezService, entry.Key);
                device.DisconnectAsync().GetAwaiter().GetResult();
                _log.Info($"Disconnected Bluetooth device {deviceMac}");
                return true;
            }

            _log.Warning($"Bluetooth device {mac} not found in BlueZ");
            return false;
        }
        catch (Exception ex)
        {
            _log.Warning($"Bluetooth disconnect failed for device {mac}: {ex.Message}");
            _log.LogExceptionDetails(ex);
            return false;
        }
    }

    /// <summary>
    /// Formats a 48-bit address as XX:XX:XX:XX:XX:XX, matching the BlueZ
    /// <c>Device1.Address</c> property format.
    /// </summary>
    private static string FormatMacAddress(ulong address)
    {
        Span<char> chars = stackalloc char[17];
        for (int i = 0; i < 6; i++)
        {
            if (i > 0)
            {
                chars[i * 3 - 1] = ':';
            }
            byte b = (byte)(address >> (8 * (5 - i)));
            chars[i * 3] = HexDigit(b >> 4);
            chars[i * 3 + 1] = HexDigit(b & 0x0F);
        }
        return new string(chars);
    }

    /// <summary>
    /// Converts a 4-bit nibble to its uppercase hex character.
    /// </summary>
    private static char HexDigit(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);
}

/// <summary>
/// org.freedesktop.DBus.ObjectManager proxy, used to enumerate BlueZ objects.
/// </summary>
[DBusInterface("org.freedesktop.DBus.ObjectManager")]
internal interface IObjectManager : IDBusObject
{
    /// <summary>
    /// Gets all managed objects: object path to interface name to property values.
    /// </summary>
    Task<IDictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>>> GetManagedObjectsAsync();
}

/// <summary>
/// org.bluez.Device1 proxy for a remote Bluetooth device.
/// </summary>
[DBusInterface("org.bluez.Device1")]
internal interface IDevice1 : IDBusObject
{
    /// <summary>
    /// Disconnects all profiles and terminates the low-level ACL connection.
    /// </summary>
    Task DisconnectAsync();
}