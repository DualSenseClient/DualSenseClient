using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

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
/// <see cref="CompositeLogSink"/> with a <see cref="ConsoleLogSink"/> wrapped in a
/// <see cref="MinimumLevelFilterSink"/> and an unfiltered <see cref="FileLogSink"/>).
/// This ensures the file captures all levels for diagnostics while the console
/// respects the configured minimum level.
/// </para>
/// <para>
/// Throughout the application, obtain a logger via <see cref="For"/> and call level-specific
/// methods. Caller file, line, and member name are captured automatically via compiler attributes.
/// </para>
/// <code>
/// // At startup:
/// DualSenseClientLogger.Configure(LogLevel.Info,
///     new CompositeLogSink(
///         new MinimumLevelFilterSink(new ConsoleLogSink(), () => DualSenseClientLogger.MinimumLevel),
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
    private static readonly ConcurrentDictionary<string, DualSenseClientLogger> Loggers = new ConcurrentDictionary<string, DualSenseClientLogger>(StringComparer.Ordinal);

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
    private static ILogSink _sink = new ConsoleLogSink(useColors: true);

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
        get => _minimumLevel;
        set => _minimumLevel = value;
    }

    /// <summary>
    /// Gets or sets the active <see cref="ILogSink"/> that receives all log entries.
    /// </summary>
    /// <remarks>
    /// Replacing the sink automatically disposes the previous sink
    /// if it implements <see cref="IDisposable"/>.
    /// All access is synchronized via <see cref="Sync"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when setting to <c>null</c>.
    /// </exception>
    public static ILogSink Sink
    {
        get
        {
            lock (Sync)
            {
                return _sink;
            }
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
        if (Enum.TryParse<LogLevel>(normalized, ignoreCase: true, out level) && Enum.IsDefined(level) && level != LogLevel.None)
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
    /// Determines whether any logging output is active.
    /// </summary>
    /// <remarks>
    /// Per-sink level filtering is handled by <see cref="MinimumLevelFilterSink"/>.
    /// This method only returns <c>false</c> when <see cref="MinimumLevel"/> is
    /// <see cref="LogLevel.None"/>, which acts as a global kill-switch that bypasses
    /// all sinks entirely.
    /// </remarks>
    /// <param name="level">The log level to check (unused by the current implementation).</param>
    /// <returns>
    /// <c>true</c> if logging is active; otherwise, <c>false</c>.
    /// </returns>
    internal static bool IsEnabled(LogLevel level) => _minimumLevel != LogLevel.None;

    /// <summary>
    /// Constructs a <see cref="LogEntry"/> and writes it to the active sink.
    /// Entries are only blocked when <see cref="_minimumLevel"/> is
    /// <see cref="LogLevel.None"/>; all other entries pass through to the sink
    /// hierarchy where <see cref="MinimumLevelFilterSink"/> instances enforce
    /// per-sink filtering.
    /// </summary>
    internal static void Write(LogLevel level,
        string category, string message, Exception? exception,
        string sourceFilePath, int sourceLine, string sourceMemberName)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        LogEntry entry = new LogEntry(DateTimeOffset.Now, level, category, message,
            Path.GetFileName(sourceFilePath), sourceLine, sourceMemberName,
            exception);

        ILogSink sink;
        lock (Sync)
        {
            sink = _sink;
        }

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
    /// Logs a message at <see cref="LogLevel.Trace"/> with automatic caller context capture.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="sourceFilePath">Automatically captured source file path.</param>
    /// <param name="sourceLine">Automatically captured source line number.</param>
    /// <param name="sourceMemberName">Automatically captured caller member name.</param>
    public void Trace(
        string message,
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string sourceMemberName = "")
        => Write(LogLevel.Trace, Category, message, exception: null, sourceFilePath, sourceLine, sourceMemberName);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Debug"/> with automatic caller context capture.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="sourceFilePath">Automatically captured source file path.</param>
    /// <param name="sourceLine">Automatically captured source line number.</param>
    /// <param name="sourceMemberName">Automatically captured caller member name.</param>
    public void Debug(
        string message,
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string sourceMemberName = "")
        => Write(LogLevel.Debug, Category, message, exception: null, sourceFilePath, sourceLine, sourceMemberName);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Info"/> with automatic caller context capture.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="sourceFilePath">Automatically captured source file path.</param>
    /// <param name="sourceLine">Automatically captured source line number.</param>
    /// <param name="sourceMemberName">Automatically captured caller member name.</param>
    public void Info(
        string message,
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string sourceMemberName = "")
        => Write(LogLevel.Info, Category, message, exception: null, sourceFilePath, sourceLine, sourceMemberName);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Warning"/> with automatic caller context capture.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="sourceFilePath">Automatically captured source file path.</param>
    /// <param name="sourceLine">Automatically captured source line number.</param>
    /// <param name="sourceMemberName">Automatically captured caller member name.</param>
    public void Warning(
        string message,
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string sourceMemberName = "")
        => Write(LogLevel.Warning, Category, message, exception: null, sourceFilePath, sourceLine, sourceMemberName);

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
    /// Logs a detailed exception report at <see cref="LogLevel.Critical"/> level, including
    /// the full exception chain, stack traces, and optional system environment information.
    /// </summary>
    /// <param name="ex">The exception to log.</param>
    /// <param name="includeEnvironmentInfo">
    /// If <c>true</c>, includes machine name, OS version, .NET runtime, process architecture,
    /// and current directory in the report.
    /// </param>
    /// <param name="memberName">Automatically captured caller member name.</param>
    /// <param name="filePath">Automatically captured caller file path.</param>
    /// <param name="lineNumber">Automatically captured caller line number.</param>
    public void LogExceptionDetails(Exception ex, bool includeEnvironmentInfo = true,
        [CallerMemberName] string? memberName = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        string context = FormatContext(memberName, filePath, lineNumber);
        string sourceFileName = filePath != null ? Path.GetFileName(filePath) : "";
        WriteCritical($"[{context}] ===== Exception Report Start =====", sourceFileName, lineNumber, memberName);
        WriteCritical($"[{context}] Timestamp (local): {DateTime.Now:O}", sourceFileName, lineNumber, memberName);

        LogExceptionWithDepth(ex, context, sourceFileName, lineNumber, memberName);

        if (includeEnvironmentInfo)
        {
            WriteCritical($"[{context}] === System Information ===", sourceFileName, lineNumber, memberName);
            WriteCritical($"[{context}] Machine Name: {Environment.MachineName}", sourceFileName, lineNumber, memberName);
            WriteCritical($"[{context}] OS Version: {Environment.OSVersion}", sourceFileName, lineNumber, memberName);
            WriteCritical($"[{context}] .NET Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}", sourceFileName, lineNumber, memberName);
            WriteCritical($"[{context}] Process Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}", sourceFileName, lineNumber, memberName);
            WriteCritical($"[{context}] Current Directory: {Environment.CurrentDirectory}", sourceFileName, lineNumber, memberName);
        }

        WriteCritical($"[{context}] ===== Exception Report End =====", sourceFileName, lineNumber, memberName);
    }

    /// <summary>
    /// Formats caller context into a standardized <c>FileName.MemberName:LineNumber</c> string
    /// for inclusion in log output.
    /// </summary>
    private static string FormatContext(string? memberName, string? filePath, int lineNumber)
    {
        if (string.IsNullOrEmpty(memberName))
        {
            return "Unknown";
        }

        string fileName = filePath != null
            ? Path.GetFileNameWithoutExtension(filePath)
            : "Unknown";

        return lineNumber > 0
            ? $"{fileName}.{memberName}:{lineNumber}"
            : $"{fileName}.{memberName}";
    }

    /// <summary>
    /// Writes a Critical-level log entry directly, bypassing caller attribute capture
    /// to preserve the correct source location from the original caller.
    /// </summary>
    private void WriteCritical(string message, string sourceFileName, int sourceLine, string? sourceMemberName)
    {
        Write(LogLevel.Critical, Category, message, exception: null,
            sourceFileName, sourceLine, sourceMemberName ?? "");
    }

    /// <summary>
    /// Recursively logs each level of an exception chain with indented detail,
    /// including type, message, source, stack trace, data entries, and inner exceptions.
    /// </summary>
    /// <param name="ex">The exception to log.</param>
    /// <param name="context">The caller context prefix for each line.</param>
    /// <param name="sourceFileName">Source file name for log entry metadata.</param>
    /// <param name="sourceLine">Source line number for log entry metadata.</param>
    /// <param name="sourceMemberName">Source member name for log entry metadata.</param>
    /// <param name="depth">The current depth in the inner exception chain.</param>
    private void LogExceptionWithDepth(Exception ex, string? context, string sourceFileName, int sourceLine, string? sourceMemberName, int depth = 0)
    {
        while (true)
        {
            string indent = new string(' ', depth * 2);
            string prefix = context != null ? $"[{context}] " : "";

            WriteCritical($"{prefix}{indent}Exception Level: {depth}", sourceFileName, sourceLine, sourceMemberName);
            WriteCritical($"{prefix}{indent}Type: {ex.GetType().FullName}", sourceFileName, sourceLine, sourceMemberName);
            WriteCritical($"{prefix}{indent}Message: {ex.Message}", sourceFileName, sourceLine, sourceMemberName);
            WriteCritical($"{prefix}{indent}Source: {ex.Source}", sourceFileName, sourceLine, sourceMemberName);
            WriteCritical($"{prefix}{indent}HResult: {ex.HResult}", sourceFileName, sourceLine, sourceMemberName);
            if (ex.HelpLink != null)
            {
                WriteCritical($"{prefix}{indent}Help Link: {ex.HelpLink}", sourceFileName, sourceLine, sourceMemberName);
            }

            if (ex.Data.Count > 0)
            {
                WriteCritical($"{prefix}{indent}Data:", sourceFileName, sourceLine, sourceMemberName);
                foreach (object? key in ex.Data.Keys)
                {
                    WriteCritical($"{prefix}{indent}  {key}: {ex.Data[key]}", sourceFileName, sourceLine, sourceMemberName);
                }
            }

            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                WriteCritical($"{prefix}{indent}StackTrace:", sourceFileName, sourceLine, sourceMemberName);
                foreach (string line in ex.StackTrace.Split(Environment.NewLine))
                {
                    WriteCritical($"{prefix}{indent}  {line}", sourceFileName, sourceLine, sourceMemberName);
                }
            }

            if (ex.TargetSite != null)
            {
                WriteCritical($"{prefix}{indent}TargetSite: {ex.TargetSite}", sourceFileName, sourceLine, sourceMemberName);
            }

            if (ex.InnerException != null)
            {
                WriteCritical($"{prefix}{indent}--- Inner Exception ---", sourceFileName, sourceLine, sourceMemberName);
                ex = ex.InnerException;
                depth = depth + 1;
                continue;
            }
            break;
        }
    }
}