using System.Diagnostics;
using System.Runtime.InteropServices;
using DualSenseClient.Logging;
using DualSenseClient.VIIPER;
using DualSenseClient.VIIPER.Callbacks;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Captures the audio the host renders to the virtual DualShock 4's speaker endpoint
/// and forwards it to <see cref="ViiperDualSenseAudioForwarder"/>, so the physical
/// controller plays the game's audio (speaker + derived haptics) over Bluetooth.
/// </summary>
/// <remarks>
/// <para>
/// libVIIPER invokes the registered callback with the exact bytes the host wrote to
/// the endpoint: two S16LE channels at 32 kHz. The PCM is converted to interleaved
/// stereo float and upsampled to the 48 kHz stereo stream the forwarder consumes
/// (an exact 3:2 ratio). The callback runs on the libVIIPER audio thread and the
/// buffer is only valid during the call, so the bytes are copied out first.
/// </para>
/// <para>
/// The speaker-reset callback marks a stream generation barrier: the host reopened
/// or re-alternated the audio interface, so buffered PCM from the previous generation
/// is flushed and the Bluetooth lane re-primes on the next block.
/// </para>
/// </remarks>
public sealed class ViiperDualShock4AudioCapture : IDisposable
{
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("ViiperDualShock4AudioCapture");

    /// <summary>
    /// Bytes per frame of the DS4 speaker endpoint layout: 2 channels of S16LE.
    /// </summary>
    private const int StereoBytesPerFrame = sizeof(short) * 2;

    /// <summary>
    /// The virtual DualShock 4 device handle the callbacks are registered on.
    /// </summary>
    private readonly nuint _deviceHandle;

    /// <summary>
    /// The forwarder receiving the captured audio.
    /// </summary>
    private readonly ViiperDualSenseAudioForwarder _forwarder;

    /// <summary>
    /// Keeps the native callbacks alive for the lifetime of the capture.
    /// </summary>
    private readonly DS4SpeakerCallback _speakerCallback;

    private readonly DS4SpeakerResetCallback _resetCallback;

    /// <summary>
    /// Serializes callback invocations and guards the scratch buffers.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// Streaming 32 kHz → 48 kHz resampler, fed once per callback.
    /// </summary>
    private readonly Upsampler32To48 _upsampler = new Upsampler32To48();

    /// <summary>
    /// Scratch buffer for the raw S16LE bytes copied out of the native callback.
    /// </summary>
    private byte[] _rawScratch = new byte[512];

    /// <summary>
    /// Scratch buffer for the converted interleaved stereo float block at 32 kHz.
    /// </summary>
    private float[] _floatScratch = [];

    /// <summary>
    /// Scratch buffer for the resampled interleaved stereo float block at 48 kHz.
    /// </summary>
    private float[] _stereoScratch = [];

    /// <summary>
    /// Capture-content diagnostics: whether the host writes real audio or silence.
    /// </summary>
    private bool _firstChunkLogged;

    private long _statsTimestamp;
    private long _totalBytes;
    private long _totalSamples;
    private long _nonZeroSamples;

    /// <summary>
    /// Registers the host-audio capture on the given virtual DualShock 4 device. The
    /// capture takes no ownership of the handle or the forwarder; unregister the
    /// callbacks with <see cref="Dispose"/> before the device is removed.
    /// </summary>
    public ViiperDualShock4AudioCapture(nuint deviceHandle, ViiperDualSenseAudioForwarder forwarder)
    {
        _deviceHandle = deviceHandle;
        _forwarder = forwarder;
        _speakerCallback = OnSpeakerPcm;
        _resetCallback = OnSpeakerReset;
        if (!LibVIIPER.SetDS4SpeakerCallback(deviceHandle, _speakerCallback))
        {
            _log.Error("Failed to register the virtual DualShock 4 speaker callback");
        }
        if (!LibVIIPER.SetDS4SpeakerResetCallback(deviceHandle, _resetCallback))
        {
            _log.Error("Failed to register the virtual DualShock 4 speaker-reset callback");
        }
    }

    /// <summary>
    /// Converts an interleaved 2-channel S16LE block into interleaved stereo float.
    /// </summary>
    /// <param name="s16Le">Raw little-endian S16LE samples, 4 bytes per frame.</param>
    /// <param name="stereo">Receives one interleaved float pair per input frame.</param>
    public static void ConvertToStereoFloat(ReadOnlySpan<byte> s16Le, Span<float> stereo)
    {
        ReadOnlySpan<short> pcm = MemoryMarshal.Cast<byte, short>(s16Le);
        int frames = s16Le.Length / StereoBytesPerFrame;
        for (int f = 0; f < frames; f++)
        {
            stereo[f * 2] = pcm[f * 2] / 32768f;
            stereo[f * 2 + 1] = pcm[f * 2 + 1] / 32768f;
        }
    }

    /// <summary>
    /// Called on the libVIIPER audio thread for every host write to the DS4 speaker
    /// endpoint. Must return quickly: the bytes are copied out, converted to stereo
    /// float, upsampled to 48 kHz and pushed into the forwarder's ring.
    /// </summary>
    private void OnSpeakerPcm(nuint handle, IntPtr pcm, nuint length)
    {
        int byteCount = (int)length;
        if (byteCount < StereoBytesPerFrame)
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

            int frames = byteCount / StereoBytesPerFrame;
            if (_floatScratch.Length < frames * 2)
            {
                _floatScratch = new float[frames * 2];
            }
            ConvertToStereoFloat(_rawScratch.AsSpan(0, byteCount), _floatScratch);

            if (_stereoScratch.Length < ((int)(frames * 1.5) + 2) * 2)
            {
                _stereoScratch = new float[((int)(frames * 1.5) + 2) * 2];
            }
            int written = _upsampler.Process(_floatScratch.AsSpan(0, frames * 2), _stereoScratch);

            Span<float> stereo = _stereoScratch.AsSpan(0, written * 2);
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
                _log.Info($"DS4 audio capture first chunk: {byteCount} bytes, {stereo.Length} stereo samples, {nonZero}/{stereo.Length} non-zero; head {head}");
            }
            else if (now - _statsTimestamp >= Stopwatch.Frequency * 2)
            {
                _statsTimestamp = now;
                double pct = _totalSamples == 0 ? 0 : _nonZeroSamples * 100.0 / _totalSamples;
                _log.Debug($"DS4 audio capture stats: {_totalBytes / 1024.0:F0} KiB fed, {_nonZeroSamples}/{_totalSamples} non-zero samples ({pct:F1}%)");
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
        LibVIIPER.SetDS4SpeakerCallback(_deviceHandle, null);
        LibVIIPER.SetDS4SpeakerResetCallback(_deviceHandle, null);
    }

    /// <summary>
    /// Streaming linear-interpolation resampler from 32 kHz to 48 kHz (an exact 3:2
    /// ratio): every two input frames yield three output frames. State (the previous
    /// input frame and the fractional source position) carries across chunk
    /// boundaries, so arbitrarily sized feeds stay phase-continuous. Output must be
    /// sized for <c>ceil(inputFrames × 1.5) + 1</c> frames.
    /// </summary>
    public sealed class Upsampler32To48
    {
        /// <summary>
        /// Source-position step per output frame (2 input frames → 3 output frames).
        /// </summary>
        private const float Step = 2f / 3f;

        /// <summary>
        /// The last input frame of the previous chunk, the source sample at position 0.
        /// </summary>
        private float _prevLeft;

        private float _prevRight;

        /// <summary>
        /// Fractional source position of the next output frame, relative to the current
        /// chunk's samples (position 0 = <see cref="_prevLeft"/>/<see cref="_prevRight"/>,
        /// position <c>k</c> = chunk sample <c>k - 1</c>).
        /// </summary>
        private float _position;

        /// <summary>
        /// Resamples one interleaved stereo chunk. Returns the number of output frames
        /// written (inputFrames × 1.5 when the output is large enough).
        /// </summary>
        public int Process(ReadOnlySpan<float> input, Span<float> output)
        {
            int frames = input.Length / 2;
            if (frames == 0)
            {
                return 0;
            }

            int written = 0;
            float position = _position;
            bool inputExhausted = false;
            while (written < output.Length / 2)
            {
                int index = (int)position;
                if (index >= frames)
                {
                    inputExhausted = true;
                    break;
                }

                float frac = position - index;
                float s0Left = index == 0 ? _prevLeft : input[(index - 1) * 2];
                float s0Right = index == 0 ? _prevRight : input[(index - 1) * 2 + 1];
                float s1Left = input[index * 2];
                float s1Right = input[index * 2 + 1];
                output[written * 2] = s0Left + (s1Left - s0Left) * frac;
                output[written * 2 + 1] = s0Right + (s1Right - s0Right) * frac;
                written++;
                position += Step;
            }

            if (inputExhausted)
            {
                _position = position - frames;
                _prevLeft = input[(frames - 1) * 2];
                _prevRight = input[(frames - 1) * 2 + 1];
            }
            return written;
        }
    }
}