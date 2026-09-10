using DualSenseClient.Core.Foreground;

namespace DualSenseClient.Tests.Core;

public class ForegroundAppProviderTests
{
    [Test]
    public void IsSupported_MatchesWindows()
    {
        WindowsForegroundAppProvider provider = new WindowsForegroundAppProvider();

        Assert.That(provider.IsSupported, Is.EqualTo(OperatingSystem.IsWindows()));
    }

    [Test]
    public void GetForegroundApp_UnsupportedPlatform_ReturnsNull()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Pass("Windows-only assertion; nothing to verify here.");
        }

        WindowsForegroundAppProvider provider = new WindowsForegroundAppProvider();

        Assert.That(provider.GetForegroundApp(), Is.Null);
    }

    [Test]
    public void GetForegroundApp_DoesNotThrow()
    {
        WindowsForegroundAppProvider provider = new WindowsForegroundAppProvider();

        Assert.DoesNotThrow(() => provider.GetForegroundApp());
    }
}