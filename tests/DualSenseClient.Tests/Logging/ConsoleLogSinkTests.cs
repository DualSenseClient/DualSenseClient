using DualSenseClient.Logging;

namespace DualSenseClient.Tests.Logging;

public class ConsoleLogSinkTests
{
    [Test]
    public void Constructor_DefaultUseColors_True()
    {
        ConsoleLogSink sink = new ConsoleLogSink();
        Assert.That(sink.UseColors, Is.True);
    }

    [Test]
    public void Constructor_CustomUseColors()
    {
        ConsoleLogSink sink = new ConsoleLogSink(false);
        Assert.That(sink.UseColors, Is.False);
    }

    [Test]
    public void Write_InfoLevel_WritesToConsoleOut()
    {
        ConsoleLogSink sink = new ConsoleLogSink(false);
        LogEntry entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Info, "TestCat", "hello", "TestFile.cs", 10, "TestMethod");

        TextWriter originalOut = Console.Out;
        StringWriter captured = new StringWriter();
        Console.SetOut(captured);

        try
        {
            sink.Write(in entry);
            string output = captured.ToString();
            Assert.That(output, Does.Contain("[INFO]"));
            Assert.That(output, Does.Contain("[TestCat]"));
            Assert.That(output, Does.Contain("hello"));
            Assert.That(output, Does.Contain("TestFile.cs:10"));
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Test]
    public void Write_ErrorLevel_WritesToConsoleError()
    {
        ConsoleLogSink sink = new ConsoleLogSink(false);
        LogEntry entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Error, "TestCat", "error msg", "ErrFile.cs", 5, "ErrMethod");

        TextWriter originalErr = Console.Error;
        StringWriter captured = new StringWriter();
        Console.SetError(captured);

        try
        {
            sink.Write(in entry);
            string output = captured.ToString();
            Assert.That(output, Does.Contain("[ERROR]"));
            Assert.That(output, Does.Contain("error msg"));
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Test]
    public void Write_CriticalLevel_WritesToConsoleError()
    {
        ConsoleLogSink sink = new ConsoleLogSink(false);
        LogEntry entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Critical, "TestCat", "critical msg", "File.cs", 1, "Method");

        TextWriter originalErr = Console.Error;
        StringWriter captured = new StringWriter();
        Console.SetError(captured);

        try
        {
            sink.Write(in entry);
            string output = captured.ToString();
            Assert.That(output, Does.Contain("[CRITICAL]"));
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Test]
    public void Write_WithException_IncludesExceptionInOutput()
    {
        ConsoleLogSink sink = new ConsoleLogSink(false);
        InvalidOperationException ex = new InvalidOperationException("test exception");
        LogEntry entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Error, "TestCat", "msg", "File.cs", 1, "Method", ex);

        TextWriter originalErr = Console.Error;
        StringWriter captured = new StringWriter();
        Console.SetError(captured);

        try
        {
            sink.Write(in entry);
            string output = captured.ToString();
            Assert.That(output, Does.Contain("test exception"));
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Test]
    public void Write_SourceLineZero_OmitsLineNumber()
    {
        ConsoleLogSink sink = new ConsoleLogSink(false);
        LogEntry entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Info, "TestCat", "msg", "File.cs", 0, "Method");

        TextWriter originalOut = Console.Out;
        StringWriter captured = new StringWriter();
        Console.SetOut(captured);

        try
        {
            sink.Write(in entry);
            string output = captured.ToString();
            Assert.That(output, Does.Not.Contain("File.cs:0"));
            Assert.That(output, Does.Contain("File.cs"));
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Test]
    public void Write_WithColors_WritesLevelLabel()
    {
        ConsoleLogSink sink = new ConsoleLogSink(true);
        LogEntry entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Warning, "TestCat", "colored msg", "File.cs", 10, "Method");

        TextWriter originalOut = Console.Out;
        StringWriter captured = new StringWriter();
        Console.SetOut(captured);

        try
        {
            sink.Write(in entry);
            string output = captured.ToString();
            Assert.That(output, Does.Contain("[WARNING]"));
            Assert.That(output, Does.Contain("colored msg"));
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}