using DualSenseClient.Hid;

namespace DualSenseClient.Controllers.Devices;

/// <summary>
/// DualSense connected through a DS5Dongle Bluetooth bridge. The host sees a wired USB
/// DualSense; this type owns the bridge-specific endpoints.
/// </summary>
public class DualSenseDongle : DualSenseDevice
{
    /// <summary>
    /// Creates a new DS5Dongle bridge wrapper around an already-opened HID device.
    /// </summary>
    /// <param name="device">The opened HID device for this controller.</param>
    /// <param name="info">The device info that was used to discover and open the device.</param>
    public DualSenseDongle(IHidDevice device, IHidDeviceInfo info) : base(device, info)
    {
    }
}
