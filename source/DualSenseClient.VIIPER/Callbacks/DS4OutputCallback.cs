using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.Callbacks;

/// <summary>
/// Receives rumble/LED/flash output commands from the host for a DualShock 4 device.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void DS4OutputCallback(nuint handle, byte rumbleSmall, byte rumbleLarge, byte ledRed, byte ledGreen, byte ledBlue, byte flashOn, byte flashOff);