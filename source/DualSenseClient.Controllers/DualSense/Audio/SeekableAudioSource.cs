using DualSenseClient.Logging;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Decodes an audio file and presents it as a seekable 48 kHz stereo float stream.
/// Wraps a SoundFlow <see cref="StreamDataProvider"/> (FFmpeg-backed decoding to
/// 32-bit float at the file's native rate and channel count) behind a channel
/// converter and a linear-interpolation resampler to the DualSense native rate.
/// </summary>
/// <remarks>
/// Seeking re-positions the decoder and resets the resampler state (fractional
/// position and buffered samples), since interpolation state is invalid after a jump.
/// </remarks>
public sealed class SeekableAudioSource : IDisposable
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("SeekableAudioSource");

    /// <summary>
    /// Target output sample rate: 48 kHz, the DualSense native rate.
    /// </summary>
    public const int TargetSampleRate = 48000;

    /// <summary>
    /// Interleaved output channels (stereo).
    /// </summary>
    private const int TargetChannels = 2;

    /// <summary>
    /// Source frames requested per decoder refill.
    /// </summary>
    private const int SourceChunkFrames = 2048;

    /// <summary>
    /// The FFmpeg-backed decoder provider that yields 48 kHz stereo float samples.
    /// </summary>
    private readonly StreamDataProvider _provider;

    /// <summary>
    /// The full path of the file being decoded.
    /// </summary>
    private readonly string _fileName;

    /// <summary>
    /// Channel count the provider actually outputs, used to convert every source frame to
    /// stereo (mono and multi-channel sources are widened to a duplicated downmix).
    /// </summary>
    private readonly int _sourceChannels;

    /// <summary>
    /// Source frames consumed per output frame; the linear resampler steps by this each
    /// output sample.
    /// </summary>
    private readonly float _framesPerOutputFrame;

    /// <summary>
    /// Scratch buffer holding the not-yet-consumed decoded source frames.
    /// </summary>
    private float[] _source;

    /// <summary>
    /// Number of valid source frames currently held in <see cref="_source"/>.
    /// </summary>
    private int _sourceValidFrames;

    /// <summary>
    /// Fractional source-frame position of the next output sample.
    /// </summary>
    private double _fraction;

    /// <summary>
    /// Whether the decoder has been exhausted.
    /// </summary>
    private bool _endReached;

    /// <summary>
    /// Total output frames produced, used to report the read position.
    /// </summary>
    private long _outputFrames;

    /// <summary>
    /// Total duration of the file.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// The file being played.
    /// </summary>
    public string FileName => _fileName;

    /// <summary>
    /// Current read position; seeking resets the resampling state.
    /// </summary>
    public TimeSpan CurrentTime
    {
        get => TimeSpan.FromSeconds((double)_outputFrames / TargetSampleRate);
        set => Seek(value);
    }

    /// <summary>
    /// Opens the given audio file and prepares the 48 kHz stereo chain.
    /// </summary>
    /// <param name="engine">The shared audio engine used to decode the file.</param>
    /// <param name="path">Path of the audio file to decode.</param>
    public SeekableAudioSource(AudioEngine engine, string path)
    {
        _fileName = path;

        // SoundFlow's metadata readers are async internally and are driven synchronously
        // (ReadAsync().GetAwaiter().GetResult()). On the UI thread those awaits capture the
        // dispatcher's SynchronizationContext, whose continuations can only run on the UI
        // thread that is blocked waiting on the result — a deadlock that hangs the app on
        // open. (MP3's reader awaits throughout its fast path; FLAC/WAV are fully
        // synchronous, which is why they worked.) Build the provider with no
        // SynchronizationContext so continuations land on the thread pool.
        SynchronizationContext? original = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            _provider = new StreamDataProvider(
                engine,
                new AudioFormat
                {
                    Format = SampleFormat.F32,
                    Channels = TargetChannels,
                    Layout = ChannelLayout.Stereo,
                    SampleRate = TargetSampleRate
                },
                new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }

        _sourceChannels = Math.Max(1, _provider.FormatInfo?.ChannelCount ?? TargetChannels);
        _framesPerOutputFrame = _provider.SampleRate / (float)TargetSampleRate;
        _source = new float[SourceChunkFrames * _sourceChannels];

        Duration = _provider.Length > 0
            ? TimeSpan.FromSeconds((double)_provider.Length / (_provider.SampleRate * _sourceChannels))
            : TimeSpan.Zero;

        _log.Debug($"Decoding '{_fileName}' ({_sourceChannels} ch @ {_provider.SampleRate} Hz, "
                   + $"{_framesPerOutputFrame:F3} source frames/output frame, {Duration.TotalSeconds:F1}s)");
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> interleaved 48 kHz stereo floats. Returns the
    /// number of floats written, which is zero once the file has been fully consumed.
    /// </summary>
    public int Read(float[] buffer, int offset, int count)
    {
        int outputFrames = count / TargetChannels;
        int produced = 0;

        for (int f = 0; f < outputFrames; f++)
        {
            int idx0 = (int)_fraction;
            while (_sourceValidFrames <= idx0 + 1 && !_endReached)
            {
                Refill();
            }

            if (_sourceValidFrames <= idx0)
            {
                break;
            }

            float t = (float)(_fraction - idx0);
            int o = offset + f * TargetChannels;
            GetFrame(idx0, out float l0, out float r0);
            if (idx0 + 1 < _sourceValidFrames)
            {
                GetFrame(idx0 + 1, out float l1, out float r1);
                buffer[o] = l0 + (l1 - l0) * t;
                buffer[o + 1] = r0 + (r1 - r0) * t;
            }
            else
            {
                buffer[o] = l0;
                buffer[o + 1] = r0;
            }

            _fraction += _framesPerOutputFrame;
            produced += TargetChannels;
        }

        Compact();
        _outputFrames += produced / TargetChannels;
        return produced;
    }

    /// <summary>
    /// Jumps to the given position, re-creating the resampling state.
    /// </summary>
    /// <param name="time">Target playback position.</param>
    public void Seek(TimeSpan time)
    {
        long targetSourceFrame = (long)(time.TotalSeconds * _provider.SampleRate);
        int sampleOffset = (int)Math.Clamp(targetSourceFrame * _sourceChannels, 0L, (long)_provider.Length);
        _provider.Seek(sampleOffset);

        _sourceValidFrames = 0;
        _fraction = 0;
        _endReached = false;
        _outputFrames = (long)(time.TotalSeconds * TargetSampleRate);
    }

    /// <inheritdoc/>
    public void Dispose() => _provider.Dispose();

    /// <summary>
    /// Reads another chunk of decoded source frames into <see cref="_source"/>, or marks
    /// end-of-stream when the decoder is exhausted.
    /// </summary>
    private void Refill()
    {
        int framesToRead = SourceChunkFrames;
        while (framesToRead > 0 && !_endReached)
        {
            EnsureCapacity(_sourceValidFrames + framesToRead);
            int want = framesToRead * _sourceChannels;
            int got = _provider.ReadBytes(_source.AsSpan(_sourceValidFrames * _sourceChannels, want));
            if (got <= 0)
            {
                _endReached = true;
                break;
            }

            int gotFrames = got / _sourceChannels;
            _sourceValidFrames += gotFrames;
            framesToRead -= gotFrames;
        }
    }

    /// <summary>
    /// Discards source frames whose interpolation is complete, keeping
    /// <see cref="_fraction"/> in the range <c>[0, 1)</c>.
    /// </summary>
    private void Compact()
    {
        int consumedFrames = (int)_fraction;
        if (consumedFrames <= 0)
        {
            return;
        }

        int consumedSamples = consumedFrames * _sourceChannels;
        int remainingSamples = (_sourceValidFrames - consumedFrames) * _sourceChannels;
        if (remainingSamples > 0)
        {
            Array.Copy(_source, consumedSamples, _source, 0, remainingSamples);
        }

        _sourceValidFrames -= consumedFrames;
        _fraction -= consumedFrames;
    }

    /// <summary>
    /// Grows <see cref="_source"/> so it can hold <paramref name="requiredFrames"/>.
    /// </summary>
    private void EnsureCapacity(int requiredFrames)
    {
        int required = requiredFrames * _sourceChannels;
        if (required <= _source.Length)
        {
            return;
        }

        int newSize = _source.Length;
        while (newSize < required)
        {
            newSize *= 2;
        }

        Array.Resize(ref _source, newSize);
    }

    /// <summary>
    /// Converts a source frame to stereo (mono and multi-channel sources are widened to
    /// a duplicated downmix, matching the old mono-to-stereo widening).
    /// </summary>
    private void GetFrame(int frame, out float left, out float right)
    {
        switch (_sourceChannels)
        {
            case 1:
                left = right = _source[frame];
                break;
            case 2:
                left = _source[frame * 2];
                right = _source[frame * 2 + 1];
                break;
            default:
            {
                float sum = 0f;
                for (int c = 0; c < _sourceChannels; c++)
                {
                    sum += _source[frame * _sourceChannels + c];
                }

                left = right = sum / _sourceChannels;
                break;
            }
        }
    }
}