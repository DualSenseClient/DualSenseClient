namespace DualSenseClient.Controllers.DualSense.Audio.Linux;

/// <summary>
/// One process from a Linux <c>/proc</c> snapshot: command name and parent, used to resolve
/// a stored audio-application selector and follow its process tree. Executable paths are
/// resolved on demand for the few processes that own output streams.
/// </summary>
internal sealed class LinuxProcessInfo
{
    /// <summary>
    /// Creates a process descriptor.
    /// </summary>
    /// <param name="id">The process id.</param>
    /// <param name="parentId">The parent process id, or zero when unknown.</param>
    /// <param name="executablePath">The resolved executable path, or empty when unreadable.</param>
    /// <param name="name">The command name reported by <c>/proc/&lt;pid&gt;/status</c>.</param>
    public LinuxProcessInfo(int id, int parentId, string executablePath, string name)
    {
        Id = id;
        ParentId = parentId;
        ExecutablePath = executablePath;
        Name = name;
    }

    /// <summary>
    /// The process id.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// The parent process id, or zero when unknown.
    /// </summary>
    public int ParentId { get; }

    /// <summary>
    /// The resolved executable path, or empty when the process is gone, unreadable, or the
    /// path was not requested. Process scans deliberately leave this empty; use
    /// <see cref="LinuxProcessTable.TryReadExecutablePath"/> for the few stream owners that
    /// need it.
    /// </summary>
    public string ExecutablePath { get; }

    /// <summary>
    /// The command name reported by <c>/proc/&lt;pid&gt;/status</c>.
    /// </summary>
    public string Name { get; }
}

/// <summary>
/// Reads the Linux process table once. Individual unreadable or disappearing processes are
/// skipped; a scan never throws because a process exited midway.
/// </summary>
internal static class LinuxProcessTable
{
    /// <summary>
    /// Reads every visible process that exposes a <c>/proc/&lt;pid&gt;/status</c> file.
    /// </summary>
    /// <returns>The process table keyed by process id; empty on non-Linux platforms.</returns>
    public static IReadOnlyDictionary<int, LinuxProcessInfo> Read()
    {
        Dictionary<int, LinuxProcessInfo> processes = new Dictionary<int, LinuxProcessInfo>();
        if (!OperatingSystem.IsLinux())
        {
            return processes;
        }

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories("/proc");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return processes;
        }

        foreach (string directory in directories)
        {
            if (!int.TryParse(Path.GetFileName(directory), out int id) || id <= 0)
            {
                continue;
            }

            LinuxProcessInfo? process = TryReadProcess(id);
            if (process is not null)
            {
                processes[id] = process;
            }
        }

        return processes;
    }

    /// <summary>
    /// Reads one process, returning <c>null</c> when it disappeared or cannot be read.
    /// </summary>
    private static LinuxProcessInfo? TryReadProcess(int id)
    {
        string statusPath = Path.Combine("/proc", id.ToString(), "status");
        try
        {
            int parentId = 0;
            string name = string.Empty;
            foreach (string line in File.ReadLines(statusPath))
            {
                if (line.StartsWith("Name:", StringComparison.Ordinal))
                {
                    name = line[5..].Trim();
                }
                else if (line.StartsWith("PPid:", StringComparison.Ordinal)
                         && int.TryParse(line[5..].Trim(), out int parsedParent))
                {
                    parentId = parsedParent;
                }

                if (name.Length > 0 && parentId != 0)
                {
                    break;
                }
            }

            return new LinuxProcessInfo(id, parentId, string.Empty, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a process's executable symlink on demand, or <c>null</c> when it is
    /// unreadable. The final symlink target is deliberately not followed: a process can be
    /// backed by a FUSE/AppImage mount, and following it from a periodic scan can block for
    /// seconds.
    /// </summary>
    /// <param name="id">The process id.</param>
    public static string? TryReadExecutablePath(int id)
    {
        try
        {
            FileSystemInfo? target = File.ResolveLinkTarget(Path.Combine("/proc", id.ToString(), "exe"), false);
            return target?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}