using System.Buffers.Binary;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Converts the microphone PCM this client decodes (mono S16LE at 48 kHz) into the fixed
/// one-millisecond frames the Emulation personas read from their virtual microphone endpoints:
/// two-channel 48 kHz for the DualSense and one-channel 16 kHz for the DualShock 4.
/// </summary>
/// <remarks>
/// <para>
/// The DualSense frame is the mono source duplicated on both channels. The DualShock 4 frame is
/// the source run through a 7 kHz Butterworth low-pass and then decimated 3:1, so band limiting
/// happens before the sample rate drops to 16 kHz (a gentle 7 kHz response, so near-Nyquist
/// content is attenuated rather than removed).
/// </para>
/// <para>
/// The filter and decimation phase persist across <see cref="Convert"/> calls, keeping the stream
/// continuous; a caller that restarts or re-primes its stream must call <see cref="Reset"/>. The
/// converter is stateful and not thread-safe: the owning bridge must serialize access.
/// </para>
/// </remarks>
internal sealed class MicrophoneTargetConverter
{
    /// <summary>Sample rate of the decoded source stream, in hertz.</summary>
    private const int SourceSampleRateHz = 48000;

    /// <summary>Interleaved channels of a DualSense microphone frame.</summary>
    private const int DualSenseChannels = 2;

    /// <summary>Bytes of one DualSense microphone frame: two 48 kHz channels over one millisecond.</summary>
    private const int DualSenseFrameBytes = 192;

    /// <summary>Interleaved channels of a DualShock 4 microphone frame.</summary>
    private const int DualShock4Channels = 1;

    /// <summary>Sample rate of the DualShock 4 microphone path, in hertz.</summary>
    private const int DualShock4SampleRateHz = 16000;

    /// <summary>Bytes of one DualShock 4 microphone frame: one 16 kHz channel over one millisecond.</summary>
    private const int DualShock4FrameBytes = 32;

    /// <summary>Cutoff of the low-pass applied before the DualShock 4 decimation, in hertz.</summary>
    private const double LowPassCutoffHz = 7000;

    /// <summary>Quality factor of the low-pass, a Butterworth response without passband ripple.</summary>
    private const double LowPassQ = 0.707;

    /// <summary>Source samples per DualShock 4 target sample: 48 kHz to 16 kHz.</summary>
    private const int DecimationFactor = 3;

    /// <summary>The target frame layout the converter writes.</summary>
    private readonly TargetKind _kind;

    /// <summary>Current-sample coefficient of the low-pass, normalized by <c>a0</c>.</summary>
    private readonly double _b0;

    /// <summary>One-sample-delayed input coefficient of the low-pass.</summary>
    private readonly double _b1;

    /// <summary>Two-sample-delayed input coefficient of the low-pass.</summary>
    private readonly double _b2;

    /// <summary>One-sample-delayed output coefficient of the low-pass.</summary>
    private readonly double _a1;

    /// <summary>Two-sample-delayed output coefficient of the low-pass.</summary>
    private readonly double _a2;

    /// <summary>Previous low-pass input sample, <c>x[n-1]</c>.</summary>
    private double _x1;

    /// <summary>Low-pass input sample before that, <c>x[n-2]</c>.</summary>
    private double _x2;

    /// <summary>Previous low-pass output sample, <c>y[n-1]</c>.</summary>
    private double _y1;

    /// <summary>Low-pass output sample before that, <c>y[n-2]</c>.</summary>
    private double _y2;

    /// <summary>Position of the next source sample within the decimation period, 0 to 2.</summary>
    private int _decimationPhase;

    /// <summary>
    /// Initializes a converter for one target format. Only the two microphone geometries the
    /// Emulation personas expose are supported.
    /// </summary>
    /// <param name="format">Microphone format of the target persona.</param>
    /// <exception cref="NotSupportedException">
    /// The format is neither the DualSense (two-channel 48 kHz) nor the DualShock 4 (one-channel
    /// 16 kHz) microphone geometry.
    /// </exception>
    public MicrophoneTargetConverter(AudioFunctionFormat format)
    {
        _kind = ResolveKind(format);

        double omega = 2 * Math.PI * LowPassCutoffHz / SourceSampleRateHz;
        double sine = Math.Sin(omega);
        double cosine = Math.Cos(omega);
        double alpha = sine / (2 * LowPassQ);
        double a0 = 1 + alpha;

        _b0 = (1 - cosine) / 2 / a0;
        _b1 = (1 - cosine) / a0;
        _b2 = _b0;
        _a1 = -2 * cosine / a0;
        _a2 = (1 - alpha) / a0;
    }

    /// <summary>
    /// Converts one block of 48 kHz mono source samples into the target frame layout, keeping the
    /// DualShock 4 filter and decimation state for the next call.
    /// </summary>
    /// <param name="source48Mono">Mono source samples at 48 kHz, nominally one millisecond (48 samples).</param>
    /// <param name="destination">Receives the target PCM as little-endian S16.</param>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> cannot hold the frame produced from the source block.
    /// </exception>
    public int Convert(ReadOnlySpan<short> source48Mono, Span<byte> destination)
    {
        if (_kind == TargetKind.DualSense)
        {
            return ConvertToDualSense(source48Mono, destination);
        }

        return ConvertToDualShock4(source48Mono, destination);
    }

    /// <summary>
    /// Clears the low-pass history and the decimation phase, so the next source block starts a new
    /// stream generation.
    /// </summary>
    public void Reset()
    {
        _x1 = 0;
        _x2 = 0;
        _y1 = 0;
        _y2 = 0;
        _decimationPhase = 0;
    }

    /// <summary>
    /// Maps a persona microphone format onto the target layout the converter knows how to write.
    /// </summary>
    /// <param name="format">Microphone format of the target persona.</param>
    /// <returns>The supported target layout.</returns>
    /// <exception cref="NotSupportedException">The format is not one of the two persona geometries.</exception>
    private static TargetKind ResolveKind(AudioFunctionFormat format)
    {
        if (format.MicrophoneChannels == DualSenseChannels
            && format.MicrophoneSampleRateHz == SourceSampleRateHz
            && format.MicrophoneFrameBytes == DualSenseFrameBytes)
        {
            return TargetKind.DualSense;
        }

        if (format.MicrophoneChannels == DualShock4Channels
            && format.MicrophoneSampleRateHz == DualShock4SampleRateHz
            && format.MicrophoneFrameBytes == DualShock4FrameBytes)
        {
            return TargetKind.DualShock4;
        }

        throw new NotSupportedException(
            $"No microphone target conversion for {format.MicrophoneChannels} channel(s) at "
            + $"{format.MicrophoneSampleRateHz} Hz with {format.MicrophoneFrameBytes}-byte frames.");
    }

    /// <summary>
    /// Writes each mono source sample to both channels of the two-channel 48 kHz DualSense frame.
    /// </summary>
    /// <param name="source48Mono">Mono source samples at 48 kHz.</param>
    /// <param name="destination">Receives the interleaved stereo S16 frame.</param>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    private static int ConvertToDualSense(ReadOnlySpan<short> source48Mono, Span<byte> destination)
    {
        int requiredBytes = source48Mono.Length * DualSenseChannels * sizeof(short);
        if (destination.Length < requiredBytes)
        {
            throw new ArgumentException(
                $"A DualSense frame needs {requiredBytes} bytes, but the destination holds {destination.Length}.",
                nameof(destination));
        }

        int offset = 0;
        foreach (short sample in source48Mono)
        {
            BinaryPrimitives.WriteInt16LittleEndian(destination[offset..], sample);
            BinaryPrimitives.WriteInt16LittleEndian(destination[(offset + sizeof(short))..], sample);
            offset += DualSenseChannels * sizeof(short);
        }

        return requiredBytes;
    }

    /// <summary>
    /// Low-passes every source sample at 48 kHz, then writes every third filtered sample as the
    /// one-channel 16 kHz DualShock 4 frame.
    /// </summary>
    /// <param name="source48Mono">Mono source samples at 48 kHz.</param>
    /// <param name="destination">Receives the one-channel S16 frame.</param>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    private int ConvertToDualShock4(ReadOnlySpan<short> source48Mono, Span<byte> destination)
    {
        int requiredBytes = CountDecimatedSamples(source48Mono.Length) * sizeof(short);
        if (destination.Length < requiredBytes)
        {
            throw new ArgumentException(
                $"A DualShock 4 frame needs {requiredBytes} bytes, but the destination holds {destination.Length}.",
                nameof(destination));
        }

        int offset = 0;
        for (int i = 0; i < source48Mono.Length; i++)
        {
            double filtered = Filter(source48Mono[i]);
            if (_decimationPhase == 0)
            {
                short sample = (short)Math.Clamp(Math.Round(filtered), short.MinValue, short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(destination[offset..], sample);
                offset += sizeof(short);
            }

            _decimationPhase = (_decimationPhase + 1) % DecimationFactor;
        }

        return requiredBytes;
    }

    /// <summary>
    /// Returns how many DualShock 4 samples a source block yields from the current decimation
    /// phase, without advancing it.
    /// </summary>
    /// <param name="sourceSampleCount">Number of source samples in the block.</param>
    /// <returns>The number of decimated samples the block produces.</returns>
    private int CountDecimatedSamples(int sourceSampleCount)
    {
        if (sourceSampleCount <= 0)
        {
            return 0;
        }

        int firstKept = (DecimationFactor - _decimationPhase) % DecimationFactor;
        if (sourceSampleCount <= firstKept)
        {
            return 0;
        }

        return (sourceSampleCount - firstKept + DecimationFactor - 1) / DecimationFactor;
    }

    /// <summary>
    /// Applies one step of the Direct Form I biquad low-pass and advances its history.
    /// </summary>
    /// <param name="sample">Source sample at 48 kHz.</param>
    /// <returns>The filtered sample.</returns>
    private double Filter(double sample)
    {
        double output = _b0 * sample + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1;
        _x1 = sample;
        _y2 = _y1;
        _y1 = output;
        return output;
    }

    /// <summary>The microphone target layouts the converter supports.</summary>
    private enum TargetKind
    {
        /// <summary>Two interleaved 48 kHz channels, 192 bytes per one-millisecond frame.</summary>
        DualSense,

        /// <summary>One 16 kHz channel, 32 bytes per one-millisecond frame.</summary>
        DualShock4
    }
}