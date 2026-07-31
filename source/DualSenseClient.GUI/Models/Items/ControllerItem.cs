using DualSenseClient.Controllers;
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
    /// Creates a new controller item wrapping the given device.
    /// </summary>
    /// <param name="device">The controller device to display.</param>
    public ControllerItem(IControllerDevice device)
    {
        Device = device;
    }
}