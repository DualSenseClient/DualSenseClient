using DualSenseClient.Logging;

namespace DualSenseClient.Tests.Logging;

public class CompositeLogSinkTests
{
    [Test]
    public void Constructor_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeLogSink(null!));
    }

    [Test]
    public void Constructor_ThrowsOnNullElement()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeLogSink(new ILogSink[] { null! }));
    }

    [Test]
    public void Sinks_ReturnsChildSinks()
    {
        RecordingSink sink1 = new();
        RecordingSink sink2 = new();

        CompositeLogSink composite = new(sink1, sink2);

        Assert.That(composite.Sinks, Has.Count.EqualTo(2));
        Assert.That(composite.Sinks, Does.Contain(sink1));
        Assert.That(composite.Sinks, Does.Contain(sink2));
    }

    [Test]
    public void Write_DispatchesToAllChildren()
    {
        RecordingSink sink1 = new();
        RecordingSink sink2 = new();

        CompositeLogSink composite = new(sink1, sink2);
        LogEntry entry = new(
            DateTimeOffset.UtcNow,
            LogLevel.Info,
            "Cat",
            "msg",
            "file.cs",
            1,
            "Method");

        composite.Write(in entry);

        Assert.That(sink1.LastEntry.HasValue, Is.True);
        Assert.That(sink2.LastEntry.HasValue, Is.True);
        Assert.That(sink1.LastEntry!.Value.Message, Is.EqualTo("msg"));
        Assert.That(sink2.LastEntry!.Value.Message, Is.EqualTo("msg"));
    }

    [Test]
    public void Write_ExceptionInChild_DoesNotAffectOthers()
    {
        ThrowingSink thrower = new();
        RecordingSink recorder = new();

        CompositeLogSink composite = new(thrower, recorder);
        LogEntry entry = new(
            DateTimeOffset.UtcNow,
            LogLevel.Info,
            "Cat",
            "msg",
            "file.cs",
            1,
            "Method");

        Assert.DoesNotThrow(() => composite.Write(in entry));
        Assert.That(recorder.LastEntry.HasValue, Is.True);
    }

    [Test]
    public void Dispose_DisposesDisposableChildren()
    {
        DisposableRecordingSink disposable1 = new();
        DisposableRecordingSink disposable2 = new();

        CompositeLogSink composite = new(disposable1, disposable2);
        composite.Dispose();

        Assert.That(disposable1.Disposed, Is.True);
        Assert.That(disposable2.Disposed, Is.True);
    }

    [Test]
    public void Dispose_OnlyDisposesOnce()
    {
        DisposableRecordingSink disposable = new();
        CompositeLogSink composite = new(disposable);

        composite.Dispose();
        composite.Dispose();

        Assert.That(disposable.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_ExceptionInChild_DoesNotAffectOthers()
    {
        ThrowingDisposable thrower = new();
        DisposableRecordingSink recorder = new();

        CompositeLogSink composite = new(thrower, recorder);

        Assert.DoesNotThrow(() => composite.Dispose());
        Assert.That(recorder.Disposed, Is.True);
    }

    private class RecordingSink : ILogSink
    {
        public LogEntry? LastEntry { get; private set; }

        public void Write(in LogEntry entry)
        {
            LastEntry = entry;
        }
    }

    private class DisposableRecordingSink : ILogSink, IDisposable
    {
        public LogEntry? LastEntry { get; private set; }
        public bool Disposed { get; private set; }
        public int DisposeCount { get; private set; }

        public void Write(in LogEntry entry) => LastEntry = entry;
        public void Dispose()
        {
            Disposed = true;
            DisposeCount++;
        }
    }

    private class ThrowingSink : ILogSink
    {
        public void Write(in LogEntry entry) => throw new InvalidOperationException("fail");
    }

    private class ThrowingDisposable : ILogSink, IDisposable
    {
        public void Write(in LogEntry entry)
        {
        }
        public void Dispose() => throw new InvalidOperationException("fail dispose");
    }
}