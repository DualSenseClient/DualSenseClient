namespace DualSenseClient.Emulation.Windows;

/// <summary>
/// Outcome of one process invocation: the exit code and the complete captured standard output and
/// standard error streams.
/// </summary>
public readonly struct ProcessResult
{
    /// <summary>
    /// Initializes a result with the exit code and the captured output streams.
    /// </summary>
    /// <param name="exitCode">Exit code the process terminated with.</param>
    /// <param name="standardOutput">Complete standard output the process wrote.</param>
    /// <param name="standardError">Complete standard error the process wrote.</param>
    public ProcessResult(int exitCode, string standardOutput, string standardError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    /// <summary>Exit code the process terminated with.</summary>
    public int ExitCode { get; }

    /// <summary>Complete standard output the process wrote.</summary>
    public string StandardOutput { get; }

    /// <summary>Complete standard error the process wrote.</summary>
    public string StandardError { get; }
}

/// <summary>
/// Process seam of the attach orchestration: runs one executable to completion with redirected
/// output. Production uses <see cref="ProcessRunner"/>; tests substitute a fake so no real tool
/// ever runs.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/> and waits for it to exit.
    /// </summary>
    /// <param name="fileName">Executable to run.</param>
    /// <param name="arguments">Arguments passed verbatim, one list entry per argument.</param>
    /// <param name="timeout">How long to wait for the process to exit.</param>
    /// <returns>The exit code and the captured output streams.</returns>
    /// <exception cref="TimeoutException">The process did not exit within <paramref name="timeout"/>; the runner has killed it.</exception>
    ProcessResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout);
}