using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseClient.Logging;
using SettingsModel = DualSenseClient.Settings.Settings;

namespace DualSenseClient.Tests.Settings;

public class LenientJsonDeserializerTests
{
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    public void Deserialize_ValidJson_PopulatesSettings()
    {
        string json = """
                      {
                          "debug": {
                              "log_level": "Warning"
                          },
                          "ui": {
                              "language": "de",
                              "theme": "Dark"
                          }
                      }
                      """;

        SettingsModel result = DualSenseClient.Settings.LenientJsonDeserializer.Deserialize<SettingsModel>(json, _options);

        Assert.That(result.Debug.LogLevel, Is.EqualTo(LogLevel.Warning));
        Assert.That(result.Ui.Language, Is.EqualTo("de"));
        Assert.That(result.Ui.Theme, Is.EqualTo(DualSenseClient.Core.Models.Theme.Dark));
    }

    [Test]
    public void Deserialize_InvalidPropertyValue_KeepsDefault()
    {
        string json = """
                      {
                          "debug": {
                              "log_level": "not_a_valid_level"
                          }
                      }
                      """;

        SettingsModel result = DualSenseClient.Settings.LenientJsonDeserializer.Deserialize<SettingsModel>(json, _options);

        Assert.That(result.Debug.LogLevel, Is.EqualTo(LogLevel.Info));
    }

    [Test]
    public void Deserialize_PartialJson_FillsAvailableFields()
    {
        string json = """
                      {
                          "ui": {
                              "language": "fr"
                          }
                      }
                      """;

        SettingsModel result = DualSenseClient.Settings.LenientJsonDeserializer.Deserialize<SettingsModel>(json, _options);

        Assert.That(result.Ui.Language, Is.EqualTo("fr"));
        Assert.That(result.Debug.LogLevel, Is.EqualTo(LogLevel.Info));
    }

    [Test]
    public void Deserialize_NestedObject_MergedCorrectly()
    {
        string json = """
                      {
                          "debug": {
                              "log_level": "Critical"
                          },
                          "ui": {
                              "language": "ja"
                          }
                      }
                      """;

        SettingsModel result = DualSenseClient.Settings.LenientJsonDeserializer.Deserialize<SettingsModel>(json, _options);

        Assert.That(result.Debug.LogLevel, Is.EqualTo(LogLevel.Critical));
        Assert.That(result.Ui.Language, Is.EqualTo("ja"));
    }

    [Test]
    public void Deserialize_EmptyJson_ReturnsDefaults()
    {
        string json = "{}";

        SettingsModel result = DualSenseClient.Settings.LenientJsonDeserializer.Deserialize<SettingsModel>(json, _options);

        Assert.That(result.Debug.LogLevel, Is.EqualTo(LogLevel.Info));
        Assert.That(result.Ui.Language, Is.EqualTo("en"));
        Assert.That(result.Ui.Theme, Is.EqualTo(DualSenseClient.Core.Models.Theme.System));
    }

    [Test]
    public void Deserialize_NullJson_ReturnsDefaults()
    {
        SettingsModel result = DualSenseClient.Settings.LenientJsonDeserializer.Deserialize<SettingsModel>("null", _options);

        Assert.That(result.Debug.LogLevel, Is.EqualTo(LogLevel.Info));
        Assert.That(result.Ui.Language, Is.EqualTo("en"));
    }

    [Test]
    public void Deserialize_InvalidJson_ReturnsDefaults()
    {
        SettingsModel result = DualSenseClient.Settings.LenientJsonDeserializer.Deserialize<SettingsModel>("not json", _options);

        Assert.That(result.Debug.LogLevel, Is.EqualTo(LogLevel.Info));
    }

    [Test]
    public void Deserialize_MissingJsonProperty_KeepsDefaultForThatProperty()
    {
        string json = """
                      {
                          "debug": {}
                      }
                      """;

        SettingsModel result = DualSenseClient.Settings.LenientJsonDeserializer.Deserialize<SettingsModel>(json, _options);

        Assert.That(result.Debug.LogLevel, Is.EqualTo(LogLevel.Info));
        Assert.That(result.Ui.Language, Is.EqualTo("en"));
    }
}