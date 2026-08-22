using System.Diagnostics;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Core.Utilities;
using DualSenseClient.Hid;
using DualSenseClient.Logging;
using DualSenseClient.VIIPER.DualSense;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Forwards host audio to the physical DualSense while VIIPER emulation is active, so
/// the pad's own speaker plays the host's audio. The host loopback capture feeds one
/// 512-frame (10.667 ms) stereo block per <see cref="FeedPcm"/> call into a bounded
/// ring; a pump thread paced at the DualSense Bluetooth audio-clock cadence pops blocks
/// and fans them out to the active transport: Bluetooth HID reports (<c>0x35</c>/<c>0x36</c>)
/// or the USB UAC render endpoint via <see cref="DualSenseUsbAudioTarget"/>.
/// </summary>
/// <remarks>
/// <para>
/// The ring absorbs the capture-callback jitter and the difference between the host
/// loopback period and the 512/48000 s controller cadence. Underruns send silence so
/// the stream stays paced; when the ring fills beyond the target latency the pump
/// discards one extra block per tick until it settles, keeping end-to-end latency
/// bounded. Haptics are derived from the same audio block (voice-coil follows the
/// audio), matching <see cref="DualSenseAudioPlayer"/>.
/// </para>
/// <para>
/// The Bluetooth audio lane is only opened once audio is actually being fed: the
/// first block of a session primes the stream (reset, init-prime and silence
/// preroll) and, after the lane has been idle long enough to underrun, the next
/// session re-primes. An idle forwarder therefore never occupies the Bluetooth
/// audio lane â€” which would make the controller ignore the virtual controller's
/// output-state reports (rumble, adaptive triggers, LEDs) â€” and never feeds the
/// USB endpoint.
/// </para>
/// <para>
/// While the lane is open the pad still ignores standalone output-state reports, so
/// the game's output state is embedded into the combined reports instead: the
/// <see cref="UpdateGameOutputState"/> snapshot rides the <c>0x36</c> state block
/// and the <c>0x32</c> init-prime (triggers, lightbar and player LEDs apply
/// normally, with the audio bytes overridden), and the game's classic rumble is
/// synthesized into the haptics PCM as 60/180 Hz sine waves.
/// </para>
/// <para>
/// When libVIIPER's realtime-haptics callback delivers a fresh, non-silent haptics
/// payload (the game's own voice-coil data extracted from the 398-byte combined
/// report via <see cref="UpdateGameHaptics"/>), it replaces the audio-derived
/// haptics so the pad reproduces the game's actual haptics; the audio-derived path
/// is only the fallback for games that do not drive the haptics channels.
/// </para>
/// </remarks>
public sealed class ViiperDualSenseAudioForwarder : IDisposable
{
    /// <summary>
    /// Interleaved float samples in one 512-frame stereo block.
    /// </summary>
    private const int BlockSamples = DualSenseBtAudioPipeline.FramesPerBlock * DualSenseBtAudioPipeline.Channels;

    /// <summary>
    /// Interleaved samples in one 480-frame Opus block.
    /// </summary>
    private const int OpusBlockSamples = DualSenseBtAudioPipeline.OpusFrameSamples * DualSenseBtAudioPipeline.Channels;

    /// <summary>
    /// Interleaved samples in one 512-frame quad block fed to the USB render target.
    /// </summary>
    private const int QuadBlockSamples = DualSenseBtAudioPipeline.FramesPerBlock * 4;

    /// <summary>
    /// Silence audio/haptics reports sent after priming the Bluetooth stream, so the
    /// controller's speaker path and Opus decoder warm up before real audio. 8 packets
    /// â‰ˆ 85 ms at the 10.667 ms cadence.
    /// </summary>
    private const int BtPrerollPackets = 8;

    /// <summary>
    /// Ring capacity in blocks (32 Ã— 10.667 ms â‰ˆ 341 ms of buffered audio).
    /// </summary>
    private const int RingBlocks = 32;

    /// <summary>
    /// Steady-state latency the pump targets (3 blocks â‰ˆ 32 ms). Above it, the pump
    /// discards one extra block per tick until the ring drains back down.
    /// </summary>
    private const int TargetBlocks = 3;

    /// <summary>
    /// Offset of the haptics payload within the 398-byte combined Bluetooth report
    /// delivered by libVIIPER's realtime-haptics callback. The report is the vDS-style
    /// <c>0x36</c> transport used by the virtual-device feedback stream: <c>[0]</c> is
    /// the report id, <c>[2..10]</c> the session block (<c>0x91 0x07 0xFE</c> + codec
    /// flags), <c>[11..73]</c> the <c>0x90 0x3F</c> state block carrying the native
    /// 47-byte output state at <c>[13..59]</c>, <c>[74..77]</c> the audio sub-packet
    /// header, and then <see cref="DualSenseBtAudioPipeline.HapticsBytes"/> of
    /// interleaved s8 stereo PCM at 3 kHz. This matches DS4Windows' consumption of the
    /// same feedback (<c>DualSenseBluetoothAudioPacer.RealtimeHapticsDataOffset = 78</c>).
    /// </summary>
    private const int BluetoothCombinedHapticsPcmOffset = 78;

    /// <summary>
    /// How long an update from the realtime-haptics callback stays fresh before the
    /// forwarder falls back to the audio-derived haptics. The callback fires every
    /// rear-haptics interval (~10.667 ms), so this window (~9 intervals) tolerates
    /// scheduling gaps while keeping the fallback prompt when the game stops driving
    /// the haptics channels.
    /// </summary>
    private const int GameHapticsFreshWindowMs = 100;

    /// <summary>
    /// Target interval between pump ticks: 512/48000 s = 10.667 ms.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(DualSenseBtAudioPipeline.FramesPerBlock / (double)DualSenseBtAudioPipeline.SampleRate);

    /// <summary>
    /// <see cref="GameHapticsFreshWindowMs"/> in stopwatch ticks.
    /// </summary>
    private static readonly long GameHapticsFreshWindowTicks = (long)(Stopwatch.Frequency * GameHapticsFreshWindowMs / 1000.0);

    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("ViiperDualSenseAudioForwarder");

    /// <summary>
    /// The physical controller's Bluetooth audio lane, or <c>null</c> when the pad is
    /// not Bluetooth-connected.
    /// </summary>
    private readonly IDualSenseAudioOutputs? _outputs;

    /// <summary>
    /// The physical controller's USB render target, or <c>null</c> when the pad is not
    /// USB-connected or no render endpoint exists.
    /// </summary>
    private readonly DualSenseUsbAudioTarget? _usbTarget;

    /// <summary>
    /// Shared Bluetooth audio conversion pipeline (resample, Opus encode, haptics).
    /// </summary>
    private readonly DualSenseBtAudioPipeline _pipeline = new DualSenseBtAudioPipeline();

    /// <summary>
    /// Ring holding the captured stereo blocks between the capture callback and the pump.
    /// </summary>
    private readonly PcmBlockRing _audioRing = new PcmBlockRing(RingBlocks);

    /// <summary>
    /// Serializes start/stop and the pump's per-tick work with feed/stop calls.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// Cancels the running pump loop; <c>null</c> while the forwarder is stopped.
    /// </summary>
    private CancellationTokenSource? _cts;

    /// <summary>
    /// The background task running the pump loop.
    /// </summary>
    private Task? _loopTask;

    /// <summary>
    /// Scratch buffer for one block popped from the ring.
    /// </summary>
    private readonly float[] _floatBlock = new float[BlockSamples];

    /// <summary>
    /// Scratch buffer holding the block converted to signed 16-bit PCM.
    /// </summary>
    private readonly short[] _pcmBlock = new short[BlockSamples];

    /// <summary>
    /// Scratch buffer for one 480-frame Opus block.
    /// </summary>
    private readonly short[] _opusBlock = new short[OpusBlockSamples];

    /// <summary>
    /// Scratch buffer for the fixed 200-byte Opus frame sent to the controller.
    /// </summary>
    private readonly byte[] _opusFrame = new byte[DualSenseBtAudioPipeline.OpusBytes];

    /// <summary>
    /// Scratch buffer for the 64-byte Bluetooth haptics payload.
    /// </summary>
    private readonly byte[] _hapticsPcm = new byte[DualSenseBtAudioPipeline.HapticsBytes];

    /// <summary>
    /// Latest haptics payload delivered by libVIIPER's realtime-haptics callback (the
    /// game's own voice-coil data), valid while <see cref="_lastGameHapticsTimestamp"/>
    /// is within <see cref="GameHapticsFreshWindowTicks"/>. Guarded by <see cref="_sync"/>.
    /// </summary>
    private readonly byte[] _gameHapticsPcm = new byte[DualSenseBtAudioPipeline.HapticsBytes];

    /// <summary>
    /// Stopwatch timestamp of the last accepted realtime-haptics update, or 0 when none
    /// was accepted yet. Guarded by <see cref="_sync"/>.
    /// </summary>
    private long _lastGameHapticsTimestamp;

    /// <summary>
    /// One-shot diagnostics for the realtime-haptics combined report validation.
    /// </summary>
    private int _gameHapticsAcceptedLogged;

    private int _gameHapticsRejectedLogged;

    /// <summary>
    /// Scratch buffer for the interleaved quad (S16) block fed to the USB render target.
    /// </summary>
    private readonly short[] _usbQuadBlock = new short[QuadBlockSamples];

    /// <summary>
    /// Scratch buffer used to discard catch-up blocks.
    /// </summary>
    private readonly float[] _discardBlock = new float[BlockSamples];

    /// <summary>
    /// Idle period after which the Bluetooth audio lane is considered dead (the pad's
    /// shallow receive buffer has underrun), so the next audio session re-primes the
    /// stream. Long enough to ignore capture-jitter gaps, short enough to release the
    /// lane promptly when audio stops.
    /// </summary>
    private static readonly long RePrimeIdleTicks =
        (long)(Stopwatch.Frequency * TimeSpan.FromMilliseconds(150).TotalSeconds);

    /// <summary>
    /// Speaker volume (0-255) applied to the controller's speaker/headset hardware.
    /// </summary>
    private volatile byte _speakerVolume = 0x50;

    /// <summary>
    /// Whether the haptic actuators follow the audio.
    /// </summary>
    private volatile bool _hapticsEnabled = true;

    /// <summary>
    /// Haptic vibration strength multiplier (1.0 = full).
    /// </summary>
    private volatile float _hapticStrength = 1f;

    /// <summary>
    /// Whether audio is routed to the headset jack instead of the internal speaker.
    /// </summary>
    private volatile bool _playToHeadset;

    /// <summary>
    /// Last volume/route signature applied to the controller, so the pump only issues
    /// <see cref="IDualSenseAudioOutputs.SetAudioOutput"/> when the configuration changed.
    /// </summary>
    private int _appliedConfigSignature = -1;

    /// <summary>
    /// Whether the Bluetooth audio lane is currently open on the controller. Guarded by
    /// <see cref="_sync"/>; only the pump (and <see cref="Start"/>) mutate it.
    /// </summary>
    private bool _btStreamPrimed;

    /// <summary>
    /// The game's latest output state (rumble, lightbar, player LEDs, trigger effects),
    /// embedded into the Bluetooth audio-lane reports while the lane is open. Guarded
    /// by <see cref="_sync"/>; set from the libVIIPER output-state callback thread.
    /// </summary>
    private SetStateData? _gameState;

    /// <summary>
    /// Phase (in cycles) of the rumble-emulation haptics oscillators, advanced once per
    /// 3 kHz haptics sample so the waveform stays continuous across report boundaries.
    /// </summary>
    private double _rumbleLeftPhase;

    private double _rumbleRightPhase;

    /// <summary>
    /// Smoothed magnitude (0-1) of each rumble-emulation oscillator, approached
    /// exponentially per sample so strength changes ramp instead of clicking.
    /// </summary>
    private float _rumbleLeftMagnitude;

    private float _rumbleRightMagnitude;

    /// <summary>
    /// Whether the pump is currently running.
    /// </summary>
    public bool IsActive => _cts is not null;

    /// <summary>
    /// Speaker volume (0-255) applied to the controller's speaker/headset hardware.
    /// </summary>
    public byte SpeakerVolume
    {
        get => _speakerVolume;
        set => _speakerVolume = value;
    }

    /// <summary>
    /// Whether the haptic actuators follow the audio.
    /// </summary>
    public bool HapticsEnabled
    {
        get => _hapticsEnabled;
        set => _hapticsEnabled = value;
    }

    /// <summary>
    /// Haptic vibration strength multiplier (1.0 = full, 2.0 = 200%).
    /// </summary>
    public float HapticStrength
    {
        get => _hapticStrength;
        set => _hapticStrength = Math.Clamp(value, 0f, 2f);
    }

    /// <summary>
    /// Whether audio is routed to the headset jack instead of the internal speaker.
    /// </summary>
    public bool PlayToHeadset
    {
        get => _playToHeadset;
        set => _playToHeadset = value;
    }

    /// <summary>
    /// Creates the forwarder for the given physical controller lanes. At least one lane
    /// must be non-<c>null</c>; the forwarder takes ownership of <paramref name="usbTarget"/>.
    /// </summary>
    public ViiperDualSenseAudioForwarder(IDualSenseAudioOutputs? outputs, DualSenseUsbAudioTarget? usbTarget)
    {
        _outputs = outputs;
        _usbTarget = usbTarget;
    }

    /// <summary>
    /// Starts the pump and opens the USB render endpoint (when available). The
    /// Bluetooth audio lane is intentionally left closed until the first audio block
    /// is actually fed, so an idle forwarder never occupies the lane â€” which would
    /// make the controller ignore the virtual controller's output-state reports.
    /// Returns <c>false</c> when no transport is available.
    /// </summary>
    public bool Start()
    {
        lock (_sync)
        {
            if (_cts is not null)
            {
                return true;
            }

            bool bluetooth = _outputs is not null && _outputs.ConnectionType == ConnectionType.Bluetooth;
            bool usb = _usbTarget is not null && _usbTarget.Start();
            if (!bluetooth && !usb)
            {
                _log.Info("No forwarding transport available (neither Bluetooth audio lane nor USB render endpoint)");
                return false;
            }

            _btStreamPrimed = false;
            _appliedConfigSignature = -1;
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => PumpLoop(_cts.Token));
            _log.Info($"Audio forwarding started ({(bluetooth ? "bluetooth" : "usb")})");
            return true;
        }
    }

    /// <summary>
    /// Stops the pump and releases the transports. Safe to call repeatedly.
    /// </summary>
    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? loopTask;
        lock (_sync)
        {
            cts = _cts;
            _cts = null;
            loopTask = _loopTask;
            _loopTask = null;
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        loopTask?.Wait(TimeSpan.FromSeconds(2));
        _usbTarget?.Stop();
        _log.Info("Audio forwarding stopped");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Stop();
        lock (_sync)
        {
            _pipeline.Dispose();
        }

        _usbTarget?.Dispose();
    }

    /// <summary>
    /// Feeds one chunk of interleaved 48 kHz stereo float PCM captured from the host.
    /// Blocks are accumulated and pushed to the ring whole; a partially filled tail
    /// block is kept for the next call. Safe to call from the capture callback.
    /// </summary>
    public void FeedPcm(ReadOnlySpan<float> stereo)
    {
        lock (_sync)
        {
            if (_cts is null)
            {
                return;
            }

            int offset = 0;
            int partial = _audioRing.PartialCount;
            if (partial > 0)
            {
                int take = Math.Min(BlockSamples - partial, stereo.Length - offset);
                _audioRing.AppendPartial(stereo.Slice(offset, take));
                offset += take;
                if (_audioRing.PartialCount == BlockSamples)
                {
                    _audioRing.PushPartial();
                }
            }

            while (offset + BlockSamples <= stereo.Length)
            {
                _audioRing.Push(stereo.Slice(offset, BlockSamples));
                offset += BlockSamples;
            }

            if (offset < stereo.Length)
            {
                _audioRing.AppendPartial(stereo.Slice(offset));
            }
        }
    }

    /// <summary>
    /// Stores the game's latest output state so the pump can embed it into the
    /// Bluetooth audio-lane reports (combined <c>0x36</c> and <c>0x32</c> init-prime)
    /// and drive the classic rumble motors through the haptics PCM. Called from the
    /// libVIIPER output-state callback thread; safe to call while the forwarder is
    /// stopped.
    /// </summary>
    public void UpdateGameOutputState(SetStateData state)
    {
        lock (_sync)
        {
            _gameState = state;
        }
    }

    /// <summary>
    /// Stores the game's actual haptics payload from libVIIPER's realtime-haptics
    /// callback, so the pump can embed it into the Bluetooth audio-lane reports instead
    /// of the audio-derived haptics. The haptics are extracted from the 398-byte
    /// combined Bluetooth report carried by the callback (<c>0x36</c> header with the
    /// 64-byte haptics PCM at offset <see cref="BluetoothCombinedHapticsPcmOffset"/>);
    /// reports that do not match the expected shape are rejected so the forwarder keeps
    /// the audio-derived fallback. Called from the libVIIPER callback thread; safe to
    /// call while the forwarder is stopped.
    /// </summary>
    public void UpdateGameHaptics(DSOutputState output)
    {
        byte[] combined = output.BluetoothCombinedOutputReport;
        if (combined is not { Length: 398 } || combined[0] != 0x36
                                            || combined[11] != 0x90 || combined[12] != 0x3F)
        {
            if (Interlocked.Exchange(ref _gameHapticsRejectedLogged, 1) == 0)
            {
                string head = combined is null
                    ? "(null)"
                    : string.Join(" ", combined.AsSpan(0, Math.Min(16, combined.Length)).ToArray().Select(b => b.ToString("X2")));
                _log.Info(
                    $"Realtime-haptics combined report rejected (expected 0x36 report with 0x90 0x3F state block and 64-byte haptics at offset 78); head {head}");
            }

            return;
        }

        lock (_sync)
        {
            combined.AsSpan(BluetoothCombinedHapticsPcmOffset, DualSenseBtAudioPipeline.HapticsBytes)
                .CopyTo(_gameHapticsPcm);
            _lastGameHapticsTimestamp = Stopwatch.GetTimestamp();
        }

        if (Interlocked.Exchange(ref _gameHapticsAcceptedLogged, 1) == 0)
        {
            _log.Info("Game haptics active: the pad now reproduces the game's own haptics payload");
        }
    }

    /// <summary>
    /// Discards all buffered audio and marks the Bluetooth audio lane as closed, so the
    /// next audio block re-primes the stream. Called when the virtual controller's
    /// audio interface resets or changes alternate setting (a stream-generation
    /// barrier), so stale PCM from the previous generation cannot burst into the pad's
    /// shallow receive buffer. Safe to call from the libVIIPER speaker-reset callback
    /// thread.
    /// </summary>
    public void Flush()
    {
        lock (_sync)
        {
            _audioRing.Clear();
            _btStreamPrimed = false;
            _lastGameHapticsTimestamp = 0;
        }
    }

    /// <summary>
    /// Runs the pump loop until canceled: waits for audio to appear in the ring, then
    /// pops one block per tick and fans it out to every active transport. Ticks are
    /// paced against an accumulated deadline so the average cadence matches
    /// <see cref="TickInterval"/> exactly (a per-tick restart would drift). While the
    /// ring is empty the pump idles without sending, so an idle forwarder never
    /// occupies the Bluetooth audio lane (which would corrupt <see cref="DualSenseAudioPlayer"/>
    /// playback and make the controller ignore the virtual controller's output-state
    /// reports); the first block of a session primes the lane, and after the lane has
    /// been idle beyond <see cref="RePrimeIdleTicks"/> the next session re-primes.
    /// When audio resumes, the deadline is re-anchored to now.
    /// </summary>
    private void PumpLoop(CancellationToken ct)
    {
        _log.Debug("Pump loop started");
        TimerResolution.AddRef();
        try
        {
            long tickPeriodTicks = (long)(Stopwatch.Frequency * TickInterval.TotalSeconds);
            long nextTick = Stopwatch.GetTimestamp() + tickPeriodTicks;
            long idleSinceTicks = long.MinValue;
            while (!ct.IsCancellationRequested)
            {
                bool wasIdle = false;
                while (!ct.IsCancellationRequested && _audioRing.Count == 0)
                {
                    if (!wasIdle)
                    {
                        wasIdle = true;
                        idleSinceTicks = Stopwatch.GetTimestamp();
                    }

                    Thread.Sleep(2);
                }

                if (ct.IsCancellationRequested)
                {
                    break;
                }

                if (wasIdle)
                {
                    // After a long enough empty gap the pad's audio buffer has
                    // underrun, so the next session must open the lane again.
                    if (_outputs is not null && _outputs.ConnectionType == ConnectionType.Bluetooth
                                             && Stopwatch.GetTimestamp() - idleSinceTicks >= RePrimeIdleTicks)
                    {
                        _btStreamPrimed = false;
                    }

                    // Re-anchor after an idle gap: the accumulated deadline would be far
                    // in the past, so the loop would burst catch-up frames into the pad's
                    // shallow receive buffer (which drops audio and crackles).
                    nextTick = Stopwatch.GetTimestamp();
                }

                lock (_sync)
                {
                    if (_outputs is not null && _outputs.ConnectionType == ConnectionType.Bluetooth && !_btStreamPrimed)
                    {
                        PrimeBluetoothStream();
                    }
                }

                try
                {
                    ProcessBlock(ct);
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

                nextTick = Math.Max(nextTick + tickPeriodTicks, Stopwatch.GetTimestamp());
                WaitUntil(nextTick);
            }
        }
        finally
        {
            TimerResolution.Release();
        }

        _log.Debug("Pump loop ended");
    }

    /// <summary>
    /// Consumes one block from the ring and feeds every active transport.
    /// </summary>
    private void ProcessBlock(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (!_audioRing.TryPop(_floatBlock))
            {
                _floatBlock.AsSpan().Clear();
            }
            else if (_audioRing.Count > TargetBlocks)
            {
                _audioRing.TryPop(_discardBlock);
            }

            DualSenseBtAudioPipeline.ConvertToPcm16(_floatBlock, _pcmBlock);

            bool bluetooth = _outputs is not null && _outputs.ConnectionType == ConnectionType.Bluetooth;
            if (bluetooth)
            {
                ApplyAudioStateIfChanged();
                DualSenseBtAudioPipeline.ResampleToOpusBlock(_pcmBlock, _opusBlock);
                _pipeline.EncodeOpus(_opusBlock, _opusFrame);
                if (_hapticsEnabled)
                {
                    FillHapticsPcm();
                }

                SendBluetoothReports();
            }

            if (_usbTarget is { IsActive: true })
            {
                ToUsbQuadS16(_pcmBlock, _usbQuadBlock);
                _usbTarget.Feed(_usbQuadBlock);
            }
        }
    }

    /// <summary>
    /// Fills <see cref="_hapticsPcm"/> for the current block. The game's own haptics
    /// payload takes priority while it is fresh and non-silent; otherwise the haptics
    /// are derived from the block's audio content (with the classic rumble folded in).
    /// Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void FillHapticsPcm()
    {
        if (TryGetFreshGameHaptics(_hapticsPcm))
        {
            return;
        }

        DualSenseBtAudioPipeline.ToHapticsPcm(_pcmBlock, _hapticsPcm, _hapticStrength);
    }

    /// <summary>
    /// Copies the freshest realtime-haptics payload into <paramref name="destination"/>
    /// if one was delivered recently. A payload is fresh while its timestamp is within
    /// <see cref="GameHapticsFreshWindowTicks"/> and silent (all zero) payloads are
    /// ignored, so the derived haptics remain the fallback the moment the game stops
    /// driving the haptics channels. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private bool TryGetFreshGameHaptics(Span<byte> destination)
    {
        if (_lastGameHapticsTimestamp == 0
            || Stopwatch.GetTimestamp() - _lastGameHapticsTimestamp > GameHapticsFreshWindowTicks)
        {
            return false;
        }

        _gameHapticsPcm.CopyTo(destination);
        return destination.ContainsAnyExcept((byte)0);
    }

    /// <summary>
    /// Sends the Bluetooth audio frame to the configured route, folding the haptics
    /// packet into the combined <c>0x36</c> report when haptics are enabled.
    /// </summary>
    private void SendBluetoothReports()
    {
        SetStateData state = CreateAudioState();
        BluetoothAudioRoute route = _playToHeadset ? BluetoothAudioRoute.Headset : BluetoothAudioRoute.Speaker;
        if (_hapticsEnabled)
        {
            MixRumbleIntoHaptics();
            _outputs!.SendBluetoothAudioAndHaptics(state, _opusFrame, _hapticsPcm, route);
        }
        else
        {
            _outputs!.SendBluetoothAudio(_opusFrame, route);
        }
    }

    /// <summary>
    /// Applies the volume/route configuration to the controller's speaker/headset
    /// hardware when it changed since the last tick.
    /// </summary>
    private void ApplyAudioStateIfChanged()
    {
        bool headset = _playToHeadset;
        byte volume = _speakerVolume;
        int signature = volume | (headset ? 1 << 8 : 0);
        if (signature == _appliedConfigSignature)
        {
            return;
        }

        _appliedConfigSignature = signature;
        _outputs!.SetAudioOutput(headset ? AudioControl.OutputPathHeadphones : AudioControl.OutputPathSpeaker, volume, headset ? volume : (byte)0x3F);
    }

    /// <summary>
    /// The controller's output path selected by <see cref="PlayToHeadset"/>.
    /// </summary>
    private AudioControl OutputControl => _playToHeadset ? AudioControl.OutputPathHeadphones : AudioControl.OutputPathSpeaker;

    /// <summary>
    /// Headphone volume for the active route: the headset gets the slider value while
    /// the speaker route keeps the hardware default.
    /// </summary>
    private byte HeadphoneVolume => _playToHeadset ? _speakerVolume : (byte)0x3F;

    /// <summary>
    /// Builds the 47-byte output state embedded in both the init-prime and the per-tick
    /// combined <c>0x36</c> report: the game's output state (rumble bytes, adaptive
    /// triggers, lightbar, player LEDs) overridden by the forwarder's audio
    /// configuration. The rumble-mode bits are cleared â€” while the audio lane is open
    /// the pad ignores the motor bytes (and switching modes mid-stream breaks audio),
    /// so rumble is driven through the haptics PCM instead â€” while the trigger enable
    /// bits stay set so the pad applies the embedded trigger blocks (with a bit cleared
    /// it retains the previous effect). Caller must hold <see cref="_sync"/>.
    /// </summary>
    private SetStateData CreateAudioState()
    {
        SetStateData game = _gameState ?? new SetStateData();

        // Recompute the flags from the game's bytes every tick: the `with` expression
        // below writes through the shared raw buffer, so the merge must be idempotent.
        ValidFlags flag0 = (game.ValidFlag0 & ~(ValidFlags.EnableRumbleEmulation | ValidFlags.UseRumbleNotHaptics))
                           | ValidFlags.AllowRightTriggerFfb | ValidFlags.AllowLeftTriggerFfb
                           | ValidFlags.AllowSpeakerVolume | ValidFlags.AllowHeadphoneVolume
                           | ValidFlags.AllowAudioControl;
        ValidFlags flag1 = game.ValidFlag1 | ValidFlags.AllowAudioControl2;
        ValidFlags flag2 = game.ValidFlag2 & ~ValidFlags.EnableImprovedRumbleEmu;

        return game with
        {
            ValidFlag0 = flag0,
            ValidFlag1 = flag1,
            ValidFlag2 = flag2,
            SpeakerVolume = _speakerVolume,
            HeadphoneVolume = HeadphoneVolume,
            AudioControl = OutputControl,
            AudioControl2 = 0x02
        };
    }

    /// <summary>
    /// Mixes the game's classic rumble motors into the haptics PCM: each motor is
    /// synthesized as a sine wave (60 Hz for the left/strong motor, 180 Hz for the
    /// right/weak motor, matching the reference Gamepad haptics-PCM polyfill) and
    /// summed into both haptics channels, clamped to the s8 range. The magnitudes are
    /// exponentially smoothed (about 50 ms) so strength changes ramp instead of
    /// clicking. While the audio lane is open the pad ignores the state block's motor
    /// bytes, so this is the only rumble path during streaming. Caller must hold
    /// <see cref="_sync"/>.
    /// </summary>
    private void MixRumbleIntoHaptics()
    {
        if (_gameState is not { } game)
        {
            return;
        }

        byte leftTarget = game.RumbleLeft;
        byte rightTarget = game.RumbleRight;

        // Haptics sample rate: 32 frames per 512/48000 s tick = 3 kHz.
        const double rate = DualSenseBtAudioPipeline.HapticsFrames
                            / (DualSenseBtAudioPipeline.FramesPerBlock / (double)DualSenseBtAudioPipeline.SampleRate);
        double smoothPerSample = 1.0 - Math.Exp(-1.0 / (0.05 * rate));
        const double twoPi = 2.0 * Math.PI;
        double leftStep = 60.0 / rate;
        double rightStep = 180.0 / rate;

        Span<byte> haptics = _hapticsPcm;
        for (int i = 0; i < DualSenseBtAudioPipeline.HapticsFrames; i++)
        {
            _rumbleLeftMagnitude += (float)((leftTarget / 255.0 - _rumbleLeftMagnitude) * smoothPerSample);
            _rumbleRightMagnitude += (float)((rightTarget / 255.0 - _rumbleRightMagnitude) * smoothPerSample);

            _rumbleLeftPhase += leftStep;
            _rumbleRightPhase += rightStep;

            double wave = _rumbleLeftMagnitude * Math.Sin(twoPi * _rumbleLeftPhase)
                          + _rumbleRightMagnitude * Math.Sin(twoPi * _rumbleRightPhase);
            int sample = (int)Math.Round(wave * 127.0);

            int left = (sbyte)haptics[i * 2] + sample;
            int right = (sbyte)haptics[i * 2 + 1] + sample;
            haptics[i * 2] = (byte)Math.Clamp(left, -128, 127);
            haptics[i * 2 + 1] = (byte)Math.Clamp(right, -128, 127);
        }
    }

    /// <summary>
    /// Expands a stereo PCM block into the quad S16 USB representation: channels 1/2
    /// carry the audio, channels 3/4 carry the audio scaled by the haptic strength â€”
    /// or silence when haptics are disabled.
    /// </summary>
    private void ToUsbQuadS16(ReadOnlySpan<short> stereo, Span<short> quad)
    {
        float strength = _hapticsEnabled ? _hapticStrength : 0f;
        for (int f = 0; f < DualSenseBtAudioPipeline.FramesPerBlock; f++)
        {
            int o = f * 4;
            short left = stereo[f * 2];
            short right = stereo[f * 2 + 1];
            quad[o] = left;
            quad[o + 1] = right;
            quad[o + 2] = (short)Math.Clamp(left * strength, short.MinValue, short.MaxValue);
            quad[o + 3] = (short)Math.Clamp(right * strength, short.MinValue, short.MaxValue);
        }
    }

    /// <summary>
    /// Opens the Bluetooth audio lane on the controller: resets the report sequence and
    /// Opus encoder, applies the audio configuration, sends the <c>0x32</c> init-prime
    /// and warms the stream with <see cref="SendBtPreroll"/>. Blocks ~85 ms. Called from
    /// the pump right before the first report of an audio session, so an idle forwarder
    /// never occupies the lane. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void PrimeBluetoothStream()
    {
        _outputs!.ResetBluetoothAudioStream();
        _pipeline.ResetEncoder();

        // Capture the values actually sent: the configuration may change while the
        // ~85 ms prime blocks the lock, so the applied-signature must reflect what
        // was written, not the live value.
        bool headset = _playToHeadset;
        byte volume = _speakerVolume;
        _outputs.SetAudioOutput(headset ? AudioControl.OutputPathHeadphones : AudioControl.OutputPathSpeaker, volume, headset ? volume : (byte)0x3F);

        _outputs.SendBluetoothAudioPrime(CreateAudioState());
        SendBtPreroll();
        _appliedConfigSignature = volume | (headset ? 1 << 8 : 0);
        _btStreamPrimed = true;
        _log.Debug("Bluetooth audio stream primed");
    }

    /// <summary>
    /// Sends <see cref="BtPrerollPackets"/> of silence audio/haptics reports at the
    /// 10.667 ms cadence so the controller's speaker path and Opus decoder are warmed up
    /// before real audio arrives. Blocks the caller for ~85 ms; call right after priming
    /// the stream. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void SendBtPreroll()
    {
        if (_outputs is null)
        {
            return;
        }

        _log.Debug($"Sending {BtPrerollPackets} silence packets to warm up the Bluetooth audio stream");
        _opusBlock.AsSpan().Clear();
        try
        {
            _pipeline.EncodeOpus(_opusBlock, _opusFrame);
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
                SendBluetoothReports();
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
    /// Bounded ring of fixed 512-frame stereo blocks, shared between the capture
    /// callback (producer) and the pump (consumer). Full pushes drop the oldest block;
    /// the pump handles draining back to the target latency.
    /// </summary>
    private sealed class PcmBlockRing
    {
        private readonly Lock _lock = new Lock();
        private readonly float[][] _blocks;
        private readonly int _capacity;
        private int _write;
        private int _read;
        private int _count;

        /// <summary>
        /// Creates a ring holding <paramref name="capacity"/> blocks.
        /// </summary>
        public PcmBlockRing(int capacity)
        {
            _capacity = capacity;
            _blocks = new float[capacity][];
            for (int i = 0; i < capacity; i++)
            {
                _blocks[i] = new float[BlockSamples];
            }
        }

        /// <summary>
        /// Number of complete blocks currently in the ring.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _count;
                }
            }
        }

        /// <summary>
        /// Number of samples buffered in the partial-block accumulator.
        /// </summary>
        public int PartialCount
        {
            get
            {
                lock (_lock)
                {
                    return _partialCount;
                }
            }
        }

        /// <summary>
        /// Partial-block accumulator; appends the tail of a feed chunk.
        /// </summary>
        private int _partialCount;

        /// <summary>
        /// Partial-block accumulator; holds the tail of a feed chunk.
        /// </summary>
        private readonly float[] _partial = new float[BlockSamples];

        /// <summary>
        /// Appends samples to the partial-block accumulator.
        /// </summary>
        public void AppendPartial(ReadOnlySpan<float> samples)
        {
            lock (_lock)
            {
                samples.CopyTo(_partial.AsSpan(_partialCount, samples.Length));
                _partialCount += samples.Length;
            }
        }

        /// <summary>
        /// Pushes the completed partial block into the ring, dropping the oldest block
        /// when full, and resets the accumulator.
        /// </summary>
        public void PushPartial()
        {
            lock (_lock)
            {
                if (_partialCount == BlockSamples)
                {
                    _partial.CopyTo(_blocks[_write], 0);
                    AdvanceWrite();
                    _partialCount = 0;
                }
            }
        }

        /// <summary>
        /// Pushes one complete block, dropping the oldest block when the ring is full.
        /// </summary>
        public void Push(ReadOnlySpan<float> block)
        {
            lock (_lock)
            {
                block.CopyTo(_blocks[_write].AsSpan());
                AdvanceWrite();
            }
        }

        /// <summary>
        /// Pops the oldest block into <paramref name="destination"/>; returns <c>false</c>
        /// when the ring is empty.
        /// </summary>
        public bool TryPop(float[] destination)
        {
            lock (_lock)
            {
                if (_count == 0)
                {
                    return false;
                }

                _blocks[_read].CopyTo(destination, 0);
                _read = (_read + 1) % _capacity;
                _count--;
                return true;
            }
        }

        /// <summary>
        /// Drops every buffered block and the partial-block accumulator.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _write = 0;
                _read = 0;
                _count = 0;
                _partialCount = 0;
            }
        }

        /// <summary>
        /// Writes one block at the write cursor, dropping the oldest when full. Caller
        /// must hold <see cref="_lock"/>.
        /// </summary>
        private void AdvanceWrite()
        {
            if (_count == _capacity)
            {
                _read = (_read + 1) % _capacity;
            }
            else
            {
                _count++;
            }

            _write = (_write + 1) % _capacity;
        }
    }
}