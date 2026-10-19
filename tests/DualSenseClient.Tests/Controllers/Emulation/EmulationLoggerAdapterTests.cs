using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Logging;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Controllers.Emulation;

[TestFixture]
public sealed class EmulationLoggerAdapterTests
{
    private ILogSink _originalSink = null!;

    [SetUp]
    public void Setup()
    {
        _originalSink = DualSenseClientLogger.Sink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Trace;
    }

    [TearDown]
    public void TearDown()
    {
        DualSenseClientLogger.Sink = _originalSink;
        DualSenseClientLogger.MinimumLevel = LogLevel.Info;
    }

    [Test]
    [TestCase(EmulationLogLevel.Trace, LogLevel.Trace)]
    [TestCase(EmulationLogLevel.Debug, LogLevel.Debug)]
    [TestCase(EmulationLogLevel.Information, LogLevel.Info)]
    [TestCase(EmulationLogLevel.Warning, LogLevel.Warning)]
    [TestCase(EmulationLogLevel.Error, LogLevel.Error)]
    public void Log_EngineLevel_MapsToClientLevel(EmulationLogLevel engineLevel, LogLevel expectedLevel)
    {
        CapturingLogSink sink = new CapturingLogSink();
        DualSenseClientLogger.Sink = sink;
        EmulationLoggerAdapter adapter = new EmulationLoggerAdapter();

        adapter.Log(engineLevel, "level mapping");

        Assert.That(sink.LastEntry, Is.Not.Null);
        Assert.That(sink.LastEntry!.Value.Level, Is.EqualTo(expectedLevel));
    }

    [Test]
    public void Log_Message_PassesThroughUnchanged()
    {
        CapturingLogSink sink = new CapturingLogSink();
        DualSenseClientLogger.Sink = sink;
        EmulationLoggerAdapter adapter = new EmulationLoggerAdapter();

        adapter.Log(EmulationLogLevel.Information, "Device \"busid 1-2\" imported.");

        Assert.That(sink.LastEntry, Is.Not.Null);
        Assert.That(sink.LastEntry!.Value.Message, Is.EqualTo("Device \"busid 1-2\" imported."));
    }

    [Test]
    public void Log_AnyLevel_UsesEmulationCategory()
    {
        CapturingLogSink sink = new CapturingLogSink();
        DualSenseClientLogger.Sink = sink;
        EmulationLoggerAdapter adapter = new EmulationLoggerAdapter();

        adapter.Log(EmulationLogLevel.Debug, "session accepted");

        Assert.That(sink.LastEntry, Is.Not.Null);
        Assert.That(sink.LastEntry!.Value.Category, Is.EqualTo("Emulation"));
    }

    [Test]
    public void Log_NullMessage_DoesNotThrowAndNormalizesToEmpty()
    {
        CapturingLogSink sink = new CapturingLogSink();
        DualSenseClientLogger.Sink = sink;
        EmulationLoggerAdapter adapter = new EmulationLoggerAdapter();

        Assert.That(() => adapter.Log(EmulationLogLevel.Warning, null!), Throws.Nothing);

        Assert.That(sink.LastEntry, Is.Not.Null);
        Assert.That(sink.LastEntry!.Value.Message, Is.Empty);
        Assert.That(sink.LastEntry.Value.Level, Is.EqualTo(LogLevel.Warning));
    }

    [Test]
    public void Log_SinkThrows_DoesNotPropagate()
    {
        ThrowingLogSink sink = new ThrowingLogSink();
        DualSenseClientLogger.Sink = sink;
        EmulationLoggerAdapter adapter = new EmulationLoggerAdapter();

        Assert.That(() => adapter.Log(EmulationLogLevel.Error, "sink failure"), Throws.Nothing);
        Assert.That(sink.WasCalled, Is.True);
    }

    private sealed class CapturingLogSink : ILogSink
    {
        public LogEntry? LastEntry { get; private set; }

        public void Write(in LogEntry entry) => LastEntry = entry;
    }

    private sealed class ThrowingLogSink : ILogSink
    {
        public bool WasCalled { get; private set; }

        public void Write(in LogEntry entry)
        {
            WasCalled = true;
            throw new InvalidOperationException("simulated sink failure");
        }
    }
}