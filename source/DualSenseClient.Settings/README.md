# DualSenseClient.Settings

JSON-based settings persistence with lenient deserialization, backup recovery, and a reusable generic file store.

## Project Structure

```
DualSenseClient.Settings/
├── SettingsService.cs              # App settings service (backup, lazy load, events)
├── JsonFileStore.cs                # Generic JSON persistence for any type
├── LenientJsonDeserializer.cs      # Error-tolerant JSON deserialization
├── Settings.cs                     # Root settings model
└── Sections/
    ├── DebugSettings.cs            # Log level configuration
    └── UiSettings.cs               # Language and theme configuration
```

## Quick Start

### Using SettingsService

Register as a singleton in your DI container:

```csharp
services.AddSingleton<SettingsService>();
```

Access settings (lazy-loads on first access):

```csharp
SettingsService settings = serviceProvider.GetRequiredService<SettingsService>();

// Read
string language = settings.Settings.Ui.Language;
LogLevel level = settings.Settings.Debug.LogLevel;

// Write (mutate + save)
settings.Settings.Ui.Language = "de";
settings.Settings.Debug.LogLevel = LogLevel.Warning;
settings.SaveSettings();
```

Listen for changes:

```csharp
settings.SettingsChanged += (sender, args) =>
{
    // Settings were written to disk
};
```

### Using JsonFileStore\<T\>

For persisting any object type (caches, user data, feature flags, etc.):

```csharp
using DualSenseClient.Settings;

// Save data
var store = new JsonFileStore<MyData>(Path.Combine(appDir, "data.json"));
store.Item.Name = "test";
store.Item.Value = 42;
store.Save();

// Load data
var store = new JsonFileStore<MyData>(Path.Combine(appDir, "data.json"));
store.Load();
Console.WriteLine(store.Item.Name); // "test"
```

Enable optional behaviors:

```csharp
var store = new JsonFileStore<MyData>(path)
{
    WriteDefaultsWhenMissing = true,  // Write defaults to disk if no file exists
    BackupBeforeSave = true           // Create .backup copy before saving
};
```

## Adding a New Settings Section

1. Create a class in `Sections/`:

```csharp
using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

public class NetworkSettings
{
    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 30;

    [JsonPropertyName("retries")]
    public int Retries { get; set; } = 3;
}
```

2. Add a property to `Settings.cs`:

```csharp
[JsonPropertyName("network")]
public NetworkSettings Network { get; set; } = new NetworkSettings();
```

That's it. The lenient deserializer handles it automatically via reflection.

## Creating a New Settings Service

You can create a separate settings service for a different domain (e.g., test config, user preferences) by reusing
`JsonFileStore<T>`. This gives you your own file, your own sections, and your own service class.

### 1. Define the root model

```csharp
using System.Text.Json.Serialization;

namespace DualSenseClient.Settings;

public class TestConfig
{
    [JsonPropertyName("server")]
    public ServerSection Server { get; set; } = new();

    [JsonPropertyName("database")]
    public DatabaseSection Database { get; set; } = new();
}
```

### 2. Define the sections

```csharp
using System.Text.Json.Serialization;

namespace DualSenseClient.Settings;

public class ServerSection
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "localhost";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 8080;
}

public class DatabaseSection
{
    [JsonPropertyName("connection_string")]
    public string ConnectionString { get; set; } = "";

    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 30;
}
```

### 3. Create the service class

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DualSenseClient.Settings;

public sealed class TestConfigService
{
    private readonly JsonFileStore<TestConfig> _store;

    public TestConfig Config => _store.Item;

    public TestConfigService()
        : this(null) { }

    public TestConfigService(JsonSerializerOptions? jsonOptions = null, string? configPath = null)
    {
        string path = configPath
                      ?? Path.Combine(AppContext.BaseDirectory, "Config", "test.json");
        _store = new JsonFileStore<TestConfig>(path, jsonOptions)
        {
            WriteDefaultsWhenMissing = true,
            BackupBeforeSave = true
        };
    }

    public void Load() => _store.Load();
    public void Save() => _store.Save();
}
```

### 4. Register in DI and use

```csharp
services.AddSingleton<TestConfigService>();
```

```csharp
TestConfigService config = serviceProvider.GetRequiredService<TestConfigService>();
config.Load();

Console.WriteLine(config.Config.Server.Port); // 8080

config.Config.Server.Port = 9090;
config.Save();
```

The resulting `test.json`:

```json
{
  "server": {
    "host": "localhost",
    "port": 9090
  },
  "database": {
    "connection_string": "",
    "timeout": 30
  }
}
```

This follows the same pattern as `SettingsService` but with its own model, file, and service class.

## Lenient Deserialization

`LenientJsonDeserializer` provides error-tolerant loading. Instead of failing on the first invalid value, it:

1. Creates a fresh instance with all C# defaults
2. Overlays valid values from the JSON file
3. Skips invalid/malformed properties silently

This means if a user manually edits `config.json` and introduces a typo, the app keeps defaults for that field while
preserving all valid fields.

```json
{
  "debug": {
    "log_level": "not_a_valid_level"
  },
  "ui": {
    "language": "fr"
  }
}
```

Result: `LogLevel` stays at default (`Info`), `Language` is set to `"fr"`.

## Configuration

| Setting      | Default                                           | Description                                    |
|--------------|---------------------------------------------------|------------------------------------------------|
| File path    | `{AppContext.BaseDirectory}/Config/config.json`   | Pass `settingsPath` to constructor to override |
| JSON options | `WriteIndented = true`, `JsonStringEnumConverter` | Pass `jsonOptions` to constructor to override  |
| Backup path  | `{FilePath}.backup`                               | Created automatically before each save         |
