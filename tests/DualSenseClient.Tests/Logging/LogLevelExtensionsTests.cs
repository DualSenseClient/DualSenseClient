using DualSenseClient.Logging;

namespace DualSenseClient.Tests.Logging;

public class LogLevelExtensionsTests
{
    [TestCase(LogLevel.Trace, "TRACE")]
    [TestCase(LogLevel.Debug, "DEBUG")]
    [TestCase(LogLevel.Info, "INFO")]
    [TestCase(LogLevel.Warning, "WARNING")]
    [TestCase(LogLevel.Error, "ERROR")]
    [TestCase(LogLevel.Critical, "CRITICAL")]
    public void ToLevelLabel_ReturnsUppercaseLabel(LogLevel level, string expected)
    {
        Assert.That(level.ToLevelLabel(), Is.EqualTo(expected));
    }

    [Test]
    public void ToLevelLabel_UnrecognizedValue_ReturnsLog()
    {
        LogLevel unknown = (LogLevel)99;
        Assert.That(unknown.ToLevelLabel(), Is.EqualTo("LOG"));
    }
}