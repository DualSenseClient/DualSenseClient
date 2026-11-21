using System.Diagnostics;
using DualSenseClient.Controllers.DualSense.Audio.Linux;
using DualSenseClient.Controllers.DualSense.Audio.Windows;
using DualSenseClient.Logging;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Enumerates the applications currently rendering audio on the default output device and
/// resolves a stored executable path back to a running process, backing the per-application
/// forwarded-audio source.
/// </summary>
/// <remarks>
/// Apps are identified by executable path (not by process id) so a stored selection survives
/// restarts; the process id is resolved at capture time and can change between sessions. On
/// Linux a native PipeWire client may expose no process id, so the selection falls back to an
/// application or binary name. Unsupported platforms return an empty list and resolve nothing.
/// </remarks>
public sealed class AudioApplicationService
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("AudioApplicationService");

    /// <summary>
    /// Whether per-application capture is available on this machine: WASAPI process loopback
    /// on Windows 10 build 20348 (Windows 11) or later, or the PipeWire command-line tools on
    /// Linux.
    /// </summary>
    public static bool IsApplicationCaptureSupported
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);
            }

            return OperatingSystem.IsLinux() && PipeWireAudioTopology.IsAvailable;
        }
    }

    /// <summary>
    /// Returns the applications with a live audio session on the default output device,
    /// de-duplicated by executable path and sorted by display name. On Linux the output
    /// streams of the PipeWire graph are used instead of WASAPI sessions.
    /// </summary>
    public IReadOnlyList<AudioApplicationInfo> GetPlayingApplications()
    {
        List<AudioApplicationInfo> applications = new List<AudioApplicationInfo>();
        if (OperatingSystem.IsLinux())
        {
            PipeWireGraph? graph = PipeWireAudioTopology.TryGetGraph();
            return graph is null
                ? applications
                : LinuxAudioApplications.GetPlayingApplications(graph);
        }

        if (!IsApplicationCaptureSupported)
        {
            return applications;
        }

        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (int processId in WasapiSessionInterop.GetRenderSessionProcessIds(
                     (message, exception) => _log.Warning($"Application audio list unavailable: {message}", exception),
                     message => _log.Debug(message)))
        {
            string? path = TryGetProcessPath(processId);
            if (string.IsNullOrEmpty(path) || !seen.Add(path))
            {
                continue;
            }

            string displayName = TryGetProcessName(processId) ?? Path.GetFileNameWithoutExtension(path);
            applications.Add(new AudioApplicationInfo(displayName, path, (uint)processId));
        }

        applications.Sort((left, right) =>
            string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        return applications;
    }

    /// <summary>
    /// Resolves the executable path of a stored application selection to a live process
    /// id. Prefers a process that is currently rendering audio, then any process with a
    /// matching executable path, then a matching process name (so a selection stored as a
    /// bare file name still resolves). Returns <c>false</c> when nothing matches. On Linux a
    /// matching stream that exposes no process metadata returns <c>true</c> with id zero.
    /// </summary>
    /// <param name="executablePath">The stored executable path or process name.</param>
    /// <param name="processId">Receives the resolved process id.</param>
    public bool TryResolveProcessId(string executablePath, out uint processId)
    {
        processId = 0;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxAudioApplications.TryResolveProcessId(
                executablePath, LinuxProcessTable.Read(), PipeWireAudioTopology.TryGetGraph(), out processId);
        }

        if (!IsApplicationCaptureSupported)
        {
            return false;
        }

        string target = executablePath.Trim();

        // A process already rendering audio is the best match: its session is live.
        foreach (int candidate in WasapiSessionInterop.GetRenderSessionProcessIds(
                     (message, exception) => _log.Warning(
                         $"Resolving a process for '{executablePath}' failed; falling back to a full process scan: {message}", exception),
                     message => _log.Debug(message)))
        {
            if (PathMatches(candidate, target))
            {
                processId = (uint)candidate;
                return true;
            }
        }

        string name = Path.GetFileNameWithoutExtension(target);
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (PathMatches(process.Id, target)
                    || (!string.IsNullOrEmpty(name)
                        && string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    processId = (uint)process.Id;
                    return true;
                }
            }
            catch (Exception)
            {
                // Protected processes refuse access; skip them.
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the process's executable path matches the target, by full path or, when
    /// the target has no directory, by file name.
    /// </summary>
    private static bool PathMatches(int processId, string target)
    {
        string? path = TryGetProcessPath(processId);
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (string.Equals(path, target, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        bool bareName = !target.Contains('\\') && !target.Contains('/');
        return bareName && string.Equals(Path.GetFileName(path), target, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the process's main module path, or <c>null</c> when it cannot be read
    /// (protected, elevated, or already exited).
    /// </summary>
    private static string? TryGetProcessPath(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.MainModule?.FileName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the process name, or <c>null</c> when it cannot be read.
    /// </summary>
    private static string? TryGetProcessName(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}