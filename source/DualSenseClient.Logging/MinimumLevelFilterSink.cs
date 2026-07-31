namespace DualSenseClient.Logging;

/// <summary>
/// A log sink wrapper that forwards entries to an inner sink only when the entry's
/// severity meets or exceeds a configurable minimum level.
/// </summary>
/// <remarks>
/// <para>
/// This sink enables the common pattern of having a file sink capture all levels
/// (for full diagnostic capture) while the console only displays entries above a
/// threshold. Wrap the <see cref="ConsoleLogSink"/> with this filter and leave the
/// <see cref="FileLogSink"/> unwrapped.
/// </para>
/// <para>
/// The minimum level is resolved via a <see cref="Func{TResult}"/> delegate each
/// time <see cref="Write"/> is called, so changes to the global
/// <see cref="DualSenseClientLogger.MinimumLevel"/> are reflected automatically
/// without needing to reconfigure the sink hierarchy.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// new CompositeLogSink(
///     new MinimumLevelFilterSink(new ConsoleLogSink(), () => DualSenseClientLogger.MinimumLevel),
///     new FileLogSink(@"Logs\app.log"));
/// </code>
/// </example>
public sealed class MinimumLevelFilterSink : ILogSink
{
    /// <summary>
    /// The inner sink entries are forwarded to when they pass the level check.
    /// </summary>
    private readonly ILogSink _inner;

    /// <summary>
    /// Provides the current minimum level to enforce on each write.
    /// </summary>
    private readonly Func<LogLevel> _minLevelProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="MinimumLevelFilterSink"/> class.
    /// </summary>
    /// <param name="inner">The inner sink to forward entries to when they pass the level check. Cannot be null.</param>
    /// <param name="minLevelProvider">
    /// A delegate that returns the current minimum level to enforce.
    /// Invoked on every <see cref="Write"/> call. Cannot be null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="inner"/> or <paramref name="minLevelProvider"/> is null.
    /// </exception>
    public MinimumLevelFilterSink(ILogSink inner, Func<LogLevel> minLevelProvider)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(minLevelProvider);
        _inner = inner;
        _minLevelProvider = minLevelProvider;
    }

    /// <summary>
    /// Writes a log entry to the inner sink if its level meets or exceeds the current minimum.
    /// Entries below the threshold are silently discarded.
    /// </summary>
    /// <param name="entry">The log entry to evaluate and conditionally forward.</param>
    public void Write(in LogEntry entry)
    {
        if (entry.Level >= _minLevelProvider())
        {
            _inner.Write(in entry);
        }
    }
}