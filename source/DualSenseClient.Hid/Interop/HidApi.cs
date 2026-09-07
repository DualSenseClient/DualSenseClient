using System.Runtime.InteropServices;

namespace DualSenseClient.Hid.Interop;

/// <summary>
/// P/Invoke interop for the native HIDAPI library (libusb/hidapi 0.15.x).
/// Only the subset used by <see cref="HidDevice"/> and
/// <see cref="HidDeviceEnumerator"/> is declared, plus <see cref="GetError"/>
/// for diagnostics.
/// </summary>
internal static unsafe class HidApi
{
    /// <summary>
    /// Name of the native library, resolved by <see cref="HidNativeLibrary"/>
    /// to the bundled binary or the system HIDAPI library.
    /// </summary>
    private const string Library = "hidapi";

    /// <summary>
    /// Registers <see cref="HidNativeLibrary"/> before any P/Invoke call so the
    /// runtime resolves "hidapi" to the bundled or system native binary.
    /// </summary>
    static HidApi()
    {
        HidNativeLibrary.Register();
    }

    /// <summary>
    /// Release tag of the native hidapi library (e.g. "hidapi-0.15.0"),
    /// or null if no version information was embedded.
    /// </summary>
    public static string? NativeLibraryVersion
    {
        get
        {
            return HidNativeLibrary.NativeVersion;
        }
    }

    /// <summary>
    /// Initializes the HIDAPI library. Safe to call multiple times.
    /// </summary>
    /// <returns>0 on success, -1 on error.</returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "hid_init", ExactSpelling = true)]
    internal static extern int Init();

    /// <summary>
    /// Frees all static data associated with HIDAPI. Call at shutdown.
    /// </summary>
    /// <returns>0 on success, -1 on error.</returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "hid_exit", ExactSpelling = true)]
    internal static extern int Exit();

    /// <summary>
    /// Enumerates HID devices matching the given vendor/product IDs (0 matches any).
    /// The caller must free the result with <see cref="FreeEnumeration"/>.
    /// </summary>
    /// <param name="vendorId">Vendor ID to match, or 0 for any.</param>
    /// <param name="productId">Product ID to match, or 0 for any.</param>
    /// <returns>Head of the device info linked list, or <c>null</c> on failure or when no devices are present.</returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "hid_enumerate", ExactSpelling = true)]
    internal static extern HidDeviceInfoNative* Enumerate(ushort vendorId, ushort productId);

    /// <summary>
    /// Frees an enumeration linked list created by <see cref="Enumerate"/>.
    /// </summary>
    /// <param name="devices">Head of the list, or <c>null</c>.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "hid_free_enumeration", ExactSpelling = true)]
    internal static extern void FreeEnumeration(HidDeviceInfoNative* devices);

    /// <summary>
    /// Opens a HID device by its platform path (null-terminated UTF-8).
    /// </summary>
    /// <param name="path">Platform device path.</param>
    /// <returns>Device handle, or <c>null</c> on failure.</returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "hid_open_path", ExactSpelling = true)]
    internal static extern HidDeviceHandle* OpenPath(byte* path);

    /// <summary>
    /// Writes an output report. The first byte of <paramref name="data"/> must contain the report ID.
    /// </summary>
    /// <param name="device">Open device handle.</param>
    /// <param name="data">Report bytes including the report ID.</param>
    /// <param name="length">Bytes to write.</param>
    /// <returns>Actual bytes written, or -1 on error.</returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "hid_write", ExactSpelling = true)]
    internal static extern int Write(HidDeviceHandle* device, byte* data, nuint length);

    /// <summary>
    /// Reads an input report with a timeout (-1 blocks indefinitely).
    /// </summary>
    /// <param name="device">Open device handle.</param>
    /// <param name="data">Buffer for the report bytes.</param>
    /// <param name="length">Bytes to read.</param>
    /// <param name="milliseconds">Timeout in milliseconds, or -1 to block.</param>
    /// <returns>Actual bytes read, 0 on timeout, or -1 on error.</returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "hid_read_timeout", ExactSpelling = true)]
    internal static extern int ReadTimeout(HidDeviceHandle* device, byte* data, nuint length, int milliseconds);

    /// <summary>
    /// Sends a feature report. The first byte of <paramref name="data"/> must contain the report ID.
    /// </summary>
    /// <param name="device">Open device handle.</param>
    /// <param name="data">Report bytes including the report ID.</param>
    /// <param name="length">Bytes to send.</param>
    /// <returns>Actual bytes written, or -1 on error.</returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "hid_send_feature_report", ExactSpelling = true)]
    internal static extern int SendFeatureReport(HidDeviceHandle* device, byte* data, nuint length);

    /// <summary>
    /// Gets a feature report. Set the first byte of <paramref name="data"/> to the report ID.
    /// </summary>
    /// <param name="device">Open device handle.</param>
    /// <param name="data">Buffer for the report bytes including the report ID.</param>
    /// <param name="length">Bytes to read.</param>
    /// <returns>Bytes read plus one for the report ID, or -1 on error.</returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "hid_get_feature_report", ExactSpelling = true)]
    internal static extern int GetFeatureReport(HidDeviceHandle* device, byte* data, nuint length);

    /// <summary>
    /// Gets the product string from a device.
    /// </summary>
    /// <param name="device">Open device handle.</param>
    /// <param name="str">Wide-character buffer.</param>
    /// <param name="maxlen">Buffer length in multiples of wchar_t.</param>
    /// <returns>0 on success, -1 on error.</returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "hid_get_product_string", ExactSpelling = true)]
    internal static extern int GetProductString(HidDeviceHandle* device, IntPtr str, nuint maxlen);

    /// <summary>
    /// Gets device info for an open device. The returned pointer is owned by the
    /// device and must not be freed; it stays valid until <see cref="Close"/>.
    /// </summary>
    /// <param name="device">Open device handle.</param>
    /// <returns>Pointer to the device info, or <c>null</c> on failure.</returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "hid_get_device_info", ExactSpelling = true)]
    internal static extern HidDeviceInfoNative* GetDeviceInfo(HidDeviceHandle* device);

    /// <summary>
    /// Closes a HID device.
    /// </summary>
    /// <param name="device">Open device handle.</param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "hid_close", ExactSpelling = true)]
    internal static extern void Close(HidDeviceHandle* device);

    /// <summary>
    /// Gets a string describing the last error. Pass <c>null</c> for non-device-specific errors.
    /// The returned pointer is owned by HIDAPI and must not be freed.
    /// </summary>
    /// <param name="device">Device handle, or <c>null</c> for the last global error.</param>
    /// <returns>Pointer to the error string (never null for a valid handle).</returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "hid_error", ExactSpelling = true)]
    private static extern IntPtr hid_error(HidDeviceHandle* device);

    /// <summary>
    /// Gets a managed string describing the last native error.
    /// </summary>
    /// <param name="device">Device handle, or <c>null</c> for the last global error.</param>
    /// <returns>The error description, or <see cref="string.Empty"/> when unavailable.</returns>
    internal static string GetError(HidDeviceHandle* device) => HidStringMarshal.PtrToString(hid_error(device));

    /// <summary>
    /// Gets a managed string describing the last global (non-device-specific) native error.
    /// </summary>
    /// <returns>The error description, or <see cref="string.Empty"/> when unavailable.</returns>
    internal static string GetError() => HidStringMarshal.PtrToString(hid_error(null));
}