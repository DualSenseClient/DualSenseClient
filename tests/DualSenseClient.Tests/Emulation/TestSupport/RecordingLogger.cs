using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Emulation.TestSupport;

/// <summary>
/// <see cref="IEmulationLogger"/> test double recording every message in logging order; the
/// recorded entries are readable from the test thread while sessions log from their own threads.
/// The session tests assert on <see cref="ErrorEntries"/> to prove that no session fault was
/// swallowed silently.
/// </summary>
public sealed class RecordingLogger : IEmulationLogger
{
    private readonly object _lock = new object();

    private readonly List<RecordedLogEntry> _entries = new List<RecordedLogEntry>();

    /// <summary>Snapshot of every message logged so far, in logging order.</summary>
    public IReadOnlyList<RecordedLogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>Snapshot of the messages logged at <see cref="EmulationLogLevel.Error"/>.</summary>
    public IReadOnlyList<RecordedLogEntry> ErrorEntries
    {
        get
        {
            lock (_lock)
            {
                List<RecordedLogEntry> errors = new List<RecordedLogEntry>();
                foreach (RecordedLogEntry entry in _entries)
                {
                    if (entry.Level == EmulationLogLevel.Error)
                    {
                        errors.Add(entry);
                    }
                }

                return errors.ToArray();
            }
        }
    }

    /// <summary>Records one message.</summary>
    /// <param name="level">Severity of the message.</param>
    /// <param name="message">Message text.</param>
    public void Log(EmulationLogLevel level, string message)
    {
        lock (_lock)
        {
            _entries.Add(new RecordedLogEntry(level, message));
        }
    }
}

/// <summary>One message captured by <see cref="RecordingLogger"/>.</summary>
public readonly struct RecordedLogEntry
{
    /// <summary>
    /// Initializes an entry with the severity and text of one logged message.
    /// </summary>
    /// <param name="level">Severity of the message.</param>
    /// <param name="message">Message text.</param>
    public RecordedLogEntry(EmulationLogLevel level, string message)
    {
        Level = level;
        Message = message;
    }

    /// <summary>Severity of the message.</summary>
    public EmulationLogLevel Level { get; }

    /// <summary>Message text.</summary>
    public string Message { get; }
}