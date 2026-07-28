namespace DualSenseClient.Logging;

/// <summary>
/// Represents a single log entry containing the message, severity, source location, and optional exception data.
/// This is an immutable value type passed by reference to log sinks for efficient allocation-free logging.
/// </summary>
/// <param name="Timestamp">The local date and time when the log entry was created.</param>
/// <param name="Level">The severity level of the log entry.</param>
/// <param name="Category">
/// A string identifier for the logging category (e.g., "Settings", "DI", "Localization").
/// Typically, matches the application module or service name.
/// </param>
/// <param name="Message">The human-readable log message.</param>
/// <param name="SourceFileName">The file name (without path) where the log call originated.</param>
/// <param name="SourceLine">The line number in the source file where the log call originated.</param>
/// <param name="SourceMemberName">The name of the method or property where the log call originated.</param>
/// <param name="Exception">An optional exception associated with this log entry.</param>
public readonly record struct LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string SourceFileName,
    int SourceLine,
    string SourceMemberName,
    Exception? Exception = null);