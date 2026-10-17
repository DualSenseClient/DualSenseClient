using DualSenseClient.Emulation.Windows;

namespace DualSenseClient.Tests.Emulation.TestSupport;

/// <summary>
/// <see cref="IProcessRunner"/> test double: records every run and then either returns the
/// configured <see cref="Result"/> or throws the configured <see cref="ExceptionToThrow"/>, so
/// attach failures (non-zero exit, timeout, missing executable) can be simulated without spawning
/// a process.
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly object _lock = new object();

    private readonly List<RecordedProcessRun> _runs = new List<RecordedProcessRun>();

    /// <summary>Result returned by every run; defaults to a clean successful run with no output.</summary>
    public ProcessResult Result { get; set; } = new ProcessResult(0, string.Empty, string.Empty);

    /// <summary>Exception thrown by every run instead of returning <see cref="Result"/>; <see langword="null"/> returns it.</summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <summary>Snapshot of every run so far, in invocation order.</summary>
    public IReadOnlyList<RecordedProcessRun> Runs
    {
        get
        {
            lock (_lock)
            {
                return _runs.ToArray();
            }
        }
    }

    /// <summary>Records one run, then returns <see cref="Result"/> or throws <see cref="ExceptionToThrow"/>.</summary>
    /// <param name="fileName">Executable path the caller asked to run.</param>
    /// <param name="arguments">Arguments the caller asked to pass.</param>
    /// <param name="timeout">Timeout the caller asked to apply.</param>
    /// <returns>The configured <see cref="Result"/>.</returns>
    /// <exception cref="Exception">The configured <see cref="ExceptionToThrow"/>, when set.</exception>
    public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        lock (_lock)
        {
            _runs.Add(new RecordedProcessRun(fileName, arguments, timeout));
        }

        if (ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }

        return Result;
    }
}

/// <summary>One invocation captured by <see cref="FakeProcessRunner"/>.</summary>
public readonly struct RecordedProcessRun
{
    /// <summary>
    /// Initializes a record of one run with the executable path, the arguments and the timeout.
    /// </summary>
    /// <param name="fileName">Executable path the caller asked to run.</param>
    /// <param name="arguments">Arguments the caller asked to pass.</param>
    /// <param name="timeout">Timeout the caller asked to apply.</param>
    public RecordedProcessRun(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        FileName = fileName;
        Arguments = arguments;
        Timeout = timeout;
    }

    /// <summary>Executable path the caller asked to run.</summary>
    public string FileName { get; }

    /// <summary>Arguments the caller asked to pass.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Timeout the caller asked to apply.</summary>
    public TimeSpan Timeout { get; }
}