using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER;

/// <summary>
/// Configuration for a new USB server.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct USBServerConfig
{
    /// <summary>
    /// Listen address, e.g. "localhost:3245". Empty string uses the default ":3241".
    /// </summary>
    [MarshalAs(UnmanagedType.LPUTF8Str)] public string addr;

    /// <summary>
    /// Connection timeout in milliseconds (default 30000).
    /// </summary>
    public ulong connection_timeout_ms;

    /// <summary>
    /// Device handler connect timeout in milliseconds (default 5000).
    /// </summary>
    public ulong device_handler_connect_timeout_ms;

    /// <summary>
    /// Write batch flush interval in milliseconds (default 1).
    /// </summary>
    public uint write_batch_flush_interval_ms;
}