using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.Callbacks;

/// <summary>
/// Invoked when the DualSense haptics audio interface is reset or its alternate setting changes.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void DSSpeakerResetCallback(nuint handle);