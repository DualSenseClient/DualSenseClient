using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace DualSenseClient.Emulation.Windows;

/// <summary>
/// The plugin_hardware request of the usbip-win2 VHCI control device: the exact 1124-byte wire
/// image the driver expects for the <c>PLUGIN_HARDWARE</c> IOCTL (interface facts of usbip-win2
/// 0.9.8.1, x64; see the compat notes' VHCI driver IOCTL section). The buffer carries the IN self
/// size, OUT fields the driver fills with the assigned vhci hub port and location hash, and the
/// remote device location as NUL-terminated UTF-8 strings. The driver assigns the port during the
/// IOCTL and writes it back into offset 4 of the same (METHOD_BUFFERED) buffer.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = SizeInBytes)]
public struct VhciAttachRequest
{
    /// <summary>Recorded fact, x64: <c>sizeof(plugin_hardware)</c> in bytes (v0.9.8.1 added <c>location_hash</c>).</summary>
    public const int SizeInBytes = 1124;

    /// <summary>Offset 0: IN self size; the driver requires exactly <see cref="SizeInBytes"/>.</summary>
    [FieldOffset(0)] private uint _size;

    /// <summary>Offset 4: OUT driver-assigned hub port (>= 1), 0 in a request and on error.</summary>
    [FieldOffset(4)] private int _port;

    /// <summary>Offset 8: OUT hash of the remote location; 0 in a request, filled by the driver on success.</summary>
    [FieldOffset(8)] private uint _locationHash;

    /// <summary>Offset 12: remote bus id (<c>char busid[32]</c>, BUS_ID_SIZE), like the usbip <c>-b</c> argument.</summary>
    [FieldOffset(12)] private Utf8Buffer32 _busId;

    /// <summary>Offset 44: TCP/IP port number of the USB/IP server as a string (<c>char service[32]</c>).</summary>
    [FieldOffset(44)] private Utf8Buffer32 _service;

    /// <summary>Offset 76: hostname or IP of the USB/IP server (<c>char host[1025]</c>).</summary>
    [FieldOffset(76)] private Utf8Buffer1025 _host;

    /// <summary>Offset 1104: optional serial override (<c>char serial[16]</c>, SERIAL_BUFSZ); empty means the device serial.</summary>
    [FieldOffset(1104)] private Utf8Buffer16 _serial;

    /// <summary>Offset 1120: receive mode flag (<c>bool wsk_events</c>); 0 = zero-copy receive, the vendor default.</summary>
    [FieldOffset(1120)] private byte _wskEvents;

    /// <summary>
    /// Initializes an attach request for a remote device using the default receive mode and no
    /// serial override.
    /// </summary>
    /// <param name="host">Hostname or IP of the USB/IP server.</param>
    /// <param name="service">TCP/IP port number of the USB/IP server, as a string (e.g. <c>"3240"</c>).</param>
    /// <param name="busId">Remote bus id of the exported device (e.g. <c>"1-1"</c>).</param>
    public VhciAttachRequest(string host, string service, string busId)
        : this(host, service, busId, string.Empty, false)
    {
    }

    /// <summary>
    /// Initializes an attach request for a remote device.
    /// </summary>
    /// <param name="host">Hostname or IP of the USB/IP server.</param>
    /// <param name="service">TCP/IP port number of the USB/IP server, as a string (e.g. <c>"3240"</c>).</param>
    /// <param name="busId">Remote bus id of the exported device (e.g. <c>"1-1"</c>).</param>
    /// <param name="serial">Serial override; empty keeps the device's own serial.</param>
    /// <param name="wskEvents"><see langword="true"/> selects the WSK-event receive mode (low latency); the default zero-copy mode is used otherwise.</param>
    public VhciAttachRequest(string host, string service, string busId, string serial, bool wskEvents)
    {
        _size = SizeInBytes;
        _port = 0;
        _locationHash = 0;
        _busId = default;
        _service = default;
        _host = default;
        _serial = default;
        _wskEvents = 0;

        Encoding.UTF8.GetBytes(busId, _busId);
        Encoding.UTF8.GetBytes(service, _service);
        Encoding.UTF8.GetBytes(host, _host);
        Encoding.UTF8.GetBytes(serial, _serial);
        _wskEvents = wskEvents ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// Copies the exact plugin_hardware request bytes (always <see cref="SizeInBytes"/>, including
    /// the zeroed padding, NUL terminators and the default port 0) to the start of
    /// <paramref name="destination"/>. Strings are encoded as UTF-8 per the vendor interface.
    /// Bytes beyond <see cref="SizeInBytes"/> are left untouched.
    /// </summary>
    /// <param name="destination">Destination buffer for the request image.</param>
    /// <exception cref="ArgumentException">The destination span is smaller than <see cref="SizeInBytes"/> (programming error).</exception>
    public void Write(Span<byte> destination)
    {
        if (destination.Length < SizeInBytes)
        {
            throw new ArgumentException($"The destination must be at least {SizeInBytes} bytes long.", nameof(destination));
        }

        MemoryMarshal.Write(destination, in this);
    }

    /// <summary>Fixed 32-byte UTF-8 string buffer (BUS_ID_SIZE / NI_MAXSERV sized fields).</summary>
    [InlineArray(32)]
    private struct Utf8Buffer32
    {
        private byte _element0;
    }

    /// <summary>Fixed 1025-byte UTF-8 string buffer (NI_MAXHOST sized host field).</summary>
    [InlineArray(1025)]
    private struct Utf8Buffer1025
    {
        private byte _element0;
    }

    /// <summary>Fixed 16-byte UTF-8 string buffer (SERIAL_BUFSZ sized serial field).</summary>
    [InlineArray(16)]
    private struct Utf8Buffer16
    {
        private byte _element0;
    }
}

/// <summary>
/// IOCTL codes of the usbip-win2 VHCI control device (recorded interface facts: each code is
/// <c>CTL_CODE(FILE_DEVICE_UNKNOWN, function, METHOD_BUFFERED, FILE_READ_DATA | FILE_WRITE_DATA)</c>
/// with a 12-bit function starting at 0x800; see the compat notes).
/// </summary>
internal static class VhciIoctl
{
    /// <summary><c>PLUGIN_HARDWARE</c> (function 0x800): attach a remote device; the request and response are the 1124-byte plugin_hardware image.</summary>
    internal const uint PluginHardware = 0x0022E000;

    /// <summary>
    /// <c>PLUGOUT_HARDWARE</c> (function 0x801): detach the device on a previously assigned hub port;
    /// the request is the 8-byte plugout_hardware image.
    /// </summary>
    internal const uint PlugoutHardware = 0x0022E004;
}