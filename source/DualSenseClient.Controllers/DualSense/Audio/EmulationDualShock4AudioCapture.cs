using System.Diagnostics;
using DualSenseClient.Logging;
using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Captures the speaker audio the host renders to an Emulation DualShock 4 persona and forwards it
/// to an <see cref="IAudioPcmSink"/> (the <see cref="ViiperDualSenseAudioForwarder"/>), so the
/// physical controller plays the game's audio.
/// </summary>
/// <remarks>
/// <para>
/// The persona raises <see cref="DualShock4Device.AudioOutReceived"/> with the exact bytes the host
/// wrote to the speaker endpoint: two S16LE channels at 32 kHz, 4 bytes per frame and 128 bytes in
/// a nominal one-millisecond packet. The event memory is a view over the persona's reusable scratch
/// buffer and is only valid during the callback, so the bytes are copied and converted to
/// interleaved stereo float synchronously, before the callback returns. The 32 kHz stream is then
/// upsampled to the 48 kHz stereo stream the sink consumes (an exact 3:2 ratio) by one
/// <see cref="ViiperDualShock4AudioCapture.Upsampler32To48"/> instance owned by this capture, so
/// the resampler phase carries across callbacks.
/// </para>
/// <para>
/// <see cref="DualShock4Device.SpeakerResetReceived"/> marks a stream-generation barrier: the host
/// reopened or re-alternated the speaker interface, so the sink is flushed and the forwarded lane
/// re-primes on the next block.
/// </para>
/// </remarks>
public sealed class EmulationDualShock4AudioCapture : IDisposable, IViiperAudioCapture
{
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("EmulationDualShock4AudioCapture");

    /// <summary>
    /// Bytes per frame of the endpoint layout: 2 channels of S16LE.
    /// </summary>
    private const int StereoBytesPerFrame = sizeof(short) * 2;

    /// <summary>
    /// The persona whose speaker endpoint is captured.
    /// </summary>
    private readonly DualShock4Device _device;

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
    /// Streaming 32 kHz → 48 kHz resampler, fed once per callback. One instance per capture keeps
    /// the interpolation history continuous across packets.
    /// </summary>
    private readonly ViiperDualShock4AudioCapture.Upsampler32To48 _upsampler = new ViiperDualShock4AudioCapture.Upsampler32To48();

    /// <summary>
    /// Scratch buffer for the raw S16LE bytes copied out of the event callback.
    /// </summary>
    private byte[] _rawScratch = new byte[StereoBytesPerFrame * 128];

    /// <summary>
    /// Scratch buffer for the converted interleaved stereo float block at 32 kHz.
    /// </summary>
    private float[] _floatScratch = [];

    /// <summary>
    /// Scratch buffer for the resampled interleaved stereo float block at 48 kHz.
    /// </summary>
    private float[] _stereoScratch = [];

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
    /// <param name="device">The Emulation DualShock 4 persona to capture the speaker endpoint of.</param>
    /// <param name="sink">The sink receiving interleaved 48 kHz stereo float PCM.</param>
    public EmulationDualShock4AudioCapture(DualShock4Device device, IAudioPcmSink sink)
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
    /// Called for every host write to the speaker endpoint. The packet memory is only valid during
    /// this call, so the bytes are copied out, converted to 32 kHz stereo float, upsampled to
    /// 48 kHz and pushed to the sink before returning.
    /// </summary>
    private void OnAudioOut(ReadOnlyMemory<byte> packet)
    {
        if (_isSuspended || packet.Length < StereoBytesPerFrame)
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

            int frames = byteCount / StereoBytesPerFrame;
            if (_floatScratch.Length < frames * 2)
            {
                _floatScratch = new float[frames * 2];
            }

            ViiperDualShock4AudioCapture.ConvertToStereoFloat(_rawScratch.AsSpan(0, byteCount), _floatScratch);

            if (_stereoScratch.Length < ((int)(frames * 1.5) + 2) * 2)
            {
                _stereoScratch = new float[((int)(frames * 1.5) + 2) * 2];
            }

            int written = _upsampler.Process(_floatScratch.AsSpan(0, frames * 2), _stereoScratch);

            // The streaming resampler keeps phase across callbacks and can emit one boundary frame
            // beyond the exact 3:2 count (its output contract allows the extra frame). The sink
            // consumes a fixed 48 kHz stream, so the block is clamped to ceil(frames × 3 / 2)
            // frames; the resampler state has already advanced and the next packet continues from it.
            int outputFrames = Math.Min(written, (frames * 3 + 1) / 2);
            Span<float> stereo = _stereoScratch.AsSpan(0, outputFrames * 2);
            int nonZero = 0;
            for (int i = 0; i < stereo.Length; i++)
            {
                if (stereo[i] != 0f)
                {
                    nonZero++;
                }
            }

            _totalBytes += byteCount;
            _totalSamples += stereo.Length;
            _nonZeroSamples += nonZero;

            long now = Stopwatch.GetTimestamp();
            if (!_firstChunkLogged)
            {
                _firstChunkLogged = true;
                _statsTimestamp = now;
                string head = string.Join(
                    " ",
                    _rawScratch.AsSpan(0, Math.Min(16, byteCount)).ToArray().Select(b => b.ToString("X2")));
                _log.Info(
                    $"Emulation DS4 audio capture first chunk: {byteCount} bytes, {stereo.Length} stereo samples, {nonZero}/{stereo.Length} non-zero; head {head}");
            }
            else if (now - _statsTimestamp >= Stopwatch.Frequency * 2)
            {
                _statsTimestamp = now;
                double pct = _totalSamples == 0 ? 0 : _nonZeroSamples * 100.0 / _totalSamples;
                _log.Debug(
                    $"Emulation DS4 audio capture stats: {_totalBytes / 1024.0:F0} KiB fed, {_nonZeroSamples}/{_totalSamples} non-zero samples ({pct:F1}%)");
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