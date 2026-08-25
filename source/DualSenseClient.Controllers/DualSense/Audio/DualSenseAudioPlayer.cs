using System.Diagnostics;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Core.Utilities;
using DualSenseClient.Hid;
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
/// Plays a decoded audio stream to the desktop, to a DualSense over Bluetooth
/// (report <c>0x35</c> speaker/headset, combined with the <c>0x12</c> haptics packet when
/// haptics are enabled), and — over USB — to the controller's UAC render endpoint.
/// </summary>
/// <remarks>
/// <para>
/// A dedicated writer thread reads 512-frame blocks (512/48000 s = 10.667 ms) from a
/// <see cref="SeekableAudioSource"/> and fans each block out to every enabled target: a
/// desktop/Windows endpoint, the controller render endpoint (USB), the Bluetooth Opus
/// lane, and the Bluetooth haptics lane. Bluetooth reports are paced at the controller's
/// audio-clock cadence of 512/48000 s; the reverse-engineered reference implementations
/// (DS5_Bridge et al.) show that the firmware expects exactly that period — a 10 ms
/// scheduler is ~6.7% too fast and overflows the controller buffer every ~0.5 s, causing
/// periodic stutter.
/// </para>
/// <para>
/// USB haptics ride channels 3/4 of a 4-channel WASAPI stream (channel mask
/// <c>0x33</c> = L/R/LS/RS). The DualSense UAC endpoint only opens 4-channel streams in
/// exclusive mode, so wired haptics open the endpoint exclusively and fall back to shared
/// stereo (sound only) when that is refused.
/// </para>
/// </remarks>
public sealed class DualSenseAudioPlayer : IDisposable
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
    /// audio-clock cadence. Also the feed granularity for the desktop and USB render
    /// targets, which buffer independently at 48 kHz.
    /// </summary>
    public const int FramesPerBlock = 512;

    /// <summary>
    /// Opus frames per encoded Bluetooth audio frame. The 512-frame input block is
    /// resampled down to 480 frames (10 ms at 48 kHz) so each frame fits the fixed
    /// 200-byte CBR payload, matching the reference implementations.
    /// </summary>
    public const int OpusFrameSamples = 480;

    /// <summary>
    /// Interleaved float samples in one 512-frame stereo block (feeds the desktop and
    /// USB render targets).
    /// </summary>
    private const int BlockSamples = FramesPerBlock * Channels;

    /// <summary>
    /// Interleaved samples in one 480-frame Opus block.
    /// </summary>
    private const int OpusBlockSamples = OpusFrameSamples * Channels;

    /// <summary>
    /// Stereo frames carried by the Bluetooth haptics report (64 s8 bytes).
    /// </summary>
    private const int HapticsFrames = 32;

    /// <summary>
    /// Silence audio/haptics reports sent after priming the Bluetooth stream, so the
    /// controller's speaker path and Opus decoder warm up before real audio. 8 packets
    /// ≈ 85 ms at the 10.667 ms cadence.
    /// </summary>
    private const int BtPrerollPackets = 8;

    /// <summary>
    /// Target interval between writer ticks: 512/48000 s = 10.667 ms.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(FramesPerBlock / (double)SampleRate);

    /// <summary>
    /// Desktop/USB output format: 48 kHz stereo float, the stream the writer feeds.
    /// </summary>
    private static readonly AudioFormat StereoFormat = new AudioFormat
    {
        Format = SampleFormat.F32,
        Channels = 2,
        Layout = ChannelLayout.Stereo,
        SampleRate = SampleRate
    };

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
        SampleRate = SampleRate
    };

    /// <summary>
    /// SoundFlow applies a constant-power pan law (≈0.707 per channel) at the center
    /// position, on both the master mixer and every player — two stages equal a 0.5 gain.
    /// Scaling both back up by √2 restores unity so the writer's samples reach the device
    /// unattenuated.
    /// </summary>
    private static readonly float UnityGain = MathF.Sqrt(2f);

    /// <summary>
    /// Blocks pre-rolled into each render target's queue when the writer is idle, so the
    /// device starts with real audio (≈85 ms) instead of silence and absorbs writer
    /// scheduling jitter.
    /// </summary>
    private const int PrerollBlocks = 8;

    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("DualSenseAudioPlayer");

    /// <summary>
    /// The shared audio engine used to decode sources and open render devices.
    /// </summary>
    private readonly AudioEngine _engine;

    /// <summary>
    /// The controller audio is played to, or <c>null</c> for desktop-only playback.
    /// </summary>
    private readonly DualSenseDevice? _device;

    /// <summary>
    /// Locates the DualSense UAC render endpoint for USB playback.
    /// </summary>
    private readonly DualSenseAudioEndpointFinder _endpointFinder;

    /// <summary>
    /// Serializes access to the source, render targets, and writer state shared with the
    /// UI thread (seek, options, open/stop).
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// Cancels the running writer loop; <c>null</c> while playback is stopped or paused.
    /// </summary>
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Set by <see cref="Seek"/> while playing to request the writer loop to re-prime
    /// the Bluetooth stream and rebuild the render targets on its next tick, so the
    /// ~85 ms Bluetooth preroll and the device opens happen off the UI thread.
    /// </summary>
    private bool _restartRequested;

    /// <summary>
    /// The background task running the writer loop.
    /// </summary>
    private Task? _loopTask;

    /// <summary>
    /// The decoded, seekable 48 kHz stereo source, or <c>null</c> when no file is loaded.
    /// </summary>
    private SeekableAudioSource? _source;

    /// <summary>
    /// Queue feeding the desktop render player, or <c>null</c> when disabled.
    /// </summary>
    private QueueDataProvider? _desktopQueue;

    /// <summary>
    /// Desktop render player, or <c>null</c> when disabled.
    /// </summary>
    private SoundPlayer? _desktopPlayer;

    /// <summary>
    /// Desktop playback device, or <c>null</c> when disabled.
    /// </summary>
    private AudioPlaybackDevice? _desktopDevice;

    /// <summary>
    /// Queue feeding the USB render player, or <c>null</c> when the endpoint is not open.
    /// </summary>
    private QueueDataProvider? _usbQueue;

    /// <summary>
    /// USB render player, or <c>null</c> when the endpoint is not open.
    /// </summary>
    private SoundPlayer? _usbPlayer;

    /// <summary>
    /// USB playback device, or <c>null</c> when the endpoint is not open.
    /// </summary>
    private AudioPlaybackDevice? _usbDevice;

    /// <summary>
    /// Whether the USB stream is 4-channel (audio on channels 1/2, haptics on 3/4) rather
    /// than the shared stereo fallback.
    /// </summary>
    private bool _usbFourChannel;

    /// <summary>
    /// Shared Bluetooth audio conversion pipeline (resample, Opus encode, haptics).
    /// </summary>
    private readonly DualSenseBtAudioPipeline _btPipeline = new DualSenseBtAudioPipeline();

    /// <summary>
    /// Scratch buffer for one 512-frame stereo block read from the source.
    /// </summary>
    private readonly float[] _floatBlock = new float[BlockSamples];

    /// <summary>
    /// Scratch buffer holding the block converted to signed 16-bit PCM.
    /// </summary>
    private readonly short[] _pcmBlock = new short[BlockSamples];

    /// <summary>
    /// Scratch buffer for the 4-channel (quad) USB representation of one block.
    /// </summary>
    private readonly float[] _usbFloatBlock4 = new float[BlockSamples * 2];

    /// <summary>
    /// Scratch buffer for one 480-frame Opus block.
    /// </summary>
    private readonly short[] _opusBlock = new short[OpusBlockSamples];

    /// <summary>
    /// Scratch buffer for the fixed 200-byte Opus frame sent to the controller.
    /// </summary>
    private readonly byte[] _opusFrame = new byte[200];

    /// <summary>
    /// Scratch buffer for the 64-byte Bluetooth haptics payload.
    /// </summary>
    private readonly byte[] _hapticsPcm = new byte[HapticsFrames * 2];

    /// <summary>
    /// Speaker volume (0-255) applied to the controller and routed to the active output.
    /// </summary>
    private byte _speakerVolume = 0x50;

    /// <summary>
    /// Haptic vibration strength multiplier (1.0 = full).
    /// </summary>
    private float _hapticStrength = 1f;

    /// <summary>
    /// Whether audio is played to the desktop render target.
    /// </summary>
    private bool _playToDesktop = true;

    /// <summary>
    /// Whether audio is routed to the DualSense internal speaker.
    /// </summary>
    private bool _playToSpeaker;

    /// <summary>
    /// Whether audio is routed to the headset jack.
    /// </summary>
    private bool _playToHeadset;

    /// <summary>
    /// Whether the haptic actuators follow the audio.
    /// </summary>
    private bool _playToHaptics;

    private AudioControl OutputControl
    {
        get
        {
            return (_playToSpeaker, _playToHeadset) switch
            {
                (true, true) => AudioControl.OutputPathBoth,
                (true, false) => AudioControl.OutputPathSpeaker,
                _ => AudioControl.OutputPathHeadphones
            };
        }
    }

    /// <summary>
    /// Headphone volume for the active route. The single volume slider drives whichever
    /// destination is selected, so the headset gets the slider value while the speaker
    /// route keeps the hardware default.
    /// </summary>
    private byte HeadphoneVolume
    {
        get
        {
            return _playToHeadset ? _speakerVolume : (byte)0x3F;
        }
    }

    /// <summary>
    /// Whether the wrapped controller is connected over Bluetooth, the only transport
    /// that carries the <c>0x35</c>/<c>0x32</c> audio reports.
    /// </summary>
    private bool IsBluetooth
    {
        get
        {
            return _device?.ConnectionType == ConnectionType.Bluetooth;
        }
    }

    /// <summary>
    /// Whether the writer loop is currently consuming the source.
    /// </summary>
    public bool IsPlaying
    {
        get
        {
            return _cts is not null;
        }
    }

    /// <summary>
    /// Whether the writer loop task is still running. A loop that was just stopped may
    /// briefly outlive <see cref="IsPlaying"/> while it finishes its current frame.
    /// </summary>
    public bool IsWriterActive
    {
        get
        {
            return _loopTask is { IsCompleted: false };
        }
    }

    /// <summary>
    /// Current playback position (the source's read cursor).
    /// </summary>
    public TimeSpan Position { get; private set; }

    /// <summary>
    /// Duration of the loaded file, or <see cref="TimeSpan.Zero"/> when none is loaded.
    /// </summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>
    /// Raised after every writer tick with the new playback position.
    /// </summary>
    public event EventHandler? PositionChanged;

    /// <summary>
    /// Raised when playback starts or stops.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Raised when the source reaches the end of the file.
    /// </summary>
    public event EventHandler? PlaybackEnded;

    /// <summary>
    /// Raised after the writer loop has fully exited — that is, when playback ended
    /// naturally or was stopped and no further frames will be sent. Consumers can use it
    /// to order controller-side cleanup (e.g. releasing the audio route) after the last
    /// audio frame.
    /// </summary>
    public event EventHandler? WriterExited;

    /// <summary>
    /// Creates the player. Pass <c>null</c> for <paramref name="device"/> to limit
    /// output to the desktop.
    /// </summary>
    public DualSenseAudioPlayer(DualSenseDevice? device, DualSenseAudioEndpointFinder endpointFinder, AudioEngine engine)
    {
        _device = device;
        _endpointFinder = endpointFinder;
        _engine = engine;
    }

    /// <summary>
    /// Opens an audio file, stopping any current playback. The file is not played until
    /// <see cref="Play"/> is called.
    /// </summary>
    /// <param name="path">Path of the audio file to load.</param>
    public void OpenFile(string path)
    {
        Stop();
        SeekableAudioSource source = new SeekableAudioSource(_engine, path);
        lock (_sync)
        {
            _source?.Dispose();
            _source = source;
            Duration = source.Duration;
            Position = TimeSpan.Zero;
            ApplyControllerState();
        }

        _log.Info($"Opened '{source.FileName}' ({source.Duration.TotalSeconds:F1}s)");
    }

    /// <summary>
    /// Re-applies the output toggles, volumes, and render targets. Safe to call while
    /// playing: enabled targets are (re)created and primed, disabled ones are stopped.
    /// </summary>
    public void ApplyOptions(bool desktop, bool speaker, bool headset, bool haptics, byte speakerVolume, float hapticStrength)
    {
        _playToDesktop = desktop;
        _playToSpeaker = speaker;
        _playToHeadset = headset;
        _playToHaptics = haptics;
        _speakerVolume = speakerVolume;
        _hapticStrength = hapticStrength;

        ApplyControllerState();
        RebuildRenderTargets();

        _log.Debug($"Output options applied (desktop={desktop}, speaker={speaker}, headset={headset}, "
                   + $"haptics={haptics}, speakerVolume=0x{speakerVolume:X2}, hapticStrength={hapticStrength:F2})");
    }

    /// <summary>
    /// Starts (or resumes) the writer loop. The Bluetooth stream is primed and the
    /// render targets are opened on the writer thread before its first tick, so the
    /// ~85 ms preroll does not block the caller.
    /// </summary>
    public void Play()
    {
        lock (_sync)
        {
            if (IsPlaying || _source is null)
            {
                return;
            }

            if (_device is not null)
            {
                _device.SetAudioOutput(OutputControl, _speakerVolume, HeadphoneVolume);
            }

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => WriterLoop(_cts.Token));
        }

        TimerResolution.AddRef();
        OnStateChanged();
        _log.Debug($"Playback started ({(_source is not null ? _source.FileName : "no file")})");
    }

    /// <summary>
    /// Pauses the writer loop without releasing the source.
    /// </summary>
    public void Pause()
    {
        CancellationTokenSource? cts;
        lock (_sync)
        {
            cts = _cts;
            _cts = null;
        }

        cts?.Cancel();
        if (cts is not null)
        {
            TimerResolution.Release();
            OnStateChanged();
            _log.Debug("Playback paused");
        }
    }

    /// <summary>
    /// Stops playback and releases the loaded source.
    /// </summary>
    public void Stop()
    {
        Pause();
        lock (_sync)
        {
            _source?.Dispose();
            _source = null;
            Position = TimeSpan.Zero;
            Duration = TimeSpan.Zero;
        }

        OnPositionChanged();
        _log.Debug("Playback stopped, source released");
    }

    /// <summary>
    /// Jumps the source to the given position. The Bluetooth stream re-prime and the
    /// render-target rebuild are deferred to the writer loop's next tick (flagged via
    /// <see cref="_restartRequested"/>), so seeking does not block the caller.
    /// </summary>
    /// <param name="position">Target playback position.</param>
    public void Seek(TimeSpan position)
    {
        lock (_sync)
        {
            if (_source is null)
            {
                return;
            }

            _source.CurrentTime = ClampPosition(position);
            Position = _source.CurrentTime;
            if (IsPlaying)
            {
                _restartRequested = true;
            }
        }

        OnPositionChanged();
        _log.Debug($"Seek to {Position.TotalSeconds:F2}s");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Stop();
        lock (_sync)
        {
            _btPipeline.Dispose();
        }
    }

    /// <summary>
    /// Runs the writer loop until canceled, the source ends, or the file is released.
    /// The Bluetooth stream is primed and the render targets are (re)opened here rather
    /// than on the caller's thread, since the preroll blocks for ~85 ms and opening
    /// WASAPI devices can take tens of milliseconds. Ticks are paced against an
    /// accumulated deadline so the average cadence matches <see cref="TickInterval"/>
    /// exactly (a per-tick restart would drift).
    /// </summary>
    private void WriterLoop(CancellationToken ct)
    {
        _log.Debug("Writer loop started");

        lock (_sync)
        {
            // A seek-then-pause race can leave a stale restart request behind; the
            // startup below already primes the stream and rebuilds the targets.
            _restartRequested = false;

            if (_source is not null)
            {
                // Rebuild the render targets first: the WASAPI opens and the queue
                // pre-roll can take tens of milliseconds, and the Bluetooth preroll
                // must end exactly one tick before the first real audio frame — a
                // gap after the silence lets the controller's buffer underrun and
                // crackle when audio resumes.
                RebuildRenderTargets();
                if (_device is not null && IsBluetooth)
                {
                    RestartBluetoothStream();
                }
            }
        }

        long tickPeriodTicks = (long)(Stopwatch.Frequency * TickInterval.TotalSeconds);
        long nextTick = Stopwatch.GetTimestamp() + tickPeriodTicks;
        while (!ct.IsCancellationRequested)
        {
            bool restartOccurred;
            try
            {
                if (!ProcessBlock(ct, out restartOccurred))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogExceptionDetails(ex);
                break;
            }

            // A restart consumes ~100 ms (preroll + render-target rebuild); re-anchor
            // the deadline so the first real frame lands exactly one tick after the
            // last preroll silence and the loop never bursts catch-up frames into the
            // pad's shallow receive buffer (which drops audio and crackles).
            nextTick = restartOccurred
                ? Stopwatch.GetTimestamp() + tickPeriodTicks
                : nextTick + tickPeriodTicks;
            WaitUntil(nextTick);
        }

        _log.Debug("Writer loop ended");
        WriterExited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Blocks until the stopwatch reaches <paramref name="deadlineTimestamp"/>, sleeping
    /// in whole milliseconds and spinning out the last sub-millisecond.
    /// </summary>
    private static void WaitUntil(long deadlineTimestamp)
    {
        while (Stopwatch.GetTimestamp() < deadlineTimestamp)
        {
            double remainingMs = (deadlineTimestamp - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
            if (remainingMs > 1.0)
            {
                Thread.Sleep(Math.Max(1, (int)(remainingMs - 1.0)));
            }
            else
            {
                Thread.Yield();
            }
        }
    }

    /// <summary>
    /// Reads one block from the source and feeds every enabled target. Returns
    /// <c>false</c> when playback should stop.
    /// </summary>
    private bool ProcessBlock(CancellationToken ct, out bool restartOccurred)
    {
        ct.ThrowIfCancellationRequested();

        restartOccurred = false;
        int read;
        lock (_sync)
        {
            if (_source is null)
            {
                EndPlayback();
                return false;
            }

            if (_restartRequested)
            {
                _restartRequested = false;
                restartOccurred = true;
                RebuildRenderTargets();
                if (IsBluetooth)
                {
                    RestartBluetoothStream();
                }
            }

            read = _source.Read(_floatBlock, 0, BlockSamples);
            Position = _source.CurrentTime;

            if (read <= 0)
            {
                EndPlayback();
                return false;
            }

            if (read < BlockSamples)
            {
                Array.Clear(_floatBlock, read, BlockSamples - read);
            }

            _desktopQueue?.AddSamples(_floatBlock);

            if (_usbQueue is not null)
            {
                if (_usbFourChannel)
                {
                    ToUsbFourChannel(_floatBlock, _usbFloatBlock4);
                    _usbQueue.AddSamples(_usbFloatBlock4);
                }
                else
                {
                    _usbQueue.AddSamples(_floatBlock);
                }
            }

            if (IsBluetooth && (_playToSpeaker || _playToHeadset || _playToHaptics))
            {
                DualSenseBtAudioPipeline.ConvertToPcm16(_floatBlock, _pcmBlock);
                if (_playToSpeaker || _playToHeadset)
                {
                    DualSenseBtAudioPipeline.ResampleToOpusBlock(_pcmBlock, _opusBlock);
                    _btPipeline.EncodeOpus(_opusBlock, _opusFrame);
                    if (_playToHaptics)
                    {
                        DualSenseBtAudioPipeline.ToHapticsPcm(_pcmBlock, _hapticsPcm, _hapticStrength);
                    }

                    SendBluetoothAudioReports(_opusFrame);
                }
                else
                {
                    DualSenseBtAudioPipeline.ToHapticsPcm(_pcmBlock, _hapticsPcm, _hapticStrength);
                    _device!.SendBluetoothHaptics(_hapticsPcm);
                }
            }
        }

        OnPositionChanged();
        return true;
    }

    /// <summary>
    /// Stops playback and reports that the file ended.
    /// </summary>
    private void EndPlayback()
    {
        _log.Debug("End of file reached");
        Pause();
        OnPlaybackEnded();
    }

    /// <summary>
    /// Applies the current routing and volumes to the controller (works on both USB and
    /// Bluetooth), re-priming the Bluetooth stream when it is active.
    /// </summary>
    private void ApplyControllerState()
    {
        if (_device is null)
        {
            return;
        }

        _device.SetAudioOutput(OutputControl, _speakerVolume, HeadphoneVolume);
        if (IsPlaying && IsBluetooth)
        {
            SendBluetoothPrime();
        }
    }

    /// <summary>
    /// Sends the report <c>0x32</c> init-prime for the current output state.
    /// </summary>
    private void SendBluetoothPrime()
    {
        _log.Debug("Sending Bluetooth audio stream init-prime");
        _device!.SendBluetoothAudioPrime(CreateAudioState());
    }

    /// <summary>
    /// Resets and re-primes the Bluetooth audio/haptics stream and warms it up with
    /// <see cref="BtPrerollPackets"/> silence reports. The Opus encoder is recreated so
    /// its internal state starts clean together with the controller's decoder after the
    /// re-prime; the caller must then re-anchor the tick deadline so the first real
    /// audio frame follows exactly one tick after the last silence report.
    /// </summary>
    private void RestartBluetoothStream()
    {
        _device!.ResetBluetoothAudioStream();
        _btPipeline.ResetEncoder();
        SendBluetoothPrime();
        SendBtPreroll();
    }

    /// <summary>
    /// Builds the 47-byte output state (routing, volumes and audio control) that both
    /// the init-prime and the per-tick combined <c>0x36</c> report embed.
    /// </summary>
    private SetStateData CreateAudioState()
    {
        return new SetStateData
        {
            ValidFlag0 = ValidFlags.AllowSpeakerVolume | ValidFlags.AllowHeadphoneVolume | ValidFlags.AllowAudioControl,
            ValidFlag1 = ValidFlags.AllowAudioControl2,
            SpeakerVolume = _speakerVolume,
            HeadphoneVolume = HeadphoneVolume,
            AudioControl = OutputControl,
            AudioControl2 = 0x02
        };
    }

    /// <summary>
    /// Sends the Bluetooth audio frame to every enabled route. When haptics are enabled the
    /// first route's report is the combined <c>0x36</c> (state + audio + haptics packets in
    /// one report); the remaining routes carry audio only. Haptics are never sent as a
    /// separate <c>0x32</c> report while an audio route is active.
    /// </summary>
    private void SendBluetoothAudioReports(ReadOnlySpan<byte> opusFrame)
    {
        SetStateData state = CreateAudioState();
        bool first = true;
        if (_playToSpeaker)
        {
            SendBluetoothRoute(state, opusFrame, BluetoothAudioRoute.Speaker, first);
            first = false;
        }

        if (_playToHeadset)
        {
            SendBluetoothRoute(state, opusFrame, BluetoothAudioRoute.Headset, first);
        }
    }

    /// <summary>
    /// Sends one Bluetooth audio route, folding the haptics packet into the first route's
    /// report when haptics are enabled.
    /// </summary>
    private void SendBluetoothRoute(SetStateData state, ReadOnlySpan<byte> opusFrame, BluetoothAudioRoute route, bool first)
    {
        if (first && _playToHaptics)
        {
            _device!.SendBluetoothAudioAndHaptics(state, opusFrame, _hapticsPcm, route);
        }
        else
        {
            _device!.SendBluetoothAudio(opusFrame, route);
        }
    }

    /// <summary>
    /// (Re)creates the PCM render targets (desktop and USB) to match the current toggles,
    /// pre-rolling each queue with <see cref="PrerollBlocks"/> of freshly decoded audio so
    /// the device starts without underrun. Safe while the writer runs: both sides serialize
    /// on <see cref="_sync"/>, so reading ahead in <see cref="PrimeQueue"/> cannot lose
    /// samples — the writer simply continues after the primed blocks.
    /// </summary>
    private void RebuildRenderTargets()
    {
        lock (_sync)
        {
            StopRenderTargets();
            if (_source is null)
            {
                return;
            }

            if (_playToDesktop)
            {
                (_desktopDevice, _desktopQueue, _desktopPlayer) = CreateRenderTarget(_engine, null, StereoFormat, null);
                PrimeQueue(_desktopQueue, false);
                _desktopPlayer.Play();
                _desktopDevice.Start();
            }

            if (_device is not null
                && _device.ConnectionType == ConnectionType.Usb
                && (_playToSpeaker || _playToHeadset))
            {
                OpenUsbTarget();
            }
        }
    }

    /// <summary>
    /// Opens the controller render endpoint for USB playback. The DualSense UAC endpoint is
    /// natively 4-channel (audio on 1/2, haptics on 3/4), so the stream is always
    /// quadraphonic — a stereo stream would be up-mixed by the host (or miniaudio) into the
    /// haptic channels, vibrating the controller even with haptics disabled. Channels 3/4
    /// are zeroed in <see cref="ToUsbFourChannel"/> whenever haptics are off. Haptics
    /// prefer the exact 4-channel 48 kHz format in exclusive mode; sound-only playback
    /// prefers shared quadraphonic, falling back to shared stereo when both are refused.
    /// </summary>
    private void OpenUsbTarget()
    {
        try
        {
            DeviceInfo? endpoint = _endpointFinder.FindRenderDevice();
            if (endpoint is null)
            {
                _log.Info("DualSense render endpoint not found; USB audio unavailable");
                return;
            }

            if (_playToHaptics)
            {
                try
                {
                    _usbFourChannel = true;
                    OpenUsbDevice(endpoint.Value, QuadFormat, CreateExclusiveQuadConfig(), true);
                    return;
                }
                catch (Exception ex)
                {
                    DisposeUsbTarget();
                    _log.Info($"Exclusive quadraphonic stream unavailable ({ex.Message}); USB haptics need "
                              + "\"Allow applications to take exclusive control\" on the DualSense in Windows "
                              + "Sound settings — trying a shared quadraphonic stream");
                    _log.LogExceptionDetails(ex);
                }
            }

            try
            {
                _usbFourChannel = true;
                OpenUsbDevice(endpoint.Value, QuadFormat, null, true);
                return;
            }
            catch (Exception ex)
            {
                DisposeUsbTarget();
                _log.Info($"Shared quadraphonic stream unavailable ({ex.Message}); falling back to shared "
                          + "stereo — haptics may pulse with the audio because the host up-mixes stereo "
                          + "into the haptic channels");
                _log.LogExceptionDetails(ex);
            }

            _usbFourChannel = false;
            OpenUsbDevice(endpoint.Value, StereoFormat, null, false);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to open DualSense render endpoint: {ex.Message}");
            DisposeUsbTarget();
        }
    }

    /// <summary>
    /// Initializes a USB render device, its queue, and its player, pre-rolling the queue
    /// and starting the device.
    /// </summary>
    private void OpenUsbDevice(DeviceInfo endpoint, AudioFormat format, MiniAudioDeviceConfig? config, bool fourChannel)
    {
        (_usbDevice, _usbQueue, _usbPlayer) = CreateRenderTarget(_engine, endpoint, format, config);
        PrimeQueue(_usbQueue, fourChannel);
        _usbPlayer.Play();
        _usbDevice.Start();
    }

    /// <summary>
    /// Initializes a render device, its queue, and its player with the SoundFlow
    /// unity-gain setup. Callers pre-roll or feed the queue and start the device.
    /// </summary>
    private static (AudioPlaybackDevice Device, QueueDataProvider Queue, SoundPlayer Player) CreateRenderTarget(
        AudioEngine engine, DeviceInfo? endpoint, AudioFormat format, MiniAudioDeviceConfig? config)
    {
        AudioPlaybackDevice device = engine.InitializePlaybackDevice(endpoint, format, config);
        QueueDataProvider queue = new QueueDataProvider(format, MaxQueueSamples(format), QueueFullBehavior.Drop);
        SoundPlayer player = new SoundPlayer(engine, format, queue);
        device.MasterMixer.AddComponent(player);
        device.MasterMixer.Volume = UnityGain;
        player.Volume = UnityGain;
        return (device, queue, player);
    }

    /// <summary>
    /// Pre-rolls a render target's queue with <see cref="PrerollBlocks"/> of freshly
    /// decoded audio so the device starts with real data instead of silence. Advances the
    /// source cursor, so it is only used while the writer loop is not running.
    /// </summary>
    private void PrimeQueue(QueueDataProvider queue, bool fourChannel)
    {
        for (int i = 0; i < PrerollBlocks && _source is not null; i++)
        {
            int read = _source.Read(_floatBlock, 0, BlockSamples);
            if (read <= 0)
            {
                break;
            }

            if (read < BlockSamples)
            {
                Array.Clear(_floatBlock, read, BlockSamples - read);
            }

            if (fourChannel)
            {
                ToUsbFourChannel(_floatBlock, _usbFloatBlock4);
                queue.AddSamples(_usbFloatBlock4);
            }
            else
            {
                queue.AddSamples(_floatBlock);
            }
        }
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
            Playback = new DeviceSubConfig
            {
                ShareMode = ShareMode.Exclusive
            },
            Wasapi = new WasapiSettings
            {
                NoAutoConvertSRC = true
            }
        };
    }

    /// <summary>
    /// Queue capacity for one render target: half a second of its format. Fills beyond
    /// the cap are dropped rather than blocking the writer.
    /// </summary>
    private static int MaxQueueSamples(AudioFormat format) => (int)(SampleRate * format.Channels * 0.5);

    /// <summary>
    /// Stops and releases the desktop and USB render targets.
    /// </summary>
    private void StopRenderTargets()
    {
        _desktopDevice?.Dispose();
        _desktopDevice = null;
        _desktopPlayer?.Dispose();
        _desktopPlayer = null;
        _desktopQueue = null;

        DisposeUsbTarget();
    }

    /// <summary>
    /// Stops and releases the USB render target.
    /// </summary>
    private void DisposeUsbTarget()
    {
        _usbDevice?.Dispose();
        _usbDevice = null;
        _usbPlayer?.Dispose();
        _usbPlayer = null;
        _usbQueue = null;
        _usbFourChannel = false;
    }

    /// <summary>
    /// Sends <see cref="BtPrerollPackets"/> of silence audio/haptics reports at the
    /// 10.667 ms cadence so the controller's speaker path and Opus decoder are warmed up
    /// before real audio arrives. Blocks the caller for ~85 ms; call right after priming
    /// the stream.
    /// </summary>
    private void SendBtPreroll()
    {
        if (_device is null || !IsBluetooth)
        {
            return;
        }

        _log.Debug($"Sending {BtPrerollPackets} silence packets to warm up the Bluetooth audio stream");
        _opusBlock.AsSpan().Clear();
        try
        {
            _btPipeline.EncodeOpus(_opusBlock, _opusFrame);
        }
        catch (InvalidOperationException ex)
        {
            _log.Error("Failed to encode the Bluetooth preroll silence packet; skipping warmup");
            _log.LogExceptionDetails(ex);
            return;
        }

        _hapticsPcm.AsSpan().Clear();
        long prerollTicks = (long)(Stopwatch.Frequency * TickInterval.TotalSeconds);
        long next = Stopwatch.GetTimestamp() + prerollTicks;
        TimerResolution.AddRef();
        try
        {
            for (int i = 0; i < BtPrerollPackets; i++)
            {
                SendBluetoothAudioReports(_opusFrame);
                if (_playToHaptics && !(_playToSpeaker || _playToHeadset))
                {
                    _device.SendBluetoothHaptics(_hapticsPcm);
                }

                next += prerollTicks;
                WaitUntil(next);
            }
        }
        finally
        {
            TimerResolution.Release();
        }
    }

    /// <summary>
    /// Expands a stereo float block into a 4-channel float USB stream: channels 1/2 carry
    /// the audio, channels 3/4 carry the haptics scaled by the current strength — or
    /// silence when haptics are disabled.
    /// </summary>
    private void ToUsbFourChannel(ReadOnlySpan<float> stereo, Span<float> quad)
    {
        float haptic = _playToHaptics ? _hapticStrength : 0f;
        for (int f = 0; f < FramesPerBlock; f++)
        {
            int o = f * 4;
            quad[o] = stereo[f * 2];
            quad[o + 1] = stereo[f * 2 + 1];
            quad[o + 2] = stereo[f * 2] * haptic;
            quad[o + 3] = stereo[f * 2 + 1] * haptic;
        }
    }

    /// <summary>
    /// Clamps a requested position to be non-negative.
    /// </summary>
    private static TimeSpan ClampPosition(TimeSpan position) => position < TimeSpan.Zero ? TimeSpan.Zero : position;

    /// <summary>
    /// Raises <see cref="PositionChanged"/>.
    /// </summary>
    private void OnPositionChanged() => PositionChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Raises <see cref="StateChanged"/>.
    /// </summary>
    private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Raises <see cref="PlaybackEnded"/>.
    /// </summary>
    private void OnPlaybackEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
}