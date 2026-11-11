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
    /// Minimum delay between attempts to reopen a log file after an open or rotation failure.
    /// </summary>
    private static readonly TimeSpan ReopenRetryInterval = TimeSpan.FromSeconds(5);

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
    /// The full path of the currently open log file, or <c>null</c> when no file is open.
    /// </summary>
    private string? _currentFilePath;

    /// <summary>
    /// The time of the last open attempt, used to throttle retries after a failure.
    /// </summary>
    private DateTimeOffset _lastOpenAttempt;

    /// <summary>
    /// Provides the clock used for retry throttling and initial date selection.
    /// </summary>
    private readonly TimeProvider _timeProvider;

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
    /// <param name="timeProvider">
    /// The clock used for retry throttling and the initial date. Defaults to <see cref="TimeProvider.System"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/> is null or whitespace.
    /// </exception>
    public FileLogSink(string path, bool append = true, bool includeTimestamp = true, bool rotateDaily = true,
        TimeProvider? timeProvider = null)
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

        _append = append;
        _includeTimestamp = includeTimestamp;
        _rotateDaily = rotateDaily;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _currentDate = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

        try
        {
            OpenWriter(_currentDate);
        }
        catch
        {
            // Logging must never prevent the application from starting. The writer stays
            // null and the next write retries after ReopenRetryInterval.
            _writer = null;
            _currentFilePath = null;
        }

        _lastOpenAttempt = _timeProvider.GetUtcNow();

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
        get
        {
            return _includeTimestamp;
        }
        set
        {
            _includeTimestamp = value;
        }
    }

    /// <summary>
    /// Gets the full path of the log file currently being written, or <c>null</c>
    /// when no file is open.
    /// </summary>
    public string? CurrentFilePath
    {
        get
        {
            lock (_sync)
            {
                return _currentFilePath;
            }
        }
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

            if (_writer is null && _timeProvider.GetUtcNow() - _lastOpenAttempt >= ReopenRetryInterval)
            {
                _lastOpenAttempt = _timeProvider.GetUtcNow();
                DateOnly targetDate = _rotateDaily ? DateOnly.FromDateTime(entry.Timestamp.Date) : _currentDate;
                try
                {
                    OpenWriter(targetDate);
                    _currentDate = targetDate;
                }
                catch
                {
                    _writer = null;
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
            WriteThreadSegment(_writer, in entry);

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
    /// Writes the thread segment <c>[TId]</c> or <c>[TId:Name]</c> to the writer,
    /// when the entry carries a thread identifier.
    /// </summary>
    private static void WriteThreadSegment(TextWriter writer, in LogEntry entry)
    {
        if (entry.ThreadId <= 0)
        {
            return;
        }

        writer.Write("[T");
        writer.Write(entry.ThreadId);
        if (!string.IsNullOrEmpty(entry.ThreadName))
        {
            writer.Write(':');
            writer.Write(entry.ThreadName);
        }

        writer.Write(']');
        writer.Write(' ');
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
    /// When the previous open failed, rotation attempts are throttled by
    /// <see cref="ReopenRetryInterval"/>; a failed open leaves the writer null and a later
    /// write retries.
    /// </summary>
    private void Rotate(DateOnly newDate)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_writer is null && now - _lastOpenAttempt < ReopenRetryInterval)
        {
            return;
        }

        _lastOpenAttempt = now;
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

        _currentFilePath = null;
        Directory.CreateDirectory(_directory);

        FileStream fileStream = new FileStream(
            fullPath,
            _append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            65536,
            FileOptions.SequentialScan);
        _writer = new StreamWriter(fileStream, Encoding.UTF8, 65536)
        {
            AutoFlush = false
        };
        _currentFilePath = Path.GetFullPath(fullPath);
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

        _currentFilePath = null;
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