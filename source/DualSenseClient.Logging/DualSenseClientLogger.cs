using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace DualSenseClient.Logging;

/// <summary>
/// The central logging orchestrator and per-category logger for the application.
/// Provides static configuration methods and factory-level logger creation via <see cref="For"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class serves two roles: a static orchestrator that manages the global logging sink and
/// minimum level, and a per-category logger instance that captures caller context automatically.
/// </para>
/// <para>
/// At application startup, call <see cref="Configure"/> to set up the log sink (e.g., a
/// <see cref="CompositeLogSink"/> with a <see cref="ConsoleLogSink"/> and a
/// <see cref="FileLogSink"/>). The configured minimum level is enforced centrally by
/// the logger and therefore applies uniformly to every sink.
/// </para>
/// <para>
/// Throughout the application, obtain a logger via <see cref="For"/> and call level-specific
/// methods. Caller file, line, and member name are captured automatically via compiler attributes.
/// </para>
/// <code>
/// // At startup:
/// DualSenseClientLogger.Configure(LogLevel.Info,
///     new CompositeLogSink(
///         new ConsoleLogSink(),
///         new FileLogSink("Logs\\app.log")));
///
/// // In consumer code:
/// private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("MyService");
/// _log.Info("Service started");
/// </code>
/// </remarks>
public sealed class DualSenseClientLogger
{
    // --- Static orchestrator ---

    /// <summary>
    /// Cached per-category logger instances, keyed by category name.
    /// </summary>
    private static readonly ConcurrentDictionary<string, DualSenseClientLogger> Loggers =
        new ConcurrentDictionary<string, DualSenseClientLogger>(StringComparer.Ordinal);

    /// <summary>
    /// Synchronizes access to the static sink configuration.
    /// </summary>
    private static readonly Lock Sync = new Lock();

    /// <summary>
    /// The minimum <see cref="LogLevel"/> below which entries are silently dropped.
    /// </summary>
    private static volatile LogLevel _minimumLevel = LogLevel.Info;

    /// <summary>
    /// The active log sink that receives all log entries.
    /// </summary>
    private static ILogSink _sink = new ConsoleLogSink(true);

    /// <summary>
    /// Gets or sets the minimum <see cref="LogLevel"/> below which log entries are silently dropped.
    /// Defaults to <see cref="LogLevel.Info"/>.
    /// </summary>
    /// <remarks>
    /// Setting this to <see cref="LogLevel.None"/> disables all logging, including file output.
    /// This property is thread-safe for reads via volatile semantics.
    /// </remarks>
    public static LogLevel MinimumLevel
    {
        get
        {
            return _minimumLevel;
        }
        set
        {
            _minimumLevel = value;
        }
    }

    /// <summary>
    /// Gets or sets the active <see cref="ILogSink"/> that receives all log entries.
    /// </summary>
    /// <remarks>
    /// Replacing the sink automatically disposes the previous sink
    /// if it implements <see cref="IDisposable"/>.
    /// Writes are synchronized under <see cref="Sync"/>; reads use volatile semantics so
    /// normal logging does not serialize on the configuration lock.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when setting to <c>null</c>.
    /// </exception>
    public static ILogSink Sink
    {
        get
        {
            return Volatile.Read(ref _sink);
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (Sync)
            {
                if (ReferenceEquals(_sink, value))
                {
                    return;
                }

                if (_sink is IDisposable disposable)
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

                _sink = value;
            }
        }
    }

    /// <summary>
    /// Configures the logging system.
    /// Should be called once at application startup before any log entries are emitted.
    /// </summary>
    /// <param name="minimumLevel">
    /// Optional minimum log level.
    /// If <c>null</c>, the current level is preserved.
    /// </param>
    /// <param name="sink">
    /// Optional log sink.
    /// If <c>null</c>, the current sink is preserved.
    /// </param>
    public static void Configure(LogLevel? minimumLevel = null, ILogSink? sink = null)
    {
        lock (Sync)
        {
            if (minimumLevel.HasValue)
            {
                _minimumLevel = minimumLevel.Value;
            }

            if (sink is not null)
            {
                ArgumentNullException.ThrowIfNull(sink);

                if (!ReferenceEquals(_sink, sink))
                {
                    if (_sink is IDisposable disposable)
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

                    _sink = sink;
                }
            }
        }
    }

    /// <summary>
    /// Updates the minimum log level at runtime and logs a confirmation message.
    /// </summary>
    /// <param name="level">The new minimum log level to apply.</param>
    public static void SetLogLevel(LogLevel level)
    {
        _minimumLevel = level;
        DualSenseClientLogger logger = For("App");
        logger.Info($"Logging level updated: {level}");
    }

    /// <summary>
    /// Disposes the active sink if it implements <see cref="IDisposable"/> and flushes
    /// any buffered log entries to disk. Should be called during application shutdown.
    /// </summary>
    /// <remarks>
    /// After calling this method, logging continues to work for non-disposable sinks
    /// (e.g., <see cref="ConsoleLogSink"/>), but file sinks will no longer write.
    /// </remarks>
    public static void Shutdown()
    {
        lock (Sync)
        {
            if (_sink is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _sink = new NoOpLogSink();
        }
    }

    /// <summary>
    /// A no-op sink that silently discards all log entries.
    /// Used as the default sink and as a replacement after <see cref="Shutdown"/>.
    /// </summary>
    private sealed class NoOpLogSink : ILogSink
    {
        public void Write(in LogEntry entry)
        {
        }
    }

    /// <summary>
    /// Returns a per-category <see cref="DualSenseClientLogger"/> instance.
    /// Logger instances are cached by category name, so repeated calls with the same
    /// category return the same instance.
    /// </summary>
    /// <param name="category">
    /// A string identifying the logging category (e.g., "Settings", "DI", "Localization").
    /// Typically, the application or module name is used as the category.
    /// </param>
    /// <returns>
    /// A cached <see cref="DualSenseClientLogger"/> for the specified category.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="category"/> is null or whitespace.
    /// </exception>
    public static DualSenseClientLogger For(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        return Loggers.GetOrAdd(category, static key => new DualSenseClientLogger(key));
    }

    /// <summary>
    /// Attempts to parse a string into a <see cref="LogLevel"/>.
    /// Supports standard enum names and common aliases
    /// ("warn" for Warning, "fatal" for Critical).
    /// Parsing is case-insensitive.
    /// </summary>
    /// <param name="text">
    /// The string to parse (e.g., "Info", "warning", "3").
    /// </param>
    /// <param name="level">
    /// When this method returns <c>true</c>, contains the parsed <see cref="LogLevel"/>.
    /// </param>
    /// <returns>
    /// <c>true</c> if the string was successfully parsed; otherwise, <c>false</c>.
    /// </returns>
    public static bool TryParseLevel(string? text, out LogLevel level)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            level = default;
            return false;
        }

        string normalized = text.Trim();
        if (Enum.TryParse<LogLevel>(normalized, true, out level) && Enum.IsDefined(level) && level != LogLevel.None)
        {
            return true;
        }

        if (string.Equals(normalized, "warn", StringComparison.OrdinalIgnoreCase))
        {
            level = LogLevel.Warning;
            return true;
        }

        if (string.Equals(normalized, "fatal", StringComparison.OrdinalIgnoreCase))
        {
            level = LogLevel.Critical;
            return true;
        }

        level = default;
        return false;
    }

    /// <summary>
    /// Determines whether a log entry at the given level would be emitted.
    /// </summary>
    /// <param name="level">The log level to check.</param>
    /// <returns>
    /// <c>true</c> if <paramref name="level"/> meets or exceeds <see cref="MinimumLevel"/>;
    /// otherwise, <c>false</c>. When <see cref="MinimumLevel"/> is <see cref="LogLevel.None"/>,
    /// this always returns <c>false</c>, acting as a global kill-switch.
    /// </returns>
    internal static bool IsLevelEnabled(LogLevel level) => level >= _minimumLevel;

    /// <summary>
    /// Constructs a <see cref="LogEntry"/> and writes it to the active sink.
    /// Entries below <see cref="MinimumLevel"/> are silently dropped here, so the
    /// minimum level applies uniformly to every sink in the hierarchy.
    /// </summary>
    internal static void Write(LogLevel level,
        string category, string message, Exception? exception,
        string sourceFilePath, int sourceLine, string sourceMemberName)
    {
        if (!IsLevelEnabled(level))
        {
            return;
        }

        LogEntry entry = new LogEntry(DateTimeOffset.Now, level, category, message,
            Path.GetFileName(sourceFilePath), sourceLine, sourceMemberName,
            exception, Environment.CurrentManagedThreadId, Thread.CurrentThread.Name ?? "");

        ILogSink sink = Volatile.Read(ref _sink);
        sink.Write(in entry);
    }

    // --- Per-category logger instance ---

    /// <summary>
    /// Initializes a new logger instance for the specified category.
    /// Instances should be obtained via <see cref="For"/> rather than constructed directly.
    /// </summary>
    /// <param name="category">The category name for this logger.</param>
    private DualSenseClientLogger(string category)
    {
        Category = category;
    }

    /// <summary>
    /// Gets the category name associated with this logger instance.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Determines whether an entry at the given level would be emitted.
    /// </summary>
    /// <param name="level">The level to check.</param>
    /// <returns>
    /// <c>true</c> when the level meets the current minimum; otherwise, <c>false</c>.
    /// </returns>
    public bool IsEnabled(LogLevel level) => IsLevelEnabled(level);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Trace"/> with automatic caller context capture.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="exception">An optional exception to include with the log entry.</param>
    /// <param name="sourceFilePath">Automatically captured source file path.</param>
    /// <param name="sourceLine">Automatically captured source line number.</param>
    /// <param name="sourceMemberName">Automatically captured caller member name.</param>
    public void Trace(
        string message,
        Exception? exception = null,
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string sourceMemberName = "")
        => Write(LogLevel.Trace, Category, message, exception, sourceFilePath, sourceLine, sourceMemberName);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Debug"/> with automatic caller context capture.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="exception">An optional exception to include with the log entry.</param>
    /// <param name="sourceFilePath">Automatically captured source file path.</param>
    /// <param name="sourceLine">Automatically captured source line number.</param>
    /// <param name="sourceMemberName">Automatically captured caller member name.</param>
    public void Debug(
        string message,
        Exception? exception = null,
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string sourceMemberName = "")
        => Write(LogLevel.Debug, Category, message, exception, sourceFilePath, sourceLine, sourceMemberName);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Info"/> with automatic caller context capture.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="exception">An optional exception to include with the log entry.</param>
    /// <param name="sourceFilePath">Automatically captured source file path.</param>
    /// <param name="sourceLine">Automatically captured source line number.</param>
    /// <param name="sourceMemberName">Automatically captured caller member name.</param>
    public void Info(
        string message,
        Exception? exception = null,
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string sourceMemberName = "")
        => Write(LogLevel.Info, Category, message, exception, sourceFilePath, sourceLine, sourceMemberName);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Warning"/> with automatic caller context capture.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="exception">An optional exception to include with the log entry.</param>
    /// <param name="sourceFilePath">Automatically captured source file path.</param>
    /// <param name="sourceLine">Automatically captured source line number.</param>
    /// <param name="sourceMemberName">Automatically captured caller member name.</param>
    public void Warning(
        string message,
        Exception? exception = null,
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string sourceMemberName = "")
        => Write(LogLevel.Warning, Category, message, exception, sourceFilePath, sourceLine, sourceMemberName);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Error"/> with automatic caller context capture.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="exception">An optional exception to include with the log entry.</param>
    /// <param name="sourceFilePath">Automatically captured source file path.</param>
    /// <param name="sourceLine">Automatically captured source line number.</param>
    /// <param name="sourceMemberName">Automatically captured caller member name.</param>
    public void Error(
        string message,
        Exception? exception = null,
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string sourceMemberName = "")
        => Write(LogLevel.Error, Category, message, exception, sourceFilePath, sourceLine, sourceMemberName);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Critical"/> with automatic caller context capture.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="exception">An optional exception to include with the log entry.</param>
    /// <param name="sourceFilePath">Automatically captured source file path.</param>
    /// <param name="sourceLine">Automatically captured source line number.</param>
    /// <param name="sourceMemberName">Automatically captured caller member name.</param>
    public void Critical(
        string message,
        Exception? exception = null,
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string sourceMemberName = "")
        => Write(LogLevel.Critical, Category, message, exception, sourceFilePath, sourceLine, sourceMemberName);

    /// <summary>
    /// Logs an exception report as a single entry at the specified level, including the
    /// exception type, message, and full exception chain.
    /// </summary>
    /// <param name="ex">The exception to log.</param>
    /// <param name="includeEnvironmentInfo">
    /// If <c>true</c>, appends machine name, OS version, .NET runtime, process architecture,
    /// and current directory to the message.
    /// </param>
    /// <param name="level">
    /// The level for the report. Defaults to <see cref="LogLevel.Error"/>; use
    /// <see cref="LogLevel.Critical"/> for unhandled or terminating exceptions.
    /// </param>
    /// <param name="memberName">Automatically captured caller member name.</param>
    /// <param name="filePath">Automatically captured caller file path.</param>
    /// <param name="lineNumber">Automatically captured caller line number.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ex"/> is null.</exception>
    public void LogExceptionDetails(Exception ex, bool includeEnvironmentInfo = false, LogLevel level = LogLevel.Error,
        [CallerMemberName] string? memberName = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (!IsLevelEnabled(level))
        {
            return;
        }

        StringBuilder message = new StringBuilder();
        message.Append(ex.GetType().FullName);
        message.Append(": ");
        message.Append(ex.Message);

        if (includeEnvironmentInfo)
        {
            message.AppendLine();
            message.AppendLine("=== System Information ===");
            message.Append("Machine Name: ");
            message.AppendLine(Environment.MachineName);
            message.Append("OS Version: ");
            message.AppendLine(Environment.OSVersion.ToString());
            message.Append(".NET Runtime: ");
            message.AppendLine(RuntimeInformation.FrameworkDescription);
            message.Append("Process Architecture: ");
            message.AppendLine(RuntimeInformation.ProcessArchitecture.ToString());
            message.Append("Current Directory: ");
            message.Append(Environment.CurrentDirectory);
        }

        Write(level, Category, message.ToString(), ex,
            filePath != null ? Path.GetFileName(filePath) : "", lineNumber, memberName ?? "");
    }
}