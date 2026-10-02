using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.Callbacks;

/// <summary>
/// Receives the rear voice-coil haptics pair (2ch S16LE @48kHz) for a DualSense device.
/// The buffer is only valid during the call.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void DSRealtimeHapticsCallback(nuint handle, IntPtr pcm, nuint length);