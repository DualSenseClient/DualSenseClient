using DualSenseClient.Core.Utilities;

namespace DualSenseClient.Tests.Core;

public class ChangelogTests
{
    private static readonly DateTimeOffset Day = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Changelog.Entry Stable(string tag, int dayOffset = 0) => new Changelog.Entry(tag, tag, Day.AddDays(dayOffset), false, $"Notes for {tag}");

    private static Changelog.Entry Nightly(string tag, int dayOffset = 0) => new Changelog.Entry(tag, tag, Day.AddDays(dayOffset), true, $"Notes for {tag}");

    private static List<Changelog.Entry> Mixed() =>
    [
        Stable("v1.2.0", 3),
        Nightly("v1.2.0-2.def5678", 2),
        Stable("v1.1.0", 1),
        Stable("v1.0.0", 0)
    ];

    [Test]
    public void SelectNewer_Stable_ReturnsNewerStableOnly()
    {
        IReadOnlyList<Changelog.Entry> newer = Changelog.SelectNewer(Mixed(), "v1.0.0 (abc1234)", false);

        Assert.That(newer.Select(e => e.Tag), Is.EqualTo(["v1.2.0", "v1.1.0"]));
    }

    [Test]
    public void SelectNewer_StableUpToDate_ReturnsEmpty()
    {
        IReadOnlyList<Changelog.Entry> newer = Changelog.SelectNewer(Mixed(), "v1.2.0 (abc1234)", false);

        Assert.That(newer, Is.Empty);
    }

    [Test]
    public void SelectNewer_Nightly_StopsAtKnownSha()
    {
        List<Changelog.Entry> entries =
        [
            Nightly("v1.0.0-3.def5678", 2),
            Nightly("v1.0.0-2.abc1234", 1),
            Nightly("v1.0.0-1.1111111", 0)
        ];

        IReadOnlyList<Changelog.Entry> newer = Changelog.SelectNewer(entries, "v1.0.0 (abc1234)", true);

        Assert.That(newer.Select(e => e.Tag), Is.EqualTo(["v1.0.0-3.def5678"]));
    }

    [Test]
    public void SelectNewer_StableTargetOnNightlyChannel_IgnoresNightlies()
    {
        List<Changelog.Entry> entries =
        [
            Stable("v1.2.0", 3),
            Nightly("v1.2.0-2.def5678", 2),
            Stable("v1.1.0", 1),
            Nightly("v1.1.0-1.abc1234", 0)
        ];

        IReadOnlyList<Changelog.Entry> newer = Changelog.SelectNewer(entries, "v1.0.0 (abc1234)", true);

        Assert.That(newer.Select(e => e.Tag), Is.EqualTo(["v1.2.0", "v1.1.0"]));
    }

    [Test]
    public void SelectNewer_NightlyTargetOnNightlyChannel_ShowsAll()
    {
        List<Changelog.Entry> entries =
        [
            Nightly("v1.2.0-1.def5678", 2),
            Stable("v1.1.0", 1),
            Nightly("v1.1.0-5.abc1234", 0)
        ];

        IReadOnlyList<Changelog.Entry> newer = Changelog.SelectNewer(entries, "v1.0.0 (abc1234)", true);

        Assert.That(newer.Select(e => e.Tag), Is.EqualTo(["v1.2.0-1.def5678", "v1.1.0"]));
    }

    [Test]
    public void SelectNewer_UnknownPrevious_ReturnsNewestOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Changelog.SelectNewer(Mixed(), "", false).Select(e => e.Tag), Is.EqualTo(["v1.2.0"]));
            Assert.That(Changelog.SelectNewer(Mixed(), "garbage", true).Select(e => e.Tag), Is.EqualTo(["v1.2.0"]));
        });
    }

    [Test]
    public void SelectNewer_EmptyEntries_ReturnsEmpty() =>
        Assert.That(Changelog.SelectNewer([], "v1.0.0 (abc1234)", false), Is.Empty);
}