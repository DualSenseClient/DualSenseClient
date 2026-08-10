using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.Callbacks;

/// <summary>
/// Invoked when the DualShock 4 speaker audio interface is reset or its alternate setting changes.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void DS4SpeakerResetCallback(nuint handle);