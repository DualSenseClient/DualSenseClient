using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.Callbacks;

/// <summary>
/// Receives haptics/speaker PCM from the host for a DualSense device.
/// <para>
/// The buffer is only valid during the call.
/// </para>
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void DSAudioCallback(nuint handle, IntPtr pcm, nuint length);