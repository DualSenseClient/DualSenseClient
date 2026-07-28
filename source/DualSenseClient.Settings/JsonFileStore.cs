using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseClient.Logging;

namespace DualSenseClient.Settings;

/// <summary>
/// Generic JSON file persistence for any object type.
/// Provides load, save, and lenient deserialization with a configurable file path.
/// </summary>
/// <typeparam name="T">The type of object to persist. Must have a parameterless constructor.</typeparam>
public class JsonFileStore<T> where T : class, new()
{
    private readonly DualSenseClientLogger _log = DualSenseClientLogger.For("JsonFileStore");
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Lock _lock = new Lock();

    /// <summary>
    /// The file path this store reads from and writes to.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// The file path used for backups. Defaults to <c>{FilePath}.backup</c>.
    /// </summary>
    public string BackupPath { get; }

    /// <summary>
    /// The current in-memory item.
    /// </summary>
    public T Item { get; set; } = new T();

    /// <summary>
    /// When <c>true</c>, <see cref="Load"/> writes defaults to disk if no file exists.
    /// Default is <c>false</c>.
    /// </summary>
    public bool WriteDefaultsWhenMissing { get; set; }

    /// <summary>
    /// When <c>true</c>, <see cref="Save"/> creates a backup of the existing file before writing.
    /// Default is <c>false</c>.
    /// </summary>
    public bool BackupBeforeSave { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="JsonFileStore{T}"/>.
    /// </summary>
    /// <param name="filePath">The full path to the JSON file.</param>
    /// <param name="jsonOptions">
    /// Custom JSON serialization options.
    /// If <c>null</c>, default options with <see cref="JsonStringEnumConverter"/> are used.
    /// </param>
    public JsonFileStore(string filePath, JsonSerializerOptions? jsonOptions = null)
    {
        FilePath = filePath;
        BackupPath = filePath + ".backup";
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Loads the item from disk using lenient deserialization.
    /// If the file does not exist or is invalid, <see cref="Item"/> is reset to defaults.
    /// When <see cref="WriteDefaultsWhenMissing"/> is <c>true</c> and the file does not exist,
    /// defaults are written to disk.
    /// </summary>
    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    Item = new T();

                    if (WriteDefaultsWhenMissing)
                    {
                        SaveUnsafe();
                    }

                    return;
                }

                string json = File.ReadAllText(FilePath);
                Item = LenientJsonDeserializer.Deserialize<T>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to load from '{FilePath}'");
                _log.LogExceptionDetails(ex);
                Item = new T();
            }
        }
    }

    /// <summary>
    /// Asynchronously loads the item from disk.
    /// </summary>
    public async Task LoadAsync()
    {
        await Task.Run(Load);
    }

    /// <summary>
    /// Saves the current <see cref="Item"/> to disk.
    /// When <see cref="BackupBeforeSave"/> is <c>true</c>, creates a backup of the existing file first.
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            if (BackupBeforeSave)
            {
                CreateBackup();
            }

            SaveUnsafe();
        }
    }

    /// <summary>
    /// Asynchronously saves the current <see cref="Item"/> to disk.
    /// </summary>
    public async Task SaveAsync()
    {
        await Task.Run(Save);
    }

    private void SaveUnsafe()
    {
        try
        {
            string json = JsonSerializer.Serialize(Item, _jsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to save to '{FilePath}'");
            _log.LogExceptionDetails(ex);
        }
    }

    private void CreateBackup()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Copy(FilePath, BackupPath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to create backup at '{BackupPath}'");
            _log.LogExceptionDetails(ex);
        }
    }
}