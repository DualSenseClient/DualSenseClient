using System;
using DualSenseClient.Core.Utilities;
using DualSenseClient.Logging;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Configures the application log before dependency injection and settings are available,
/// then lets the application apply the persisted log level without replacing the sink.
/// </summary>
internal static class LoggingSetup
{
    /// <summary>
    /// The live file sink created during bootstrap, or <c>null</c> before initialization.
    /// </summary>
    private static FileLogSink? _fileSink;

    /// <summary>
    /// Gets the full path used for the application log file.
    /// </summary>
    public static string LogPath
    {
        get
        {
            return PathResolver.GetFullPath("Logs", "DualSenseClient.log");
        }
    }

    /// <summary>
    /// Gets the full path of the log file currently being written, or <c>null</c> when
    /// logging has not been initialized.
    /// </summary>
    public static string? CurrentLogFilePath
    {
        get
        {
            return _fileSink?.CurrentFilePath;
        }
    }

    /// <summary>
    /// Creates the console and file sinks and configures the logger at the bootstrap level.
    /// Calling this again only updates the minimum level and keeps the existing sink.
    /// </summary>
    /// <param name="level">The bootstrap minimum level. Defaults to <see cref="LogLevel.Info"/>.</param>
    public static void Initialize(LogLevel level = LogLevel.Info)
    {
        if (_fileSink is not null)
        {
            SetLevel(level);
            return;
        }

        try
        {
            _fileSink = new FileLogSink(LogPath);
            DualSenseClientLogger.Configure(level,
                new CompositeLogSink(new ConsoleLogSink(), _fileSink));
        }
        catch (Exception ex)
        {
            // File logging is best-effort: a bad path or an unavailable log directory
            // must never stop the application from starting.
            _fileSink = null;
            DualSenseClientLogger.Configure(level, new ConsoleLogSink());
            DualSenseClientLogger.For(nameof(LoggingSetup))
                .Warning("File logging is unavailable; continuing with console output only", ex);
        }
    }

    /// <summary>
    /// Applies a new minimum level while keeping the existing sink, so the file handle
    /// opened during bootstrap stays valid.
    /// </summary>
    /// <param name="level">The minimum level to apply.</param>
    public static void SetLevel(LogLevel level) => DualSenseClientLogger.Configure(level);
}