using Concentus;
using Concentus.Enums;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Shared conversion pipeline for DualSense Bluetooth audio: converts a 512-frame
/// (10.667 ms) stereo PCM block into the controller's fixed-size payloads — one
/// 200-byte Opus frame (resampled 512→480 frames) and one 64-byte haptics sample
/// (32 s8 stereo frames at 3 kHz, 16:1 decimated). Owns the Opus encoder state.
/// Used by <see cref="DualSenseAudioPlayer"/> for local file playback and by the
/// virtual-controller audio forwarder for host audio, so both sides stay in one implementation.
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
        Span<byte> encoded = stackalloc byte[OpusBytes];
        int written = _opusEncoder.Encode(pcm, OpusFrameSamples, encoded, encoded.Length);
        if (written < 0)
        {
            throw new InvalidOperationException($"Opus encoder failed with error {written}.");
        }

        PadOpusFrame(encoded[..written], frame);
    }

    /// <summary>
    /// Copies one encoded Opus packet into the controller's fixed 200-byte payload and
    /// zero-pads the remainder. The controller's block length is always 200 regardless of
    /// the encoder's real output size, so short packets must be padded rather than
    /// rejected; a packet that does not fit cannot be sent.
    /// </summary>
    /// <param name="encoded">Encoded Opus packet (at most 200 bytes).</param>
    /// <param name="frame">Destination payload of at least 200 bytes.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="encoded"/> is longer than 200 bytes or <paramref name="frame"/> is shorter.
    /// </exception>
    internal static void PadOpusFrame(ReadOnlySpan<byte> encoded, Span<byte> frame)
    {
        if (encoded.Length > OpusBytes)
        {
            throw new InvalidOperationException(
                $"An Opus packet of {encoded.Length} bytes does not fit the controller's {OpusBytes}-byte payload.");
        }

        if (frame.Length < OpusBytes)
        {
            throw new InvalidOperationException(
                $"The Opus payload buffer must hold {OpusBytes} bytes; got {frame.Length}.");
        }

        encoded.CopyTo(frame);
        frame[encoded.Length..OpusBytes].Clear();
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
    /// Applies the controller's software gain to one PCM sample. Gains up to unity are
    /// linear; gains above unity pass through a soft limiter so a boost cannot clip.
    /// </summary>
    /// <param name="sample">The source sample.</param>
    /// <param name="gain">The gain multiplier (0-8).</param>
    /// <returns>The scaled sample.</returns>
    public static short ApplySoftwareGain(short sample, float gain)
    {
        if (gain == 1f)
        {
            return sample;
        }

        if (!float.IsFinite(gain))
        {
            gain = 1f;
        }

        gain = Math.Clamp(gain, 0f, 8f);
        float value = sample * gain;
        if (gain > 1f)
        {
            // Soft-knee limiter in the S16 domain: linear below 90% of full scale, then
            // asymptotically approaches full scale. It keeps a 200% boost audible without
            // hard clipping.
            const float threshold = 0.9f * 32768f;
            const float knee = 0.1f * 32768f;
            float magnitude = MathF.Abs(value);
            if (magnitude > threshold)
            {
                float excess = (magnitude - threshold) / knee;
                magnitude = threshold + knee * MathF.Tanh(excess);
                value = MathF.CopySign(magnitude, value);
            }
        }

        return (short)Math.Clamp(MathF.Round(value), short.MinValue, short.MaxValue);
    }

    /// <summary>
    /// Applies the controller's software gain to one normalized float sample. Gains up
    /// to unity are linear; gains above unity pass through a soft limiter so a boost
    /// cannot clip.
    /// </summary>
    /// <param name="sample">The source sample (-1..1).</param>
    /// <param name="gain">The gain multiplier (0-8).</param>
    /// <returns>The scaled sample.</returns>
    public static float ApplySoftwareGain(float sample, float gain)
    {
        if (!float.IsFinite(gain))
        {
            gain = 1f;
        }

        gain = Math.Clamp(gain, 0f, 8f);
        if (gain == 1f)
        {
            return sample;
        }

        float value = sample * gain;
        if (gain > 1f)
        {
            // Soft-knee limiter: linear below 90% of full scale, then asymptotically
            // approaches full scale. It keeps a 200% boost audible without hard clipping.
            const float threshold = 0.9f;
            const float knee = 0.1f;
            float magnitude = MathF.Abs(value);
            if (magnitude > threshold)
            {
                float excess = (magnitude - threshold) / knee;
                magnitude = threshold + knee * MathF.Tanh(excess);
                value = MathF.CopySign(magnitude, value);
            }
        }

        return Math.Clamp(value, -1f, 1f);
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
    /// <param name="source">The 512-frame stereo source block.</param>
    /// <param name="target">The 480-frame stereo target block.</param>
    /// <param name="gain">
    /// Software gain applied to the resampled audio (0-8). Haptics are derived from the
    /// unmodified source, so this only affects the audio path.
    /// </param>
    public static void ResampleToOpusBlock(ReadOnlySpan<short> source, Span<short> target, float gain = 1f)
    {
        gain = float.IsFinite(gain) ? Math.Clamp(gain, 0f, 8f) : 1f;
        const double step = (FramesPerBlock - 1) / (double)(OpusFrameSamples - 1);
        for (int i = 0; i < OpusFrameSamples; i++)
        {
            if (i == OpusFrameSamples - 1)
            {
                target[i * 2] = ApplySoftwareGain(source[(FramesPerBlock - 1) * 2], gain);
                target[i * 2 + 1] = ApplySoftwareGain(source[(FramesPerBlock - 1) * 2 + 1], gain);
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
            target[i * 2] = ApplySoftwareGain((short)(int)(l0 + (l1 - l0) * frac), gain);
            target[i * 2 + 1] = ApplySoftwareGain((short)(int)(r0 + (r1 - r0) * frac), gain);
        }
    }

    /// <summary>
    /// Creates the Opus encoder with the DualSense-compatible fixed settings.
    /// </summary>
    private static IOpusEncoder CreateOpusEncoder()
    {
        IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY);
        encoder.ForceMode = OpusMode.MODE_CELT_ONLY;
        encoder.ExpertFrameDuration = OpusFramesize.OPUS_FRAMESIZE_10_MS;
        encoder.Bitrate = 200 * 8 * 100;
        encoder.UseVBR = false;
        encoder.Complexity = 0;
        return encoder;
    }

    /// <inheritdoc/>
    public void Dispose() => ResetEncoder();
}