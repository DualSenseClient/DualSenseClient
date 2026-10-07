using System.Diagnostics;
using System.Runtime.InteropServices;
using DualSenseClient.Logging;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Captures the audio a single application (and its child processes) renders, using the
/// WASAPI application-loopback API, and forwards it to
/// <see cref="ViiperDualSenseAudioForwarder"/>.
/// </summary>
/// <remarks>
/// <para>
/// The per-application alternative to <see cref="SystemOutputAudioCapture"/>: that one
/// taps the whole default output device, while this one taps only the selected
/// application's process tree, so other apps cannot drive the controller. Activation
/// goes through <c>ActivateAudioInterfaceAsync</c> with
/// <c>AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK</c> in include-tree mode, which
/// requires Windows 10 build 20348 (Windows 11) or later.
/// </para>
/// <para>
/// The capture requests 48 kHz interleaved stereo float and lets the audio engine convert
/// (<c>AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM</c>), because process-loopback clients reject
/// <c>GetMixFormat</c> and cannot be asked for a negotiated format. Captured blocks
/// therefore already match the forwarder's stream format and need no resampling. A
/// process that renders no audio simply yields silence.
/// </para>
/// </remarks>
public sealed class ApplicationLoopbackAudioCapture : IForwardedAudioCapture, IDisposable
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("ApplicationLoopbackAudioCapture");

    /// <summary>
    /// Interleaved channels in the captured stream.
    /// </summary>
    private const int Channels = DualSenseBtAudioPipeline.Channels;

    /// <summary>
    /// Buffer duration requested from the engine, in 100 ns units (20 ms).
    /// </summary>
    private const long BufferDurationHns = 200_000;

    /// <summary>
    /// How long to wait for activation before giving up.
    /// </summary>
    private const int ActivationTimeoutMs = 5_000;

    /// <summary>
    /// How long the pump waits for a sample event before re-checking the running flag.
    /// </summary>
    private const int PumpWaitMs = 100;

    /// <summary>
    /// The process whose audio is captured.
    /// </summary>
    private readonly uint _processId;

    /// <summary>
    /// The executable path the process id was resolved from, for diagnostics.
    /// </summary>
    private readonly string _applicationPath;

    /// <summary>
    /// The forwarder receiving the captured audio.
    /// </summary>
    private readonly ViiperDualSenseAudioForwarder _forwarder;

    /// <summary>
    /// Serializes start/stop and guards the thread handle.
    /// </summary>
    private readonly Lock _sync = new Lock();

    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private AutoResetEvent? _sampleReady;
    private Thread? _pumpThread;

    /// <summary>
    /// Whether the pump loop is running. Written by the capture thread and <see cref="Stop"/>.
    /// </summary>
    private volatile bool _running;

    /// <summary>
    /// Whether the last start successfully activated the interface.
    /// </summary>
    private volatile bool _initialized;

    private bool _firstChunkLogged;
    private long _statsTimestamp;
    private long _totalFrames;
    private long _nonZeroSamples;

    /// <summary>
    /// Creates a capture of the given process feeding the given forwarder. The capture
    /// takes no ownership of the forwarder.
    /// </summary>
    /// <param name="processId">The process whose audio should be captured.</param>
    /// <param name="applicationPath">The executable the process was resolved from (diagnostics).</param>
    /// <param name="forwarder">The forwarder receiving the captured audio.</param>
    public ApplicationLoopbackAudioCapture(uint processId, string applicationPath, ViiperDualSenseAudioForwarder forwarder)
    {
        _processId = processId;
        _applicationPath = applicationPath;
        _forwarder = forwarder;
    }

    /// <inheritdoc/>
    public bool IsActive
    {
        get
        {
            return _running;
        }
    }

    /// <inheritdoc/>
    public bool Start()
    {
        ManualResetEventSlim ready = new ManualResetEventSlim(false);
        lock (_sync)
        {
            if (_running)
            {
                return true;
            }

            if (!AudioApplicationService.IsProcessLoopbackSupported)
            {
                _log.Warning("Application audio capture requires Windows 10 build 20348 (Windows 11) or later");
                return false;
            }

            if (_processId == 0)
            {
                _log.Warning($"No running process resolves to '{_applicationPath}'; nothing to capture");
                return false;
            }

            Thread thread = new Thread(() => RunCaptureThread(ready))
            {
                IsBackground = true,
                Name = "ApplicationLoopbackAudioCapture"
            };
            _pumpThread = thread;
            thread.Start();
        }

        if (!ready.Wait(ActivationTimeoutMs))
        {
            _log.Warning($"Timed out activating application audio capture for '{_applicationPath}'");
            Stop();
            return false;
        }

        return _initialized;
    }

    /// <inheritdoc/>
    public void Stop()
    {
        Thread? thread;
        lock (_sync)
        {
            thread = _pumpThread;
            _pumpThread = null;
            _initialized = false;
        }

        _running = false;
        _sampleReady?.Set();

        IAudioClient? audioClient = _audioClient;
        if (audioClient is not null)
        {
            try
            {
                audioClient.Stop();
            }
            catch (Exception ex)
            {
                _log.Debug($"Stopping the application audio capture failed: {ex.Message}");
            }
        }

        if (thread is not null && !ReferenceEquals(thread, Thread.CurrentThread))
        {
            thread.Join(500);
        }

        ReleaseComObjects();
        _log.Info($"Application audio capture stopped for '{_applicationPath}'");
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();

    /// <summary>
    /// Runs on the capture thread: activates and initializes the stream, signals
    /// <paramref name="ready"/> with the result, then pumps packets until stopped.
    /// </summary>
    private void RunCaptureThread(ManualResetEventSlim ready)
    {
        bool initialized;
        try
        {
            initialized = Initialize();
        }
        catch (Exception ex)
        {
            _log.Warning($"Application audio capture failed to start for '{_applicationPath}': {ex.Message}");
            ReleaseComObjects();
            initialized = false;
        }

        _initialized = initialized;
        if (!initialized)
        {
            ready.Set();
            return;
        }

        _running = true;
        ready.Set();
        try
        {
            PumpLoop();
        }
        catch (Exception ex)
        {
            _log.LogExceptionDetails(ex);
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>
    /// Activates the process-loopback interface and prepares the stream. Returns
    /// <c>false</c> when activation or initialization fails.
    /// </summary>
    private bool Initialize()
    {
        ActivationHandler handler = new ActivationHandler();
        int hr = WasapiAudioInterop.ActivateProcessLoopback(_processId, handler, out IActivateAudioInterfaceAsyncOperation _);
        if (hr < 0)
        {
            _log.Warning($"ActivateAudioInterfaceAsync failed (HRESULT 0x{hr:X8})");
            return false;
        }

        if (!handler.Completed.Wait(ActivationTimeoutMs) || handler.AudioClient is not { } audioClient)
        {
            _log.Warning(
                $"The application audio interface could not be activated (HRESULT 0x{handler.ActivateResult:X8})");
            return false;
        }

        WaveFormatEx format = WasapiAudioInterop.CreateStereoFloatFormat(DualSenseBtAudioPipeline.SampleRate);
        hr = audioClient.Initialize(WasapiAudioInterop.ShareModeShared, WasapiAudioInterop.StreamFlags,
            BufferDurationHns, 0, ref format, IntPtr.Zero);
        if (hr < 0)
        {
            _log.Warning($"The application audio stream could not be initialized (HRESULT 0x{hr:X8})");
            return false;
        }

        hr = audioClient.GetBufferSize(out uint bufferFrames);
        if (hr < 0)
        {
            _log.Warning($"The application audio buffer size could not be read (HRESULT 0x{hr:X8})");
            return false;
        }

        Guid captureClientIid = WasapiAudioInterop.AudioCaptureClientIid;
        hr = audioClient.GetService(ref captureClientIid, out object service);
        if (hr < 0 || service is not IAudioCaptureClient captureClient)
        {
            _log.Warning($"The application audio capture client is unavailable (HRESULT 0x{hr:X8})");
            return false;
        }

        AutoResetEvent sampleReady = new AutoResetEvent(false);
        hr = audioClient.SetEventHandle(sampleReady.SafeWaitHandle.DangerousGetHandle());
        if (hr < 0)
        {
            _log.Warning($"The application audio event handle could not be set (HRESULT 0x{hr:X8})");
            sampleReady.Dispose();
            return false;
        }

        hr = audioClient.Start();
        if (hr < 0)
        {
            _log.Warning($"The application audio stream could not be started (HRESULT 0x{hr:X8})");
            sampleReady.Dispose();
            return false;
        }

        _audioClient = audioClient;
        _captureClient = captureClient;
        _sampleReady = sampleReady;
        _log.Info(
            $"Application audio capture started for '{_applicationPath}' (PID {_processId}, buffer {bufferFrames} frames)");
        return true;
    }

    /// <summary>
    /// Waits for sample events and drains the capture client until stopped. Runs on the
    /// capture thread.
    /// </summary>
    private void PumpLoop()
    {
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        }
        catch (Exception)
        {
            // Priority elevation is best-effort.
        }

        _log.Debug($"Application audio pump started (PID {_processId})");
        float[] scratch = new float[4096 * Channels];
        while (_running)
        {
            AutoResetEvent? sampleReady = _sampleReady;
            if (sampleReady is null || !sampleReady.WaitOne(PumpWaitMs))
            {
                continue;
            }

            Drain(scratch);
        }

        _log.Debug("Application audio pump ended");
    }

    /// <summary>
    /// Drains every packet currently available and feeds it to the forwarder.
    /// </summary>
    private void Drain(float[] scratch)
    {
        IAudioCaptureClient? captureClient = _captureClient;
        if (captureClient is null)
        {
            return;
        }

        while (_running)
        {
            int hr = captureClient.GetNextPacketSize(out uint frames);
            if (hr < 0 || frames == 0)
            {
                return;
            }

            hr = captureClient.GetBuffer(out IntPtr data, out uint framesRead, out int flags, out ulong _, out ulong _);
            if (hr < 0)
            {
                return;
            }

            if (framesRead > 0)
            {
                int samples = (int)framesRead * Channels;
                if (scratch.Length < samples)
                {
                    scratch = new float[samples];
                }

                if ((flags & WasapiAudioInterop.BufferFlagSilent) != 0 || data == IntPtr.Zero)
                {
                    Array.Clear(scratch, 0, samples);
                }
                else
                {
                    Marshal.Copy(data, scratch, 0, samples);
                }

                ReportAndFeed(scratch, samples);
            }

            captureClient.ReleaseBuffer(framesRead);
        }
    }

    /// <summary>
    /// Records capture-content diagnostics and hands one block to the forwarder.
    /// </summary>
    private void ReportAndFeed(float[] samples, int count)
    {
        int nonZero = 0;
        for (int i = 0; i < count; i++)
        {
            if (samples[i] != 0f)
            {
                nonZero++;
            }
        }

        int frames = count / Channels;
        _totalFrames += frames;
        _nonZeroSamples += nonZero;

        long now = Stopwatch.GetTimestamp();
        if (!_firstChunkLogged)
        {
            _firstChunkLogged = true;
            _statsTimestamp = now;
            _log.Info(
                $"Application audio capture first chunk ('{_applicationPath}'): {frames} frames, {nonZero}/{count} non-zero samples");
        }
        else if (now - _statsTimestamp >= Stopwatch.Frequency * 2)
        {
            _statsTimestamp = now;
            double pct = _totalFrames == 0 ? 0 : _nonZeroSamples * 100.0 / (_totalFrames * Channels);
            _log.Debug(
                $"Application audio capture stats: {_totalFrames / 48.0 / 1000.0:F0} s fed, {pct:F1}% non-zero samples");
        }

        _forwarder.FeedPcm(samples.AsSpan(0, count));
    }

    /// <summary>
    /// Releases the COM interfaces and the sample event.
    /// </summary>
    private void ReleaseComObjects()
    {
        IAudioCaptureClient? captureClient = Interlocked.Exchange(ref _captureClient, null);
        IAudioClient? audioClient = Interlocked.Exchange(ref _audioClient, null);
        AutoResetEvent? sampleReady = Interlocked.Exchange(ref _sampleReady, null);

        if (OperatingSystem.IsWindows())
        {
            ReleaseComObject(audioClient);
            ReleaseComObject(captureClient);
        }

        sampleReady?.Dispose();
    }

    /// <summary>
    /// Releases a single COM interface reference, ignoring detached-already failures.
    /// </summary>
    private static void ReleaseComObject(object? instance)
    {
        if (!OperatingSystem.IsWindows() || instance is null || !Marshal.IsComObject(instance))
        {
            return;
        }

        try
        {
            Marshal.ReleaseComObject(instance);
        }
        catch (Exception)
        {
            // Nothing useful to do while tearing down the capture.
        }
    }

    /// <summary>
    /// The <c>IActivateAudioInterfaceCompletionHandler</c> implementation that captures
    /// the activated audio client and signals the waiting start.
    /// </summary>
    private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler
    {
        /// <summary>
        /// Signaled once the activation callback ran.
        /// </summary>
        public readonly ManualResetEventSlim Completed = new ManualResetEventSlim(false);

        /// <summary>
        /// The activated audio client, or <c>null</c> when activation failed.
        /// </summary>
        public IAudioClient? AudioClient;

        /// <summary>
        /// The HRESULT reported by the activation callback.
        /// </summary>
        public int ActivateResult;

        /// <inheritdoc/>
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            try
            {
                int hr = activateOperation.GetActivateResult(out int activateResult, out object activated);
                ActivateResult = activateResult;
                if (hr >= 0 && activateResult >= 0 && activated is IAudioClient client)
                {
                    AudioClient = client;
                }
            }
            catch (Exception)
            {
                // The waiting start observes the missing client and reports the failure.
            }
            finally
            {
                Completed.Set();
            }
        }
    }
}