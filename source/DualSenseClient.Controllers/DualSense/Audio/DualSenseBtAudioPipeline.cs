using Concentus;
using Concentus.Enums;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Shared conversion pipeline for DualSense Bluetooth audio: converts a 512-frame
/// (10.667 ms) stereo PCM block into the controller's fixed-size payloads — one
/// 200-byte Opus frame (resampled 512→480 frames) and one 64-byte haptics sample
/// (32 s8 stereo frames at 3 kHz, 16:1 decimated). Owns the Opus encoder state.
/// Used by <see cref="DualSenseAudioPlayer"/> for local file playback and by the
/// VIIPER audio forwarder for host audio, so both sides stay in one implementation.
/// </summary>
public sealed class DualSenseBtAudioPipeline : IDisposable
{
    /// <summary>
    /// Output sample rate (48 kHz, the DualSense native rate).
    /// </summary>
    public const int SampleRate = 48000;

    /// <summary>
    /// Interleaved channels of the decoded stream.
    /// </summary>
    public const int Channels = 2;

    /// <summary>
    /// Frames per writer tick: 512 = 512/48000 s = 10.667 ms, the DualSense Bluetooth
    /// audio-clock cadence.
    /// </summary>
    public const int FramesPerBlock = 512;

    /// <summary>
    /// Opus frames per encoded Bluetooth audio frame. The 512-frame input block is
    /// resampled down to 480 frames (10 ms at 48 kHz) so each frame fits the fixed
    /// 200-byte CBR payload, matching the reference implementations.
    /// </summary>
    public const int OpusFrameSamples = 480;

    /// <summary>
    /// Stereo frames carried by the Bluetooth haptics report (64 s8 bytes).
    /// </summary>
    public const int HapticsFrames = 32;

    /// <summary>
    /// Fixed size of one Bluetooth audio frame (Opus CBR payload).
    /// </summary>
    public const int OpusBytes = 200;

    /// <summary>
    /// Fixed size of one Bluetooth haptics payload: 32 stereo s8 frames at 3 kHz.
    /// </summary>
    public const int HapticsBytes = HapticsFrames * Channels;

    /// <summary>
    /// The lazy-initialized Opus encoder (48 kHz stereo, 160 kbps CBR).
    /// </summary>
    private IOpusEncoder? _opusEncoder;

    /// <summary>
    /// Discards the current Opus encoder so the next <see cref="EncodeOpus"/> call
    /// starts with a fresh instance. Call when the controller's stream is re-primed
    /// so the encoder state starts clean together with the controller's decoder.
    /// </summary>
    public void ResetEncoder()
    {
        _opusEncoder?.Dispose();
        _opusEncoder = null;
    }

    /// <summary>
    /// Encodes one 480-frame block into a fixed 200-byte Opus frame (48 kHz stereo,
    /// 160 kbps CBR). The DualSense lane requires exactly 200 bytes; a short frame
    /// would silently mask a broken encoder configuration, so it is rejected instead.
    /// </summary>
    public void EncodeOpus(ReadOnlySpan<short> pcm, Span<byte> frame)
    {
        _opusEncoder ??= CreateOpusEncoder();
        int written = _opusEncoder.Encode(pcm, OpusFrameSamples, frame, frame.Length);
        if (written != OpusBytes)
        {
            throw new InvalidOperationException(
                $"Opus encoder produced {written} bytes for a {OpusFrameSamples}-frame block (required {OpusBytes}); " +
                "validate the 48 kHz / 10 ms / 160 kbps CBR settings against a real controller.");
        }
    }

    /// <summary>
    /// Converts a float block to interleaved signed 16-bit PCM.
    /// </summary>
    public static void ConvertToPcm16(ReadOnlySpan<float> source, Span<short> target)
    {
        for (int i = 0; i < source.Length; i++)
        {
            target[i] = (short)Math.Clamp(source[i] * 32767f, short.MinValue, short.MaxValue);
        }
    }

    /// <summary>
    /// Downsamples the block to <see cref="HapticsBytes"/> bytes of interleaved s8
    /// stereo (32 frames at 3 kHz) for the Bluetooth haptics report, scaled by the
    /// given strength. Each 3 kHz sample is the mean of its 16-frame decimation window
    /// (512→32) rather than a point sample, low-passing the audio before the coarse s8
    /// quantization so high-frequency content does not alias into the haptic band
    /// (matching vDS's per-chunk averaging).
    /// </summary>
    public static void ToHapticsPcm(ReadOnlySpan<short> stereo, Span<byte> haptics, float strength)
    {
        const int decimation = FramesPerBlock / HapticsFrames;
        for (int i = 0; i < HapticsFrames; i++)
        {
            int start = i * decimation;
            long leftSum = 0;
            long rightSum = 0;
            for (int f = 0; f < decimation; f++)
            {
                leftSum += stereo[(start + f) * 2];
                rightSum += stereo[(start + f) * 2 + 1];
            }

            int left = Math.Clamp((int)(leftSum / (decimation * 256f) * strength), -128, 127);
            int right = Math.Clamp((int)(rightSum / (decimation * 256f) * strength), -128, 127);
            haptics[i * 2] = (byte)left;
            haptics[i * 2 + 1] = (byte)right;
        }
    }

    /// <summary>
    /// Linearly resamples a 512-frame stereo block down to 480 frames (the Opus frame
    /// size at 48 kHz), matching the reference implementations' 512→480 conversion.
    /// The final output sample is pinned to the source's final sample: the fractional
    /// source index of the last output lands one ulp short of the true endpoint due to
    /// floating-point rounding, which would otherwise smear the last frame by one LSB.
    /// </summary>
    public static void ResampleToOpusBlock(ReadOnlySpan<short> source, Span<short> target)
    {
        const double step = (FramesPerBlock - 1) / (double)(OpusFrameSamples - 1);
        for (int i = 0; i < OpusFrameSamples; i++)
        {
            if (i == OpusFrameSamples - 1)
            {
                target[i * 2] = source[(FramesPerBlock - 1) * 2];
                target[i * 2 + 1] = source[(FramesPerBlock - 1) * 2 + 1];
                continue;
            }

            double src = i * step;
            int idx = (int)src;
            int nxt = idx + 1;
            double frac = src - idx;
            int l0 = source[idx * 2];
            int l1 = source[nxt * 2];
            int r0 = source[idx * 2 + 1];
            int r1 = source[nxt * 2 + 1];
            target[i * 2] = (short)(int)(l0 + (l1 - l0) * frac);
            target[i * 2 + 1] = (short)(int)(r0 + (r1 - r0) * frac);
        }
    }

    /// <summary>
    /// Creates the Opus encoder with the DualSense-compatible fixed settings.
    /// </summary>
    private static IOpusEncoder CreateOpusEncoder()
    {
        IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_AUDIO);
        encoder.ExpertFrameDuration = OpusFramesize.OPUS_FRAMESIZE_10_MS;
        encoder.Bitrate = 200 * 8 * 100;
        encoder.UseVBR = false;
        encoder.Complexity = 0;
        return encoder;
    }

    /// <inheritdoc/>
    public void Dispose() => ResetEncoder();
}
