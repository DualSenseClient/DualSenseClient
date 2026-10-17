using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Emulation.Windows;

/// <summary>
/// <see cref="IUsbIpAttachStrategy"/> that drives the usbip-win2 vendor CLI: locates
/// <c>usbip.exe</c>, runs <c>usbip.exe --tcp-port &lt;port&gt; attach -r &lt;host&gt; -b &lt;busId&gt;</c>
/// and remembers the vhci port the tool reports, so <c>usbip.exe detach -p &lt;port&gt;</c> can
/// release the device again. When no explicit search directories are given, the PATH entries and
/// the default usbip-win2 install locations are searched. Every failure (tool missing, non-zero
/// exit, timeout, ...) is returned as <see langword="false"/> plus an actionable error that carries
/// the install guidance; the strategy never throws for operational failures.
/// </summary>
public sealed class UsbIpExeAttachStrategy : IUsbIpAttachStrategy
{
    /// <summary>File name of the USB/IP client executable.</summary>
    private const string ExeFileName = "usbip.exe";

    /// <summary>Install guidance every failure message carries, so the caller can show one actionable line.</summary>
    private const string InstallGuidance =
        "USB/IP must be installed for the device to appear (usbip-win2: https://github.com/vadimgrn/usbip-win2).";

    /// <summary>How long one usbip.exe run may take before it is killed and reported as a timeout.</summary>
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Matches the vhci port the tool reports, e.g. the "port 1" of "successfully attached to port 1".</summary>
    private static readonly Regex VhciPortPattern = new Regex(
        @"port\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Directories searched after the PATH entries when the constructor got no explicit search directories.</summary>
    private static readonly string[] DefaultSearchDirectories = BuildDefaultSearchDirectories();

    private readonly IProcessRunner _runner;

    private readonly IEmulationLogger? _logger;

    private readonly IReadOnlyList<string>? _searchDirectories;

    private readonly object _portsLock = new object();

    private readonly Dictionary<string, int> _vhciPortsByBusId = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a strategy that runs the given process seam.
    /// </summary>
    /// <param name="runner">Process seam used to run usbip.exe.</param>
    /// <param name="logger">Logging seam; <see langword="null"/> disables logging.</param>
    /// <param name="searchDirectories">
    /// Directories searched for usbip.exe in order; <see langword="null"/> searches the PATH entries
    /// and the default usbip-win2 install locations instead. An explicit list is used verbatim, which
    /// lets callers and tests pin the discovery set.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="runner"/> is <see langword="null"/>.</exception>
    public UsbIpExeAttachStrategy(IProcessRunner runner, IEmulationLogger? logger = null, IEnumerable<string>? searchDirectories = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _logger = logger;
        _searchDirectories = searchDirectories?.ToArray();
    }

    /// <summary>
    /// Attaches the device by running <c>usbip.exe attach</c>. On success the vhci port is parsed
    /// from the tool output and remembered per bus id for a later <see cref="TryDetach"/>.
    /// </summary>
    /// <param name="host">Host of the USB/IP server (typically loopback).</param>
    /// <param name="port">TCP port of the USB/IP server.</param>
    /// <param name="busId">Bus id of the exported device.</param>
    /// <param name="error">Actionable failure reason; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the tool reported a successful attach.</returns>
    public bool TryAttach(string host, int port, string busId, out string? error)
    {
        if (!TryFindUsbIpExe(ResolveSearchDirectories(), out string exePath))
        {
            error = $"usbip.exe was not found. {InstallGuidance}";
            LogWarning(error);
            return false;
        }

        // The TCP port is a global option of the vendor CLI and precedes the subcommand; the rest is
        // the documented attach argv. ArgumentList entries reach the tool verbatim, so no quoting.
        List<string> arguments = new List<string>
        {
            "--tcp-port",
            port.ToString(CultureInfo.InvariantCulture),
            "attach",
            "-r",
            host,
            "-b",
            busId
        };
        if (!TryRun(exePath, arguments, $"usbip.exe attach for bus id '{busId}'", out ProcessResult result, out error))
        {
            return false;
        }

        if (TryParseVhciPort(result.StandardOutput, out int vhciPort) || TryParseVhciPort(result.StandardError, out vhciPort))
        {
            lock (_portsLock)
            {
                _vhciPortsByBusId[busId] = vhciPort;
            }

            Log(EmulationLogLevel.Information, $"usbip.exe attached bus id '{busId}' on vhci port {vhciPort}.");
        }
        else
        {
            // Attach succeeded; only the detach bookkeeping is degraded, so the caller still sees success.
            LogWarning($"usbip.exe attached bus id '{busId}' but reported no vhci port; detach needs the port from 'usbip.exe port'.");
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Detaches the device by running <c>usbip.exe detach -p &lt;vhciPort&gt;</c> with the port a
    /// successful <see cref="TryAttach"/> remembered. Without a remembered port the device cannot be
    /// released: the strategy logs a warning and returns <see langword="false"/>. A failed detach
    /// keeps the port, so the caller can retry.
    /// </summary>
    /// <param name="busId">Bus id of the device an earlier <see cref="TryAttach"/> attached.</param>
    /// <param name="error">Actionable failure reason; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the tool reported a successful detach.</returns>
    public bool TryDetach(string busId, out string? error)
    {
        int vhciPort;
        bool portKnown;
        lock (_portsLock)
        {
            portKnown = _vhciPortsByBusId.TryGetValue(busId, out vhciPort);
        }

        if (!portKnown)
        {
            error = $"usbip.exe cannot detach bus id '{busId}': no vhci port was recorded for it (attach it through this strategy first). {InstallGuidance}";
            LogWarning(error);
            return false;
        }

        if (!TryFindUsbIpExe(ResolveSearchDirectories(), out string exePath))
        {
            error = $"usbip.exe was not found. {InstallGuidance}";
            LogWarning(error);
            return false;
        }

        List<string> arguments = new List<string>
        {
            "detach",
            "-p",
            vhciPort.ToString(CultureInfo.InvariantCulture)
        };
        if (!TryRun(exePath, arguments, $"usbip.exe detach for bus id '{busId}' (vhci port {vhciPort})", out ProcessResult _, out error))
        {
            return false;
        }

        lock (_portsLock)
        {
            _vhciPortsByBusId.Remove(busId);
        }

        Log(EmulationLogLevel.Information, $"usbip.exe detached bus id '{busId}' from vhci port {vhciPort}.");
        error = null;
        return true;
    }

    /// <summary>
    /// Searches <paramref name="searchDirectories"/> in order for a <c>usbip.exe</c> file.
    /// </summary>
    /// <param name="searchDirectories">Directories to search, in order.</param>
    /// <param name="path">Path of the first <c>usbip.exe</c> found; empty when none was found.</param>
    /// <returns><see langword="true"/> when one of the directories contains a <c>usbip.exe</c> file.</returns>
    public static bool TryFindUsbIpExe(IEnumerable<string> searchDirectories, out string path)
    {
        if (searchDirectories != null)
        {
            foreach (string directory in searchDirectories)
            {
                if (TryFindInDirectory(directory, out path))
                {
                    return true;
                }
            }
        }

        path = string.Empty;
        return false;
    }

    private static bool TryFindInDirectory(string? directory, out string path)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            string candidate = Path.Combine(directory, ExeFileName);
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private static string[] BuildDefaultSearchDirectories()
    {
        List<string> directories = new List<string>();
        AddEnvironmentDirectory(directories, "ProgramFiles", "usbip-win2");
        AddEnvironmentDirectory(directories, "ProgramFiles(x86)", "usbip-win2");
        AddEnvironmentDirectory(directories, "LOCALAPPDATA", Path.Combine("Programs", "usbip-win2"));
        // Observed install location of the vendor installer on a real machine (usbip-win2 0.9.8.x
        // installs to "C:\Program Files\USBip" and does not add itself to the PATH).
        AddEnvironmentDirectory(directories, "ProgramFiles", "USBip");
        return directories.ToArray();
    }

    private static void AddEnvironmentDirectory(List<string> directories, string variableName, string relativePath)
    {
        string? root = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(root))
        {
            directories.Add(Path.Combine(root, relativePath));
        }
    }

    private static IEnumerable<string> GetPathVariableDirectories()
    {
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            yield break;
        }

        foreach (string entry in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return entry.Trim('"');
        }
    }

    private static bool TryParseVhciPort(string output, out int port)
    {
        Match match = VhciPortPattern.Match(output);
        if (!match.Success)
        {
            port = 0;
            return false;
        }

        return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out port);
    }

    private bool TryRun(string exePath, IReadOnlyList<string> arguments, string operation, out ProcessResult result, out string? error)
    {
        try
        {
            result = _runner.Run(exePath, arguments, RunTimeout);
        }
        catch (TimeoutException)
        {
            result = default;
            error = $"{operation} timed out after {RunTimeout.TotalSeconds:0} seconds. {InstallGuidance}";
            LogWarning(error);
            return false;
        }
        catch (Exception exception)
        {
            result = default;
            error = $"{operation} could not run: {exception.Message} {InstallGuidance}";
            LogWarning(error);
            return false;
        }

        if (result.ExitCode != 0)
        {
            error = $"{operation} failed with exit code {result.ExitCode}: {DescribeOutput(result)} {InstallGuidance}";
            LogWarning(error);
            return false;
        }

        error = null;
        return true;
    }

    private static string DescribeOutput(ProcessResult result)
    {
        string output = result.StandardError.Trim();
        if (output.Length == 0)
        {
            output = result.StandardOutput.Trim();
        }

        return output.Length == 0 ? "no output" : output;
    }

    private IReadOnlyList<string> ResolveSearchDirectories()
    {
        if (_searchDirectories != null)
        {
            return _searchDirectories;
        }

        List<string> directories = new List<string>();
        directories.AddRange(GetPathVariableDirectories());
        directories.AddRange(DefaultSearchDirectories);
        return directories;
    }

    private void LogWarning(string message) => Log(EmulationLogLevel.Warning, message);

    private void Log(EmulationLogLevel level, string message) => _logger?.Log(level, message);
}