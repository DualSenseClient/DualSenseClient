using System.Diagnostics;

namespace DualSenseClient.Emulation.Windows;

/// <summary>
/// Production <see cref="IProcessRunner"/>: starts the executable with redirected output, waits for
/// it to exit and captures both output streams. A process that outlives the timeout is killed
/// (including its children) and reported as a <see cref="TimeoutException"/>, so callers never
/// block on a hung tool.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/> and waits for it to exit.
    /// </summary>
    /// <param name="fileName">Executable to run.</param>
    /// <param name="arguments">Arguments passed verbatim, one list entry per argument.</param>
    /// <param name="timeout">How long to wait for the process to exit.</param>
    /// <returns>The exit code and the captured output streams.</returns>
    /// <exception cref="TimeoutException">The process did not exit within <paramref name="timeout"/>; it has been killed.</exception>
    public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
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

        using Process process = new Process
        {
            StartInfo = startInfo
        };
        process.Start();
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            KillProcess(process);
            throw new TimeoutException($"'{fileName}' did not exit within {timeout.TotalSeconds:0.###} seconds.");
        }

        return new ProcessResult(process.ExitCode, standardOutput.GetAwaiter().GetResult(), standardError.GetAwaiter().GetResult());
    }

    private static void KillProcess(Process process)
    {
        try
        {
            process.Kill(true);
            process.WaitForExit(5000);
        }
        catch (Exception)
        {
            // The process may have exited between the timeout and the kill; nothing left to do.
        }
    }
}