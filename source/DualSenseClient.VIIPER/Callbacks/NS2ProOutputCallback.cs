using System.Runtime.InteropServices;
using DualSenseClient.VIIPER.NS2Pro;

namespace DualSenseClient.VIIPER.Callbacks;

/// <summary>
/// Receives the full output state (HD rumble data, flags, player LED mask) from the host for an NS2Pro device.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void NS2ProOutputCallback(nuint handle, NS2ProOutputState output);