using System.Diagnostics;
using DualSenseClient.Logging;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Captures the audio the host plays to its default output device (WASAPI loopback) and
/// forwards it to <see cref="ViiperDualSenseAudioForwarder"/>, so the physical controller's
/// speaker and haptics follow whatever the system is playing — without the game having to
/// select the virtual controller as its audio output.
/// </summary>
/// <remarks>
/// <para>
/// The alternative source to <see cref="ViiperDualSenseAudioCapture"/>: that one taps the
/// audio the host renders to the virtual controller's own endpoint (only works when the
/// game picks that endpoint), while this one taps the default output device's render stream
/// (works for any audio the user hears). The two are exclusive — the owning service
/// activates exactly one of them per virtual controller, or the forwarder would receive two
/// unrelated streams interleaved into one ring.
/// </para>
/// <para>
/// SoundFlow delivers the loopback stream as interleaved float with the format the device
/// negotiated (usually F32 stereo at the device's sample rate), on the audio thread. The
/// samples are converted to the 48 kHz interleaved stereo float the forwarder consumes:
/// channels are folded to the front pair and <see cref="LinearResampler"/> converts the rate
/// when the device does not run at 48 kHz.
/// </para>
/// <para>
/// The loopback stream is continuous — it keeps delivering silence while nothing is playing
/// — so an active capture holds the Bluetooth audio lane open, exactly like a game streaming
/// silence to the virtual controller does. Rumble, lightbar and trigger state keep reaching
/// the pad through the combined reports the forwarder sends while its lane is open.
/// </para>
/// </remarks>
public sealed class SystemOutputAudioCapture : IDisposable, IForwardedAudioCapture
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("SystemOutputAudioCapture");

    /// <summary>
    /// The format requested from the loopback device: the forwarder's own stream format.
    /// The device may negotiate a different one, which the conversion path handles.
    /// </summary>
    private static readonly AudioFormat RequestFormat = new AudioFormat
    {
        Format = SampleFormat.F32,
        Channels = 2,
        Layout = ChannelLayout.Stereo,
        SampleRate = DualSenseBtAudioPipeline.SampleRate
    };

    /// <summary>
    /// The shared audio engine used to open the loopback device.
    /// </summary>
    private readonly AudioEngine _engine;

    /// <summary>
    /// The forwarder receiving the captured audio.
    /// </summary>
    private readonly ViiperDualSenseAudioForwarder _forwarder;

    /// <summary>
    /// Keeps the native callback alive for the lifetime of the capture.
    /// </summary>
    private readonly AudioProcessCallback _callback;

    /// <summary>
    /// Serializes start/stop and guards the captured-device state and scratch buffers.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// The open loopback device, or <c>null</c> while stopped.
    /// </summary>
    private AudioCaptureDevice? _device;

    /// <summary>
    /// Rate converter for the negotiated device rate, or <c>null</c> when the device
    /// already runs at <see cref="DualSenseBtAudioPipeline.SampleRate"/>.
    /// </summary>
    private LinearResampler? _resampler;

    /// <summary>
    /// Scratch buffer for the captured samples folded to interleaved stereo at the device
    /// rate.
    /// </summary>
    private float[] _stereoScratch = [];

    /// <summary>
    /// Scratch buffer for the resampled interleaved stereo float block at 48 kHz.
    /// </summary>
    private float[] _outputScratch = [];

    /// <summary>
    /// Capture-content diagnostics: whether the system is actually playing audio.
    /// </summary>
    private bool _firstChunkLogged;

    private long _statsTimestamp;
    private long _totalFrames;
    private long _nonZeroSamples;

    /// <summary>
    /// Creates the capture feeding the given forwarder. The capture takes no ownership of
    /// the engine or the forwarder.
    /// </summary>
    public SystemOutputAudioCapture(AudioEngine engine, ViiperDualSenseAudioForwarder forwarder)
    {
        _engine = engine;
        _forwarder = forwarder;
        _callback = OnAudioProcessed;
    }

    /// <summary>
    /// Whether the loopback device is currently open.
    /// </summary>
    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _device is not null;
            }
        }
    }

    /// <summary>
    /// Opens the default output device for loopback capture and starts feeding the
    /// forwarder. Returns <c>false</c> when the platform or the machine has no default
    /// playback device to capture, or the device refuses the stream. Safe to call
    /// repeatedly; a running capture is left untouched.
    /// </summary>
    public bool Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            _log.Warning("System output loopback capture is only available on Windows");
            return false;
        }

        AudioCaptureDevice? device = null;
        lock (_sync)
        {
            if (_device is not null)
            {
                return true;
            }

            try
            {
                device = _engine.InitializeLoopbackDevice(RequestFormat, CreateConfig());
                device.OnAudioProcessed += _callback;
                device.Start();
            }
            catch (Exception ex)
            {
                _log.Warning($"Could not open the system output loopback capture: {ex.Message}");
                if (device is not null)
                {
                    device.OnAudioProcessed -= _callback;
                    device.Dispose();
                }

                return false;
            }

            AudioFormat format = device.Format;
            int channels = Math.Max(1, format.Channels);
            int sampleRate = format.SampleRate > 0 ? format.SampleRate : DualSenseBtAudioPipeline.SampleRate;
            _resampler = sampleRate == DualSenseBtAudioPipeline.SampleRate
                ? null
                : new LinearResampler((double)sampleRate / DualSenseBtAudioPipeline.SampleRate);
            _device = device;
            _log.Info(
                $"System output loopback started ('{device.Info?.Name ?? "default output"}', {channels}ch {sampleRate} Hz)"
                + (_resampler is null ? string.Empty : ", resampling to 48 kHz"));
        }

        return true;
    }

    /// <summary>
    /// Stops the capture and releases the device. Safe to call repeatedly.
    /// </summary>
    public void Stop()
    {
        AudioCaptureDevice? device;
        lock (_sync)
        {
            device = _device;
            _device = null;
            _resampler = null;
            _stereoScratch = [];
            _outputScratch = [];
        }

        if (device is null)
        {
            return;
        }

        // Unsubscribe before stopping so no new callback starts, then stop outside the lock:
        // stopping waits for the audio thread, and an in-flight callback that still needs
        // the lock must be able to finish.
        device.OnAudioProcessed -= _callback;
        try
        {
            device.Stop();
        }
        catch (Exception ex)
        {
            _log.Debug($"Stopping the system output loopback capture failed: {ex.Message}");
        }

        device.Dispose();
        _log.Info("System output loopback stopped");
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();

    /// <summary>
    /// Folds an interleaved frame block down to interleaved stereo: mono is duplicated to
    /// both channels, anything wider keeps the front pair (miniaudio's channel order puts
    /// front left/right first). <paramref name="stereo"/> must hold two samples per frame.
    /// </summary>
    public static void ToStereo(ReadOnlySpan<float> interleaved, int channels, Span<float> stereo)
    {
        if (channels <= 0)
        {
            return;
        }

        int frames = interleaved.Length / channels;
        for (int f = 0; f < frames; f++)
        {
            int source = f * channels;
            if (channels == 1)
            {
                stereo[f * 2] = interleaved[source];
                stereo[f * 2 + 1] = interleaved[source];
            }
            else
            {
                stereo[f * 2] = interleaved[source];
                stereo[f * 2 + 1] = interleaved[source + 1];
            }
        }
    }

    /// <summary>
    /// Called on the audio thread for every captured block: folds the block to interleaved
    /// stereo, converts the rate to 48 kHz and pushes it into the forwarder's ring.
    /// </summary>
    private void OnAudioProcessed(Span<float> samples, Capability capability)
    {
        lock (_sync)
        {
            if (_device is not { } device || samples.Length == 0)
            {
                return;
            }

            int channels = Math.Max(1, device.Format.Channels);
            int frames = samples.Length / channels;
            if (frames == 0)
            {
                return;
            }

            if (_stereoScratch.Length < frames * 2)
            {
                _stereoScratch = new float[frames * 2];
            }

            ToStereo(samples, channels, _stereoScratch);
            Span<float> stereo = _stereoScratch.AsSpan(0, frames * 2);

            if (_resampler is not { } resampler)
            {
                Feed(stereo, frames, capability);
                return;
            }

            int capacity = (int)(frames / resampler.Step) + 4;
            if (_outputScratch.Length < capacity * 2)
            {
                _outputScratch = new float[capacity * 2];
            }

            int written = resampler.Process(stereo, _outputScratch);
            Feed(_outputScratch.AsSpan(0, written * 2), written, capability);
        }
    }

    /// <summary>
    /// Records the content diagnostics and hands one 48 kHz stereo block to the forwarder.
    /// Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void Feed(ReadOnlySpan<float> stereo, int frames, Capability capability)
    {
        int nonZero = 0;
        for (int i = 0; i < stereo.Length; i++)
        {
            if (stereo[i] != 0f)
            {
                nonZero++;
            }
        }

        _totalFrames += frames;
        _nonZeroSamples += nonZero;

        long now = Stopwatch.GetTimestamp();
        if (!_firstChunkLogged)
        {
            _firstChunkLogged = true;
            _statsTimestamp = now;
            _log.Info($"System output capture first chunk ({capability}): {frames} frames, {nonZero}/{stereo.Length} non-zero samples");
        }
        else if (now - _statsTimestamp >= Stopwatch.Frequency * 2)
        {
            _statsTimestamp = now;
            double pct = _totalFrames == 0 ? 0 : _nonZeroSamples * 100.0 / (_totalFrames * 2);
            _log.Debug($"System output capture stats: {_totalFrames / 48.0 / 1000.0:F0} s fed, {pct:F1}% non-zero samples");
        }

        _forwarder.FeedPcm(stereo);
    }

    /// <summary>
    /// Device configuration for the loopback stream: a period matching the forwarder's
    /// block cadence, so captured blocks arrive at roughly the rate they are consumed.
    /// </summary>
    private static MiniAudioDeviceConfig CreateConfig()
    {
        return new MiniAudioDeviceConfig
        {
            PeriodSizeInFrames = DualSenseBtAudioPipeline.FramesPerBlock
        };
    }

    /// <summary>
    /// Streaming linear-interpolation resampler from the device's rate to
    /// <see cref="DualSenseBtAudioPipeline.SampleRate"/>. State (the previous frame and the
    /// fractional source position) carries across chunks, so arbitrarily sized feeds stay
    /// phase-continuous. Output must be sized for <c>ceil(frames / step) + 1</c> frames.
    /// </summary>
    public sealed class LinearResampler
    {
        /// <summary>
        /// Source frames consumed per output frame (device rate ÷ 48 kHz).
        /// </summary>
        private readonly double _step;

        /// <summary>
        /// The last input frame of the previous chunk, the source sample at position 0.
        /// </summary>
        private float _prevLeft;

        private float _prevRight;

        /// <summary>
        /// Fractional source position of the next output frame, relative to the current
        /// chunk's samples (position 0 = the previous chunk's last frame, position
        /// <c>k</c> = chunk frame <c>k - 1</c>).
        /// </summary>
        private double _position;

        /// <summary>
        /// Creates the resampler for the given source-to-target rate ratio.
        /// </summary>
        public LinearResampler(double step)
        {
            _step = step;
        }

        /// <summary>
        /// Source frames consumed per output frame.
        /// </summary>
        public double Step
        {
            get
            {
                return _step;
            }
        }

        /// <summary>
        /// Resamples one interleaved stereo chunk. Returns the number of output frames
        /// written.
        /// </summary>
        public int Process(ReadOnlySpan<float> input, Span<float> output)
        {
            int frames = input.Length / 2;
            if (frames == 0 || _step <= 0)
            {
                return 0;
            }

            int written = 0;
            double position = _position;
            bool inputExhausted = false;
            while (written < output.Length / 2)
            {
                int index = (int)position;
                if (index >= frames)
                {
                    inputExhausted = true;
                    break;
                }

                float frac = (float)(position - index);
                float s0Left = index == 0 ? _prevLeft : input[(index - 1) * 2];
                float s0Right = index == 0 ? _prevRight : input[(index - 1) * 2 + 1];
                float s1Left = input[index * 2];
                float s1Right = input[index * 2 + 1];
                output[written * 2] = s0Left + (s1Left - s0Left) * frac;
                output[written * 2 + 1] = s0Right + (s1Right - s0Right) * frac;
                written++;
                position += _step;
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