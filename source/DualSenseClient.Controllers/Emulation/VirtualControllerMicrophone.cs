using Concentus;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Core.Utilities;
using DualSenseClient.Emulation.Devices;
using DualSenseClient.Logging;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Bridges the physical controller's Bluetooth microphone into the microphone endpoint of an
/// active virtual DualSense or DualShock 4 persona. One short-lived instance is owned per
/// virtual-controller entry: it decodes the Opus frames the controller sends, buffers the
/// decoded 48 kHz mono PCM, and feeds exactly one one-millisecond target frame per packet the
/// persona serves from its ISO IN endpoint.
/// </summary>
/// <remarks>
/// <para>
/// The stream runs only while passthrough is on, the configured route is not
/// <see cref="MicrophoneSource.Disabled"/>, and the host has opened the persona's microphone
/// interface. Any of those changing starts or stops the stream immediately. Starting and
/// stopping reset the decoder, the target converter, and the PCM queue, and move the physical
/// controller's microphone gate with them. The bridge holds a 1 ms timer-resolution lease for
/// as long as the stream runs.
/// </para>
/// <para>
/// The HID read thread decodes and enqueues, while the persona's ISO scheduler thread drains one
/// frame per served packet. The decoder, the converter, and the queue are each guarded by their
/// own lock. Only the control-only transition lock serializes the endpoint and physical-controller
/// side effects of a start or stop; the HID reader and the ISO scheduler never take it, and the
/// queue lock is never held while calling the endpoint, the physical controller, or the logger.
/// </para>
/// </remarks>
public sealed class VirtualControllerMicrophone : IDisposable
{
    /// <summary>Sample rate of the decoded source stream, in hertz.</summary>
    private const int SourceSampleRateHz = 48000;

    /// <summary>Channels of the decoded source stream: the controller microphone is mono.</summary>
    private const int SourceChannels = 1;

    /// <summary>Samples in one one-millisecond target frame: 48 kHz mono.</summary>
    private const int SourceSamplesPerFrame = 48;

    /// <summary>Samples in one ten-millisecond Opus packet: 480 at 48 kHz.</summary>
    private const int OpusFrameSamples = 480;

    /// <summary>Decoded samples buffered before the stream starts: 48 ms, or 2304 samples.</summary>
    private const int PrebufferSamples = 48 * SourceSamplesPerFrame;

    /// <summary>Maximum decoded samples the queue holds: 200 ms, or 9600 samples.</summary>
    private const int QueueCapacitySamples = 200 * SourceSamplesPerFrame;

    /// <summary>Largest microphone frame any persona accepts: the DualSense's 192-byte frame.</summary>
    private const int MaxTargetFrameBytes = 192;

    /// <summary>Logger instance.</summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("VirtualControllerMicrophone");

    /// <summary>The physical controller whose microphone frames are captured.</summary>
    private readonly DualSenseDevice _physical;

    /// <summary>The virtual persona's microphone endpoint.</summary>
    private readonly IVirtualMicrophoneEndpoint _endpoint;

    /// <summary>Creates the 1 ms timer-resolution lease held while streaming.</summary>
    private readonly Func<IDisposable> _timerLeaseFactory;

    /// <summary>Decodes the controller's mono 48 kHz ten-millisecond Opus packets.</summary>
    private readonly IOpusDecoder _decoder;

    /// <summary>Converts 48 kHz mono slices to the persona's target frame layout.</summary>
    private readonly MicrophoneTargetConverter _converter;

    /// <summary>Decoded 48 kHz mono samples waiting to be served, oldest first.</summary>
    private readonly Queue<short> _queue = new Queue<short>(QueueCapacitySamples);

    /// <summary>Receives one decoded Opus packet.</summary>
    private readonly short[] _decodeBuffer = new short[OpusFrameSamples];

    /// <summary>Receives one converted target frame.</summary>
    private readonly byte[] _frameBuffer = new byte[MaxTargetFrameBytes];

    /// <summary>Guards the stream state, the queue, the primed flag, and the lease.</summary>
    private readonly Lock _sync = new Lock();

    /// <summary>Guards the decoder, which the HID reader and stream control share.</summary>
    private readonly Lock _decoderLock = new Lock();

    /// <summary>Guards the stateful target converter, which the ISO thread and stream control share.</summary>
    private readonly Lock _converterLock = new Lock();

    /// <summary>
    /// Serializes stream start and stop so their endpoint and gate side effects cannot interleave.
    /// The HID reader and the ISO scheduler never take this lock.
    /// </summary>
    private readonly Lock _transitionLock = new Lock();

    /// <summary>The configured microphone route.</summary>
    private MicrophoneSource _source;

    /// <summary>The configured microphone level as a percentage.</summary>
    private int _level;

    /// <summary>Whether microphone passthrough is enabled in the settings.</summary>
    private bool _passthroughEnabled;

    /// <summary>Whether the route and level have been written to the physical controller.</summary>
    private bool _settingsApplied;

    /// <summary>Whether the controller stream is currently running.</summary>
    private bool _streaming;

    /// <summary>Whether enough audio has accumulated to start serving frames.</summary>
    private bool _primed;

    /// <summary>Whether the bridge has been disposed.</summary>
    private bool _disposed;

    /// <summary>Whether the current overflow burst has already been logged.</summary>
    private bool _overflowWarningLogged;

    /// <summary>Whether the current decode-failure burst has already been logged.</summary>
    private bool _decodeFailureLogged;

    /// <summary>Increments on every stream start or stop so stale decodes cannot leak into a new stream.</summary>
    private int _generation;

    /// <summary>The timer-resolution lease held while streaming, or <c>null</c> when stopped.</summary>
    private IDisposable? _lease;

    /// <summary>
    /// Creates the bridge, subscribes to the physical microphone and the persona endpoint, and
    /// applies the initial route, level, and passthrough state. The stream starts immediately if
    /// the persona's microphone is already open and the settings allow it.
    /// </summary>
    /// <param name="physical">The Bluetooth controller whose microphone frames are captured.</param>
    /// <param name="endpoint">The virtual persona's microphone endpoint.</param>
    /// <param name="settings">The configured microphone source and level.</param>
    /// <param name="passthroughEnabled">Whether the microphone passthrough switch is on.</param>
    /// <param name="timerLeaseFactory">
    /// Creates the 1 ms timer-resolution lease; tests supply a fake. When omitted, the bridge
    /// uses <see cref="TimerResolution"/> directly.
    /// </param>
    public VirtualControllerMicrophone(
        DualSenseDevice physical,
        IVirtualMicrophoneEndpoint endpoint,
        MicrophoneSettings settings,
        bool passthroughEnabled,
        Func<IDisposable>? timerLeaseFactory = null)
    {
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(settings);

        _physical = physical;
        _endpoint = endpoint;
        _timerLeaseFactory = timerLeaseFactory ?? CreateTimerLease;
        _decoder = OpusCodecFactory.CreateDecoder(SourceSampleRateHz, SourceChannels);
        _converter = new MicrophoneTargetConverter(endpoint.MicrophoneFormat);

        _physical.MicrophoneFrameReceived += OnMicrophoneFrameReceived;
        _endpoint.MicrophoneActiveChanged += OnMicrophoneActiveChanged;
        _endpoint.MicrophonePacketServed += OnMicrophonePacketServed;

        ApplySettings(settings, passthroughEnabled);
        _log.Debug($"Microphone bridge created for {physical.Info.ProductName} ({endpoint.MicrophoneFormat.MicrophoneFrameBytes}-byte target frames)");
    }

    /// <summary>
    /// Gets the number of decoded 48 kHz mono samples currently queued. Exposed for tests.
    /// </summary>
    internal int QueuedSamples
    {
        get
        {
            lock (_sync)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>
    /// Applies a new microphone source, level, and passthrough state, writes the mapped route and
    /// level to the physical controller, and starts or stops the stream when the new settings
    /// change whether it may run.
    /// </summary>
    /// <param name="settings">The configured microphone source and level.</param>
    /// <param name="passthroughEnabled">Whether the microphone passthrough switch is on.</param>
    public void ApplySettings(MicrophoneSettings settings, bool passthroughEnabled)
    {
        ArgumentNullException.ThrowIfNull(settings);

        MicrophoneSource source = settings.Source;
        int level = settings.Level;

        bool routeChanged;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            routeChanged = !_settingsApplied || _source != source || _level != level;
            _source = source;
            _level = level;
            _passthroughEnabled = passthroughEnabled;
            _settingsApplied = true;
        }

        if (routeChanged)
        {
            _physical.ApplyMicrophoneSettings(source, level);
        }

        UpdateStream();
        _log.Debug($"Microphone settings applied: source {source}, level {level}%, passthrough {passthroughEnabled}");
    }

    /// <summary>
    /// Decodes one Opus microphone frame and appends its PCM to the queue while the stream runs.
    /// Ignored once the bridge is stopped or disposed. Exposed for tests; the physical event
    /// handler delegates here.
    /// </summary>
    /// <param name="opusFrame">The copied Opus payload from a Bluetooth microphone packet.</param>
    internal void HandleMicrophoneFrame(byte[] opusFrame)
    {
        if (opusFrame is null || opusFrame.Length == 0)
        {
            return;
        }

        int generation;
        lock (_sync)
        {
            if (!_streaming)
            {
                return;
            }

            generation = _generation;
        }

        int samples;
        Exception? failure = null;
        bool queued = false;
        bool overflowed = false;
        lock (_decoderLock)
        {
            try
            {
                samples = _decoder.Decode(opusFrame, _decodeBuffer, _decodeBuffer.Length);
            }
            catch (Exception ex)
            {
                samples = 0;
                failure = ex;
            }

            if (failure is null && samples > 0)
            {
                lock (_sync)
                {
                    if (_streaming && generation == _generation)
                    {
                        _decodeFailureLogged = false;
                        overflowed = EnqueueLocked(_decodeBuffer.AsSpan(0, samples));
                        queued = true;
                    }
                }
            }
        }

        if (failure is not null || samples <= 0)
        {
            ReportDecodeFailure(failure);
            return;
        }

        if (queued && overflowed)
        {
            _log.Warning($"Dropping the oldest microphone audio: the {QueueCapacitySamples / SourceSamplesPerFrame} ms queue overflowed");
        }
    }

    /// <summary>
    /// Stops the stream, releases the timer lease, disposes the Opus decoder, unsubscribes from
    /// the physical controller and the persona endpoint, and clears the physical microphone
    /// overrides. Safe to call more than once and never throws.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _physical.MicrophoneFrameReceived -= OnMicrophoneFrameReceived;
        _endpoint.MicrophoneActiveChanged -= OnMicrophoneActiveChanged;
        _endpoint.MicrophonePacketServed -= OnMicrophonePacketServed;

        try
        {
            StopStream();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to release the microphone bridge cleanly", ex);
        }

        lock (_decoderLock)
        {
            _decoder?.Dispose();
        }

        _log.Debug("Microphone bridge disposed");
    }

    /// <summary>
    /// Creates the 1 ms timer-resolution lease the stream holds until it stops.
    /// </summary>
    /// <returns>A lease that raises the timer resolution until disposed.</returns>
    private static IDisposable CreateTimerLease() => new TimerResolutionLease();

    /// <summary>
    /// Starts or stops the stream to match the current settings and endpoint state.
    /// </summary>
    private void UpdateStream()
    {
        bool shouldStream;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            shouldStream = CanStreamLocked();
            if (shouldStream == _streaming)
            {
                return;
            }
        }

        if (shouldStream)
        {
            StartStream();
        }
        else
        {
            StopStream();
        }
    }

    /// <summary>
    /// Returns whether the passthrough switch, the route, and the host endpoint currently allow
    /// streaming. Caller must hold <see cref="_sync"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the stream should run.</returns>
    private bool CanStreamLocked() =>
        !_disposed
        && _passthroughEnabled
        && _source != MicrophoneSource.Disabled
        && _endpoint.MicrophoneActive;

    /// <summary>
    /// Starts the stream: acquires the timer lease, resets the codec state, clears the persona's
    /// capture ring and the local queue, opens the physical microphone gate, and re-applies the
    /// configured route and level as an embedded-state report.
    /// </summary>
    private void StartStream()
    {
        MicrophoneSource source = MicrophoneSource.Auto;
        int level = 0;
        lock (_transitionLock)
        {
            bool canStart;
            lock (_sync)
            {
                canStart = !_disposed && !_streaming && CanStreamLocked();
            }

            if (!canStart)
            {
                return;
            }

            IDisposable lease = _timerLeaseFactory();
            bool started;
            lock (_sync)
            {
                started = !_disposed && !_streaming && CanStreamLocked();
                if (started)
                {
                    _streaming = true;
                    _primed = false;
                    _queue.Clear();
                    _overflowWarningLogged = false;
                    _decodeFailureLogged = false;
                    _generation++;
                    _lease = lease;
                    source = _source;
                    level = _level;
                }
            }

            if (!started)
            {
                lease.Dispose();
                return;
            }

            try
            {
                ResetCodecState();
                _endpoint.ClearMicrophonePcm();
                _physical.BluetoothMicrophoneStreaming = true;
                // Re-assert the route and level through the embedded-state report: a mic-only
                // session has no other state carrier, and the controller may have lost the
                // settings while idle or across a reconnect.
                _physical.ApplyMicrophoneSettings(source, level);
            }
            catch (Exception ex)
            {
                RollbackStartLocked();
                _log.Error("Failed to start the microphone stream; the bridge was rolled back", ex);
                throw;
            }
        }

        _log.Debug($"Microphone stream started: source {source}, level {level}%");
    }

    /// <summary>
    /// Reverts the state a failed <see cref="StartStream"/> committed before its side effects
    /// ran: the stream is stopped, the primed flag and queue are cleared, the generation is
    /// advanced, the physical microphone gate is closed, and the timer lease is disposed. Called
    /// with <see cref="_transitionLock"/> held so a concurrent start or stop cannot interleave.
    /// </summary>
    private void RollbackStartLocked()
    {
        IDisposable? lease;
        lock (_sync)
        {
            _streaming = false;
            _primed = false;
            _queue.Clear();
            _overflowWarningLogged = false;
            _decodeFailureLogged = false;
            _settingsApplied = false;
            _generation++;
            lease = _lease;
            _lease = null;
        }

        try
        {
            _physical.BluetoothMicrophoneStreaming = false;
        }
        finally
        {
            lease?.Dispose();
        }
    }

    /// <summary>
    /// Stops the stream: resets the codec state, clears the persona's capture ring and the local
    /// queue, closes the physical microphone gate, and releases the timer lease.
    /// </summary>
    private void StopStream()
    {
        IDisposable? lease = null;
        try
        {
            lock (_transitionLock)
            {
                lock (_sync)
                {
                    if (!_streaming)
                    {
                        return;
                    }

                    _streaming = false;
                    _primed = false;
                    _queue.Clear();
                    _overflowWarningLogged = false;
                    _generation++;
                    lease = _lease;
                    _lease = null;
                }

                try
                {
                    ResetCodecState();
                    _endpoint.ClearMicrophonePcm();
                }
                finally
                {
                    // The gate must close even when the persona flush or the codec reset fails:
                    // leaving it open would keep the controller streaming into nothing.
                    _physical.BluetoothMicrophoneStreaming = false;
                }
            }
        }
        finally
        {
            lease?.Dispose();
        }

        _log.Debug("Microphone stream stopped");
    }

    /// <summary>
    /// Resets the decoder and the target converter so a new stream generation cannot inherit the
    /// previous one's codec or filter state.
    /// </summary>
    private void ResetCodecState()
    {
        lock (_decoderLock)
        {
            _decoder.ResetState();
        }

        lock (_converterLock)
        {
            _converter.Reset();
        }
    }

    /// <summary>
    /// Serves exactly one one-millisecond target frame when the queue holds a primed 48 ms
    /// prebuffer. An underrun pushes nothing (the persona zero-fills the packet) and rebuilds the
    /// prebuffer. Runs on the persona's ISO scheduler thread, so it only copies, converts, and
    /// pushes one frame.
    /// </summary>
    private void OnMicrophonePacketServed()
    {
        Span<short> slice = stackalloc short[SourceSamplesPerFrame];
        int generation;
        lock (_sync)
        {
            if (!_streaming)
            {
                return;
            }

            if (!_primed)
            {
                if (_queue.Count < PrebufferSamples)
                {
                    return;
                }

                _primed = true;
            }

            if (_queue.Count < SourceSamplesPerFrame)
            {
                _primed = false;
                _overflowWarningLogged = false;
                return;
            }

            for (int i = 0; i < slice.Length; i++)
            {
                slice[i] = _queue.Dequeue();
            }

            if (_queue.Count == 0)
            {
                _overflowWarningLogged = false;
            }

            generation = _generation;
        }

        int bytes;
        lock (_converterLock)
        {
            bytes = _converter.Convert(slice, _frameBuffer);
        }

        lock (_sync)
        {
            if (!_streaming || generation != _generation)
            {
                return;
            }
        }

        _endpoint.SetMicrophonePcm(_frameBuffer.AsSpan(0, bytes));
    }

    /// <summary>
    /// Handles the physical controller's microphone-frame event on the HID read thread.
    /// </summary>
    /// <param name="opusFrame">The copied Opus payload from a Bluetooth microphone packet.</param>
    private void OnMicrophoneFrameReceived(byte[] opusFrame) => HandleMicrophoneFrame(opusFrame);

    /// <summary>
    /// Re-evaluates the stream when the host opens or closes the persona's microphone endpoint.
    /// </summary>
    /// <param name="active">Whether the endpoint is now streaming.</param>
    private void OnMicrophoneActiveChanged(bool active)
    {
        _log.Debug($"Host microphone endpoint {(active ? "opened" : "closed")}");
        UpdateStream();
    }

    /// <summary>
    /// Logs a decode failure at most once per burst of invalid packets, so a corrupt stream does
    /// not spam the log at packet rate.
    /// </summary>
    /// <param name="failure">The decoder exception, or <c>null</c> when the decoder returned no samples.</param>
    private void ReportDecodeFailure(Exception? failure)
    {
        bool logFailure;
        lock (_sync)
        {
            logFailure = !_decodeFailureLogged;
            _decodeFailureLogged = true;
        }

        if (logFailure)
        {
            _log.Warning("Dropping a Bluetooth microphone frame that failed to decode", failure);
        }
    }

    /// <summary>
    /// Appends decoded samples to the queue, dropping the oldest audio when the 200 ms ring is
    /// full so the newest audio survives. Caller must hold <see cref="_sync"/>.
    /// </summary>
    /// <param name="samples">The decoded 48 kHz mono samples.</param>
    /// <returns><see langword="true"/> when this call should log the start of an overflow burst.</returns>
    private bool EnqueueLocked(ReadOnlySpan<short> samples)
    {
        bool overflowed = false;
        int excess = _queue.Count + samples.Length - QueueCapacitySamples;
        if (excess > 0)
        {
            int drop = Math.Min(excess, _queue.Count);
            for (int i = 0; i < drop; i++)
            {
                _queue.Dequeue();
            }

            if (!_overflowWarningLogged)
            {
                _overflowWarningLogged = true;
                overflowed = true;
            }
        }

        for (int i = 0; i < samples.Length; i++)
        {
            _queue.Enqueue(samples[i]);
        }

        return overflowed;
    }

    /// <summary>
    /// A reference-counted 1 ms timer-resolution lease held for the lifetime of the stream.
    /// </summary>
    private sealed class TimerResolutionLease : IDisposable
    {
        /// <summary>Whether the lease has already been released.</summary>
        private int _released;

        /// <summary>
        /// Raises the process-wide timer resolution.
        /// </summary>
        public TimerResolutionLease()
        {
            TimerResolution.AddRef();
        }

        /// <summary>
        /// Releases the timer-resolution request exactly once.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                TimerResolution.Release();
            }
        }
    }
}