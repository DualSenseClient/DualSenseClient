using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.Callbacks;

/// <summary>
/// Receives rumble/motor commands from the host for an Xbox 360 device.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void Xbox360RumbleCallback(nuint handle, byte leftMotor, byte rightMotor);