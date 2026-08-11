using DualSenseClient.Logging;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Renders 4-channel 48 kHz PCM to the physical DualSense's UAC1 render endpoint
/// (its Windows audio endpoint): audio on channels 1/2, haptics on channels 3/4.
/// Opens the endpoint exclusively as quadraphonic 16-bit — falling back to shared
/// quadraphonic, then shared stereo (haptics dropped) — and feeds it from a bounded
/// queue that drops on overflow so a slow producer cannot stall the pump.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint only accepts 16-bit PCM in exclusive mode, and haptics ride channels
/// 3/4 (channel mask <c>0x33</c> = FL/FR/BL/BR). The queue format must match the
/// open format; samples are delivered as float regardless of the device format.
/// </para>
/// </remarks>
public sealed class DualSenseUsbAudioTarget : IDisposable
{
    /// <summary>
    /// Frames per feed block (10.667 ms at 48 kHz), matching the Bluetooth pump cadence.
    /// </summary>
    public const int FramesPerBlock = DualSenseBtAudioPipeline.FramesPerBlock;

    /// <summary>
    /// Interleaved samples in one 4-channel block.
    /// </summary>
    private const int QuadBlockSamples = FramesPerBlock * 4;

    /// <summary>
    /// Interleaved samples in one stereo block.
    /// </summary>
    private const int StereoBlockSamples = FramesPerBlock * 2;

    /// <summary>
    /// DualSense UAC render endpoint format: 48 kHz quadraphonic 16-bit. The endpoint is a
    /// UAC1 device that only accepts 16-bit PCM in exclusive mode, and haptics ride
    /// channels 3/4 (channel mask <c>0x33</c> = FL/FR/BL/BR).
    /// </summary>
    private static readonly AudioFormat QuadFormat = new AudioFormat
    {
        Format = SampleFormat.S16,
        Channels = 4,
        Layout = ChannelLayout.Quad,
        SampleRate = DualSenseBtAudioPipeline.SampleRate
    };

    /// <summary>
    /// Stereo fallback format for endpoints that refuse 4-channel streams.
    /// </summary>
    private static readonly AudioFormat StereoFormat = new AudioFormat
    {
        Format = SampleFormat.F32,
        Channels = 2,
        Layout = ChannelLayout.Stereo,
        SampleRate = DualSenseBtAudioPipeline.SampleRate
    };

    /// <summary>
    /// SoundFlow applies a constant-power pan law (≈0.707 per channel) at the center
    /// position, on both the master mixer and every player — two stages equal a 0.5 gain.
    /// Scaling both back up by √2 restores unity so the fed samples reach the device
    /// unattenuated.
    /// </summary>
    private static readonly float UnityGain = MathF.Sqrt(2f);

    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("DualSenseUsbAudioTarget");

    /// <summary>
    /// The shared audio engine used to open the render device.
    /// </summary>
    private readonly AudioEngine _engine;

    /// <summary>
    /// Locates the DualSense UAC render endpoint.
    /// </summary>
    private readonly DualSenseAudioEndpointFinder _endpointFinder;

    /// <summary>
    /// Serializes start/stop/feed so the pump thread and the owner never race.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// USB render player, or <c>null</c> while the target is stopped.
    /// </summary>
    private SoundPlayer? _player;

    /// <summary>
    /// Queue feeding the USB render player, or <c>null</c> while stopped.
    /// </summary>
    private QueueDataProvider? _queue;

    /// <summary>
    /// Whether the opened stream is 4-channel (audio on channels 1/2, haptics on 3/4)
    /// rather than the shared stereo fallback.
    /// </summary>
    private bool _fourChannel;

    /// <summary>
    /// Scratch buffer for one 4-channel block converted to float.
    /// </summary>
    private readonly float[] _quadScratch = new float[QuadBlockSamples];

    /// <summary>
    /// Scratch buffer for one stereo block converted to float.
    /// </summary>
    private readonly float[] _stereoScratch = new float[StereoBlockSamples];

    /// <summary>
    /// Creates the target backed by the given engine and endpoint finder.
    /// </summary>
    public DualSenseUsbAudioTarget(AudioEngine engine, DualSenseAudioEndpointFinder endpointFinder)
    {
        _engine = engine;
        _endpointFinder = endpointFinder;
    }

    /// <summary>
    /// Whether the render target is currently open.
    /// </summary>
    public bool IsActive => _player is not null;

    /// <summary>
    /// Finds the physical DualSense render endpoint and opens it, preferring exclusive
    /// quadraphonic 16-bit (audio + haptics), falling back to shared quadraphonic and
    /// then shared stereo (audio only). Returns <c>false</c> when no endpoint is
    /// available. Safe to call repeatedly: re-opens after a stop.
    /// </summary>
    public bool Start()
    {
        lock (_sync)
        {
            if (_player is not null)
            {
                return true;
            }

            DeviceInfo? endpoint = _endpointFinder.FindRenderDevice();
            if (endpoint is null)
            {
                _log.Info("DualSense render endpoint not found; USB audio forwarding unavailable");
                return false;
            }

            try
            {
                TryOpen(endpoint.Value, QuadFormat, CreateExclusiveQuadConfig(), true);
                if (_player is null)
                {
                    TryOpen(endpoint.Value, QuadFormat, null, true);
                }
                if (_player is null)
                {
                    _log.Info("Shared quadraphonic stream unavailable; falling back to shared stereo — haptics are dropped");
                    TryOpen(endpoint.Value, StereoFormat, null, false);
                }
            }
            catch (Exception ex)
            {
                _log.LogExceptionDetails(ex);
                StopLocked();
            }

            if (_player is not null)
            {
                _log.Info($"USB audio forwarding started ({(_fourChannel ? "quadraphonic" : "stereo")})");
            }
            return _player is not null;
        }
    }

    /// <summary>
    /// Feeds interleaved 4-channel signed 16-bit PCM (audio on channels 1/2, haptics on
    /// 3/4) to the render target. Must be whole 512-frame blocks; extra frames are
    /// dropped. When the stream fell back to stereo, only the front channels are fed.
    /// </summary>
    public void Feed(ReadOnlySpan<short> interleavedQuad)
    {
        lock (_sync)
        {
            if (_player is null || _queue is null)
            {
                return;
            }

            int frames = interleavedQuad.Length / 4;
            for (int block = 0; block + FramesPerBlock <= frames; block += FramesPerBlock)
            {
                int offset = block * 4;
                if (_fourChannel)
                {
                    for (int i = 0; i < FramesPerBlock; i++)
                    {
                        int o = (i * 4);
                        _quadScratch[o] = interleavedQuad[offset + i * 4] / 32768f;
                        _quadScratch[o + 1] = interleavedQuad[offset + i * 4 + 1] / 32768f;
                        _quadScratch[o + 2] = interleavedQuad[offset + i * 4 + 2] / 32768f;
                        _quadScratch[o + 3] = interleavedQuad[offset + i * 4 + 3] / 32768f;
                    }
                    _queue.AddSamples(_quadScratch);
                }
                else
                {
                    for (int i = 0; i < FramesPerBlock; i++)
                    {
                        _stereoScratch[i * 2] = interleavedQuad[offset + i * 4] / 32768f;
                        _stereoScratch[i * 2 + 1] = interleavedQuad[offset + i * 4 + 1] / 32768f;
                    }
                    _queue.AddSamples(_stereoScratch);
                }
            }
        }
    }

    /// <summary>
    /// Stops the render target and releases the endpoint. Safe to call repeatedly.
    /// </summary>
    public void Stop()
    {
        lock (_sync)
        {
            StopLocked();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();

    /// <summary>
    /// Initializes a render device, its queue, and its player with the SoundFlow
    /// unity-gain setup.
    /// </summary>
    private void TryOpen(DeviceInfo endpoint, AudioFormat format, MiniAudioDeviceConfig? config, bool fourChannel)
    {
        AudioPlaybackDevice device = _engine.InitializePlaybackDevice(endpoint, format, config);
        QueueDataProvider queue = new QueueDataProvider(format, MaxQueueSamples(format), QueueFullBehavior.Drop);
        SoundPlayer player = new SoundPlayer(_engine, format, queue);
        device.MasterMixer.AddComponent(player);
        device.MasterMixer.Volume = UnityGain;
        player.Volume = UnityGain;
        player.Play();
        device.Start();

        _player = player;
        _queue = queue;
        _fourChannel = fourChannel;
    }

    /// <summary>
    /// Stops and releases the render target. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void StopLocked()
    {
        _queue = null;
        _player?.Dispose();
        _player = null;
        _fourChannel = false;
    }

    /// <summary>
    /// SoundFlow device config that opens a WASAPI exclusive stream with no automatic
    /// SRC, asserting the exact quadraphonic 16-bit format the DualSense endpoint needs.
    /// </summary>
    private static MiniAudioDeviceConfig CreateExclusiveQuadConfig()
    {
        return new MiniAudioDeviceConfig
        {
            PeriodSizeInFrames = 480,
            Playback = new DeviceSubConfig { ShareMode = ShareMode.Exclusive },
            Wasapi = new WasapiSettings { NoAutoConvertSRC = true }
        };
    }

    /// <summary>
    /// Queue capacity for one render target: half a second of its format. Fills beyond
    /// the cap are dropped rather than blocking the feed.
    /// </summary>
    private static int MaxQueueSamples(AudioFormat format) => (int)(DualSenseBtAudioPipeline.SampleRate * format.Channels * 0.5);
}