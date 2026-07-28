namespace DualSenseClient.Logging;

/// <summary>
/// Extension methods for <see cref="LogLevel"/>.
/// </summary>
public static class LogLevelExtensions
{
    /// <summary>
    /// Converts a <see cref="LogLevel"/> to its uppercase string label (e.g., "TRACE", "WARNING").
    /// </summary>
    /// <param name="level">The log level to convert.</param>
    /// <returns>The uppercase label for the level, or "LOG" for unrecognized values.</returns>
    public static string ToLevelLabel(this LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO",
            LogLevel.Warning => "WARNING",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRITICAL",
            _ => "LOG",
        };
    }
}