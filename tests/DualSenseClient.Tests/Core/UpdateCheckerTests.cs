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

    [Test]
    public void SelectLatestNightlyTag_PicksHighestBuildNumberRegardlessOfOrder()
    {
        (string Tag, bool Prerelease, bool Draft)[] releases =
        [
            ("v1.0.0-9.4816566", true, false),
            ("v1.0.0-10.7917176", true, false),
            ("v1.0.0-8.3b0de8b", true, false)
        ];
        Assert.That(UpdateChecker.SelectLatestNightlyTag(releases), Is.EqualTo("v1.0.0-10.7917176"));
    }

    [Test]
    public void SelectLatestNightlyTag_PicksHighestRemainingWhenNewestReleaseDeleted()
    {
        // v1.0.0-10's release object was deleted while its tag and changelog
        // entry still exist: the list scan must settle on v1.0.0-9.
        (string Tag, bool Prerelease, bool Draft)[] releases =
        [
            ("v1.0.0-9.4816566", true, false),
            ("v1.0.0-8.3b0de8b", true, false),
            ("v1.0.0-7.c4584f7", true, false)
        ];
        Assert.That(UpdateChecker.SelectLatestNightlyTag(releases), Is.EqualTo("v1.0.0-9.4816566"));
    }

    [Test]
    public void SelectLatestNightlyTag_SkipsDraftsAndStable()
    {
        (string Tag, bool Prerelease, bool Draft)[] releases =
        [
            ("v1.0.0", false, false),
            ("v1.0.0-11.aaaaaaa", true, true),
            ("v1.0.0-7.c4584f7", true, false)
        ];
        Assert.That(UpdateChecker.SelectLatestNightlyTag(releases), Is.EqualTo("v1.0.0-7.c4584f7"));
    }

    [Test]
    public void SelectLatestNightlyTag_PrefersHigherVersion()
    {
        (string Tag, bool Prerelease, bool Draft)[] releases =
        [
            ("v1.0.0-99.bbbbbbb", true, false),
            ("v1.0.1-1.aaaaaaa", true, false)
        ];
        Assert.That(UpdateChecker.SelectLatestNightlyTag(releases), Is.EqualTo("v1.0.1-1.aaaaaaa"));
    }

    [Test]
    public void SelectLatestNightlyTag_NoParseableTag_ReturnsNull()
    {
        (string Tag, bool Prerelease, bool Draft)[] releases =
        [
            ("v1.0.0", false, false),
            ("weird", true, false)
        ];
        Assert.That(UpdateChecker.SelectLatestNightlyTag(releases), Is.Null);
    }

    [Test]
    public void TryParseNightlyTag_ParsesBuildNumber()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UpdateChecker.TryParseNightlyTag("v1.0.0-10.7917176", out Version? version, out int build), Is.True);
            Assert.That(version, Is.EqualTo(new Version(1, 0, 0)));
            Assert.That(build, Is.EqualTo(10));
            Assert.That(UpdateChecker.TryParseNightlyTag("v1.0.0", out _, out _), Is.False);
            Assert.That(UpdateChecker.TryParseNightlyTag("not-a-tag", out _, out _), Is.False);
        });
    }
}