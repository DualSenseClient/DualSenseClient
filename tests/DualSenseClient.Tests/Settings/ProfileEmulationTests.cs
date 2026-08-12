using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

[TestFixture]
public sealed class ProfileEmulationTests
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    public void Profile_Default_EmulationIsOff()
    {
        Profile profile = new Profile();
        Assert.That(profile.Emulation, Is.Not.Null);
        Assert.That(profile.Emulation.Mode, Is.EqualTo(EmulationMode.Off));
    }

    [Test]
    public void Profile_CanChangeEmulationMode()
    {
        Profile profile = new Profile { Emulation = { Mode = EmulationMode.Xbox360 } };
        Assert.That(profile.Emulation.Mode, Is.EqualTo(EmulationMode.Xbox360));
    }

    [Test]
    public void Profile_Serialization_RoundTripsEmulationMode()
    {
        Profile profile = new Profile { Name = "Test", Emulation = { Mode = EmulationMode.DualSense } };
        string json = JsonSerializer.Serialize(profile, Options);

        Assert.That(json, Does.Contain("\"emulation\""));
        Assert.That(json, Does.Contain("\"mode\":\"DualSense\""));

        Profile? roundTripped = JsonSerializer.Deserialize<Profile>(json, Options);
        Assert.That(roundTripped?.Emulation.Mode, Is.EqualTo(EmulationMode.DualSense));
    }

    [Test]
    public void Profile_Serialization_RoundTripsDualSenseVariantAndAudioOutput()
    {
        Profile profile = new Profile
        {
            Name = "Test",
            Emulation =
            {
                Mode = EmulationMode.DualSense,
                DeviceType = DualSenseVariant.Edge,
                ForwardAudioOutput = EmulationAudioOutput.Headset
            }
        };
        string json = JsonSerializer.Serialize(profile, Options);

        Assert.That(json, Does.Contain("\"device_type\":\"Edge\""));
        Assert.That(json, Does.Contain("\"forward_audio_output\":\"Headset\""));

        Profile? roundTripped = JsonSerializer.Deserialize<Profile>(json, Options);
        Assert.That(roundTripped?.Emulation.DeviceType, Is.EqualTo(DualSenseVariant.Edge));
        Assert.That(roundTripped?.Emulation.ForwardAudioOutput, Is.EqualTo(EmulationAudioOutput.Headset));
    }

    [Test]
    public void Profile_Deserialization_WithoutDualSenseOptionsFallsBackToDefaults()
    {
        string legacyJson = """{"name":"Legacy","emulation":{"mode":"DualSense"}}""";
        Profile? profile = JsonSerializer.Deserialize<Profile>(legacyJson, Options);
        Assert.That(profile?.Emulation.DeviceType, Is.EqualTo(DualSenseVariant.Standard));
        Assert.That(profile?.Emulation.ForwardAudioOutput, Is.EqualTo(EmulationAudioOutput.Speaker));
    }

    [Test]
    public void Profile_Deserialization_WithoutEmulationFallsBackToOff()
    {
        string legacyJson = """{"name":"Legacy","player_leds":{"mask":0}}""";
        Profile? profile = JsonSerializer.Deserialize<Profile>(legacyJson, Options);
        Assert.That(profile?.Emulation.Mode, Is.EqualTo(EmulationMode.Off));
    }
}