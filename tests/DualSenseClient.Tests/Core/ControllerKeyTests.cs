using DualSenseClient.Core.Models;

namespace DualSenseClient.Tests.Core;

[TestFixture]
public sealed class ControllerKeyTests
{
    [Test]
    public void From_NormalizesMacAndPath()
    {
        ControllerKey key = ControllerKey.From(" aa:bb:cc:dd:ee:ff ", " \\\\?\\hid#vid_054c ");

        Assert.Multiple(() =>
        {
            Assert.That(key.Mac, Is.EqualTo("AA:BB:CC:DD:EE:FF"));
            Assert.That(key.Path, Is.EqualTo("\\\\?\\hid#vid_054c"));
        });
    }

    [Test]
    public void Matches_UsesMacFirstAndFallsBackToPath()
    {
        ControllerKey baseline = ControllerKey.From("AA:BB", "path-1");

        Assert.Multiple(() =>
        {
            Assert.That(baseline.Matches(ControllerKey.From("aa:bb", "path-2")), Is.True);
            Assert.That(baseline.Matches(ControllerKey.From("CC:DD", "path-1")), Is.True);
            Assert.That(baseline.Matches(ControllerKey.From("CC:DD", "path-2")), Is.False);
            Assert.That(ControllerKey.From(null, null).Matches(baseline), Is.False);
        });
    }

    [Test]
    public void Id_PrefersMac_ThenPath_AndIsNullWhenEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ControllerKey.From("AA:BB", "path-1").Id, Is.EqualTo("AA:BB"));
            Assert.That(ControllerKey.From(null, "path-1").Id, Is.EqualTo("path-1"));
            Assert.That(ControllerKey.From(null, null).Id, Is.Null);
        });
    }
}