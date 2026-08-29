using DualSenseClient.Logging;

namespace DualSenseClient.Tests.Logging;

public class FileLogSinkTests
{
    private string _tempDir = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FileLogSinkTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // cleanup best-effort
        }
    }

    [Test]
    public void Constructor_ThrowsOnNull() => Assert.Throws<ArgumentNullException>(() => new FileLogSink(null!));

    [Test]
    public void Constructor_ThrowsOnWhitespace() => Assert.Throws<ArgumentException>(() => new FileLogSink("   "));

    [Test]
    public void Constructor_CreatesDirectory()
    {
        string path = Path.Combine(_tempDir, "subdir", "app.log");
        using FileLogSink sink = new FileLogSink(path);
        Assert.That(Directory.Exists(Path.GetDirectoryName(path)), Is.True);
    }

    [Test]
    public void Constructor_MixedSeparators_NormalizesForwardSlashes()
    {
        using FileLogSink sink = new FileLogSink($"{_tempDir}/subdir/app.log");
        Assert.That(Directory.Exists(Path.Combine(_tempDir, "subdir")), Is.True);
    }

    [Test]
    public void Constructor_MixedSeparators_NormalizesBackslashes()
    {
        using FileLogSink sink = new FileLogSink($"{_tempDir}{Path.DirectorySeparatorChar}subdir\\app.log", rotateDaily: false);
        string expectedFile = Path.Combine(_tempDir, "subdir", "app.log");
        Assert.That(File.Exists(expectedFile), Is.True);
    }

    [Test]
    public void Write_CreatesLogFile()
    {
        string path = Path.Combine(_tempDir, "app.log");
        using (FileLogSink sink = new FileLogSink(path, rotateDaily: false))
        {
            LogEntry entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Info, "TestCat", "test message", "TestFile.cs", 1, "TestMethod");
            sink.Write(in entry);
        }

        string expectedFile = Path.Combine(_tempDir, "app.log");
        Assert.That(File.Exists(expectedFile), Is.True);
    }

    [Test]
    public void Write_IncludesLevelLabel()
    {
        string path = Path.Combine(_tempDir, "app.log");
        using (FileLogSink sink = new FileLogSink(path, rotateDaily: false, includeTimestamp: false))
        {
            LogEntry entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Warning, "TestCat", "warn msg", "File.cs", 10, "Method");
            sink.Write(in entry);
        }

        string content = File.ReadAllText(Path.Combine(_tempDir, "app.log"));
        Assert.That(content, Does.Contain("[WARNING]"));
        Assert.That(content, Does.Contain("[TestCat]"));
        Assert.That(content, Does.Contain("warn msg"));
        Assert.That(content, Does.Contain("File.cs:10"));
    }

    [Test]
    public void Write_IncludesTimestamp_WhenEnabled()
    {
        string path = Path.Combine(_tempDir, "app.log");
        using (FileLogSink sink = new FileLogSink(path, rotateDaily: false, includeTimestamp: true))
        {
            LogEntry entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Info, "Cat", "msg", "f.cs", 1, "M");
            sink.Write(in entry);
        }

        string content = File.ReadAllText(Path.Combine(_tempDir, "app.log"));
        Assert.That(content, Does.Match(@"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\]"));
    }

    [Test]
    public void Write_OmitsTimestamp_WhenDisabled()
    {
        string path = Path.Combine(_tempDir, "app.log");
        using (FileLogSink sink = new FileLogSink(path, rotateDaily: false, includeTimestamp: false))
        {
            LogEntry entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Info, "Cat", "msg", "f.cs", 1, "M");
            sink.Write(in entry);
        }

        string content = File.ReadAllText(Path.Combine(_tempDir, "app.log"));
        Assert.That(content, Does.Not.Match(@"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\]"));
    }

    [Test]
    public void Write_RotateDaily_IncludesDateInFilename()
    {
        string path = Path.Combine(_tempDir, "rotating.log");
        using FileLogSink sink = new FileLogSink(path, rotateDaily: true);

        string[] files = Directory.GetFiles(_tempDir, "rotating*.log");
        Assert.That(files.Length, Is.EqualTo(1));
        Assert.That(Path.GetFileName(files[0]), Does.Match(@"rotating-\d{4}-\d{2}-\d{2}\.log"));
    }

    [Test]
    public void Write_RotateDailyFalse_NoDateInFilename()
    {
        string path = Path.Combine(_tempDir, "fixed.log");
        using FileLogSink sink = new FileLogSink(path, rotateDaily: false);

        string[] files = Directory.GetFiles(_tempDir, "fixed*.log");
        Assert.That(files.Length, Is.EqualTo(1));
        Assert.That(Path.GetFileName(files[0]), Is.EqualTo("fixed.log"));
    }

    [Test]
    public void Write_WithException_IncludesExceptionInFile()
    {
        string path = Path.Combine(_tempDir, "app.log");
        InvalidOperationException ex = new InvalidOperationException("file exception");
        using (FileLogSink sink = new FileLogSink(path, rotateDaily: false))
        {
            LogEntry entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Error, "Cat", "msg", "f.cs", 1, "M", ex);
            sink.Write(in entry);
        }

        string content = File.ReadAllText(Path.Combine(_tempDir, "app.log"));
        Assert.That(content, Does.Contain("file exception"));
    }

    [Test]
    public void Dispose_ClosesFile()
    {
        string path = Path.Combine(_tempDir, "app.log");
        FileLogSink sink = new FileLogSink(path, rotateDaily: false);
        LogEntry entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Info, "Cat", "msg", "f.cs", 1, "M");
        sink.Write(in entry);
        sink.Dispose();

        string[] files = Directory.GetFiles(_tempDir, "app.log");
        Assert.That(files.Length, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_DoubleDispose_NoException()
    {
        string path = Path.Combine(_tempDir, "app.log");
        FileLogSink sink = new FileLogSink(path, rotateDaily: false);

        Assert.DoesNotThrow(() =>
        {
            sink.Dispose();
            sink.Dispose();
        });
    }

    [Test]
    public void DefaultExtension_UsesDotLog()
    {
        string path = Path.Combine(_tempDir, "noext");
        using FileLogSink sink = new FileLogSink(path, rotateDaily: false);

        string[] files = Directory.GetFiles(_tempDir, "noext*");
        Assert.That(files.Length, Is.EqualTo(1));
        Assert.That(Path.GetExtension(files[0]), Is.EqualTo(".log"));
    }

    [Test]
    public void Write_AfterRotationFailure_DoesNotThrow()
    {
        string path = Path.Combine(_tempDir, "app.log");
        using FileLogSink sink = new FileLogSink(path, rotateDaily: true);

        LogEntry entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Info, "Cat", "before rotation", "f.cs", 1, "M");
        sink.Write(in entry);

        DateOnly futureDate = new DateOnly(2099, 1, 1);
        string rotatedPath = Path.Combine(_tempDir, $"app-{futureDate:yyyy-MM-dd}.log");
        File.WriteAllBytes(rotatedPath, []);
        File.SetAttributes(rotatedPath, FileAttributes.ReadOnly);

        LogEntry futureEntry = new LogEntry(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero), LogLevel.Info, "Cat", "after failed rotation", "f.cs", 1,
            "M");

        Assert.DoesNotThrow(() => sink.Write(in futureEntry));
    }

    [Test]
    public void Write_AfterRotationFailure_SilentlyDropsEntries()
    {
        string path = Path.Combine(_tempDir, "app.log");
        using FileLogSink sink = new FileLogSink(path, rotateDaily: true);

        LogEntry entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Info, "Cat", "before rotation", "f.cs", 1, "M");
        sink.Write(in entry);

        DateOnly futureDate = new DateOnly(2099, 1, 1);
        string rotatedPath = Path.Combine(_tempDir, $"app-{futureDate:yyyy-MM-dd}.log");
        File.WriteAllBytes(rotatedPath, []);
        File.SetAttributes(rotatedPath, FileAttributes.ReadOnly);

        LogEntry futureEntry = new LogEntry(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero), LogLevel.Info, "Cat", "after failed rotation", "f.cs", 1,
            "M");
        sink.Write(in futureEntry);

        string[] files = Directory.GetFiles(_tempDir, "app*.log");
        foreach (string file in files)
        {
            string content = File.ReadAllText(file);
            Assert.That(content, Does.Not.Contain("after failed rotation"));
        }
    }
}