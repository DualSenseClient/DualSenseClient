using DualSenseClient.Core.Utilities;

namespace DualSenseClient.Tests.Core;

public class UpdateCheckerTests
{
    [Test]
    public void IsNewer_StableNewerVersion_ReturnsTrue() =>
        Assert.That(UpdateChecker.IsNewer("v1.2.0", new Version(1, 0, 0), "abc1234", false), Is.True);

    [Test]
    public void IsNewer_StableSameOrOlderVersion_ReturnsFalse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UpdateChecker.IsNewer("v1.0.0", new Version(1, 0, 0), "abc1234", false), Is.False);
            Assert.That(UpdateChecker.IsNewer("v0.9.0", new Version(1, 0, 0), "abc1234", false), Is.False);
        });
    }

    [Test]
    public void IsNewer_StableBadTag_ReturnsFalse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UpdateChecker.IsNewer("", new Version(1, 0, 0), "abc1234", false), Is.False);
            Assert.That(UpdateChecker.IsNewer("not-a-version", new Version(1, 0, 0), "abc1234", false), Is.False);
        });
    }

    [Test]
    public void IsNewer_NightlyDifferentSha_ReturnsTrue() =>
        Assert.That(UpdateChecker.IsNewer("v1.0.0-3.def5678", new Version(1, 0, 0), "abc1234", true), Is.True);

    [Test]
    public void IsNewer_NightlySameSha_ReturnsFalse() =>
        Assert.That(UpdateChecker.IsNewer("v1.0.0-3.abc1234", new Version(1, 0, 0), "abc1234", true), Is.False);

    [Test]
    public void IsNewer_NightlyWithoutLocalSha_ReturnsTrue() =>
        Assert.That(UpdateChecker.IsNewer("v1.0.0-3.abc1234", new Version(1, 0, 0), "", true), Is.True);
}