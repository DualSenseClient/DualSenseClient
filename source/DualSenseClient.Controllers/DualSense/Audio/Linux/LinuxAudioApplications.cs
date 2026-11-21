namespace DualSenseClient.Controllers.DualSense.Audio.Linux;

/// <summary>
/// Linux application selection for forwarded audio: lists the programs currently rendering
/// audio, resolves a stored executable selector to processes, and finds the PipeWire stream
/// nodes that belong to that program's process tree.
/// </summary>
internal static class LinuxAudioApplications
{
    /// <summary>
    /// Deepest parent chain walked when expanding a selector's process tree. Prevents a
    /// corrupt table from looping forever.
    /// </summary>
    private const int MaxParentDepth = 64;

    /// <summary>
    /// Returns the programs with at least one output stream, de-duplicated by selector and
    /// sorted by display name. Reads the process table itself.
    /// </summary>
    /// <param name="graph">The parsed PipeWire graph.</param>
    public static IReadOnlyList<AudioApplicationInfo> GetPlayingApplications(PipeWireGraph graph)
        => GetPlayingApplications(graph, LinuxProcessTable.Read());

    /// <summary>
    /// Returns the programs with at least one output stream, de-duplicated by selector and
    /// sorted by display name.
    /// </summary>
    /// <param name="graph">The parsed PipeWire graph.</param>
    /// <param name="processes">The process table used to resolve executable paths.</param>
    public static IReadOnlyList<AudioApplicationInfo> GetPlayingApplications(
        PipeWireGraph graph, IReadOnlyDictionary<int, LinuxProcessInfo> processes)
    {
        List<AudioApplicationInfo> applications = new List<AudioApplicationInfo>();
        Dictionary<string, AudioApplicationInfo> bySelector = new Dictionary<string, AudioApplicationInfo>(StringComparer.Ordinal);
        Dictionary<int, string> executablePaths = new Dictionary<int, string>();
        foreach (PipeWireNode node in graph.OutputStreams)
        {
            string selector = ResolveSelector(node, processes, executablePaths);
            if (selector.Length == 0 || bySelector.ContainsKey(selector))
            {
                continue;
            }

            AudioApplicationInfo application = new AudioApplicationInfo(
                ResolveDisplayName(node, selector), selector, node.ProcessId);
            bySelector.Add(selector, application);
            applications.Add(application);
        }

        applications.Sort((left, right) =>
            string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        return applications;
    }

    /// <summary>
    /// Resolves a selector to every process in its process tree. A process matches by
    /// supplied executable path, executable file name, or command name; descendants of a
    /// matching process are included. Real process scans expose only the command name, so
    /// matching never touches a FUSE/AppImage executable during the periodic poll.
    /// </summary>
    /// <param name="selector">The stored executable path or application name.</param>
    /// <param name="processes">The process table.</param>
    public static IReadOnlySet<int> GetMatchingProcessIds(
        string selector, IReadOnlyDictionary<int, LinuxProcessInfo> processes)
    {
        HashSet<int> roots = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(selector))
        {
            return roots;
        }

        string trimmed = selector.Trim();
        IReadOnlyList<string> candidates = GetSelectorCandidates(trimmed);
        foreach (LinuxProcessInfo process in processes.Values)
        {
            if (MatchesSelector(trimmed, candidates, process))
            {
                roots.Add(process.Id);
            }
        }

        if (roots.Count == 0)
        {
            return roots;
        }

        HashSet<int> matches = new HashSet<int>(roots);
        foreach (LinuxProcessInfo process in processes.Values)
        {
            if (matches.Contains(process.Id))
            {
                continue;
            }

            int parentId = process.ParentId;
            for (int depth = 0; parentId > 0 && depth < MaxParentDepth; depth++)
            {
                if (roots.Contains(parentId))
                {
                    matches.Add(process.Id);
                    break;
                }

                if (!processes.TryGetValue(parentId, out LinuxProcessInfo? parent))
                {
                    break;
                }

                parentId = parent.ParentId;
            }
        }

        return matches;
    }

    /// <summary>
    /// Finds the output-stream node ids belonging to the selector's process tree. Streams
    /// with no known process fall back to an application/binary/node name match.
    /// </summary>
    /// <param name="selector">The stored executable path or application name.</param>
    /// <param name="graph">The parsed PipeWire graph.</param>
    /// <param name="processes">The process table.</param>
    public static IReadOnlyList<int> GetMatchingStreamNodeIds(
        string selector, PipeWireGraph graph, IReadOnlyDictionary<int, LinuxProcessInfo> processes)
    {
        IReadOnlySet<int> processIds = GetMatchingProcessIds(selector, processes);
        List<int> nodes = new List<int>();
        foreach (PipeWireNode node in graph.OutputStreams)
        {
            if (MatchesNode(selector, node, processIds, processes))
            {
                nodes.Add(node.Id);
            }
        }

        return nodes;
    }

    /// <summary>
    /// Finds the output-stream node ids belonging to the selector's process tree, reading the
    /// process table itself.
    /// </summary>
    /// <param name="selector">The stored executable path or application name.</param>
    /// <param name="graph">The parsed PipeWire graph.</param>
    public static IReadOnlyList<int> GetMatchingStreamNodeIds(string selector, PipeWireGraph graph)
        => GetMatchingStreamNodeIds(selector, graph, LinuxProcessTable.Read());

    /// <summary>
    /// Resolves the selector against a supplied process table and graph. When only a stream
    /// name matches (native clients without process metadata), returns <c>true</c> with a
    /// process id of zero.
    /// </summary>
    /// <param name="selector">The stored executable path or application name.</param>
    /// <param name="processes">The process table.</param>
    /// <param name="graph">The parsed PipeWire graph, or <c>null</c> when unavailable.</param>
    /// <param name="processId">Receives the resolved process id, or zero.</param>
    public static bool TryResolveProcessId(
        string selector,
        IReadOnlyDictionary<int, LinuxProcessInfo> processes,
        PipeWireGraph? graph,
        out uint processId)
    {
        processId = 0;
        if (string.IsNullOrWhiteSpace(selector))
        {
            return false;
        }

        IReadOnlySet<int> processIds = GetMatchingProcessIds(selector, processes);
        if (processIds.Count > 0)
        {
            if (graph is not null)
            {
                foreach (PipeWireNode node in graph.OutputStreams)
                {
                    if (node.ProcessId != 0 && processIds.Contains((int)node.ProcessId))
                    {
                        processId = node.ProcessId;
                        return true;
                    }
                }
            }

            int currentProcessId = Environment.ProcessId;
            if (processIds.Contains(currentProcessId))
            {
                processId = (uint)currentProcessId;
                return true;
            }

            processId = (uint)processIds.Min();
            return true;
        }

        if (graph is null)
        {
            return false;
        }

        IReadOnlyList<int> nodes = GetMatchingStreamNodeIds(selector, graph, processes);
        if (nodes.Count == 0)
        {
            return false;
        }

        int nodeId = nodes[0];
        foreach (PipeWireNode node in graph.OutputStreams)
        {
            if (node.Id == nodeId)
            {
                processId = node.ProcessId;
                break;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the selector matches the process itself, ignoring descendants.
    /// </summary>
    private static bool MatchesSelector(
        string selector, IReadOnlyList<string> candidates, LinuxProcessInfo process)
    {
        foreach (string candidate in candidates)
        {
            if (process.ExecutablePath.Length > 0
                && string.Equals(process.ExecutablePath, candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        string name = Path.GetFileName(selector.TrimEnd('/'));
        if (name.Length == 0)
        {
            return false;
        }

        string stem = Path.GetFileNameWithoutExtension(name);
        if (string.Equals(process.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(process.Name, stem, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (process.ExecutablePath.Length == 0)
        {
            return false;
        }

        string executableName = Path.GetFileName(process.ExecutablePath);
        return string.Equals(executableName, name, StringComparison.OrdinalIgnoreCase)
               || string.Equals(Path.GetFileNameWithoutExtension(executableName), stem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a stream node belongs to the selector. A known but unrelated process id wins
    /// over a name match, so a different application with the same name cannot be captured
    /// by accident.
    /// </summary>
    private static bool MatchesNode(
        string selector,
        PipeWireNode node,
        IReadOnlySet<int> processIds,
        IReadOnlyDictionary<int, LinuxProcessInfo> processes)
    {
        if (node.ProcessId != 0)
        {
            int processId = (int)node.ProcessId;
            if (processIds.Contains(processId))
            {
                return true;
            }

            if (processes.ContainsKey(processId))
            {
                return false;
            }
        }

        return MatchesApplicationName(selector, node);
    }

    /// <summary>
    /// Whether the selector's file name or stem matches the node's application, binary, or
    /// node name.
    /// </summary>
    private static bool MatchesApplicationName(string selector, PipeWireNode node)
    {
        string name = Path.GetFileName(selector.Trim().TrimEnd('/'));
        if (name.Length == 0)
        {
            return false;
        }

        string stem = Path.GetFileNameWithoutExtension(name);
        return NameMatches(node.ApplicationName, name, stem)
               || NameMatches(node.BinaryName, name, stem)
               || NameMatches(node.Name, name, stem);
    }

    /// <summary>
    /// Whether a node property equals the selector file name or its stem.
    /// </summary>
    private static bool NameMatches(string value, string name, string stem)
    {
        return value.Length > 0
               && (string.Equals(value, name, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, stem, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The selector and its resolved symlink target. Resolving lets a browsed
    /// <c>/usr/bin/app</c> match a process whose executable lives elsewhere.
    /// </summary>
    private static IReadOnlyList<string> GetSelectorCandidates(string selector)
    {
        List<string> candidates = new List<string>
        {
            selector
        };

        try
        {
            FileSystemInfo? target = File.ResolveLinkTarget(selector, false);
            if (target is not null && target.FullName.Length > 0
                                   && !string.Equals(target.FullName, selector, StringComparison.Ordinal))
            {
                candidates.Add(target.FullName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A bare application name or a path that is not a link has no target; the name
            // fallbacks still apply.
        }

        return candidates;
    }

    /// <summary>
    /// The stored selector for a stream: its process executable when known, else an
    /// application/binary/node name. Executable paths are resolved only for the few processes
    /// that own output streams, never for every process in the table.
    /// </summary>
    private static string ResolveSelector(
        PipeWireNode node,
        IReadOnlyDictionary<int, LinuxProcessInfo> processes,
        Dictionary<int, string> executablePaths)
    {
        if (node.ProcessId != 0)
        {
            int processId = (int)node.ProcessId;
            string executablePath = ResolveExecutablePath(processId, processes, executablePaths);
            if (executablePath.Length > 0)
            {
                return executablePath;
            }
        }

        if (node.ApplicationName.Length > 0)
        {
            return node.ApplicationName;
        }

        return node.BinaryName.Length > 0 ? node.BinaryName : node.Name;
    }

    /// <summary>
    /// Resolves one process's executable path, preferring a caller-supplied test table and
    /// caching the result so an app with several streams resolves its process once.
    /// </summary>
    private static string ResolveExecutablePath(
        int processId,
        IReadOnlyDictionary<int, LinuxProcessInfo> processes,
        Dictionary<int, string> executablePaths)
    {
        if (processes.TryGetValue(processId, out LinuxProcessInfo? process) && process.ExecutablePath.Length > 0)
        {
            return process.ExecutablePath;
        }

        if (executablePaths.TryGetValue(processId, out string? cached))
        {
            return cached;
        }

        string resolved = LinuxProcessTable.TryReadExecutablePath(processId) ?? string.Empty;
        executablePaths[processId] = resolved;
        return resolved;
    }

    /// <summary>
    /// The name shown in the picker.
    /// </summary>
    private static string ResolveDisplayName(PipeWireNode node, string selector)
    {
        if (node.ApplicationName.Length > 0)
        {
            return node.ApplicationName;
        }

        if (node.BinaryName.Length > 0)
        {
            return node.BinaryName;
        }

        string name = Path.GetFileNameWithoutExtension(selector);
        return name.Length > 0 ? name : selector;
    }
}