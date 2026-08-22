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
    public void ControllerInfo_Serialization_RoundTripsDualSenseVariantAndAudioOutput()
    {
        ControllerInfo info = new ControllerInfo
        {
            Emulation =
            {
                Mode = EmulationMode.DualSense,
                DeviceType = DualSenseVariant.Edge,
                ForwardAudioOutput = EmulationAudioOutput.Headset
            }
        };
        string json = JsonSerializer.Serialize(info, Options);

        Assert.That(json, Does.Contain("\"device_type\":\"Edge\""));
        Assert.That(json, Does.Contain("\"forward_audio_output\":\"Headset\""));

        ControllerInfo? roundTripped = JsonSerializer.Deserialize<ControllerInfo>(json, Options);
        Assert.That(roundTripped?.Emulation.DeviceType, Is.EqualTo(DualSenseVariant.Edge));
        Assert.That(roundTripped?.Emulation.ForwardAudioOutput, Is.EqualTo(EmulationAudioOutput.Headset));
    }

    [Test]
    public void ControllerInfo_Serialization_RoundTripsDualShock4Variant()
    {
        ControllerInfo info = new ControllerInfo
        {
            Emulation =
            {
                Mode = EmulationMode.DualShock4,
                Ds4Variant = DualShock4Variant.V1
            }
        };
        string json = JsonSerializer.Serialize(info, Options);

        Assert.That(json, Does.Contain("\"ds4_variant\":\"V1\""));

        ControllerInfo? roundTripped = JsonSerializer.Deserialize<ControllerInfo>(json, Options);
        Assert.That(roundTripped?.Emulation.Ds4Variant, Is.EqualTo(DualShock4Variant.V1));
    }

    [Test]
    public void ControllerInfo_Deserialization_WithoutDualSenseOptionsFallsBackToDefaults()
    {
        string legacyJson = """{"mac_address":"AA:BB:CC:DD:EE:FF","emulation":{"mode":"DualSense"}}""";
        ControllerInfo? info = JsonSerializer.Deserialize<ControllerInfo>(legacyJson, Options);
        Assert.That(info?.Emulation.DeviceType, Is.EqualTo(DualSenseVariant.Standard));
        Assert.That(info?.Emulation.ForwardAudioOutput, Is.EqualTo(EmulationAudioOutput.Speaker));
    }

    [Test]
    public void ControllerInfo_Deserialization_WithoutDs4VariantFallsBackToV2()
    {
        string legacyJson = """{"mac_address":"AA:BB:CC:DD:EE:FF","emulation":{"mode":"DualShock4"}}""";
        ControllerInfo? info = JsonSerializer.Deserialize<ControllerInfo>(legacyJson, Options);
        Assert.That(info?.Emulation.Ds4Variant, Is.EqualTo(DualShock4Variant.V2));
    }

    [Test]
    public void ControllerInfo_Deserialization_WithoutEmulationFallsBackToOff()
    {
        string legacyJson = """{"mac_address":"AA:BB:CC:DD:EE:FF","name":"Pad"}""";
        ControllerInfo? info = JsonSerializer.Deserialize<ControllerInfo>(legacyJson, Options);
        Assert.That(info?.Emulation.Mode, Is.EqualTo(EmulationMode.Off));
    }
}