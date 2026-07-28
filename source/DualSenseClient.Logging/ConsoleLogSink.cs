namespace DualSenseClient.Logging;

/// <summary>
/// A log sink that writes entries to the standard console output streams.
/// Entries at <see cref="LogLevel.Error"/> or above are written to <see cref="Console.Error"/>;
/// all other levels are written to <see cref="Console.Out"/>.
/// </summary>
/// <remarks>
/// All writes are synchronized via a lock to prevent interleaved output from concurrent threads.
/// When color mode is enabled, the level label is rendered in a color that matches its severity.
/// </remarks>
public sealed class ConsoleLogSink : ILogSink
{
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleLogSink"/> class.
    /// </summary>
    /// <param name="useColors">
    /// If <c>true</c>, level labels are rendered using <see cref="Console.ForegroundColor"/> color coding.
    /// Set to <c>false</c> when output is redirected to a file or pipe.
    /// </param>
    public ConsoleLogSink(bool useColors = true)
    {
        UseColors = useColors;
    }

    /// <summary>
    /// Gets or sets whether log level labels are rendered with color.
    /// </summary>
    public bool UseColors { get; set; }

    /// <summary>
    /// Writes a log entry to the console.
    /// Output format: <c>[LEVEL][Category] SourceFile:Line Message</c>
    /// </summary>
    /// <param name="entry">The log entry to write.</param>
    public void Write(in LogEntry entry)
    {
        lock (_sync)
        {
            TextWriter writer = entry.Level >= LogLevel.Error ? Console.Error : Console.Out;

            string levelLabel = entry.Level.ToLevelLabel();
            WriteLevelSegment(writer, levelLabel, entry.Level);
            writer.Write('[');
            writer.Write(entry.Category);
            writer.Write(']');
            writer.Write(' ');
            writer.Write(entry.SourceFileName);
            if (entry.SourceLine > 0)
            {
                writer.Write(':');
                writer.Write(entry.SourceLine);
            }

            writer.Write(' ');
            writer.Write(entry.Message);
            writer.WriteLine();
            if (entry.Exception is not null)
            {
                writer.WriteLine(entry.Exception);
            }
        }
    }

    /// <summary>
    /// Writes the level label segment <c>[LABEL]</c> to the writer, optionally with color.
    /// </summary>
    private void WriteLevelSegment(TextWriter writer, string label, LogLevel level)
    {
        if (!UseColors)
        {
            writer.Write('[');
            writer.Write(label);
            writer.Write(']');
            return;
        }

        ConsoleColor originalColor = Console.ForegroundColor;
        Console.ForegroundColor = GetLevelColor(level);
        writer.Write('[');
        writer.Write(label);
        writer.Write(']');
        Console.ForegroundColor = originalColor;
    }

    /// <summary>
    /// Maps a <see cref="LogLevel"/> to a <see cref="ConsoleColor"/> for colorized output.
    /// </summary>
    private static ConsoleColor GetLevelColor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => ConsoleColor.DarkGray,
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Info => ConsoleColor.Blue,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Critical => ConsoleColor.DarkRed,
            _ => ConsoleColor.White,
        };
    }
}