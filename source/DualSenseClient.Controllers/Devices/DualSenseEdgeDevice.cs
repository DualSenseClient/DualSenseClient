using DualSenseClient.Hid;

namespace DualSenseClient.Controllers.Devices;

/// <summary>
/// Concrete controller implementation for the Sony DualSense Edge. The Edge speaks
/// the same protocol as the base DualSense; this subclass only marks the device as
/// an Edge so the extra Fn buttons and back paddles are surfaced and the always-on
/// "vibration v2" rumble encoding is used.
/// </summary>
public sealed class DualSenseEdgeDevice : DualSenseDevice
{
    /// <summary>
    /// Creates a new DualSense Edge wrapper around an already-opened HID device.
    /// Profiles are not applied here; the owning application applies a profile later
    /// via <see cref="ApplyProfile"/> once the device is connected.
    /// </summary>
    /// <param name="device">The opened HID device for this controller.</param>
    /// <param name="info">The device info that was used to discover and open the device.</param>
    public DualSenseEdgeDevice(IHidDevice device, IHidDeviceInfo info) : base(device, info)
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
    /// This controller is a DualSense Edge: it has the extra Fn buttons and back
    /// paddles and always uses the "vibration v2" rumble encoding.
    /// </summary>
    public override bool IsEdge
    {
        get
        {
            return true;
        }
    }
}