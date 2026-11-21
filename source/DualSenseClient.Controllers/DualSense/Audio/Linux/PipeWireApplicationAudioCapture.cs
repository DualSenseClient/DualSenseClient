using System.Diagnostics;
using DualSenseClient.Logging;

namespace DualSenseClient.Controllers.DualSense.Audio.Linux;

/// <summary>
/// Captures the audio a single application and its child processes render, by linking their
/// PipeWire output-stream nodes into one capture stream. Other applications cannot drive the
/// controller, and the captured program keeps playing to its normal sink.
/// </summary>
/// <remarks>
/// <para>
/// The selected executable is stored as a path (or an application name when a stream exposes
/// no process metadata). The process table is re-read on every poll so a program restart and
/// new child streams are picked up. Streams that appear after capture starts are linked into
/// the same capture node, and PipeWire mixes them natively.
/// </para>
/// <para>
/// An unlinked capture node produces no data, so a selected program that is running but not
/// yet playing does not hold the Bluetooth audio lane open. Once a stream is linked the
/// reader feeds it to the forwarder like the system-output source.
/// </para>
/// </remarks>
internal sealed class PipeWireApplicationAudioCapture : IForwardedAudioCapture, IDisposable
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("PipeWireApplicationAudioCapture");

    /// <summary>
    /// How often the graph is rescanned for new or removed streams.
    /// </summary>
    private const int PollIntervalMs = 1000;

    /// <summary>
    /// The stored executable path or application name.
    /// </summary>
    private readonly string _selector;

    /// <summary>
    /// The forwarder receiving the captured audio.
    /// </summary>
    private readonly DualSenseAudioForwarder _forwarder;

    /// <summary>
    /// Serializes start/stop and guards the capture, poll thread, and linked node set.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// Stream node ids already linked into the capture, so a rescan never creates a second
    /// link that would double the stream.
    /// </summary>
    private readonly HashSet<int> _linkedNodeIds = new HashSet<int>();

    private PipeWireCaptureProcess? _capture;
    private Thread? _pollThread;
    private ManualResetEventSlim? _pollStop;
    private volatile bool _running;

    /// <summary>
    /// Creates a capture of the given application feeding the given forwarder.
    /// </summary>
    /// <param name="selector">The stored executable path or application name.</param>
    /// <param name="forwarder">The forwarder receiving the captured audio.</param>
    public PipeWireApplicationAudioCapture(string selector, DualSenseAudioForwarder forwarder)
    {
        _selector = selector;
        _forwarder = forwarder;
    }

    /// <inheritdoc/>
    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _running && _capture?.IsActive == true;
            }
        }
    }

    /// <inheritdoc/>
    public bool Start()
    {
        if (!OperatingSystem.IsLinux())
        {
            _log.Debug("PipeWire application audio capture is only available on Linux; capture not started");
            return false;
        }

        lock (_sync)
        {
            if (_capture is not null)
            {
                return _running && _capture.IsActive;
            }

            long setupStarted = Stopwatch.GetTimestamp();
            PipeWireGraph? graph = PipeWireAudioTopology.TryGetGraph();
            if (graph is null)
            {
                _log.Warning($"Could not read the PipeWire graph while starting capture for '{_selector}'");
                return false;
            }

            long graphRead = Stopwatch.GetTimestamp();
            IReadOnlyDictionary<int, LinuxProcessInfo> processes = LinuxProcessTable.Read();
            long tableRead = Stopwatch.GetTimestamp();
            IReadOnlySet<int> processIds = LinuxAudioApplications.GetMatchingProcessIds(_selector, processes);
            long matchDone = Stopwatch.GetTimestamp();
            IReadOnlyList<int> nodes = LinuxAudioApplications.GetMatchingStreamNodeIds(_selector, graph, processes);
            long matched = Stopwatch.GetTimestamp();
            if (processIds.Count == 0 && nodes.Count == 0)
            {
                _log.Info($"The application '{Path.GetFileName(_selector)}' is not running; capture not started");
                return false;
            }

            PipeWireCaptureProcess capture = new PipeWireCaptureProcess(_forwarder);
            if (!capture.Start())
            {
                capture.Dispose();
                return false;
            }

            long processStarted = Stopwatch.GetTimestamp();
            _linkedNodeIds.Clear();
            foreach (int nodeId in nodes)
            {
                if (PipeWireAudioTopology.TryLinkNodes(nodeId, capture.NodeId))
                {
                    _linkedNodeIds.Add(nodeId);
                }
            }

            long linksDone = Stopwatch.GetTimestamp();
            if (nodes.Count > 0 && _linkedNodeIds.Count == 0)
            {
                _log.Warning($"Could not link any output stream for '{_selector}'; retrying while the capture runs");
            }

            _capture = capture;
            _running = true;
            ManualResetEventSlim stop = new ManualResetEventSlim(false);
            _pollStop = stop;
            Thread poll = new Thread(() => PollLoop(capture, stop))
            {
                IsBackground = true,
                Name = "PipeWireApplicationAudioCapture"
            };
            _pollThread = poll;
            poll.Start();

            _log.Info(
                $"Application audio capture follows '{_selector}' ({processIds.Count} process(es), {_linkedNodeIds.Count} linked stream(s))");
            _log.Debug(
                $"Application capture setup: graph {ElapsedMs(setupStarted, graphRead):F0} ms, "
                + $"table {ElapsedMs(graphRead, tableRead):F0} ms, "
                + $"match {ElapsedMs(tableRead, matchDone):F0} ms, "
                + $"nodes {ElapsedMs(matchDone, matched):F0} ms, "
                + $"process {ElapsedMs(matched, processStarted):F0} ms, "
                + $"links {ElapsedMs(processStarted, linksDone):F0} ms, "
                + $"total {ElapsedMs(setupStarted, linksDone):F0} ms");
            return true;
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        PipeWireCaptureProcess? capture;
        Thread? pollThread;
        ManualResetEventSlim? pollStop;
        lock (_sync)
        {
            capture = _capture;
            _capture = null;
            pollThread = _pollThread;
            _pollThread = null;
            pollStop = _pollStop;
            _pollStop = null;
            _running = false;
            _linkedNodeIds.Clear();
        }

        pollStop?.Set();
        bool joined = pollThread is null || ReferenceEquals(pollThread, Thread.CurrentThread);
        if (!joined)
        {
            // A poll can be inside a bounded pw-dump/pw-link call; wait past the tool timeout
            // before considering the thread still busy.
            joined = pollThread!.Join(6000);
        }

        if (joined)
        {
            pollStop?.Dispose();
        }

        capture?.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();

    /// <summary>
    /// Rescans the graph until stopped, linking streams that appeared since the last pass and
    /// forgetting streams whose nodes disappeared.
    /// </summary>
    private void PollLoop(PipeWireCaptureProcess capture, ManualResetEventSlim stop)
    {
        try
        {
            while (!stop.Wait(PollIntervalMs))
            {
                if (!_running || !capture.IsActive)
                {
                    break;
                }

                PipeWireGraph? graph = PipeWireAudioTopology.TryGetGraph();
                if (graph is null)
                {
                    continue;
                }

                long pollStarted = Stopwatch.GetTimestamp();
                IReadOnlyDictionary<int, LinuxProcessInfo> processes = LinuxProcessTable.Read();
                long tableDone = Stopwatch.GetTimestamp();
                IReadOnlyList<int> nodes = LinuxAudioApplications.GetMatchingStreamNodeIds(_selector, graph, processes);
                long matchDone = Stopwatch.GetTimestamp();
                if (ElapsedMs(pollStarted, matchDone) > 500)
                {
                    _log.Warning(
                        $"Application capture scan took {ElapsedMs(pollStarted, matchDone):F0} ms "
                        + $"(table {ElapsedMs(pollStarted, tableDone):F0} ms, "
                        + $"match {ElapsedMs(tableDone, matchDone):F0} ms) for '{_selector}'");
                }

                HashSet<int> current = new HashSet<int>(nodes);
                HashSet<int> linked;
                lock (_sync)
                {
                    if (!_running)
                    {
                        break;
                    }

                    _linkedNodeIds.RemoveWhere(nodeId => !current.Contains(nodeId));
                    linked = new HashSet<int>(_linkedNodeIds);
                }

                foreach (int nodeId in nodes)
                {
                    if (linked.Contains(nodeId))
                    {
                        continue;
                    }

                    if (!_running || !capture.IsActive)
                    {
                        break;
                    }

                    if (!PipeWireAudioTopology.TryLinkNodes(nodeId, capture.NodeId))
                    {
                        continue;
                    }

                    lock (_sync)
                    {
                        if (!_running)
                        {
                            continue;
                        }

                        _linkedNodeIds.Add(nodeId);
                    }

                    _log.Debug($"Application audio capture linked stream node {nodeId} for '{_selector}'");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Application audio capture scanner failed for '{_selector}'", ex);
        }
    }

    /// <summary>
    /// Milliseconds between two stopwatch timestamps.
    /// </summary>
    private static double ElapsedMs(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;
}