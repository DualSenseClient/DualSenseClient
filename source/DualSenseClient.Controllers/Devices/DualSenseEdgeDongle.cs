using DualSenseClient.Hid;

namespace DualSenseClient.Controllers.Devices;

/// <summary>
/// DS5Dongle bridge presenting itself as a DualSense Edge over USB.
/// </summary>
public sealed class DualSenseEdgeDongle : DualSenseDongle
{
    /// <summary>
    /// Creates a new DS5Dongle Edge bridge wrapper around an already-opened HID device.
    /// </summary>
    /// <param name="device">The opened HID device for this controller.</param>
    /// <param name="info">The device info that was used to discover and open the device.</param>
    public DualSenseEdgeDongle(IHidDevice device, IHidDeviceInfo info) : base(device, info)
    {
    }

    /// <inheritdoc/>
    public override ControllerType ControllerType
    {
        get
        {
            return ControllerType.DualSenseEdge;
        }
    }

    /// <summary>
    /// This bridge presents the connected controller as a DualSense Edge.
    /// </summary>
    public override bool IsEdge
    {
        get
        {
            return true;
        }
    }
}