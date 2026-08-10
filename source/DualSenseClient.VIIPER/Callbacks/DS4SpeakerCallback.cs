using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.Callbacks;

/// <summary>
/// Receives speaker PCM from the host for a DualShock 4 device. The buffer is only valid during the call.
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void DS4SpeakerCallback(nuint handle, IntPtr pcm, nuint length);