using System.Diagnostics;
using System.Runtime.InteropServices;
using DualSenseClient.Logging;
using DualSenseClient.VIIPER;
using DualSenseClient.VIIPER.Callbacks;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Captures the audio the host renders to the virtual DualSense's haptics audio-out
/// endpoint and forwards it to <see cref="ViiperDualSenseAudioForwarder"/>, so the
/// physical controller plays the game's audio (speaker + haptics) over Bluetooth.
/// </summary>
/// <remarks>
/// <para>
/// libVIIPER invokes the registered callback with the exact bytes the host wrote to
/// the endpoint: four S16LE channels at 48 kHz — the front stereo speaker pair plus
/// the rear voice-coil haptics pair (channel configuration 0x0033). Only the front
/// pair is forwarded as the speaker audio; the haptics are derived from it by the
/// forwarder. The callback runs on the libVIIPER audio thread and the buffer is only
/// valid during the call, so the bytes are copied and converted to interleaved stereo
/// float before being handed to the forwarder's ring.
/// </para>
/// <para>
/// The speaker-reset callback marks a stream generation barrier: the host reopened
/// or re-alternated the audio interface, so buffered PCM from the previous generation
/// is flushed and the Bluetooth lane re-primes on the next block.
/// </para>
/// </remarks>
public sealed class ViiperDualSenseAudioCapture : IDisposable
{
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("ViiperDualSenseAudioCapture");

    /// <summary>
    /// Bytes per frame of the endpoint layout: 4 channels of S16LE.
    /// </summary>
    private const int QuadBytesPerFrame = sizeof(short) * 4;

    /// <summary>
    /// The virtual DualSense device handle the callbacks are registered on.
    /// </summary>
    private readonly nuint _deviceHandle;

    /// <summary>
    /// The forwarder receiving the captured audio.
    /// </summary>
    private readonly ViiperDualSenseAudioForwarder _forwarder;

    /// <summary>
    /// Keeps the native callbacks alive for the lifetime of the capture.
    /// </summary>
    private readonly DSAudioCallback _audioCallback;

    private readonly DSSpeakerResetCallback _resetCallback;

    /// <summary>
    /// Serializes callback invocations and guards the scratch buffers.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// Scratch buffer for the raw S16LE bytes copied out of the native callback.
    /// </summary>
    private byte[] _rawScratch = new byte[QuadBytesPerFrame * 480];

    /// <summary>
    /// Scratch buffer for the converted interleaved stereo float block.
    /// </summary>
    private float[] _stereoScratch = new float[480 * 2];

    /// <summary>
    /// Capture-content diagnostics: whether the host writes real audio or silence.
    /// </summary>
    private bool _firstChunkLogged;

    private long _statsTimestamp;
    private long _totalBytes;
    private long _totalSamples;
    private long _nonZeroSamples;

    /// <summary>
    /// Registers the host-audio capture on the given virtual DualSense device. The
    /// capture takes no ownership of the handle or the forwarder; unregister the
    /// callbacks with <see cref="Dispose"/> before the device is removed.
    /// </summary>
    public ViiperDualSenseAudioCapture(nuint deviceHandle, ViiperDualSenseAudioForwarder forwarder)
    {
        _deviceHandle = deviceHandle;
        _forwarder = forwarder;
        _audioCallback = OnAudioOut;
        _resetCallback = OnSpeakerReset;
        if (!LibVIIPER.SetDualSenseAudioOutCallback(deviceHandle, _audioCallback))
        {
            _log.Error("Failed to register the virtual DualSense audio-out callback");
        }

        if (!LibVIIPER.SetDualSenseSpeakerResetCallback(deviceHandle, _resetCallback))
        {
            _log.Error("Failed to register the virtual DualSense speaker-reset callback");
        }
    }

    /// <summary>
    /// Converts a 4-channel S16LE block (front stereo + rear haptics) into interleaved
    /// stereo float, keeping only the front speaker pair.
    /// </summary>
    /// <param name="quadS16Le">Raw little-endian S16LE samples, 8 bytes per frame.</param>
    /// <param name="stereo">Receives one interleaved float pair per input frame.</param>
    public static void ConvertToStereoFloat(ReadOnlySpan<byte> quadS16Le, Span<float> stereo)
    {
        ReadOnlySpan<short> quad = MemoryMarshal.Cast<byte, short>(quadS16Le);
        int frames = quadS16Le.Length / QuadBytesPerFrame;
        for (int f = 0; f < frames; f++)
        {
            stereo[f * 2] = quad[f * 4] / 32768f;
            stereo[f * 2 + 1] = quad[f * 4 + 1] / 32768f;
        }
    }

    /// <summary>
    /// Called on the libVIIPER audio thread for every host write to the haptics
    /// audio-out endpoint. Must return quickly: the bytes are copied out, converted to
    /// stereo float and pushed into the forwarder's ring.
    /// </summary>
    private void OnAudioOut(nuint handle, IntPtr pcm, nuint length)
    {
        int byteCount = (int)length;
        if (byteCount < QuadBytesPerFrame)
        {
            return;
        }

        lock (_sync)
        {
            if (_rawScratch.Length < byteCount)
            {
                _rawScratch = new byte[byteCount];
            }

            Marshal.Copy(pcm, _rawScratch, 0, byteCount);

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
                _log.Info($"Audio capture first chunk: {byteCount} bytes, {samples} stereo samples, {nonZero}/{samples} non-zero; head {head}");
            }
            else if (now - _statsTimestamp >= Stopwatch.Frequency * 2)
            {
                _statsTimestamp = now;
                double pct = _totalSamples == 0 ? 0 : _nonZeroSamples * 100.0 / _totalSamples;
                _log.Debug($"Audio capture stats: {_totalBytes / 1024.0:F0} KiB fed, {_nonZeroSamples}/{_totalSamples} non-zero samples ({pct:F1}%)");
            }

            _forwarder.FeedPcm(stereo);
        }
    }

    /// <summary>
    /// Called when the virtual audio interface resets or changes alternate setting:
    /// a stream-generation barrier that discards stale PCM and re-primes the lane.
    /// </summary>
    private void OnSpeakerReset(nuint handle) => _forwarder.Flush();

    /// <summary>
    /// Unregisters the callbacks. Safe to call after the device was removed.
    /// </summary>
    public void Dispose()
    {
        LibVIIPER.SetDualSenseAudioOutCallback(_deviceHandle, null);
        LibVIIPER.SetDualSenseSpeakerResetCallback(_deviceHandle, null);
    }
}