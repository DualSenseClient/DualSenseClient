using System.Runtime.InteropServices;
using DualSenseClient.VIIPER.DualSense;

namespace DualSenseClient.VIIPER.Callbacks;

/// <summary>
/// Receives low-latency rear haptics output from the host for a DualSense device.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void DSRealtimeHapticsCallback(nuint handle, DSOutputState output);