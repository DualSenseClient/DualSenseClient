using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using SettingsModel = DualSenseClient.Settings.Settings;

namespace DualSenseClient.Tests.Settings;

public class JsonFileStoreTests
{
    private string _tempDir = null!;

    private readonly JsonSerializerOptions _options = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"JsonFileStoreTests_{Guid.NewGuid():N}");
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
        }
    }

    [Test]
    public void FilePath_ReturnsConstructorPath()
    {
        string path = Path.Combine(_tempDir, "data.json");
        JsonFileStore<SettingsModel> store = new JsonFileStore<SettingsModel>(path);
        Assert.That(store.FilePath, Is.EqualTo(path));
    }

    [Test]
    public void Item_DefaultsToNewInstance()
    {
        string path = Path.Combine(_tempDir, "data.json");
        JsonFileStore<SettingsModel> store = new JsonFileStore<SettingsModel>(path);
        Assert.That(store.Item, Is.Not.Null);
        Assert.That(store.Item.Ui.Language, Is.EqualTo("en"));
    }

    [Test]
    public void Save_WritesJsonToDisk()
    {
        string path = Path.Combine(_tempDir, "data.json");
        JsonFileStore<SettingsModel> store = new JsonFileStore<SettingsModel>(path);
        store.Item.Ui.Language = "fr";
        store.Save();

        string json = File.ReadAllText(path);
        Assert.That(json, Does.Contain("fr"));
    }

    [Test]
    public void Load_ReadsFromDisk()
    {
        string path = Path.Combine(_tempDir, "data.json");
        SettingsModel saved = new SettingsModel();
        saved.Ui.Language = "ja";
        saved.Debug.LogLevel = LogLevel.Critical;
        string json = JsonSerializer.Serialize(saved, _options);
        File.WriteAllText(path, json);

        JsonFileStore<SettingsModel> store = new JsonFileStore<SettingsModel>(path);
        store.Load();

        Assert.That(store.Item.Ui.Language, Is.EqualTo("ja"));
        Assert.That(store.Item.Debug.LogLevel, Is.EqualTo(LogLevel.Critical));
    }

    [Test]
    public void Load_NoFile_ResetsToDefaults()
    {
        string path = Path.Combine(_tempDir, "data.json");
        JsonFileStore<SettingsModel> store = new JsonFileStore<SettingsModel>(path);
        store.Item.Ui.Language = "de";
        store.Load();

        Assert.That(store.Item.Ui.Language, Is.EqualTo("en"));
    }

    [Test]
    public void Load_CorruptFile_ResetsToDefaults()
    {
        string path = Path.Combine(_tempDir, "data.json");
        File.WriteAllText(path, "not valid json {{{");

        JsonFileStore<SettingsModel> store = new JsonFileStore<SettingsModel>(path);
        store.Load();

        Assert.That(store.Item, Is.Not.Null);
        Assert.That(store.Item.Ui.Language, Is.EqualTo("en"));
    }

    [Test]
    public void Save_ThenLoad_RoundTrips()
    {
        string path = Path.Combine(_tempDir, "data.json");
        JsonFileStore<SettingsModel> store = new JsonFileStore<SettingsModel>(path);
        store.Item.Ui.Language = "es";
        store.Item.Debug.LogLevel = LogLevel.Warning;
        store.Save();

        JsonFileStore<SettingsModel> store2 = new JsonFileStore<SettingsModel>(path);
        store2.Load();

        Assert.That(store2.Item.Ui.Language, Is.EqualTo("es"));
        Assert.That(store2.Item.Debug.LogLevel, Is.EqualTo(LogLevel.Warning));
    }

    [Test]
    public void Constructor_CreatesDirectory()
    {
        string path = Path.Combine(_tempDir, "subdir", "data.json");
        new JsonFileStore<SettingsModel>(path);
        Assert.That(Directory.Exists(Path.GetDirectoryName(path)), Is.True);
    }

    [Test]
    public async Task SaveAsync_WritesToDisk()
    {
        string path = Path.Combine(_tempDir, "data.json");
        JsonFileStore<SettingsModel> store = new JsonFileStore<SettingsModel>(path);
        store.Item.Ui.Language = "pt";

        await store.SaveAsync();

        string json = File.ReadAllText(path);
        Assert.That(json, Does.Contain("pt"));
    }

    [Test]
    public async Task LoadAsync_ReadsFromDisk()
    {
        string path = Path.Combine(_tempDir, "data.json");
        SettingsModel saved = new SettingsModel();
        saved.Ui.Language = "ko";
        File.WriteAllText(path, JsonSerializer.Serialize(saved, _options));

        JsonFileStore<SettingsModel> store = new JsonFileStore<SettingsModel>(path);
        await store.LoadAsync();

        Assert.That(store.Item.Ui.Language, Is.EqualTo("ko"));
    }

    [Test]
    public void CanStoreCustomType()
    {
        string path = Path.Combine(_tempDir, "custom.json");
        JsonFileStore<TestModel> store = new JsonFileStore<TestModel>(path);
        store.Item.Name = "test";
        store.Item.Value = 42;
        store.Save();

        JsonFileStore<TestModel> store2 = new JsonFileStore<TestModel>(path);
        store2.Load();

        Assert.That(store2.Item.Name, Is.EqualTo("test"));
        Assert.That(store2.Item.Value, Is.EqualTo(42));
    }

    private class TestModel
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }
}