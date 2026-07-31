using System.Text;

namespace DualSenseClient.Logging;

/// <summary>
/// A log sink that writes entries to date-rotated log files with periodic flushing.
/// Each day creates a new file named <c>{baseName}-{date}.log</c> (e.g., <c>DualSenseClient-2026-07-24.log</c>).
/// Supports append mode, automatic directory creation, and UTF-8 encoding.
/// </summary>
/// <remarks>
/// The file is flushed to disk automatically every 500ms via a background timer,
/// and immediately when an entry at <see cref="LogLevel.Error"/> or above is written.
/// This balances performance with data durability.
/// </remarks>
public sealed class FileLogSink : ILogSink, IDisposable
{
    /// <summary>
    /// How often buffered log entries are flushed to disk.
    /// </summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Synchronizes access to the log sink state.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// The base file name without extension.
    /// </summary>
    private readonly string _baseName;

    /// <summary>
    /// The directory log files are written to.
    /// </summary>
    private readonly string _directory;

    /// <summary>
    /// The file extension, defaulting to <c>.log</c> when none is given.
    /// </summary>
    private readonly string _extension;

    /// <summary>
    /// Whether log entries are appended to existing files.
    /// </summary>
    private readonly bool _append;

    /// <summary>
    /// Whether each log entry is prefixed with a timestamp.
    /// </summary>
    private bool _includeTimestamp;

    /// <summary>
    /// Whether a new log file is created each day.
    /// </summary>
    private readonly bool _rotateDaily;

    /// <summary>
    /// Background timer that periodically flushes buffered entries to disk.
    /// </summary>
    private readonly Timer _flushTimer;

    /// <summary>
    /// The active file writer, or <c>null</c> when no file is open.
    /// </summary>
    private StreamWriter? _writer;

    /// <summary>
    /// The date of the currently open log file.
    /// </summary>
    private DateOnly _currentDate;

    /// <summary>
    /// True once the sink has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileLogSink"/> class.
    /// </summary>
    /// <param name="path">
    /// The path to the log file. When rotation is enabled, the date is inserted before the extension
    /// (e.g., <c>"app.log"</c> produces <c>app-2026-07-24.log</c>). When rotation is disabled,
    /// this is used as-is. Parent directories are created if they do not exist.
    /// </param>
    /// <param name="append">
    /// If <c>true</c>, appends to the existing file;
    /// otherwise, truncates and creates a new file.
    /// </param>
    /// <param name="includeTimestamp">
    /// If <c>true</c>, each log entry is prefixed with
    /// a <c>[yyyy-MM-dd HH:mm:ss.fff]</c> timestamp.
    /// </param>
    /// <param name="rotateDaily">
    /// If <c>true</c> (default), creates a new log file each day with the date appended to the filename.
    /// If <c>false</c>, writes to a single file without rotation.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/> is null or whitespace.
    /// </exception>
    public FileLogSink(string path, bool append = true, bool includeTimestamp = true, bool rotateDaily = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _baseName = Path.GetFileNameWithoutExtension(path);
        _extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(_extension))
        {
            _extension = ".log";
        }

        string? directory = Path.GetDirectoryName(path);
        _directory = string.IsNullOrEmpty(directory) ? "." : directory;
        if (!Directory.Exists(_directory))
        {
            Directory.CreateDirectory(_directory);
        }

        _append = append;
        _includeTimestamp = includeTimestamp;
        _rotateDaily = rotateDaily;
        _currentDate = DateOnly.FromDateTime(DateTime.Today);

        OpenWriter(_currentDate);

        _flushTimer = new Timer(
            static state => ((FileLogSink)state!).FlushBuffered(),
            this,
            FlushInterval,
            FlushInterval);
    }

    /// <summary>
    /// Gets or sets whether each log entry is prefixed with a timestamp.
    /// </summary>
    public bool IncludeTimestamp
    {
        get => _includeTimestamp;
        set => _includeTimestamp = value;
    }

    /// <summary>
    /// Writes a log entry to the current day's file. Rotates to a new file if the date has changed.
    /// The file is flushed immediately for entries at <see cref="LogLevel.Error"/> or above.
    /// </summary>
    /// <param name="entry">The log entry to write.</param>
    public void Write(in LogEntry entry)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (_rotateDaily)
            {
                DateOnly today = DateOnly.FromDateTime(entry.Timestamp.Date);
                if (today != _currentDate)
                {
                    Rotate(today);
                }
            }

            if (_writer is null)
            {
                return;
            }

            if (IncludeTimestamp)
            {
                _writer.Write('[');
                _writer.Write(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                _writer.Write(']');
            }

            _writer.Write('[');
            _writer.Write(entry.Level.ToLevelLabel());
            _writer.Write(']');
            _writer.Write('[');
            _writer.Write(entry.Category);
            _writer.Write(']');
            _writer.Write(' ');

            _writer.Write(entry.SourceFileName);
            if (entry.SourceLine > 0)
            {
                _writer.Write(':');
                _writer.Write(entry.SourceLine);
            }

            _writer.Write(' ');
            _writer.WriteLine(entry.Message);

            if (entry.Exception is not null)
            {
                _writer.WriteLine(entry.Exception);
            }

            if (entry.Level >= LogLevel.Error)
            {
                _writer.Flush();
            }
        }
    }

    /// <summary>
    /// Flushes buffered log entries to disk.
    /// Called periodically by the background timer.
    /// </summary>
    private void FlushBuffered()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _writer?.Flush();
        }
    }

    /// <summary>
    /// Rotates to a new log file for the given date, flushing and disposing the previous writer.
    /// If opening the new file fails, the sink enters a broken state and silently drops entries.
    /// </summary>
    private void Rotate(DateOnly newDate)
    {
        CloseWriter();
        try
        {
            OpenWriter(newDate);
            _currentDate = newDate;
        }
        catch
        {
            _writer = null;
        }
    }

    /// <summary>
    /// Opens a new <see cref="StreamWriter"/> for the specified date.
    /// </summary>
    private void OpenWriter(DateOnly date)
    {
        string fileName = _rotateDaily
            ? $"{_baseName}-{date:yyyy-MM-dd}{_extension}"
            : $"{_baseName}{_extension}";
        string fullPath = Path.Combine(_directory, fileName);

        FileStream fileStream = new FileStream(
            fullPath,
            _append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 65536,
            FileOptions.SequentialScan);
        _writer = new StreamWriter(fileStream, Encoding.UTF8, bufferSize: 65536)
        {
            AutoFlush = false
        };
    }

    /// <summary>
    /// Flushes and disposes the current <see cref="StreamWriter"/>.
    /// </summary>
    private void CloseWriter()
    {
        if (_writer is not null)
        {
            _writer.Flush();
            _writer.Dispose();
            _writer = null;
        }
    }

    /// <summary>
    /// Disposes the flush timer, flushes remaining entries, and closes the current log file.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _flushTimer.Dispose();
            CloseWriter();
        }
    }
}