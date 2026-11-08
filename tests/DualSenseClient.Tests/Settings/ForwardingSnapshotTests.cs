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
    public void SoftwareVolumeScale_DefaultsToUnity() => Assert.That(new ForwardSettings().SoftwareVolumeScale, Is.EqualTo(1.0));

    [TestCase(0.5, 1.0)]
    [TestCase(1.0, 1.0)]
    [TestCase(1.1, 1.1)]
    [TestCase(2.5, 2.5)]
    [TestCase(8.0, 8.0)]
    [TestCase(10.0, 8.0)]
    public void From_ClampsSoftwareVolumeScale(double requested, double expected)
    {
        ForwardSettings settings = new ForwardSettings
        {
            SoftwareVolumeScale = requested
        };

        ForwardingSnapshot snapshot = ForwardingSnapshot.From(settings);

        Assert.That(snapshot.SoftwareVolumeScale, Is.EqualTo(expected));
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