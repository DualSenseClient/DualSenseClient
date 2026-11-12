using CommunityToolkit.Mvvm.ComponentModel;
using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.GUI.Services;
using DualSenseClient.Hid;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// Wraps a detected controller for display in the title bar combobox.
/// </summary>
public sealed partial class ControllerItem : ObservableObject
{
    /// <summary>
    /// The underlying controller device.
    /// </summary>
    public IControllerDevice Device { get; }

    /// <summary>
    /// Human-readable name shown in the title bar: the user's custom name for the
    /// controller, or the product name when none was set.
    /// </summary>
    [ObservableProperty] private string _displayName;

    /// <summary>
    /// Physical transport (USB or Bluetooth).
    /// </summary>
    public ConnectionType ConnectionType
    {
        get
        {
            return Device.ConnectionType;
        }
    }

    /// <summary>
    /// Whether this controller is connected through a DS5Dongle bridge.
    /// </summary>
    public bool IsDongle
    {
        get
        {
            return Device is DualSenseDongle;
        }
    }

    /// <summary>
    /// User-visible connection label that distinguishes a USB dongle from a direct
    /// USB or Bluetooth connection.
    /// </summary>
    public string ConnectionLabel
    {
        get
        {
            if (IsDongle)
            {
                return LocalizationService.GetText("Common.Connection.Dongle");
            }

            return ConnectionType switch
            {
                ConnectionType.Usb => LocalizationService.GetText("Common.Connection.Usb"),
                ConnectionType.Bluetooth => LocalizationService.GetText("Common.Connection.Bluetooth"),
                _ => LocalizationService.GetText("Common.Connection.Unknown")
            };
        }
    }

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
    /// Bridge status read from a DS5Dongle, or <c>null</c> for other connections.
    /// </summary>
    public DongleStatus? DongleStatus
    {
        get
        {
            return (Device as DualSenseDongle)?.DongleStatus;
        }
    }

    /// <summary>
    /// Creates a new controller item wrapping the given device.
    /// </summary>
    /// <param name="device">The controller device to display.</param>
    /// <param name="displayName">The name to show in the UI (custom name or product name).</param>
    public ControllerItem(IControllerDevice device, string displayName)
    {
        Device = device;
        _displayName = displayName;
        FirmwareInfo = (device as DualSenseDevice)?.FirmwareInfo;
        PairingInfo = (device as DualSenseDevice)?.PairingInfo;
    }
}