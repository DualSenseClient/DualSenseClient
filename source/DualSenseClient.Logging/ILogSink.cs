namespace DualSenseClient.Logging;

/// <summary>
/// Defines the contract for a log destination (sink) that receives and processes log entries.
/// Implementations write entries to specific targets such as the console, files, or external services.
/// </summary>
/// <remarks>
/// Sinks are passed log entries by reference (in LogEntry) to avoid value-type copies.
/// Implementations should be thread-safe, as multiple threads may write to the same sink concurrently.
/// </remarks>
public interface ILogSink
{
    /// <summary>
    /// Writes a log entry to this sink.
    /// </summary>
    /// <param name="entry">The log entry to write. Passed by reference to avoid copying.</param>
    void Write(in LogEntry entry);
}