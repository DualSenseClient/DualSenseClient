using DualSenseClient.Logging;

namespace DualSenseClient.Tests.Logging;

public class BuildInfoTests
{
    [Test]
    public void Banner_IncludesProcessIdentity() => Assert.That(BuildInfo.Banner, Does.Contain($"Process: {Environment.ProcessId}"));
}