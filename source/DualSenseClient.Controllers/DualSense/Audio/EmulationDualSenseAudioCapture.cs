using System.Diagnostics;
using DualSenseClient.Logging;
using DualSenseClient.Emulation.Devices.DualSense;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Captures the speaker audio the host renders to an Emulation DualSense persona and forwards it to
/// an <see cref="IAudioPcmSink"/> (the <see cref="ViiperDualSenseAudioForwarder"/>), so the
/// physical controller plays the game's audio.
/// </summary>
/// <remarks>
/// <para>
/// The persona raises <see cref="DualSenseDevice.AudioOutReceived"/> with the exact bytes the host
/// wrote to the speaker endpoint: four S16LE channels at 48 kHz — the front stereo speaker pair
/// plus the rear voice-coil haptics pair (channel configuration 0x0033). Only the front pair is
/// forwarded as speaker audio; this capture does not produce haptics. The game's haptics are
/// surfaced by the persona's separate <see cref="DualSenseDevice.RealtimeHapticsReceived"/> lane,
/// which is the primary path into the forwarder (its front-audio fallback is not used here).
/// The event memory is a view over the persona's reusable scratch buffer and is only valid
/// during the callback, so the bytes are copied and converted to interleaved stereo float
/// synchronously, before the callback returns.
/// </para>
/// <para>
/// <see cref="DualSenseDevice.SpeakerResetReceived"/> marks a stream-generation barrier: the host
/// reopened or re-alternated the speaker interface, so the sink is flushed and the forwarded lane
/// re-primes on the next block.
/// </para>
/// </remarks>
public sealed class EmulationDualSenseAudioCapture : IDisposable, IViiperAudioCapture
{
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("EmulationDualSenseAudioCapture");

    /// <summary>
    /// Bytes per frame of the endpoint layout: 4 channels of S16LE.
    /// </summary>
    private const int QuadBytesPerFrame = sizeof(short) * 4;

    /// <summary>
    /// The persona whose speaker endpoint is captured.
    /// </summary>
    private readonly DualSenseDevice _device;

    /// <summary>
    /// The sink receiving the converted audio.
    /// </summary>
    private readonly IAudioPcmSink _sink;

    /// <summary>
    /// Keeps the subscriber identity so <see cref="Dispose"/> unsubscribes exactly these handlers.
    /// </summary>
    private readonly Action<ReadOnlyMemory<byte>> _audioHandler;

    private readonly Action _resetHandler;

    /// <summary>
    /// Serializes callback invocations, disposal and the scratch buffers.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// Scratch buffer for the raw S16LE bytes copied out of the event callback.
    /// </summary>
    private byte[] _rawScratch = new byte[QuadBytesPerFrame * 480];

    /// <summary>
    /// Scratch buffer for the converted interleaved stereo float block.
    /// </summary>
    private float[] _stereoScratch = new float[480 * 2];

    /// <summary>
    /// Whether captured audio is discarded because another source owns the sink.
    /// </summary>
    private volatile bool _isSuspended;

    /// <summary>
    /// Whether this capture was disposed; late callbacks then feed and flush nothing.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Capture-content diagnostics: whether the host writes real audio or silence.
    /// </summary>
    private bool _firstChunkLogged;

    private long _statsTimestamp;
    private long _totalBytes;
    private long _totalSamples;
    private long _nonZeroSamples;

    /// <summary>
    /// Registers the speaker capture on the given persona. The capture takes no ownership of the
    /// persona or the sink; unsubscribe with <see cref="Dispose"/> before the persona is detached.
    /// </summary>
    /// <param name="device">The Emulation DualSense persona to capture the speaker endpoint of.</param>
    /// <param name="sink">The sink receiving interleaved 48 kHz stereo float PCM.</param>
    public EmulationDualSenseAudioCapture(DualSenseDevice device, IAudioPcmSink sink)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(sink);
        _device = device;
        _sink = sink;
        _audioHandler = OnAudioOut;
        _resetHandler = OnSpeakerReset;
        device.AudioOutReceived += _audioHandler;
        device.SpeakerResetReceived += _resetHandler;
    }

    /// <summary>
    /// Whether this capture is suspended: the controller's forwarded audio is taken from another
    /// source instead, so the packets the host renders to the virtual controller are discarded.
    /// A suspended capture keeps its subscriptions, so switching back is immediate.
    /// </summary>
    public bool IsSuspended
    {
        get
        {
            return _isSuspended;
        }
        set
        {
            _isSuspended = value;
        }
    }

    /// <summary>
    /// Converts a 4-channel S16LE block (front stereo + rear haptics) into interleaved stereo
    /// float, keeping only the front speaker pair. Delegates to the libVIIPER capture's pure
    /// conversion so both engines share one arithmetic.
    /// </summary>
    /// <param name="quadS16Le">Raw little-endian S16LE samples, 8 bytes per frame.</param>
    /// <param name="stereo">Receives one interleaved float pair per input frame.</param>
    public static void ConvertToStereoFloat(ReadOnlySpan<byte> quadS16Le, Span<float> stereo) =>
        ViiperDualSenseAudioCapture.ConvertToStereoFloat(quadS16Le, stereo);

    /// <summary>
    /// Called for every host write to the speaker endpoint. The packet memory is only valid during
    /// this call, so the bytes are copied out, converted to stereo float and pushed to the sink
    /// before returning.
    /// </summary>
    private void OnAudioOut(ReadOnlyMemory<byte> packet)
    {
        if (_isSuspended || packet.Length < QuadBytesPerFrame)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            int byteCount = packet.Length;
            if (_rawScratch.Length < byteCount)
            {
                _rawScratch = new byte[byteCount];
            }

            // Copy while the event's view is still valid; nothing below retains the memory.
            packet.Span[..byteCount].CopyTo(_rawScratch);

            int samples = byteCount / QuadBytesPerFrame * 2;
            if (_stereoScratch.Length < samples)
            {
                _stereoScratch = new float[samples];
            }

            ConvertToStereoFloat(_rawScratch.AsSpan(0, byteCount), _stereoScratch);

            Span<float> stereo = _stereoScratch.AsSpan(0, samples);
            int nonZero = 0;
            for (int i = 0; i < stereo.Length; i++)
            {
                if (stereo[i] != 0f)
                {
                    nonZero++;
                }
            }

            _totalBytes += byteCount;
            _totalSamples += samples;
            _nonZeroSamples += nonZero;

            long now = Stopwatch.GetTimestamp();
            if (!_firstChunkLogged)
            {
                _firstChunkLogged = true;
                _statsTimestamp = now;
                string head = string.Join(
                    " ",
                    _rawScratch.AsSpan(0, Math.Min(16, byteCount)).ToArray().Select(b => b.ToString("X2")));
                _log.Info($"Emulation audio capture first chunk: {byteCount} bytes, {samples} stereo samples, {nonZero}/{samples} non-zero; head {head}");
            }
            else if (now - _statsTimestamp >= Stopwatch.Frequency * 2)
            {
                _statsTimestamp = now;
                double pct = _totalSamples == 0 ? 0 : _nonZeroSamples * 100.0 / _totalSamples;
                _log.Debug($"Emulation audio capture stats: {_totalBytes / 1024.0:F0} KiB fed, {_nonZeroSamples}/{_totalSamples} non-zero samples ({pct:F1}%)");
            }

            _sink.FeedPcm(stereo);
        }
    }

    /// <summary>
    /// Called when the speaker interface resets or changes alternate setting: a stream-generation
    /// barrier that discards the sink's stale PCM and re-primes the lane.
    /// </summary>
    private void OnSpeakerReset()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _sink.Flush();
        }
    }

    /// <summary>
    /// Unsubscribes from the persona. Safe to call after the persona was detached, and repeated
    /// calls are no-ops; a callback already in flight feeds nothing after the unsubscribe.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _device.AudioOutReceived -= _audioHandler;
            _device.SpeakerResetReceived -= _resetHandler;
        }
    }
}