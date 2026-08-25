using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using SettingsModel = DualSenseClient.Settings.Settings;

namespace DualSenseClient.Tests.Settings;

public class SettingsServiceTests
{
    private string _tempDir = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SettingsServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // cleanup best-effort
        }
    }

    [Test]
    public void Constructor_CreatesConfigDirectory()
    {
        string settingsPath = Path.Combine(_tempDir, "subdir", "config.json");
        new SettingsService(settingsPath: settingsPath);
        Assert.That(Directory.Exists(Path.GetDirectoryName(settingsPath)), Is.True);
    }

    [Test]
    public void Constructor_CustomPath_Used()
    {
        string settingsPath = Path.Combine(_tempDir, "custom", "config.json");
        SettingsService service = new SettingsService(settingsPath: settingsPath);
        _ = service.Settings;
        Assert.That(File.Exists(settingsPath), Is.True);
    }

    [Test]
    public void LoadSettings_ReturnsDefaults_WhenNoFile()
    {
        string settingsPath = Path.Combine(_tempDir, "config.json");
        SettingsService service = new SettingsService(settingsPath: settingsPath);

        SettingsModel settings = service.Settings;

        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.Debug.LogLevel, Is.EqualTo(LogLevel.Info));
        Assert.That(settings.Ui.Language, Is.EqualTo("en"));
        Assert.That(settings.Ui.Theme, Is.EqualTo(DualSenseClient.Core.Models.Theme.System));
        Assert.That(settings.Ui.CloseToTray, Is.True);
        Assert.That(settings.Ui.StartInTray, Is.False);
        Assert.That(settings.Ui.ShowBatteryPercentage, Is.True);
    }

    [Test]
    public void LoadSettings_LoadsFromJson()
    {
        string settingsPath = Path.Combine(_tempDir, "config.json");
        SettingsModel saved = new SettingsModel();
        saved.Debug.LogLevel = LogLevel.Warning;
        saved.Ui.Language = "de";
        saved.Ui.CloseToTray = false;
        saved.Ui.StartInTray = true;
        saved.Ui.ShowBatteryPercentage = false;

        string json = JsonSerializer.Serialize(saved, CreateJsonOptions());
        File.WriteAllText(settingsPath, json);

        SettingsService service = new SettingsService(settingsPath: settingsPath);
        SettingsModel loaded = service.Settings;

        Assert.That(loaded.Debug.LogLevel, Is.EqualTo(LogLevel.Warning));
        Assert.That(loaded.Ui.Language, Is.EqualTo("de"));
        Assert.That(loaded.Ui.CloseToTray, Is.False);
        Assert.That(loaded.Ui.StartInTray, Is.True);
        Assert.That(loaded.Ui.ShowBatteryPercentage, Is.False);
    }

    [Test]
    public void LoadSettings_CorruptFile_FallsBackToDefaults()
    {
        string settingsPath = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(settingsPath, "not valid json {{{");

        SettingsService service = new SettingsService(settingsPath: settingsPath);
        SettingsModel loaded = service.Settings;

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded.Debug.LogLevel, Is.EqualTo(LogLevel.Info));
    }

    [Test]
    public void LoadSettings_CorruptPrimary_FallsBackToDefaults()
    {
        string settingsPath = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(settingsPath, "corrupt {{{");

        SettingsService service = new SettingsService(settingsPath: settingsPath);
        SettingsModel loaded = service.Settings;

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded.Debug.LogLevel, Is.EqualTo(LogLevel.Info));
    }

    [Test]
    public void SaveSettings_WritesJsonToDisk()
    {
        string settingsPath = Path.Combine(_tempDir, "config.json");
        SettingsService service = new SettingsService(settingsPath: settingsPath);
        SettingsModel settings = service.Settings;
        settings.Debug.LogLevel = LogLevel.Trace;

        service.SaveSettings();

        string json = File.ReadAllText(settingsPath);
        Assert.That(json, Does.Contain("Trace"));
    }

    [Test]
    public void SaveSettings_CreatesBackup()
    {
        string settingsPath = Path.Combine(_tempDir, "config.json");
        SettingsService service = new SettingsService(settingsPath: settingsPath);
        _ = service.Settings;

        service.SaveSettings();

        string backupPath = settingsPath + ".backup";
        Assert.That(File.Exists(backupPath), Is.True);
    }

    [Test]
    public void SaveSettings_FiresSettingsChangedEvent()
    {
        string settingsPath = Path.Combine(_tempDir, "config.json");
        SettingsService service = new SettingsService(settingsPath: settingsPath);
        _ = service.Settings;

        bool eventFired = false;
        service.SettingsChanged += (_, _) => eventFired = true;

        service.SaveSettings();

        Assert.That(eventFired, Is.True);
    }

    [Test]
    public void SaveSettings_WithParameter_ReplacesAndPersists()
    {
        string settingsPath = Path.Combine(_tempDir, "config.json");
        SettingsService service = new SettingsService(settingsPath: settingsPath);
        _ = service.Settings;

        SettingsModel newSettings = new SettingsModel();
        newSettings.Ui.Language = "fr";
        service.SaveSettings(newSettings);

        string json = File.ReadAllText(settingsPath);
        Assert.That(json, Does.Contain("fr"));

        SettingsModel reloaded = new SettingsService(settingsPath: settingsPath).Settings;
        Assert.That(reloaded.Ui.Language, Is.EqualTo("fr"));
    }

    [Test]
    public async Task LoadSettingsAsync_ReturnsSettings()
    {
        string settingsPath = Path.Combine(_tempDir, "config.json");
        SettingsService service = new SettingsService(settingsPath: settingsPath);

        SettingsModel loaded = await service.LoadSettingsAsync();

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded.Debug.LogLevel, Is.EqualTo(LogLevel.Info));
    }

    [Test]
    public async Task SaveSettingsAsync_WritesToDisk()
    {
        string settingsPath = Path.Combine(_tempDir, "config.json");
        SettingsService service = new SettingsService(settingsPath: settingsPath);
        service.Settings.Debug.LogLevel = LogLevel.Error;

        await service.SaveSettingsAsync();

        string json = File.ReadAllText(settingsPath);
        Assert.That(json, Does.Contain("Error"));
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };
    }
}