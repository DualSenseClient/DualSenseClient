using System.Runtime.InteropServices;
using DualSenseClient.VIIPER.DualSense;

namespace DualSenseClient.VIIPER.Callbacks;

/// <summary>
/// Receives a V5 output state paired with its 480-frame speaker PCM generation.
/// <para>
/// Each invocation pairs the native feedback output state with exactly that generation's
/// speaker PCM: two S16LE channels (front stereo) at 48 kHz, 1920 bytes.
/// The buffer is only valid during the call.
/// </para>
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void DSAtomicAudioHapticsCallback(nuint handle, DSOutputState output, IntPtr pcm, nuint length);