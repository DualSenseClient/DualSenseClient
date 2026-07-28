using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseClient.Logging;

namespace DualSenseClient.Settings;

/// <summary>
/// Service for managing application settings with JSON file persistence.
/// Loads and saves settings with support for backup recovery, lenient deserialization,
/// and thread-safe access.
/// </summary>
public sealed class SettingsService
{
    private readonly DualSenseClientLogger _log = DualSenseClientLogger.For("Settings");
    private readonly JsonFileStore<Settings> _store;
    private readonly Lock _lock = new();
    private bool _settingsLoaded;

    /// <summary>
    /// Gets the currently loaded settings instance.
    /// Loads settings from persistent storage if not yet initialized.
    /// </summary>
    public Settings Settings
    {
        get
        {
            if (!_settingsLoaded)
            {
                LoadSettings();
            }
            return _store.Item;
        }
    }

    /// <summary>
    /// Occurs when settings have been saved to persistent storage.
    /// </summary>
    public event EventHandler? SettingsChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsService"/> class.
    /// Settings are stored at <c>{AppContext.BaseDirectory}/Config/config.json</c>.
    /// </summary>
    public SettingsService()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsService"/> class
    /// with custom JSON serialization options.
    /// </summary>
    /// <param name="jsonOptions">
    /// Custom JSON serialization options.
    /// If <c>null</c>, default options with <see cref="JsonStringEnumConverter"/> are used.
    /// </param>
    /// <param name="settingsPath">
    /// The full path to the settings JSON file.
    /// If <c>null</c>, defaults to <c>{AppContext.BaseDirectory}/Config/config.json</c>.
    /// </param>
    public SettingsService(JsonSerializerOptions? jsonOptions = null, string? settingsPath = null)
    {
        string path = settingsPath
                      ?? Path.Combine(AppContext.BaseDirectory, "Config", "config.json");
        _store = new JsonFileStore<Settings>(path, jsonOptions)
        {
            WriteDefaultsWhenMissing = true,
            BackupBeforeSave = true
        };
    }

    /// <summary>
    /// Loads settings from persistent storage.
    /// Falls back to defaults if the file does not exist or is invalid.
    /// </summary>
    /// <returns>The loaded settings instance.</returns>
    public Settings LoadSettings()
    {
        lock (_lock)
        {
            _store.Load();
            _settingsLoaded = true;
            return _store.Item;
        }
    }

    /// <summary>
    /// Asynchronously loads settings from persistent storage.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task<Settings> LoadSettingsAsync()
    {
        return await Task.Run(LoadSettings);
    }

    /// <summary>
    /// Saves the current settings instance to persistent storage.
    /// </summary>
    public void SaveSettings()
    {
        lock (_lock)
        {
            _store.Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Asynchronously saves the current settings instance to persistent storage.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task SaveSettingsAsync()
    {
        await Task.Run(SaveSettings);
    }

    /// <summary>
    /// Saves the provided settings instance to persistent storage.
    /// </summary>
    /// <param name="settings">The settings instance to save.</param>
    public void SaveSettings(Settings settings)
    {
        lock (_lock)
        {
            _store.Item = settings;
            _store.Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Asynchronously saves the provided settings instance to persistent storage.
    /// </summary>
    /// <param name="settings">The settings instance to save.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task SaveSettingsAsync(Settings settings)
    {
        await Task.Run(() => SaveSettings(settings));
    }
}