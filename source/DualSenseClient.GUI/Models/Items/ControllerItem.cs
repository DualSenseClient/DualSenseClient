using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.Hid;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// Wraps a detected controller for display in the title bar combobox.
/// </summary>
public sealed class ControllerItem
{
    /// <summary>
    /// The underlying controller device.
    /// </summary>
    public IControllerDevice Device { get; }

    /// <summary>
    /// Human-readable product name.
    /// </summary>
    public string DisplayName => Device.Info.ProductName;

    /// <summary>
    /// Physical transport (USB / Bluetooth).
    /// </summary>
    public ConnectionType ConnectionType => Device.ConnectionType;

    /// <summary>
    /// Firmware and hardware info read from the controller, or <c>null</c> if
    /// the device does not expose it or the read failed.
    /// </summary>
    public FirmwareInfo? FirmwareInfo { get; set; }

    /// <summary>
    /// Pairing information (controller and host Bluetooth MAC addresses) read from
    /// the controller, or <c>null</c> if the device does not expose it or the read failed.
    /// </summary>
    public PairingInfo? PairingInfo { get; set; }

    /// <summary>
    /// Creates a new controller item wrapping the given device.
    /// </summary>
    /// <param name="device">The controller device to display.</param>
    public ControllerItem(IControllerDevice device)
    {
        Device = device;
        FirmwareInfo = (device as DualSenseDevice)?.FirmwareInfo;
        PairingInfo = (device as DualSenseDevice)?.PairingInfo;
    }
}