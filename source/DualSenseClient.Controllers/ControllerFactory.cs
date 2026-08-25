using DualSenseClient.Controllers.Devices;
using DualSenseClient.Hid;
using DualSenseClient.Logging;

namespace DualSenseClient.Controllers;

/// <summary>
/// Static factory that maps USB vendor/product ID pairs to known controller types
/// and instantiates the correct <see cref="IControllerDevice"/> implementation.
/// </summary>
public static class ControllerFactory
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("ControllerFactory");

    /// <summary>
    /// Maps USB vendor/product ID pairs to known <see cref="ControllerType"/> values.
    /// </summary>
    private static readonly Dictionary<(ushort Vid, ushort Pid), ControllerType> KnownDevices = new Dictionary<(ushort Vid, ushort Pid), ControllerType>
    {
        {
            (0x054C, 0x0CE6), ControllerType.DualSense
        },
        {
            (0x054C, 0x0DF2), ControllerType.DualSenseEdge
        }
    };

    /// <summary>
    /// Gets the set of (VendorId, ProductId) pairs this factory recognizes.
    /// </summary>
    public static IEnumerable<(ushort VendorId, ushort ProductId)> KnownDeviceIds
    {
        get
        {
            return KnownDevices.Keys.Select(k => (VendorId: k.Vid, ProductId: k.Pid));
        }
    }

    /// <summary>
    /// Resolves the <see cref="ControllerType"/> for a given device without opening it.
    /// Returns <see cref="ControllerType.Unknown"/> if the device is not recognized.
    /// </summary>
    public static ControllerType GetType(IHidDeviceInfo info)
        => KnownDevices.GetValueOrDefault((info.VendorId, info.ProductId), ControllerType.Unknown);

    /// <summary>
    /// Opens the device and creates the correct <see cref="IControllerDevice"/> for it.
    /// Returns <c>null</c> if the device is not a recognized controller.
    /// </summary>
    /// <param name="enumerator">The enumerator used to open the device by path.</param>
    /// <param name="info">The device info from enumeration.</param>
    /// <returns>A typed controller wrapper, or <c>null</c> for unknown devices.</returns>
    public static IControllerDevice? Create(IHidDeviceEnumerator enumerator, IHidDeviceInfo info)
    {
        ControllerType type = GetType(info);
        if (type == ControllerType.Unknown)
        {
            _log.Warning($"Create called for unknown device: {info.ProductName} (VID=0x{info.VendorId:X4}, PID=0x{info.ProductId:X4})");
            return null;
        }

        _log.Debug(
            $"Creating {type} controller for '{info.ProductName}' (VID=0x{info.VendorId:X4}, PID=0x{info.ProductId:X4}, bus={info.BusType}, path={info.Path})");
        IHidDevice device = enumerator.OpenDevice(info.Path);
        try
        {
            return type switch
            {
                ControllerType.DualSense => new DualSenseDevice(device, info),
                ControllerType.DualSenseEdge => new DualSenseEdgeDevice(device, info),
                _ => null
            };
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }
}