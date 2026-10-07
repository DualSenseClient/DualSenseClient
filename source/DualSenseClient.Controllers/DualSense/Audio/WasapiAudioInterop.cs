using System.Runtime.InteropServices;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Minimal WASAPI COM interop for application (process) loopback capture: the official
/// <c>ActivateAudioInterfaceAsync</c> activation path with
/// <c>AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK</c>, plus the pieces of
/// <c>IAudioClient</c>/<c>IAudioCaptureClient</c> the capture needs. Only usable on
/// Windows 10 build 20348 (Windows 11) or later; the caller checks that first.
/// </summary>
internal static class WasapiAudioInterop
{
    /// <summary>
    /// IID of <c>IAudioClient</c> ({1CB9AD4C-DBFA-4C32-B178-C2F568A703B2}).
    /// </summary>
    public static readonly Guid AudioClientIid = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");

    /// <summary>
    /// IID of <c>IAudioCaptureClient</c> ({C8ADBD64-E71E-48A0-A4DE-185C395CD317}).
    /// </summary>
    public static readonly Guid AudioCaptureClientIid = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

    /// <summary>
    /// The virtual device path that activates a process loopback stream.
    /// </summary>
    public const string ProcessLoopbackDevice = @"VAD\Process_Loopback";

    /// <summary>
    /// <c>AUDCLNT_SHAREMODE_SHARED</c>.
    /// </summary>
    public const int ShareModeShared = 0;

    /// <summary>
    /// <c>AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK |
    /// AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM</c>. Event callbacks let a waiter thread poll
    /// the capture client instead of spinning; auto-convert lets the caller pick the
    /// format (required, because process-loopback clients reject <c>GetMixFormat</c>).
    /// </summary>
    public const int StreamFlags = 0x00020000 | 0x00040000 | unchecked((int)0x80000000);

    /// <summary>
    /// <c>AUDCLNT_BUFFERFLAGS_SILENT</c>: the packet carries no valid data and must be
    /// treated as silence.
    /// </summary>
    public const int BufferFlagSilent = 0x2;

    /// <summary>
    /// <c>WAVE_FORMAT_IEEE_FLOAT</c>, the format requested from the engine.
    /// </summary>
    public const ushort WaveFormatIeeeFloat = 3;

    /// <summary>
    /// <c>AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK</c>.
    /// </summary>
    private const int ActivationTypeProcessLoopback = 1;

    /// <summary>
    /// <c>PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE</c>: capture the target
    /// process and its children, and nothing else.
    /// </summary>
    private const int ProcessLoopbackModeIncludeTree = 0;

    /// <summary>
    /// <c>VT_BLOB</c>: the activation parameters are passed as a binary blob.
    /// </summary>
    private const ushort VariantTypeBlob = 65;

    /// <summary>
    /// Size of the native <c>AUDIOCLIENT_ACTIVATION_PARAMS</c> for process loopback:
    /// the 4-byte activation type followed by the 4-byte process id and 4-byte mode of
    /// the union member.
    /// </summary>
    private const int ActivationParamsSize = 12;

    /// <summary>
    /// Starts activating the process-loopback audio interface for the given process id.
    /// <paramref name="completionHandler"/> is signalled when the result is available.
    /// Returns the HRESULT of the activation request itself.
    /// </summary>
    public static int ActivateProcessLoopback(uint processId,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation)
    {
        IntPtr propvariant = CreateActivationParams(processId, out IntPtr paramsPointer);
        Guid iid = AudioClientIid;
        try
        {
            return ActivateAudioInterfaceAsync(ProcessLoopbackDevice, ref iid, propvariant, completionHandler,
                out activationOperation);
        }
        finally
        {
            // ActivateAudioInterfaceAsync copies the activation parameters before it
            // returns, so the buffers can be released here.
            FreeActivationParams(propvariant, paramsPointer);
        }
    }

    /// <summary>
    /// Builds the format the engine converts captured audio into: 48 kHz interleaved
    /// stereo 32-bit float, matching the forwarder's stream format.
    /// </summary>
    public static WaveFormatEx CreateStereoFloatFormat(int samplesPerSecond)
    {
        const ushort channels = 2;
        const ushort bytesPerSample = 4;
        return new WaveFormatEx
        {
            FormatTag = WaveFormatIeeeFloat,
            Channels = channels,
            SamplesPerSec = (uint)samplesPerSecond,
            AvgBytesPerSec = (uint)(samplesPerSecond * channels * bytesPerSample),
            BlockAlign = channels * bytesPerSample,
            BitsPerSample = bytesPerSample * 8,
            ExtraSize = 0
        };
    }

    /// <summary>
    /// Allocates a <c>PROPVARIANT</c> carrying the process-loopback activation
    /// parameters, as <c>ActivateAudioInterfaceAsync</c> expects (<c>VT_BLOB</c> over an
    /// <c>AUDIOCLIENT_ACTIVATION_PARAMS</c>). <paramref name="paramsPointer"/> receives
    /// the parameter block to release with <see cref="FreeActivationParams"/>.
    /// </summary>
    private static IntPtr CreateActivationParams(uint processId, out IntPtr paramsPointer)
    {
        paramsPointer = Marshal.AllocHGlobal(ActivationParamsSize);
        ZeroMemory(paramsPointer, ActivationParamsSize);
        Marshal.WriteInt32(paramsPointer, 0, ActivationTypeProcessLoopback);
        Marshal.WriteInt32(paramsPointer, 4, unchecked((int)processId));
        Marshal.WriteInt32(paramsPointer, 8, ProcessLoopbackModeIncludeTree);

        // PROPVARIANT: the VARTYPE sits at offset 0 and the union at offset 8 on both
        // x86 and x64. The BLOB member's pointer follows its cbSize inside the union
        // (offset 8 on x64, 4 on x86), so the absolute offsets are 16 and 12.
        int propvariantSize = 8 + (IntPtr.Size == 8 ? 16 : 8);
        int blobPointerOffset = 8 + (IntPtr.Size == 8 ? 8 : 4);
        IntPtr propvariant = Marshal.AllocHGlobal(propvariantSize);
        ZeroMemory(propvariant, propvariantSize);
        Marshal.WriteInt16(propvariant, 0, unchecked((short)VariantTypeBlob));
        Marshal.WriteInt32(propvariant, 8, ActivationParamsSize);
        Marshal.WriteIntPtr(propvariant, blobPointerOffset, paramsPointer);
        return propvariant;
    }

    /// <summary>
    /// Releases the pointers returned by <see cref="CreateActivationParams"/>.
    /// </summary>
    private static void FreeActivationParams(IntPtr propvariant, IntPtr paramsPointer)
    {
        if (paramsPointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(paramsPointer);
        }

        if (propvariant != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(propvariant);
        }
    }

    /// <summary>
    /// Zeroes a block of unmanaged memory byte by byte, so the struct padding the
    /// native side may read is deterministic.
    /// </summary>
    private static void ZeroMemory(IntPtr pointer, int size)
    {
        for (int i = 0; i < size; i++)
        {
            Marshal.WriteByte(pointer, i, 0);
        }
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true)]
    private static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid riid,
        IntPtr activationParams,
        [MarshalAs(UnmanagedType.Interface)] IActivateAudioInterfaceCompletionHandler completionHandler,
        [MarshalAs(UnmanagedType.Interface)] out IActivateAudioInterfaceAsyncOperation activationOperation);
}

/// <summary>
/// Native <c>WAVEFORMATEX</c>, packed so field offsets match the native layout exactly.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WaveFormatEx
{
    /// <summary><c>wFormatTag</c>.</summary>
    public ushort FormatTag;

    /// <summary><c>nChannels</c>.</summary>
    public ushort Channels;

    /// <summary><c>nSamplesPerSec</c>.</summary>
    public uint SamplesPerSec;

    /// <summary><c>nAvgBytesPerSec</c>.</summary>
    public uint AvgBytesPerSec;

    /// <summary><c>nBlockAlign</c>.</summary>
    public ushort BlockAlign;

    /// <summary><c>wBitsPerSample</c>.</summary>
    public ushort BitsPerSample;

    /// <summary><c>cbSize</c>.</summary>
    public ushort ExtraSize;
}

/// <summary>
/// <c>IActivateAudioInterfaceCompletionHandler</c>: implemented by the capture so the
/// audio engine can report the activation result.
/// </summary>
[ComImport]
[Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    /// <summary>
    /// Called by the audio engine when activation finished, on an MTA thread.
    /// </summary>
    void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
}

/// <summary>
/// <c>IActivateAudioInterfaceAsyncOperation</c>: carries the result of an activation.
/// </summary>
[ComImport]
[Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceAsyncOperation
{
    /// <summary>
    /// Retrieves the activation result and the activated interface.
    /// </summary>
    [PreserveSig]
    int GetActivateResult(out int activateResult,
        [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
}

/// <summary>
/// <c>IAudioClient</c>: the subset of the vtable the process-loopback capture uses.
/// Method order is significant; it mirrors the native vtable.
/// </summary>
[ComImport]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    /// <summary>Initializes the audio stream.</summary>
    [PreserveSig]
    int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, ref WaveFormatEx format,
        IntPtr audioSessionGuid);

    /// <summary>Gets the size of the capture buffer.</summary>
    [PreserveSig]
    int GetBufferSize(out uint bufferFrames);

    /// <summary>Gets the stream latency.</summary>
    [PreserveSig]
    int GetStreamLatency(out long latency);

    /// <summary>Gets the number of frames in the buffer.</summary>
    [PreserveSig]
    int GetCurrentPadding(out uint padding);

    /// <summary>Tests a format for support.</summary>
    [PreserveSig]
    int IsFormatSupported(int shareMode, ref WaveFormatEx format, out IntPtr closestMatch);

    /// <summary>Gets the device mix format.</summary>
    [PreserveSig]
    int GetMixFormat(out IntPtr format);

    /// <summary>Gets the device period.</summary>
    [PreserveSig]
    int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

    /// <summary>Starts the stream.</summary>
    [PreserveSig]
    int Start();

    /// <summary>Stops the stream.</summary>
    [PreserveSig]
    int Stop();

    /// <summary>Resets the stream.</summary>
    [PreserveSig]
    int Reset();

    /// <summary>Sets the event the engine signals when a buffer is ready.</summary>
    [PreserveSig]
    int SetEventHandle(IntPtr eventHandle);

    /// <summary>Retrieves a service from the audio client.</summary>
    [PreserveSig]
    int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

/// <summary>
/// <c>IAudioCaptureClient</c>: reads the captured packets. Method order mirrors the
/// native vtable.
/// </summary>
[ComImport]
[Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    /// <summary>Gets a pointer to the next packet's data.</summary>
    [PreserveSig]
    int GetBuffer(out IntPtr data, out uint framesToRead, out int flags, out ulong devicePosition,
        out ulong qpcPosition);

    /// <summary>Releases a packet previously obtained with <see cref="GetBuffer"/>.</summary>
    [PreserveSig]
    int ReleaseBuffer(uint framesRead);

    /// <summary>Gets the size of the next available packet.</summary>
    [PreserveSig]
    int GetNextPacketSize(out uint framesInNextPacket);
}