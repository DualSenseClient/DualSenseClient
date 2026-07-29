using DualSenseClient.Hid;

namespace DualSenseClient.Controllers.Devices;

/// <summary>
/// Concrete controller implementation for the Sony DualSense (PS5) controller.
/// Opens and communicates with the DualSense over USB or Bluetooth via SDL3 HID.
/// </summary>
public sealed class DualSenseDevice : ControllerDevice
{
    /// <inheritdoc/>
    public override ControllerType ControllerType => ControllerType.DualSense;

    /// <summary>
    /// Creates a new DualSense controller wrapper around an already-opened HID device.
    /// </summary>
    /// <param name="device">The opened HID device for this controller.</param>
    /// <param name="info">The device info that was used to discover and open the device.</param>
    public DualSenseDevice(IHidDevice device, IHidDeviceInfo info) : base(device, info)
    {
    }
}