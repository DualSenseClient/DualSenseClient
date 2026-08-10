using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.Callbacks;

/// <summary>
/// Receives log messages from the USB server.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void VIIPERLogCallback(VIIPERLogLevel level, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);