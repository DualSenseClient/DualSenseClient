using DualSenseClient.Controllers.Devices;
using DualSenseClient.Hid;

namespace DualSenseClient.Controllers;

/// <summary>
/// Static factory that maps USB vendor/product ID pairs to known controller types
/// and instantiates the correct <see cref="IControllerDevice"/> implementation.
/// </summary>
public static class ControllerFactory
{
    /// <summary>
    /// Maps USB vendor/product ID pairs to known <see cref="ControllerType"/> values.
    /// </summary>
    private static readonly Dictionary<(ushort Vid, ushort Pid), ControllerType> KnownDevices = new Dictionary<(ushort Vid, ushort Pid), ControllerType>
    {
        { (0x054C, 0x0CE6), ControllerType.DualSense }
    };

    /// <summary>
    /// Gets the set of (VendorId, ProductId) pairs this factory recognizes.
    /// </summary>
    public static IEnumerable<(ushort VendorId, ushort ProductId)> KnownDeviceIds
        => KnownDevices.Keys.Select(k => (VendorId: k.Vid, ProductId: k.Pid));

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
            return null;
        }

        IHidDevice device = enumerator.OpenDevice(info.Path);
        return type switch
        {
            ControllerType.DualSense => new DualSenseDevice(device, info),
            _ => null
        };
    }
}