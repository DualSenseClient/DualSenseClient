using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

/// <summary>
/// Covers the virtual microphone settings: the source route, the capture level, the
/// host-microphone passthrough flag, and their JSON round-trips.
/// </summary>
[TestFixture]
public sealed class MicrophoneSettingsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    [Test]
    public void Defaults_AreAutoAndOneHundredAndPassthroughOn()
    {
        EmulationSettings emulation = new EmulationSettings();
        Assert.Multiple(() =>
        {
            Assert.That(emulation.Microphone.Source, Is.EqualTo(MicrophoneSource.Auto));
            Assert.That(emulation.Microphone.Level, Is.EqualTo(100));
            Assert.That(emulation.Forward.MicrophoneEnabled, Is.True);
        });
    }

    [Test]
    public void RoundTrip_PreservesSourceLevelAndPassthrough()
    {
        EmulationSettings source = new EmulationSettings
        {
            Microphone = new MicrophoneSettings
            {
                Source = MicrophoneSource.Headphone,
                Level = 42
            },
            Forward = new ForwardSettings
            {
                MicrophoneEnabled = false
            }
        };

        string json = JsonSerializer.Serialize(source, JsonOptions);
        EmulationSettings copy = JsonSerializer.Deserialize<EmulationSettings>(json, JsonOptions)!;

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"microphone\":{\"source\":\"Headphone\",\"level\":42}"),
                "the nested persisted keys stay pinned");
            Assert.That(copy.Microphone.Source, Is.EqualTo(MicrophoneSource.Headphone));
            Assert.That(copy.Microphone.Level, Is.EqualTo(42));
            Assert.That(copy.Forward.MicrophoneEnabled, Is.False);
        });
    }

    [Test]
    public void OldJsonWithoutMicrophone_UsesDefaults()
    {
        EmulationSettings copy = JsonSerializer.Deserialize<EmulationSettings>(
            "{\"mode\":\"DualSense\"}", JsonOptions)!;
        Assert.Multiple(() =>
        {
            Assert.That(copy.Microphone.Source, Is.EqualTo(MicrophoneSource.Auto));
            Assert.That(copy.Microphone.Level, Is.EqualTo(100));
            Assert.That(copy.Forward.MicrophoneEnabled, Is.True);
        });
    }

    [Test]
    public void Source_InvalidValue_FallsBackToAuto()
    {
        MicrophoneSettings settings = new MicrophoneSettings
        {
            Source = (MicrophoneSource)9
        };
        EmulationSettings copy = JsonSerializer.Deserialize<EmulationSettings>(
            "{\"microphone\":{\"source\":9}}", JsonOptions)!;

        Assert.Multiple(() =>
        {
            Assert.That(settings.Source, Is.EqualTo(MicrophoneSource.Auto));
            Assert.That(copy.Microphone.Source, Is.EqualTo(MicrophoneSource.Auto),
                "a numeric JSON value outside the enum range must not survive");
        });
    }

    [Test]
    public void Level_ClampsToZeroThroughOneHundred()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new MicrophoneSettings
            {
                Level = -1
            }.Level, Is.EqualTo(0));
            Assert.That(new MicrophoneSettings
            {
                Level = 101
            }.Level, Is.EqualTo(100));
        });
    }

    [Test]
    public void ForwardingSnapshot_CarriesMicrophoneEnabled()
    {
        ForwardSettings settings = new ForwardSettings
        {
            MicrophoneEnabled = false
        };
        ForwardingSnapshot snapshot = ForwardingSnapshot.From(settings);
        Assert.That(snapshot.MicrophoneEnabled, Is.False);
    }
}