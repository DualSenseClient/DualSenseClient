using System.Runtime.InteropServices;

namespace DualSenseClient.Hid.Interop;

/// <summary>
/// Native <c>hid_device_info</c> linked-list node, matching the upstream HIDAPI
/// (libusb/hidapi 0.13.0+) layout. Note this intentionally differs from SDL3's
/// <c>SDL_hid_device_info</c> fork, which inserts <c>interface_class</c>,
/// <c>interface_subclass</c> and <c>interface_protocol</c> before the bus type
/// and places <c>next</c> last; upstream places <c>next</c> before
/// <c>bus_type</c> and has no interface class fields.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct HidDeviceInfoNative
{
    /// <summary>
    /// Platform-specific device path (null-terminated UTF-8).
    /// </summary>
    public byte* Path;

    /// <summary>
    /// Device vendor ID.
    /// </summary>
    public ushort VendorId;

    /// <summary>
    /// Device product ID.
    /// </summary>
    public ushort ProductId;

    /// <summary>
    /// Serial number (null-terminated native wchar_t string).
    /// </summary>
    public IntPtr SerialNumber;

    /// <summary>
    /// Device release number in binary-coded decimal.
    /// </summary>
    public ushort ReleaseNumber;

    /// <summary>
    /// Manufacturer string (null-terminated native wchar_t string).
    /// </summary>
    public IntPtr ManufacturerString;

    /// <summary>
    /// Product string (null-terminated native wchar_t string).
    /// </summary>
    public IntPtr ProductString;

    /// <summary>
    /// Usage page for this device/interface.
    /// </summary>
    public ushort UsagePage;

    /// <summary>
    /// Usage for this device/interface.
    /// </summary>
    public ushort Usage;

    /// <summary>
    /// The USB interface this logical device represents, or -1 when not a USB HID device.
    /// </summary>
    public int InterfaceNumber;

    /// <summary>
    /// Pointer to the next device in the enumeration, or <c>null</c>.
    /// </summary>
    public HidDeviceInfoNative* Next;

    /// <summary>
    /// Underlying bus type.
    /// </summary>
    public HidBusType BusType;
}