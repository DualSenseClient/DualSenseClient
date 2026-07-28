namespace DualSenseClient.Logging;

/// <summary>
/// Defines the severity levels for log entries, ordered from least to most severe.
/// Used to filter and categorize log output at runtime.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Verbose diagnostic output, typically only useful during development.
    /// </summary>
    Trace = 0,

    /// <summary>
    /// Fine-grained informational events useful for debugging.
    /// </summary>
    Debug = 1,

    /// <summary>
    /// General operational messages confirming normal application flow.
    /// </summary>
    Info = 2,

    /// <summary>
    /// Potentially harmful situations that deserve attention but are not errors.
    /// </summary>
    Warning = 3,

    /// <summary>
    /// Error events that allow the application to continue running.
    /// </summary>
    Error = 4,

    /// <summary>
    /// Critical failures that may cause the application to terminate.
    /// </summary>
    Critical = 5,

    /// <summary>
    /// Disables all logging output when set as the minimum level.
    /// </summary>
    None = 6
}