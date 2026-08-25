using DualSenseClient.Logging;

namespace DualSenseClient.Tests.Logging;

public class LogEntryTests
{
    [Test]
    public void LogEntry_CreatesWithAllProperties()
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        Exception ex = new InvalidOperationException("test");

        LogEntry entry = new LogEntry(
            timestamp,
            LogLevel.Warning,
            "MyCategory",
            "test message",
            "MyFile.cs",
            42,
            "MyMethod",
            ex);

        Assert.That(entry.Timestamp, Is.EqualTo(timestamp));
        Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(entry.Category, Is.EqualTo("MyCategory"));
        Assert.That(entry.Message, Is.EqualTo("test message"));
        Assert.That(entry.SourceFileName, Is.EqualTo("MyFile.cs"));
        Assert.That(entry.SourceLine, Is.EqualTo(42));
        Assert.That(entry.SourceMemberName, Is.EqualTo("MyMethod"));
        Assert.That(entry.Exception, Is.EqualTo(ex));
    }

    [Test]
    public void LogEntry_NullException_DefaultsToNull()
    {
        LogEntry entry = new LogEntry(
            DateTimeOffset.UtcNow,
            LogLevel.Info,
            "Cat",
            "msg",
            "file",
            1,
            "member");

        Assert.That(entry.Exception, Is.Null);
    }

    [Test]
    public void LogEntry_Equality_SameValuesAreEqual()
    {
        DateTimeOffset timestamp = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        LogEntry a = new LogEntry(timestamp, LogLevel.Info, "Cat", "msg", "file.cs", 1, "Method");
        LogEntry b = new LogEntry(timestamp, LogLevel.Info, "Cat", "msg", "file.cs", 1, "Method");

        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void LogEntry_Equality_DifferentValuesAreNotEqual()
    {
        DateTimeOffset timestamp = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        LogEntry a = new LogEntry(timestamp, LogLevel.Info, "Cat", "msg1", "file.cs", 1, "Method");
        LogEntry b = new LogEntry(timestamp, LogLevel.Info, "Cat", "msg2", "file.cs", 1, "Method");

        Assert.That(a, Is.Not.EqualTo(b));
    }
}