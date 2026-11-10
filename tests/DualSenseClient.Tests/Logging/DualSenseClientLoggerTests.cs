using DualSenseClient.Logging;

namespace DualSenseClient.Tests.Logging;

public class DualSenseClientLoggerTests
{
    private ILogSink _originalSink = null!;

    [SetUp]
    public void Setup() => _originalSink = DualSenseClientLogger.Sink;

    [TearDown]
    public void TearDown()
    {
        DualSenseClientLogger.Sink = _originalSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Info;
    }

    [Test]
    public void For_ReturnsLoggerWithCorrectCategory()
    {
        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");
        Assert.That(logger.Category, Is.EqualTo("TestCategory"));
    }

    [Test]
    public void For_ThrowsOnNull() => Assert.Throws<ArgumentNullException>(() => DualSenseClientLogger.For(null!));

    [Test]
    public void For_ThrowsOnWhitespace() => Assert.Throws<ArgumentException>(() => DualSenseClientLogger.For("   "));

    [Test]
    public void For_ReturnsSameInstanceForSameCategory()
    {
        DualSenseClientLogger first = DualSenseClientLogger.For("CachedCategory");
        DualSenseClientLogger second = DualSenseClientLogger.For("CachedCategory");
        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public void For_ReturnsDifferentInstancesForDifferentCategories()
    {
        DualSenseClientLogger first = DualSenseClientLogger.For("CategoryA");
        DualSenseClientLogger second = DualSenseClientLogger.For("CategoryB");
        Assert.That(first, Is.Not.SameAs(second));
    }

    [Test]
    public void Configure_SetsMinimumLevel()
    {
        DualSenseClientLogger.Configure(LogLevel.Warning);
        Assert.That(DualSenseClientLogger.MinimumLevel, Is.EqualTo(LogLevel.Warning));
    }

    [Test]
    public void Configure_SetsSink()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Configure(sink: testSink);
        Assert.That(DualSenseClientLogger.Sink, Is.SameAs(testSink));
    }

    [Test]
    public void Configure_NullMinimumLevel_PreservesExisting()
    {
        DualSenseClientLogger.MinimumLevel = LogLevel.Debug;
        DualSenseClientLogger.Configure(null);
        Assert.That(DualSenseClientLogger.MinimumLevel, Is.EqualTo(LogLevel.Debug));
    }

    [Test]
    public void Configure_NullSink_PreservesExisting()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.Configure(sink: null);
        Assert.That(DualSenseClientLogger.Sink, Is.SameAs(testSink));
    }

    [Test]
    public void Sink_Setter_ThrowsOnNull() => Assert.Throws<ArgumentNullException>(() => DualSenseClientLogger.Sink = null!);

    [Test]
    public void Sink_Setter_DisposesOldSink()
    {
        DisposableTestSink disposable = new DisposableTestSink();
        DualSenseClientLogger.Sink = disposable;
        DualSenseClientLogger.Sink = new TestLogSink();
        Assert.That(disposable.Disposed, Is.True);
    }

    [Test]
    public void Sink_Setter_SameReference_NoOp()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.Sink = testSink;
        Assert.That(testSink.Disposed, Is.False);
    }

    [Test]
    public void TryParseLevel_StandardNames()
    {
        Assert.That(DualSenseClientLogger.TryParseLevel("Info", out LogLevel level), Is.True);
        Assert.That(level, Is.EqualTo(LogLevel.Info));
    }

    [Test]
    public void TryParseLevel_CaseInsensitive()
    {
        Assert.That(DualSenseClientLogger.TryParseLevel("warning", out LogLevel level), Is.True);
        Assert.That(level, Is.EqualTo(LogLevel.Warning));
    }

    [Test]
    public void TryParseLevel_WarnAlias()
    {
        Assert.That(DualSenseClientLogger.TryParseLevel("warn", out LogLevel level), Is.True);
        Assert.That(level, Is.EqualTo(LogLevel.Warning));
    }

    [Test]
    public void TryParseLevel_FatalAlias()
    {
        Assert.That(DualSenseClientLogger.TryParseLevel("fatal", out LogLevel level), Is.True);
        Assert.That(level, Is.EqualTo(LogLevel.Critical));
    }

    [Test]
    public void TryParseLevel_NullReturnsFalse() => Assert.That(DualSenseClientLogger.TryParseLevel(null, out _), Is.False);

    [Test]
    public void TryParseLevel_EmptyReturnsFalse() => Assert.That(DualSenseClientLogger.TryParseLevel("", out _), Is.False);

    [Test]
    public void TryParseLevel_InvalidReturnsFalse() => Assert.That(DualSenseClientLogger.TryParseLevel("notalevel", out _), Is.False);

    [Test]
    public void TryParseLevel_NoneLevelReturnsFalse()
    {
        Assert.That(DualSenseClientLogger.TryParseLevel("6", out _), Is.False);
        Assert.That(DualSenseClientLogger.TryParseLevel("None", out _), Is.False);
    }

    [Test]
    public void SetLogLevel_UpdatesLevel()
    {
        DualSenseClientLogger.SetLogLevel(LogLevel.Trace);
        Assert.That(DualSenseClientLogger.MinimumLevel, Is.EqualTo(LogLevel.Trace));
    }

    [Test]
    public void Shutdown_DisposesSink()
    {
        DisposableTestSink disposable = new DisposableTestSink();
        DualSenseClientLogger.Sink = disposable;
        DualSenseClientLogger.Shutdown();
        Assert.That(disposable.Disposed, Is.True);
    }

    [Test]
    public void Shutdown_ReplacesSinkWithNoOp()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.Shutdown();

        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");
        logger.Info("after shutdown");

        Assert.That(testSink.LastEntry, Is.Null);
    }

    [Test]
    public void LogExceptionDetails_EntrySourcePointsToCaller()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");
        InvalidOperationException ex = new InvalidOperationException("test");
        logger.LogExceptionDetails(ex, false);

        Assert.That(testSink.Entries.Count, Is.GreaterThan(0));
        Assert.That(testSink.Entries[0].SourceMemberName, Does.Contain("LogExceptionDetails_EntrySourcePointsToCaller"));
    }

    [Test]
    public void Info_LogsToSink()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");
        logger.Info("test message");

        Assert.That(testSink.LastEntry, Is.Not.Null);
        Assert.That(testSink.LastEntry!.Value.Message, Is.EqualTo("test message"));
        Assert.That(testSink.LastEntry.Value.Level, Is.EqualTo(LogLevel.Info));
        Assert.That(testSink.LastEntry.Value.Category, Is.EqualTo("TestCategory"));
    }

    [Test]
    public void Warning_LogsToSink()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");
        logger.Warning("warning message");

        Assert.That(testSink.LastEntry!.Value.Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(testSink.LastEntry.Value.Message, Is.EqualTo("warning message"));
    }

    [Test]
    public void Warning_WithException_LogsException()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        InvalidOperationException ex = new InvalidOperationException("warning failure");
        DualSenseClientLogger.For("TestCategory").Warning("warning message", ex);

        Assert.That(testSink.LastEntry, Is.Not.Null);
        Assert.That(testSink.LastEntry!.Value.Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(testSink.LastEntry.Value.Exception, Is.EqualTo(ex));
    }

    [Test]
    public void Info_CapturesCurrentThreadMetadata()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        Exception? caught = null;
        Thread thread = new Thread(() =>
        {
            try
            {
                DualSenseClientLogger.For("ThreadCategory").Info("from a named thread");
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        })
        {
            Name = "LoggingTestThread"
        };

        thread.Start();
        thread.Join();

        Assert.That(caught, Is.Null);
        Assert.That(testSink.LastEntry, Is.Not.Null);
        Assert.That(testSink.LastEntry!.Value.ThreadId, Is.EqualTo(thread.ManagedThreadId));
        Assert.That(testSink.LastEntry.Value.ThreadName, Is.EqualTo("LoggingTestThread"));
    }

    [Test]
    public void Info_WithException_LogsException()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        InvalidOperationException ex = new InvalidOperationException("info failure");
        DualSenseClientLogger.For("TestCategory").Info("info message", ex);

        Assert.That(testSink.LastEntry, Is.Not.Null);
        Assert.That(testSink.LastEntry!.Value.Level, Is.EqualTo(LogLevel.Info));
        Assert.That(testSink.LastEntry.Value.Exception, Is.EqualTo(ex));
    }

    [Test]
    public void Trace_WithException_LogsException()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        InvalidOperationException ex = new InvalidOperationException("trace failure");
        DualSenseClientLogger.For("TestCategory").Trace("trace message", ex);

        Assert.That(testSink.LastEntry, Is.Not.Null);
        Assert.That(testSink.LastEntry!.Value.Level, Is.EqualTo(LogLevel.Trace));
        Assert.That(testSink.LastEntry.Value.Exception, Is.EqualTo(ex));
    }

    [Test]
    public void IsEnabled_ReturnsTrueAtOrAboveMinimum()
    {
        DualSenseClientLogger.MinimumLevel = LogLevel.Warning;
        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");

        Assert.That(logger.IsEnabled(LogLevel.Warning), Is.True);
        Assert.That(logger.IsEnabled(LogLevel.Error), Is.True);
        Assert.That(logger.IsEnabled(LogLevel.Info), Is.False);
    }

    [Test]
    public void Error_WithException_LogsToSink()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");
        InvalidOperationException ex = new InvalidOperationException("test error");
        logger.Error("error message", ex);

        Assert.That(testSink.LastEntry!.Value.Level, Is.EqualTo(LogLevel.Error));
        Assert.That(testSink.LastEntry.Value.Exception, Is.EqualTo(ex));
    }

    [Test]
    public void Critical_WithoutException_LogsToSink()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");
        logger.Critical("critical message");

        Assert.That(testSink.LastEntry!.Value.Level, Is.EqualTo(LogLevel.Critical));
        Assert.That(testSink.LastEntry.Value.Exception, Is.Null);
    }

    [Test]
    public void BelowMinimumLevel_DropsEntry()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Warning;

        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");
        logger.Info("should be dropped");

        Assert.That(testSink.LastEntry, Is.Null);
    }

    [Test]
    public void BelowMinimumLevel_WithException_DropsEntry()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Warning;

        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");
        logger.Debug("should be dropped", new InvalidOperationException("suppressed"));

        Assert.That(testSink.Entries, Is.Empty);
    }

    [Test]
    public void LogExceptionDetails_WhenSuppressed_DoesNotWriteEntry()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Critical;

        DualSenseClientLogger.For("TestCategory")
            .LogExceptionDetails(new InvalidOperationException("suppressed"));

        Assert.That(testSink.Entries, Is.Empty);
    }

    [Test]
    public void LogExceptionDetails_LogsSingleErrorEntry()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");
        InvalidOperationException ex = new InvalidOperationException("test");
        logger.LogExceptionDetails(ex, false);

        Assert.That(testSink.Entries, Has.Count.EqualTo(1));
        Assert.That(testSink.Entries[0].Level, Is.EqualTo(LogLevel.Error));
        Assert.That(testSink.Entries[0].Exception, Is.SameAs(ex));
        Assert.That(testSink.Entries[0].Message, Does.Contain("InvalidOperationException"));
        Assert.That(testSink.Entries[0].Message, Does.Contain("test"));
    }

    [Test]
    public void LogExceptionDetails_LevelParameter_ControlsLevel()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        DualSenseClientLogger.For("TestCategory")
            .LogExceptionDetails(new InvalidOperationException("test"), false, LogLevel.Critical);

        Assert.That(testSink.Entries, Has.Count.EqualTo(1));
        Assert.That(testSink.Entries[0].Level, Is.EqualTo(LogLevel.Critical));
    }

    [Test]
    public void LogExceptionDetails_WithoutEnvironmentInfo_OmitsSystemInformation()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        DualSenseClientLogger.For("TestCategory").LogExceptionDetails(new InvalidOperationException("test"));

        Assert.That(testSink.Entries, Has.Count.EqualTo(1));
        Assert.That(testSink.Entries[0].Message, Does.Not.Contain("Machine Name"));
    }

    [Test]
    public void Trace_LogsToSink()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");
        logger.Trace("trace message");

        Assert.That(testSink.LastEntry, Is.Not.Null);
        Assert.That(testSink.LastEntry!.Value.Message, Is.EqualTo("trace message"));
        Assert.That(testSink.LastEntry.Value.Level, Is.EqualTo(LogLevel.Trace));
    }

    [Test]
    public void Debug_LogsToSink()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");
        logger.Debug("debug message");

        Assert.That(testSink.LastEntry, Is.Not.Null);
        Assert.That(testSink.LastEntry!.Value.Message, Is.EqualTo("debug message"));
        Assert.That(testSink.LastEntry.Value.Level, Is.EqualTo(LogLevel.Debug));
    }

    [Test]
    public void Debug_WithException_LogsException()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        InvalidOperationException ex = new InvalidOperationException("debug failure");
        DualSenseClientLogger.For("TestCategory").Debug("debug message", ex);

        Assert.That(testSink.LastEntry, Is.Not.Null);
        Assert.That(testSink.LastEntry!.Value.Level, Is.EqualTo(LogLevel.Debug));
        Assert.That(testSink.LastEntry.Value.Exception, Is.EqualTo(ex));
    }

    [Test]
    public void LogExceptionDetails_WithEnvironmentInfo_LogsSystemInformation()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");
        InvalidOperationException ex = new InvalidOperationException("test");
        logger.LogExceptionDetails(ex, true);

        Assert.That(testSink.Entries, Has.Count.EqualTo(1));
        Assert.That(testSink.Entries[0].Message, Does.Contain("Machine Name"));
    }

    [Test]
    public void LogExceptionDetails_PreservesInnerExceptionChain()
    {
        TestLogSink testSink = new TestLogSink();
        DualSenseClientLogger.Sink = testSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;

        DualSenseClientLogger logger = DualSenseClientLogger.For("TestCategory");
        InvalidOperationException inner = new InvalidOperationException("inner exception");
        InvalidOperationException outer = new InvalidOperationException("outer exception", inner);
        logger.LogExceptionDetails(outer, false);

        Assert.That(testSink.Entries, Has.Count.EqualTo(1));
        Assert.That(testSink.Entries[0].Exception, Is.SameAs(outer));
        Assert.That(testSink.Entries[0].Exception!.InnerException, Is.SameAs(inner));
    }

    private class TestLogSink : ILogSink, IDisposable
    {
        public List<LogEntry> Entries { get; } = new List<LogEntry>();
        public LogEntry? LastEntry { get; private set; }
        public bool Disposed { get; private set; }

        public void Write(in LogEntry entry)
        {
            LastEntry = entry;
            Entries.Add(entry);
        }

        public void Dispose() => Disposed = true;
    }

    private class DisposableTestSink : ILogSink, IDisposable
    {
        public bool Disposed { get; private set; }

        public void Write(in LogEntry entry)
        {
        }

        public void Dispose() => Disposed = true;
    }
}