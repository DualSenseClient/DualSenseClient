using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

[TestFixture]
public sealed class ControllerEmulationTests
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    [Test]
    public void ControllerInfo_Default_EmulationIsOff()
    {
        ControllerInfo info = new ControllerInfo();
        Assert.That(info.Emulation, Is.Not.Null);
        Assert.That(info.Emulation.Mode, Is.EqualTo(EmulationMode.Off));
    }

    [Test]
    public void ControllerInfo_CanChangeEmulationMode()
    {
        ControllerInfo info = new ControllerInfo
        {
            Emulation =
            {
                Mode = EmulationMode.Xbox360
            }
        };
        Assert.That(info.Emulation.Mode, Is.EqualTo(EmulationMode.Xbox360));
    }

    [Test]
    public void ControllerInfo_Serialization_RoundTripsEmulationMode()
    {
        ControllerInfo info = new ControllerInfo
        {
            Emulation =
            {
                Mode = EmulationMode.DualSense
            }
        };
        string json = JsonSerializer.Serialize(info, Options);

        Assert.That(json, Does.Contain("\"emulation\""));
        Assert.That(json, Does.Contain("\"mode\":\"DualSense\""));

        ControllerInfo? roundTripped = JsonSerializer.Deserialize<ControllerInfo>(json, Options);
        Assert.That(roundTripped?.Emulation.Mode, Is.EqualTo(EmulationMode.DualSense));
    }

    [Test]
    public void ControllerInfo_Serialization_RoundTripsVariantsAndAudioOutput()
    {
        ControllerInfo info = new ControllerInfo
        {
            Emulation =
            {
                Mode = EmulationMode.DualSense,
                Variant =
                {
                    DualSense = DualSenseVariant.Edge,
                    DualShock4 = DualShock4Variant.V1
                },
                Forward =
                {
                    AudioOutput = EmulationAudioOutput.Headset,
                    Volume = 149,
                    Haptics = 150
                }
            }
        };
        string json = JsonSerializer.Serialize(info, Options);

        Assert.That(json, Does.Contain("\"variant\":{\"dualsense\":\"Edge\",\"dualshock4\":\"V1\"}"));
        Assert.That(json, Does.Contain("\"forward\":{\"audio_output\":\"Headset\",\"volume\":149,\"haptics\":150}"));

        ControllerInfo? roundTripped = JsonSerializer.Deserialize<ControllerInfo>(json, Options);
        Assert.That(roundTripped?.Emulation.Variant.DualSense, Is.EqualTo(DualSenseVariant.Edge));
        Assert.That(roundTripped?.Emulation.Variant.DualShock4, Is.EqualTo(DualShock4Variant.V1));
        Assert.That(roundTripped?.Emulation.Forward.AudioOutput, Is.EqualTo(EmulationAudioOutput.Headset));
        Assert.That(roundTripped?.Emulation.Forward.Volume, Is.EqualTo(149));
        Assert.That(roundTripped?.Emulation.Forward.Haptics, Is.EqualTo(150));
    }

    [Test]
    public void ControllerInfo_Serialization_RoundTripsMappings()
    {
        List<ButtonMappingEntry> entries =
        [
            new ButtonMappingEntry
            {
                Keys = ["TouchPad"],
                Targets = ["Y", "Guide"],
                TargetOutput = null,
                SuppressSolos = true
            }
        ];
        ControllerInfo info = new ControllerInfo
        {
            Emulation =
            {
                Mode = EmulationMode.Xbox360,
                Mappings =
                {
                    Xbox360 = entries,
                    DualShock4 = [],
                    DualSense = null
                }
            }
        };
        string json = JsonSerializer.Serialize(info, Options);

        Assert.That(json, Does.Contain("\"mappings\":{"));

        ControllerInfo? roundTripped = JsonSerializer.Deserialize<ControllerInfo>(json, Options);
        Assert.That(roundTripped?.Emulation.Mappings.Xbox360, Has.Count.EqualTo(1));
        Assert.That(roundTripped?.Emulation.Mappings.Xbox360?[0].Keys, Is.EqualTo(entries[0].Keys));
        Assert.That(roundTripped?.Emulation.Mappings.Xbox360?[0].Targets, Is.EqualTo(entries[0].Targets));
        Assert.That(roundTripped?.Emulation.Mappings.Xbox360?[0].SuppressSolos, Is.True);
        Assert.That(roundTripped?.Emulation.Mappings.DualShock4, Is.Empty);
        Assert.That(roundTripped?.Emulation.Mappings.DualSense, Is.Null);
    }

    [Test]
    public void ControllerInfo_Deserialization_WithoutDualSenseOptionsFallsBackToDefaults()
    {
        string legacyJson = """{"mac_address":"AA:BB:CC:DD:EE:FF","emulation":{"mode":"DualSense"}}""";
        ControllerInfo? info = JsonSerializer.Deserialize<ControllerInfo>(legacyJson, Options);
        Assert.That(info?.Emulation.Variant.DualSense, Is.EqualTo(DualSenseVariant.Standard));
        Assert.That(info?.Emulation.Forward.AudioOutput, Is.EqualTo(EmulationAudioOutput.Speaker));
    }

    [Test]
    public void ControllerInfo_Deserialization_WithoutDs4VariantFallsBackToV2()
    {
        string legacyJson = """{"mac_address":"AA:BB:CC:DD:EE:FF","emulation":{"mode":"DualShock4"}}""";
        ControllerInfo? info = JsonSerializer.Deserialize<ControllerInfo>(legacyJson, Options);
        Assert.That(info?.Emulation.Variant.DualShock4, Is.EqualTo(DualShock4Variant.V2));
    }

    [Test]
    public void ControllerInfo_Deserialization_PartialSectionsKeepDefaults()
    {
        string json = """{"emulation":{"mode":"Xbox360","forward":{"volume":149},"mappings":{"xbox360":[]}}}""";
        ControllerInfo? info = JsonSerializer.Deserialize<ControllerInfo>(json, Options);
        Assert.That(info?.Emulation.Mode, Is.EqualTo(EmulationMode.Xbox360));
        Assert.That(info?.Emulation.Variant.DualSense, Is.EqualTo(DualSenseVariant.Standard));
        Assert.That(info?.Emulation.Forward.Volume, Is.EqualTo(149));
        Assert.That(info?.Emulation.Forward.Haptics, Is.EqualTo(100));
        Assert.That(info?.Emulation.Mappings.Xbox360, Is.Empty);
        Assert.That(info?.Emulation.Mappings.DualSense, Is.Null);
    }

    [Test]
    public void ControllerInfo_Deserialization_WithoutEmulationFallsBackToOff()
    {
        string legacyJson = """{"mac_address":"AA:BB:CC:DD:EE:FF","name":"Pad"}""";
        ControllerInfo? info = JsonSerializer.Deserialize<ControllerInfo>(legacyJson, Options);
        Assert.That(info?.Emulation.Mode, Is.EqualTo(EmulationMode.Off));
    }
}