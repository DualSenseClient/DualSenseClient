namespace DualSenseClient.Logging;

/// <summary>
/// A log sink that dispatches each <see cref="LogEntry"/> to multiple child sinks.
/// Exceptions thrown by individual child sinks are silently swallowed so that one failing
/// sink cannot prevent entries from reaching the others.
/// </summary>
/// <remarks>
/// This is the primary mechanism for sending log output to multiple destinations simultaneously
/// (e.g., both console and file). Implements <see cref="IDisposable"/> to propagate disposal
/// to any child sinks that are themselves disposable.
/// </remarks>
public sealed class CompositeLogSink : ILogSink, IDisposable
{
    private readonly ILogSink[] _sinks;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeLogSink"/> class
    /// with the specified child sinks.
    /// </summary>
    /// <param name="sinks">The child sinks to dispatch log entries to. Cannot be null or contain null elements.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sinks"/> is null.</exception>
    public CompositeLogSink(params ILogSink[] sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);

        foreach (ILogSink sink in sinks)
        {
            ArgumentNullException.ThrowIfNull(sink, nameof(sinks));
        }

        _sinks = sinks;
    }

    /// <summary>
    /// Gets the read-only collection of child sinks.
    /// </summary>
    public IReadOnlyList<ILogSink> Sinks => Array.AsReadOnly(_sinks);

    /// <summary>
    /// Writes a log entry to every child sink. Exceptions from individual sinks are caught and discarded.
    /// </summary>
    /// <param name="entry">The log entry to dispatch.</param>
    public void Write(in LogEntry entry)
    {
        foreach (ILogSink sink in _sinks)
        {
            try
            {
                sink.Write(in entry);
            }
            catch
            {
                // ignored
            }
        }
    }

    /// <summary>
    /// Disposes all child sinks that implement <see cref="IDisposable"/>.
    /// Exceptions thrown during disposal of individual sinks are caught and discarded.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        foreach (ILogSink sink in _sinks)
        {
            if (sink is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch
                {
                    // ignored
                }
            }
        }
    }
}