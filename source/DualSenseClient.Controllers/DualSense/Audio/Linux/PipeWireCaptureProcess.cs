using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using DualSenseClient.Logging;

namespace DualSenseClient.Controllers.DualSense.Audio.Linux;

/// <summary>
/// Owns one <c>pw-record</c> child process and pumps its raw 48 kHz interleaved stereo float
/// output into a <see cref="DualSenseAudioForwarder"/>. The capture stream is created with
/// <c>--target 0</c> (unlinked) and stays suspended until a caller links a sink or
/// application node to <see cref="NodeId"/>.
/// </summary>
/// <remarks>
/// <para>
/// The PipeWire graph performs format conversion, so the child is asked for exactly the
/// forwarder's stream format and no resampling is needed here. The process is short-lived
/// and owned: <see cref="Stop"/> kills it, which closes stdout and unblocks the reader.
/// </para>
/// <para>
/// An unlinked capture node produces no data, so an application capture with no matching
/// stream yet does not hold the Bluetooth audio lane open.
/// </para>
/// </remarks>
internal sealed class PipeWireCaptureProcess : IDisposable
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("PipeWireCaptureProcess");

    /// <summary>
    /// Frames requested per read. 2048 frames is about 43 ms at 48 kHz and fits the capture
    /// pipe comfortably without adding noticeable latency.
    /// </summary>
    private const int FramesPerRead = 2048;

    /// <summary>
    /// Interleaved channels requested from PipeWire.
    /// </summary>
    private const int ChannelCount = DualSenseBtAudioPipeline.Channels;

    /// <summary>
    /// Bytes in one float sample.
    /// </summary>
    private const int BytesPerSample = sizeof(float);

    /// <summary>
    /// Bytes in one interleaved stereo frame.
    /// </summary>
    private const int BytesPerFrame = ChannelCount * BytesPerSample;

    /// <summary>
    /// How long to wait for the capture node to appear in the graph after starting the child.
    /// </summary>
    private const int StartTimeoutMs = 2000;

    /// <summary>
    /// Delay between capture-node graph polls during startup.
    /// </summary>
    private const int NodePollMs = 50;

    /// <summary>
    /// Period of the capture diagnostics summary.
    /// </summary>
    private const int DiagnosticsWindowMs = 5000;

    /// <summary>
    /// The forwarder receiving the captured audio.
    /// </summary>
    private readonly DualSenseAudioForwarder _forwarder;

    /// <summary>
    /// Unique node name used to find the capture node in the graph.
    /// </summary>
    private readonly string _nodeName;

    /// <summary>
    /// Serializes start/stop and guards the process and reader handles.
    /// </summary>
    private readonly Lock _sync = new Lock();

    private Process? _process;
    private Thread? _readerThread;

    /// <summary>
    /// Whether the reader is running. Written by the reader thread and <see cref="Stop"/>.
    /// </summary>
    private volatile bool _running;

    private bool _firstChunkLogged;
    private long _statsTimestamp;

    /// <summary>
    /// Creates a capture process feeding the given forwarder.
    /// </summary>
    /// <param name="forwarder">The forwarder receiving the captured audio.</param>
    public PipeWireCaptureProcess(DualSenseAudioForwarder forwarder)
    {
        _forwarder = forwarder;
        _nodeName = "dualsense-client-capture-" + Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Whether the child process is running and its reader is active.
    /// </summary>
    public bool IsActive
    {
        get
        {
            return _running;
        }
    }

    /// <summary>
    /// The PipeWire node id of the capture stream, valid after <see cref="Start"/> succeeds.
    /// </summary>
    public int NodeId { get; private set; }

    /// <summary>
    /// The unique PipeWire node name used to find and link the capture stream.
    /// </summary>
    public string NodeName
    {
        get
        {
            return _nodeName;
        }
    }

    /// <summary>
    /// The running <c>pw-record</c> process id, or zero when no child is owned. Used by the
    /// opt-in integration tests to simulate a child exit.
    /// </summary>
    internal int ChildProcessId
    {
        get
        {
            lock (_sync)
            {
                if (_process is null)
                {
                    return 0;
                }

                try
                {
                    return _process.Id;
                }
                catch (InvalidOperationException)
                {
                    return 0;
                }
            }
        }
    }

    /// <summary>
    /// Starts <c>pw-record</c>, waits for its node to appear, and starts the reader. Returns
    /// <c>false</c> when the platform or tools are unavailable or the child exits early.
    /// Safe to call repeatedly; an active capture is left untouched.
    /// </summary>
    public bool Start()
    {
        if (!OperatingSystem.IsLinux())
        {
            _log.Debug("PipeWire capture is only available on Linux; capture not started");
            return false;
        }

        if (!PipeWireAudioTopology.IsAvailable)
        {
            _log.Warning("PipeWire capture requires pw-record, pw-dump, and pw-link on PATH; capture not started");
            return false;
        }

        Process? process;
        lock (_sync)
        {
            if (_process is not null)
            {
                return _running;
            }

            try
            {
                process = Process.Start(CreateStartInfo());
            }
            catch (Exception ex)
            {
                _log.Warning("Could not start pw-record", ex);
                return false;
            }

            if (process is null)
            {
                _log.Warning("Could not start pw-record: the process was not created");
                return false;
            }

            _process = process;
            _ = DrainErrorAsync(process);
        }

        if (!TryWaitForNode(process, out int nodeId))
        {
            _log.Warning(
                $"pw-record did not expose capture node '{_nodeName}' within {StartTimeoutMs} ms or exited early; capture not started");
            Stop();
            return false;
        }

        lock (_sync)
        {
            NodeId = nodeId;
            _running = true;
            Thread reader = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "PipeWireAudioCaptureReader"
            };
            _readerThread = reader;
            reader.Start();
        }

        _log.Info($"PipeWire capture started (node {nodeId}, '{_nodeName}')");
        return true;
    }

    /// <summary>
    /// Stops the child and joins the reader. Safe to call repeatedly.
    /// </summary>
    public void Stop()
    {
        Process? process;
        Thread? reader;
        lock (_sync)
        {
            _running = false;
            process = _process;
            _process = null;
            reader = _readerThread;
            _readerThread = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            process.Kill(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The child may have exited on its own between the state check and the kill.
        }

        try
        {
            process.WaitForExit(1000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }

        if (reader is not null && !ReferenceEquals(reader, Thread.CurrentThread))
        {
            reader.Join(1000);
        }

        process.Dispose();
        _log.Info("PipeWire capture stopped");
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();

    /// <summary>
    /// Builds the child command line: an unlinked stereo float capture at the forwarder's
    /// sample rate and a unique node name.
    /// </summary>
    private ProcessStartInfo CreateStartInfo()
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "pw-record",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-P");
        startInfo.ArgumentList.Add("node.name=" + _nodeName);
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("--rate");
        startInfo.ArgumentList.Add(DualSenseBtAudioPipeline.SampleRate.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--channels");
        startInfo.ArgumentList.Add(ChannelCount.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("f32");
        startInfo.ArgumentList.Add("--latency");
        startInfo.ArgumentList.Add("10ms");
        startInfo.ArgumentList.Add("--raw");
        startInfo.ArgumentList.Add("-");
        return startInfo;
    }

    /// <summary>
    /// Polls the graph until the unique capture node appears or the child exits.
    /// </summary>
    private bool TryWaitForNode(Process process, out int nodeId)
    {
        nodeId = 0;
        long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * (StartTimeoutMs / 1000.0));
        while (Stopwatch.GetTimestamp() < deadline)
        {
            try
            {
                if (process.HasExited)
                {
                    return false;
                }
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            int? found = PipeWireAudioTopology.FindNodeId(_nodeName);
            if (found is int id)
            {
                nodeId = id;
                return true;
            }

            Thread.Sleep(NodePollMs);
        }

        return false;
    }

    /// <summary>
    /// Reads raw float frames from the child until it exits or the capture is stopped. A
    /// partially read sample or frame is carried into the next read so channel alignment is
    /// never shifted.
    /// </summary>
    private void ReadLoop()
    {
        Process? process = _process;
        if (process is null)
        {
            return;
        }

        byte[] buffer = new byte[FramesPerRead * BytesPerFrame];
        int carry = 0;
        long totalFrames = 0;
        _statsTimestamp = Stopwatch.GetTimestamp();
        bool unexpected = false;
        try
        {
            Stream input = process.StandardOutput.BaseStream;
            while (_running)
            {
                int read = input.Read(buffer, carry, buffer.Length - carry);
                if (read <= 0)
                {
                    break;
                }

                int total = carry + read;
                int frames = total / BytesPerFrame;
                if (frames == 0)
                {
                    carry = total;
                    continue;
                }

                int usedBytes = frames * BytesPerFrame;
                ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, usedBytes));
                _forwarder.FeedPcm(samples);
                totalFrames += frames;
                carry = total - usedBytes;
                if (carry > 0)
                {
                    Buffer.BlockCopy(buffer, usedBytes, buffer, 0, carry);
                }

                ReportDiagnostics(frames, totalFrames);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            if (_running)
            {
                _log.Debug("Reading the PipeWire capture stream ended unexpectedly", ex);
            }
        }
        catch (Exception ex)
        {
            _log.Error("The PipeWire capture reader failed", ex);
        }
        finally
        {
            unexpected = _running;
            _running = false;
            if (unexpected)
            {
                _log.Warning($"PipeWire capture node {NodeId} stopped unexpectedly");

                // A dead child must not poison a later Start: clear the owned process and
                // reap it so the capture source can open a fresh one.
                Process? dead;
                lock (_sync)
                {
                    dead = _process;
                    _process = null;
                    _readerThread = null;
                }

                if (dead is not null)
                {
                    try
                    {
                        dead.WaitForExit(1000);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                    {
                        // The process was already reaped by Stop.
                    }

                    dead.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Logs the first chunk and a periodic summary of the frames fed to the forwarder.
    /// </summary>
    private void ReportDiagnostics(int frames, long totalFrames)
    {
        long now = Stopwatch.GetTimestamp();
        if (!_firstChunkLogged)
        {
            _firstChunkLogged = true;
            _statsTimestamp = now;
            _log.Debug($"PipeWire capture first chunk: {frames} frames");
            return;
        }

        if (now - _statsTimestamp < Stopwatch.Frequency * DiagnosticsWindowMs / 1000)
        {
            return;
        }

        _statsTimestamp = now;
        _log.Debug($"PipeWire capture stats: {totalFrames / (double)DualSenseBtAudioPipeline.SampleRate:F0} s fed");
    }

    /// <summary>
    /// Drains the child's stderr so it cannot block, keeps the tail for a failure report, and
    /// logs a warning when the child exits on its own with a non-zero code.
    /// </summary>
    private async Task DrainErrorAsync(Process process)
    {
        List<string> tail = new List<string>();
        try
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                if (tail.Count == 8)
                {
                    tail.RemoveAt(0);
                }

                tail.Add(line);
                _log.Debug($"pw-record: {line}");
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The child was stopped while the pipe was being read.
        }

        bool failedUnexpectedly;
        int exitCode = 0;
        try
        {
            failedUnexpectedly = _running && process.HasExited && process.ExitCode != 0;
            if (failedUnexpectedly)
            {
                exitCode = process.ExitCode;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            failedUnexpectedly = false;
        }

        if (failedUnexpectedly)
        {
            _log.Warning($"pw-record exited with code {exitCode}: {string.Join(" | ", tail)}");
        }
    }
}