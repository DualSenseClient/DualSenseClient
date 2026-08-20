using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DualSenseClient.Core.Utilities;
using DualSenseClient.Logging;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Settings;

/// <summary>
/// Service for managing special actions with JSON file persistence.
/// Stores a single global list of special actions (button combinations that execute an
/// action such as disconnecting the controller or changing the lightbar color), each
/// enabled for specific controllers.
/// </summary>
/// <remarks>
/// <para>
/// This is a separate settings service from <see cref="SettingsService"/>: it persists to
/// <c>special_actions.json</c> (in the same folder as <c>config.json</c>) and reuses
/// <see cref="JsonFileStore{T}"/> for lenient deserialization, backups, and thread-safe access.
/// </para>
/// <para>
/// Controllers are identified by their Bluetooth MAC address first, falling back to the HID
/// device path (see <see cref="GetControllerId"/>). Actions are global, but only fire for
/// controllers listed in <see cref="SpecialAction.EnabledControllers"/>.
/// </para>
/// </remarks>
public sealed class SpecialActionService
{
    /// <summary>
    /// Base name used for automatically created actions ("Special Action", "Special Action 2", ...).
    /// </summary>
    public const string DefaultActionName = "Special Action";

    /// <summary>
    /// Logger instance.
    /// </summary>
    private readonly DualSenseClientLogger _log = DualSenseClientLogger.For("SpecialActions");

    /// <summary>
    /// The JSON file store backing this service.
    /// </summary>
    private readonly JsonFileStore<SpecialActionsSettings> _store;

    /// <summary>
    /// The JSON serializer options used to read, write, export, and import actions.
    /// </summary>
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Synchronizes access to action load/save operations.
    /// </summary>
    private readonly Lock _lock = new Lock();

    /// <summary>
    /// Whether actions have been loaded from disk at least once.
    /// </summary>
    private bool _loaded;

    /// <summary>
    /// Gets the currently loaded special action settings.
    /// Loads them from persistent storage if not yet initialized.
    /// </summary>
    public SpecialActionsSettings Settings
    {
        get
        {
            if (!_loaded)
            {
                Load();
            }
            return _store.Item;
        }
    }

    /// <summary>
    /// Occurs when special actions have been saved to persistent storage.
    /// </summary>
    public event EventHandler? SpecialActionsChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpecialActionService"/> class.
    /// Special actions are stored at <c>{PathResolver.BaseDirectory}/Config/special_actions.json</c>.
    /// </summary>
    public SpecialActionService() : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SpecialActionService"/> class
    /// with custom JSON serialization options.
    /// </summary>
    /// <param name="jsonOptions">
    /// Custom JSON serialization options.
    /// If <c>null</c>, default options with <see cref="JsonStringEnumConverter"/> are used.
    /// </param>
    /// <param name="actionsPath">
    /// The full path to the special actions JSON file.
    /// If <c>null</c>, defaults to <c>{PathResolver.BaseDirectory}/Config/special_actions.json</c>.
    /// </param>
    public SpecialActionService(JsonSerializerOptions? jsonOptions = null, string? actionsPath = null)
    {
        string path = actionsPath
                      ?? PathResolver.GetFullPath("Config", "special_actions.json");
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };
        _store = new JsonFileStore<SpecialActionsSettings>(path, _jsonOptions)
        {
            WriteDefaultsWhenMissing = true,
            BackupBeforeSave = true
        };
        _log.Debug($"Special actions store initialized at '{path}'");
    }

    /// <summary>
    /// Loads special actions from persistent storage.
    /// Falls back to defaults if the file does not exist or is invalid.
    /// </summary>
    public void Load()
    {
        lock (_lock)
        {
            _log.Info($"Loading special actions from '{_store.FilePath}'");
            _store.Load();
            _loaded = true;
            MigrateLegacyActions();
        }
    }

    /// <summary>
    /// Saves the current special action settings to persistent storage.
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            _log.Debug($"Saving special actions to '{_store.FilePath}'");
            _store.Save();
            SpecialActionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Creates a new action with a unique name derived from <paramref name="baseName"/>
    /// (e.g. "Special Action", "Special Action 2"), optionally enabled for the controller
    /// it was created for, and persists it. The new action is returned.
    /// </summary>
    /// <param name="baseName">The base name for the new action.</param>
    /// <param name="controllerId">
    /// The identifier of the controller to enable the action for (see <see cref="GetControllerId"/>),
    /// or <c>null</c>/empty to create the action disabled.
    /// </param>
    public SpecialAction CreateAction(string? baseName, string? controllerId)
    {
        SpecialAction action = new SpecialAction
        {
            Name = GetUniqueName(baseName),
            Effects = { new SpecialActionEffect { Type = SpecialActionTypes.SetLightbarColor } }
        };
        if (!string.IsNullOrWhiteSpace(controllerId))
        {
            action.EnabledControllers.Add(controllerId.Trim());
        }

        Settings.Actions.Add(action);
        Save();
        return action;
    }

    /// <summary>
    /// Deletes an action and persists the change.
    /// </summary>
    /// <param name="id">The identifier of the action to delete.</param>
    /// <returns><c>true</c> if the action was deleted, <c>false</c> if it did not exist.</returns>
    public bool DeleteAction(Guid id)
    {
        SpecialAction? action = Settings.Actions.FirstOrDefault(a => a.Id == id);
        if (action is null)
        {
            return false;
        }

        Settings.Actions.Remove(action);
        Save();
        return true;
    }

    /// <summary>
    /// Exports all special actions to a JSON file at <paramref name="path"/>. The file has
    /// the same shape as the app's <c>special_actions.json</c> (<c>{"actions": [...]}</c>),
    /// so it can be imported back or shared between machines. Controller enablement
    /// (<c>controllers</c>) is not exported; imported actions are disabled until re-enabled.
    /// </summary>
    /// <param name="path">The full path of the file to write.</param>
    public void ExportActions(string path)
    {
        lock (_lock)
        {
            string json = SerializeForExport(Settings.Actions);
            File.WriteAllText(path, json);
            _log.Info($"Exported {Settings.Actions.Count} special actions to '{path}'");
        }
    }

    /// <summary>
    /// Exports a single special action to a JSON file at <paramref name="path"/>. The file
    /// has the same shape as <see cref="ExportActions"/> (<c>{"actions": [...]}</c>), so a
    /// single export can be imported back just like a full export. Controller enablement
    /// (<c>controllers</c>) is not exported.
    /// </summary>
    /// <param name="id">The identifier of the action to export.</param>
    /// <param name="path">The full path of the file to write.</param>
    /// <exception cref="ArgumentException">Thrown when no action with <paramref name="id"/> exists.</exception>
    public void ExportAction(Guid id, string path)
    {
        lock (_lock)
        {
            SpecialAction? action = Settings.Actions.FirstOrDefault(a => a.Id == id);
            if (action is null)
            {
                throw new ArgumentException($"Special action '{id}' does not exist", nameof(id));
            }

            string json = SerializeForExport([action]);
            File.WriteAllText(path, json);
            _log.Info($"Exported special action '{action.Name}' to '{path}'");
        }
    }

    /// <summary>
    /// Serializes actions to the export file shape, stripping the <c>controllers</c>
    /// property (controller enablement is machine- and controller-specific and must not be
    /// shared in export files).
    /// </summary>
    private string SerializeForExport(IEnumerable<SpecialAction> actions)
    {
        JsonObject wrapper = JsonSerializer.SerializeToNode(new SpecialActionsSettings { Actions = actions.ToList() }, _jsonOptions)!.AsObject();
        if (wrapper["actions"] is JsonArray array)
        {
            foreach (JsonObject? action in array.OfType<JsonObject>())
            {
                action.Remove("controllers");
            }
        }

        return wrapper.ToJsonString(_jsonOptions);
    }

    /// <summary>
    /// Imports special actions from a JSON file (either <c>{"actions": [...]}</c> or a bare
    /// array), assigns fresh identifiers and unique names so imported actions never collide
    /// with existing ones, and persists the change.
    /// </summary>
    /// <param name="path">The full path of the file to read.</param>
    /// <returns>The number of actions imported (0 when the file contains none or is invalid).</returns>
    public int ImportActions(string path)
    {
        lock (_lock)
        {
            List<SpecialAction>? imported = ParseActionList(File.ReadAllText(path));
            if (imported is null || imported.Count == 0)
            {
                _log.Warning($"No importable special actions found in '{path}'");
                return 0;
            }

            foreach (SpecialAction action in imported)
            {
                action.Id = Guid.NewGuid();
                action.Name = GetUniqueName(action.Name);
                Settings.Actions.Add(action);
            }

            _log.Info($"Imported {imported.Count} special actions from '{path}'");
            Save();
            return imported.Count;
        }
    }

    /// <summary>
    /// Parses the JSON of an actions export file into the action list it carries, or
    /// <c>null</c> when the file has no recognized shape (no <c>actions</c> array and not a
    /// bare array). Unknown properties are ignored and missing ones use defaults.
    /// </summary>
    private List<SpecialAction>? ParseActionList(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement list = doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => doc.RootElement,
                JsonValueKind.Object when doc.RootElement.TryGetProperty("actions", out JsonElement actions)
                                          && actions.ValueKind == JsonValueKind.Array => actions,
                _ => default
            };
            return list.ValueKind == JsonValueKind.Undefined
                ? null
                : list.Deserialize<List<SpecialAction>>(_jsonOptions);
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to parse special actions export: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Enables or disables an action for a specific controller and persists the change.
    /// </summary>
    /// <param name="id">The identifier of the action.</param>
    /// <param name="controllerId">The controller identifier (see <see cref="GetControllerId"/>).</param>
    /// <param name="enabled"><c>true</c> to enable the action for the controller, <c>false</c> to disable it.</param>
    /// <returns><c>true</c> if the enablement changed, <c>false</c> when the action or
    /// controller does not exist or the state was already as requested.</returns>
    public bool SetEnabledForController(Guid id, string? controllerId, bool enabled)
    {
        SpecialAction? action = Settings.Actions.FirstOrDefault(a => a.Id == id);
        if (action is null || string.IsNullOrWhiteSpace(controllerId))
        {
            return false;
        }

        string identifier = controllerId.Trim();
        bool contains = action.EnabledControllers.Any(c => string.Equals(c, identifier, StringComparison.OrdinalIgnoreCase));
        if (enabled == contains)
        {
            return false;
        }

        if (enabled)
        {
            action.EnabledControllers.Add(identifier);
        }
        else
        {
            action.EnabledControllers.RemoveAll(c => string.Equals(c, identifier, StringComparison.OrdinalIgnoreCase));
        }

        Save();
        return true;
    }

    /// <summary>
    /// Migrates actions saved by older versions of the app, which stored a single action
    /// type and its parameters directly on the action (e.g. <c>type</c>, <c>red</c>), into
    /// the current effects list. Actions that already carry effects are left untouched.
    /// Best effort: any failure is logged and skipped.
    /// </summary>
    private void MigrateLegacyActions()
    {
        try
        {
            if (!File.Exists(_store.FilePath))
            {
                return;
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(_store.FilePath));
            if (!doc.RootElement.TryGetProperty("actions", out JsonElement actions) || actions.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            Dictionary<Guid, SpecialActionEffect> legacy = new Dictionary<Guid, SpecialActionEffect>();
            foreach (JsonElement element in actions.EnumerateArray())
            {
                if (!element.TryGetProperty("id", out JsonElement idElement)
                    || !Guid.TryParse(idElement.GetString(), out Guid id)
                    || !element.TryGetProperty("type", out JsonElement typeElement))
                {
                    continue;
                }

                string? type = typeElement.GetString();
                if (string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                legacy[id] = new SpecialActionEffect
                {
                    Type = type,
                    Red = ByteProperty(element, "red"),
                    Green = ByteProperty(element, "green"),
                    Blue = ByteProperty(element, "blue", 255),
                    PlayerLedMask = ByteProperty(element, "player_leds"),
                    SoundPath = element.TryGetProperty("sound_path", out JsonElement sound) ? sound.GetString() : null,
                    SoundVolume = ByteProperty(element, "sound_volume", 0x50),
                    HapticFeedback = BoolProperty(element, "haptic_feedback"),
                    HapticStrength = IntProperty(element, "haptic_strength", 100)
                };
            }

            if (legacy.Count == 0)
            {
                return;
            }

            bool migrated = false;
            foreach (SpecialAction action in Settings.Actions)
            {
                if (action.Effects.Count > 0 || !legacy.TryGetValue(action.Id, out SpecialActionEffect? effect))
                {
                    continue;
                }

                action.Effects.Add(effect);
                migrated = true;
            }

            if (migrated)
            {
                _log.Info("Migrated legacy special actions to the effects format");
                Save();
            }
        }
        catch (JsonException)
        {
            // The store already fell back to defaults for an invalid file; there is
            // nothing to migrate from malformed legacy data.
            _log.Debug("Legacy special actions file is not valid JSON; nothing to migrate");
        }
        catch (Exception ex)
        {
            _log.Warning("Failed to migrate legacy special actions");
            _log.LogExceptionDetails(ex);
        }
    }

    /// <summary>
    /// Reads a byte property from a legacy action element, falling back to
    /// <paramref name="fallback"/> when missing or unparsable.
    /// </summary>
    private static byte ByteProperty(JsonElement element, string name, byte fallback = 0)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.TryGetByte(out byte result) ? result : fallback;
    }

    /// <summary>
    /// Reads a boolean property from a legacy action element.
    /// </summary>
    private static bool BoolProperty(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Reads an integer property from a legacy action element, falling back to
    /// <paramref name="fallback"/> when missing or unparsable.
    /// </summary>
    private static int IntProperty(JsonElement element, string name, int fallback)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result) ? result : fallback;
    }

    /// <summary>
    /// Gets the normalized identifier of a controller used to match it against
    /// <see cref="SpecialAction.EnabledControllers"/>: the Bluetooth MAC address when
    /// available, otherwise the HID device path.
    /// </summary>
    /// <param name="mac">The controller's Bluetooth MAC address, or <c>null</c>/empty when unavailable.</param>
    /// <param name="devicePath">The controller's HID device path, or <c>null</c>/empty when unavailable.</param>
    public static string? GetControllerId(string? mac, string? devicePath)
    {
        string normalizedMac = mac?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!string.IsNullOrEmpty(normalizedMac))
        {
            return normalizedMac;
        }

        string normalizedPath = devicePath?.Trim() ?? string.Empty;
        return string.IsNullOrEmpty(normalizedPath) ? null : normalizedPath;
    }

    /// <summary>
    /// Whether an action is enabled for the given controller.
    /// </summary>
    /// <param name="action">The action to check.</param>
    /// <param name="controllerId">The controller identifier (see <see cref="GetControllerId"/>).</param>
    public static bool IsEnabledFor(SpecialAction action, string? controllerId)
    {
        if (action is null || string.IsNullOrWhiteSpace(controllerId))
        {
            return false;
        }

        return action.EnabledControllers.Any(c => string.Equals(c, controllerId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Derives a unique action name from <paramref name="baseName"/> by appending a
    /// counter when the base name is already in use.
    /// </summary>
    private string GetUniqueName(string? baseName)
    {
        string candidate = string.IsNullOrWhiteSpace(baseName) ? DefaultActionName : baseName.Trim();
        if (Settings.Actions.All(a => !string.Equals(a.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            return candidate;
        }

        int suffix = 2;
        while (Settings.Actions.Any(a => string.Equals(a.Name, $"{candidate} {suffix}", StringComparison.OrdinalIgnoreCase)))
        {
            suffix++;
        }
        return $"{candidate} {suffix}";
    }
}