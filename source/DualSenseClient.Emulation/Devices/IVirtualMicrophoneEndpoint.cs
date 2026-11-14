using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Emulation.Devices;

/// <summary>
/// Capture endpoint of a virtual controller persona: the microphone streaming interface of the
/// persona's UAC1 audio function. A producer feeds capture PCM through <see cref="SetMicrophonePcm"/>
/// while the host drains it on the persona's ISO IN endpoint, and follows
/// <see cref="MicrophoneActive"/> to start and stop the stream with the interface's alternate
/// setting. Implementations never change the persona's audio format: <see cref="MicrophoneFormat"/>
/// reports the fixed geometry the persona serves.
/// </summary>
public interface IVirtualMicrophoneEndpoint
{
    /// <summary>
    /// Whether the microphone streaming interface 2 is streaming (<see langword="true"/> while it is
    /// at alternate setting 1). SET_CONFIGURATION 0 closes the interface and clears this flag.
    /// </summary>
    bool MicrophoneActive { get; }

    /// <summary>
    /// Fixed geometry of the persona's microphone path: channel count, sample rate and the exact
    /// number of bytes of one PCM frame accepted by <see cref="SetMicrophonePcm"/>.
    /// </summary>
    AudioFunctionFormat MicrophoneFormat { get; }

    /// <summary>
    /// Raised once per effective change of <see cref="MicrophoneActive"/>, synchronously on the
    /// thread that delivered the opening or closing control request.
    /// </summary>
    event Action<bool>? MicrophoneActiveChanged;

    /// <summary>
    /// Raised after the persona served one packet of its microphone ISO IN endpoint (0x82), so a
    /// producer can pace exactly one capture frame per served packet.
    /// </summary>
    event Action? MicrophonePacketServed;

    /// <summary>
    /// Enqueues one whole microphone PCM frame: interleaved 16-bit little-endian samples of
    /// <see cref="AudioFunctionFormat.MicrophoneFrameBytes"/> bytes.
    /// </summary>
    /// <param name="frame">Exactly one microphone frame, as reported by <see cref="MicrophoneFormat"/>.</param>
    void SetMicrophonePcm(ReadOnlySpan<byte> frame);

    /// <summary>
    /// Drops every queued capture byte so the next served packet starts on a frame boundary.
    /// </summary>
    void ClearMicrophonePcm();
}