using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.Callbacks;

/// <summary>
/// Receives keyboard LED state changes from the host.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void KeyboardLEDCallback(nuint handle, byte leds);