using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

[TestFixture]
public sealed class ForwardingSnapshotTests
{
    [Test]
    public void From_ClampsNumericRanges()
    {
        ForwardSettings settings = new ForwardSettings
        {
            Volume = 999,
            Haptics = -10,
            AudioBufferLength = 4
        };

        ForwardingSnapshot snapshot = ForwardingSnapshot.From(settings);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Volume, Is.EqualTo((byte)255));
            Assert.That(snapshot.Haptics, Is.EqualTo(0));
            Assert.That(snapshot.AudioBufferLength, Is.EqualTo((byte)16));
        });
    }

    [Test]
    public void From_ClampsUpperAudioBufferAndKeepsValidValues()
    {
        ForwardSettings settings = new ForwardSettings
        {
            Volume = 0x50,
            Haptics = 150,
            AudioBufferLength = 999,
            AudioSource = EmulationAudioSource.Application,
            AudioApp = "  game.exe  "
        };

        ForwardingSnapshot snapshot = ForwardingSnapshot.From(settings);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Volume, Is.EqualTo((byte)0x50));
            Assert.That(snapshot.Haptics, Is.EqualTo(150));
            Assert.That(snapshot.AudioBufferLength, Is.EqualTo((byte)128));
            Assert.That(snapshot.AudioSource, Is.EqualTo(EmulationAudioSource.Application));
            Assert.That(snapshot.AudioApp, Is.EqualTo("game.exe"));
        });
    }
}