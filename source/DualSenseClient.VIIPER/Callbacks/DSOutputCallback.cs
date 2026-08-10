using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.Callbacks;

/// <summary>
/// Receives rumble/LED output commands from the host for a DualSense device.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void DSOutputCallback(nuint handle, byte rumbleSmall, byte rumbleLarge, byte ledRed, byte ledGreen, byte ledBlue, byte playerLeds);