using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using DualSenseClient.Logging;

namespace DualSenseClient.Controllers.DualSense.Audio.Linux;

/// <summary>
/// Thin wrapper around the PipeWire command-line tools. All calls are short-lived,
/// redirected, and bounded by a timeout so a hung tool cannot block audio start or UI
/// enumeration.
/// </summary>
internal static class PipeWireTool
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("PipeWireTool");

    /// <summary>
    /// Whether <paramref name="name"/> is an executable file on <c>PATH</c>.
    /// </summary>
    public static bool ExistsOnPath(string name)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(directory, name)))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                // An unusable PATH entry does not make the tool available by itself.
            }
        }

        return false;
    }

    /// <summary>
    /// Runs a tool with redirected output and waits for it to exit.
    /// </summary>
    /// <param name="fileName">Executable to run.</param>
    /// <param name="arguments">Arguments passed verbatim, one list entry per argument.</param>
    /// <param name="timeoutMs">How long to wait for exit.</param>
    /// <param name="standardOutput">Receives the captured stdout.</param>
    /// <param name="standardError">Receives the captured stderr, including a timeout message.</param>
    /// <returns><c>true</c> when the process exited with code zero.</returns>
    public static bool TryRun(
        string fileName,
        IReadOnlyList<string> arguments,
        int timeoutMs,
        out string standardOutput,
        out string standardError)
    {
        standardOutput = string.Empty;
        standardError = string.Empty;
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            using Process process = new Process
            {
                StartInfo = startInfo
            };
            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                TryKill(process);
                standardError = $"'{fileName}' did not exit within {timeoutMs} ms";
                return false;
            }

            standardOutput = outputTask.GetAwaiter().GetResult();
            standardError = errorTask.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            standardError = ex.Message;
            return false;
        }
        finally
        {
            if (watch.ElapsedMilliseconds >= 250)
            {
                _log.Debug($"'{fileName}' took {watch.ElapsedMilliseconds} ms");
            }
        }
    }

    /// <summary>
    /// Kills a timed-out tool, including children, ignoring a process that already exited.
    /// </summary>
    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(true);
            process.WaitForExit(1000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited between the timeout check and the kill.
        }
    }
}

/// <summary>
/// PipeWire topology queries used by the Linux forwarded-audio sources: the default sink,
/// node lookup, and node-to-node links. Capture itself lives in
/// <see cref="PipeWireCaptureProcess"/>.
/// </summary>
internal static class PipeWireAudioTopology
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("PipeWireAudioTopology");

    /// <summary>
    /// Whether the PipeWire command-line tools needed for capture are installed.
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            return OperatingSystem.IsLinux()
                   && PipeWireTool.ExistsOnPath("pw-record")
                   && PipeWireTool.ExistsOnPath("pw-dump")
                   && PipeWireTool.ExistsOnPath("pw-link");
        }
    }

    /// <summary>
    /// Reads and parses a fresh <c>pw-dump</c> snapshot.
    /// </summary>
    /// <returns>The graph, or <c>null</c> when the dump fails or cannot be parsed.</returns>
    public static PipeWireGraph? TryGetGraph()
    {
        if (!PipeWireTool.TryRun("pw-dump", Array.Empty<string>(), 2500, out string output, out string error))
        {
            _log.Debug($"pw-dump failed: {error.Trim()}");
            return null;
        }

        PipeWireGraph? graph = PipeWireDump.TryParse(output);
        if (graph is null)
        {
            _log.Debug("pw-dump returned an unparseable snapshot");
        }

        return graph;
    }

    /// <summary>
    /// Resolves the session manager's default sink to its node id.
    /// </summary>
    /// <param name="graph">The parsed graph.</param>
    /// <param name="sinkName">Receives the sink node name.</param>
    /// <param name="sinkNodeId">Receives the sink node id.</param>
    /// <returns><c>false</c> when there is no default sink or its node is missing.</returns>
    public static bool TryGetDefaultSink(PipeWireGraph? graph, out string sinkName, out int sinkNodeId)
    {
        sinkName = string.Empty;
        sinkNodeId = 0;
        if (graph?.DefaultSinkName is not { Length: > 0 } name)
        {
            return false;
        }

        int? nodeId = graph.FindSinkId(name);
        if (nodeId is null)
        {
            _log.Debug($"The default sink '{name}' is not present in the PipeWire graph");
            return false;
        }

        sinkName = name;
        sinkNodeId = nodeId.Value;
        return true;
    }

    /// <summary>
    /// Reads the graph and resolves the default sink in one call.
    /// </summary>
    public static bool TryGetDefaultSink(out string sinkName, out int sinkNodeId)
        => TryGetDefaultSink(TryGetGraph(), out sinkName, out sinkNodeId);

    /// <summary>
    /// Finds a node by name in a fresh snapshot.
    /// </summary>
    /// <param name="nodeName">The unique <c>node.name</c> to look for.</param>
    public static int? FindNodeId(string nodeName)
        => TryGetGraph()?.FindNodeId(nodeName);

    /// <summary>
    /// Links every compatible output port of a source node to a capture node. Linking a sink
    /// node captures its monitor; linking an application stream node captures that stream.
    /// </summary>
    /// <param name="outputNodeId">The source node id.</param>
    /// <param name="inputNodeId">The capture node id.</param>
    public static bool TryLinkNodes(int outputNodeId, int inputNodeId)
    {
        string[] arguments =
        [
            outputNodeId.ToString(CultureInfo.InvariantCulture),
            inputNodeId.ToString(CultureInfo.InvariantCulture)
        ];
        bool linked = PipeWireTool.TryRun("pw-link", arguments, 1500, out _, out string error);
        if (!linked)
        {
            _log.Debug($"pw-link {outputNodeId} -> {inputNodeId} failed: {error.Trim()}");
        }

        return linked;
    }
}