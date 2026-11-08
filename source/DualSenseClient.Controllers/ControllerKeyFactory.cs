using DualSenseClient.Controllers.Devices;
using DualSenseClient.Core.Models;

namespace DualSenseClient.Controllers;

/// <summary>
/// Builds a <see cref="ControllerKey"/> from a connected controller.
/// </summary>
public static class ControllerKeyFactory
{
    /// <summary>
    /// Creates the normalized key for the given controller: its Bluetooth MAC when the
    /// concrete device exposes one, plus its HID device path.
    /// </summary>
    /// <param name="device">The connected controller.</param>
    /// <returns>The controller key.</returns>
    public static ControllerKey From(IControllerDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        string? mac = (device as DualSenseDevice)?.PairingInfo?.ClientMac;
        return ControllerKey.From(mac, device.Info.Path);
    }
}