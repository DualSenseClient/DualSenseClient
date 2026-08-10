using System.Runtime.InteropServices;
using DualSenseClient.VIIPER.DualSense;

namespace DualSenseClient.VIIPER.Callbacks;

/// <summary>
/// Receives the full output state (including adaptive trigger blocks) from the host for a DualSense device.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void DSOutputStateCallback(nuint handle, DSOutputState output);