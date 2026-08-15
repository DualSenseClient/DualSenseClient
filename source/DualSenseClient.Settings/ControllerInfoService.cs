using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseClient.Core.Utilities;
using DualSenseClient.Logging;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Settings;

/// <summary>
/// Service for managing persistent controller information with JSON file persistence.
/// Stores a display name (renamable by the user), Bluetooth MAC address, HID device path,
/// and bound profile name per controller. Replaces the profile-to-controller bindings that
/// used to live inside <see cref="ProfileService"/>.
/// </summary>
/// <remarks>
/// This is a separate settings service from <see cref="SettingsService"/>: it persists to
/// <c>controllers.json</c> (in the same folder as <c>config.json</c>) and reuses
/// <see cref="JsonFileStore{T}"/> for lenient deserialization, backups, and thread-safe access.
/// Controllers are identified by MAC address first, falling back to the HID device path.
/// </remarks>
public sealed class ControllerInfoService
{
    /// <summary>
    /// Maximum length of a controller's custom display name (characters).
    /// </summary>
    public const int MaxNameLength = 43;

    /// <summary>
    /// Logger instance.
    /// </summary>
    private readonly DualSenseClientLogger _log = DualSenseClientLogger.For("Controllers");

    /// <summary>
    /// The JSON file store backing this service.
    /// </summary>
    private readonly JsonFileStore<ControllerInfoSettings> _store;

    /// <summary>
    /// Synchronizes access to controller load/save operations.
    /// </summary>
    private readonly Lock _lock = new Lock();

    /// <summary>
    /// Whether controller info has been loaded from disk at least once.
    /// </summary>
    private bool _loaded;

    /// <summary>
    /// Gets the currently loaded controller settings.
    /// Loads them from persistent storage if not yet initialized.
    /// </summary>
    public ControllerInfoSettings Settings
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
    /// Occurs when controller info has been saved to persistent storage.
    /// </summary>
    public event EventHandler? ControllersChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="ControllerInfoService"/> class.
    /// Controller info is stored at <c>{PathResolver.BaseDirectory}/Config/controllers.json</c>.
    /// </summary>
    public ControllerInfoService() : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ControllerInfoService"/> class
    /// with custom JSON serialization options.
    /// </summary>
    /// <param name="jsonOptions">
    /// Custom JSON serialization options.
    /// If <c>null</c>, default options with <see cref="JsonStringEnumConverter"/> are used.
    /// </param>
    /// <param name="controllersPath">
    /// The full path to the controllers JSON file.
    /// If <c>null</c>, defaults to <c>{PathResolver.BaseDirectory}/Config/controllers.json</c>.
    /// </param>
    public ControllerInfoService(JsonSerializerOptions? jsonOptions = null, string? controllersPath = null)
    {
        string path = controllersPath
                      ?? PathResolver.GetFullPath("Config", "controllers.json");
        _store = new JsonFileStore<ControllerInfoSettings>(path, jsonOptions)
        {
            WriteDefaultsWhenMissing = true,
            BackupBeforeSave = true
        };
        _log.Debug($"Controller store initialized at '{path}'");
    }

    /// <summary>
    /// Loads controller info from persistent storage.
    /// Falls back to defaults if the file does not exist or is invalid.
    /// </summary>
    public void Load()
    {
        lock (_lock)
        {
            _log.Info($"Loading controller info from '{_store.FilePath}'");
            _store.Load();
            _loaded = true;
        }
    }

    /// <summary>
    /// Saves the current controller settings to persistent storage.
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            _log.Debug($"Saving controller info to '{_store.FilePath}'");
            _store.Save();
            ControllersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets the stored info for a controller, looked up by MAC address first and then by
    /// HID device path, or <c>null</c> when no entry matches.
    /// </summary>
    /// <param name="mac">The controller's Bluetooth MAC address, or <c>null</c>/empty when unavailable.</param>
    /// <param name="devicePath">The controller's HID device path, or <c>null</c>/empty when unavailable.</param>
    public ControllerInfo? GetControllerInfo(string? mac, string? devicePath)
    {
        return FindController(NormalizeMac(mac), NormalizePath(devicePath));
    }

    /// <summary>
    /// Gets the display name of a controller: its stored custom name, or
    /// <paramref name="fallback"/> (typically the product name) when no entry or no
    /// custom name exists.
    /// </summary>
    /// <param name="mac">The controller's Bluetooth MAC address, or <c>null</c>/empty when unavailable.</param>
    /// <param name="devicePath">The controller's HID device path, or <c>null</c>/empty when unavailable.</param>
    /// <param name="fallback">The name to return when the controller has no custom name.</param>
    public string GetDisplayName(string? mac, string? devicePath, string fallback)
    {
        ControllerInfo? info = FindController(NormalizeMac(mac), NormalizePath(devicePath));
        return info is null || string.IsNullOrEmpty(info.Name) ? fallback : info.Name;
    }

    /// <summary>
    /// Registers a connected controller so it can be renamed and assigned a profile.
    /// Creates an entry (with <paramref name="name"/> as the initial display name) when the
    /// controller is new; existing entries keep their stored name and gain any missing
    /// identifiers. Nothing is saved when the controller is already registered.
    /// </summary>
    /// <param name="mac">The controller's Bluetooth MAC address, or <c>null</c>/empty when unavailable.</param>
    /// <param name="devicePath">The controller's HID device path.</param>
    /// <param name="name">The default display name (e.g. the product name).</param>
    public void RegisterController(string? mac, string? devicePath, string? name)
    {
        string normalizedMac = NormalizeMac(mac);
        string normalizedPath = NormalizePath(devicePath);
        if (string.IsNullOrEmpty(normalizedMac) && string.IsNullOrEmpty(normalizedPath))
        {
            return;
        }

        ControllerInfo? existing = FindController(normalizedMac, normalizedPath);
        if (existing is not null)
        {
            if (!string.IsNullOrEmpty(normalizedMac))
            {
                existing.MacAddress = normalizedMac;
            }
            if (!string.IsNullOrEmpty(normalizedPath))
            {
                existing.DevicePath = normalizedPath;
            }
            return;
        }

        Settings.Controllers.Add(new ControllerInfo
        {
            Name = name ?? string.Empty,
            MacAddress = normalizedMac,
            DevicePath = normalizedPath
        });
        _log.Debug($"Registered controller '{name}' ({normalizedMac} / {normalizedPath})");
        Save();
    }

    /// <summary>
    /// Gets the name of the profile bound to a controller, or <c>null</c> when the
    /// controller has no entry or no bound profile.
    /// </summary>
    /// <param name="mac">The controller's Bluetooth MAC address, or <c>null</c>/empty when unavailable.</param>
    /// <param name="devicePath">The controller's HID device path, or <c>null</c>/empty when unavailable.</param>
    public string? GetBoundProfileName(string? mac, string? devicePath)
    {
        ControllerInfo? info = FindController(NormalizeMac(mac), NormalizePath(devicePath));
        return info is null || string.IsNullOrEmpty(info.ProfileName) ? null : info.ProfileName;
    }

    /// <summary>
    /// Sets (or clears) the profile bound to a controller and persists the change.
    /// The controller's entry is created if not registered yet.
    /// </summary>
    /// <param name="mac">The controller's Bluetooth MAC address, or <c>null</c>/empty when unavailable.</param>
    /// <param name="devicePath">The controller's HID device path, or <c>null</c>/empty when unavailable.</param>
    /// <param name="profileName">The profile name to bind, or <c>null</c>/empty to unbind.</param>
    public void SetControllerProfile(string? mac, string? devicePath, string? profileName)
    {
        string normalizedMac = NormalizeMac(mac);
        string normalizedPath = NormalizePath(devicePath);
        if (string.IsNullOrEmpty(normalizedMac) && string.IsNullOrEmpty(normalizedPath))
        {
            return;
        }

        ControllerInfo? info = FindController(normalizedMac, normalizedPath);
        if (info is null)
        {
            info = new ControllerInfo();
            Settings.Controllers.Add(info);
        }

        info.MacAddress = normalizedMac;
        info.DevicePath = normalizedPath;
        info.ProfileName = profileName ?? string.Empty;
        Save();
    }

    /// <summary>
    /// Gets the virtual controller emulation settings stored for a controller, or the
    /// defaults (emulation off) when the controller has no entry or no stored settings.
    /// The returned instance is the live stored object; persist changes with
    /// <see cref="SaveEmulationSettings"/>.
    /// </summary>
    /// <param name="mac">The controller's Bluetooth MAC address, or <c>null</c>/empty when unavailable.</param>
    /// <param name="devicePath">The controller's HID device path, or <c>null</c>/empty when unavailable.</param>
    public EmulationSettings GetEmulationSettings(string? mac, string? devicePath)
    {
        ControllerInfo? info = FindController(NormalizeMac(mac), NormalizePath(devicePath));
        return info?.Emulation ?? new EmulationSettings();
    }

    /// <summary>
    /// Stores the virtual controller emulation settings for a controller and persists
    /// the change. The controller's entry is created if not registered yet.
    /// </summary>
    /// <param name="mac">The controller's Bluetooth MAC address, or <c>null</c>/empty when unavailable.</param>
    /// <param name="devicePath">The controller's HID device path, or <c>null</c>/empty when unavailable.</param>
    /// <param name="emulation">The emulation settings to store.</param>
    public void SaveEmulationSettings(string? mac, string? devicePath, EmulationSettings emulation)
    {
        string normalizedMac = NormalizeMac(mac);
        string normalizedPath = NormalizePath(devicePath);
        if (string.IsNullOrEmpty(normalizedMac) && string.IsNullOrEmpty(normalizedPath))
        {
            return;
        }

        ControllerInfo? info = FindController(normalizedMac, normalizedPath);
        if (info is null)
        {
            info = new ControllerInfo();
            Settings.Controllers.Add(info);
        }

        info.MacAddress = normalizedMac;
        info.DevicePath = normalizedPath;
        info.Emulation = emulation;
        Save();
    }

    /// <summary>
    /// Renames a controller (its stored display name) and persists the change.
    /// </summary>
    /// <param name="mac">The controller's Bluetooth MAC address, or <c>null</c>/empty when unavailable.</param>
    /// <param name="devicePath">The controller's HID device path, or <c>null</c>/empty when unavailable.</param>
    /// <param name="newName">The desired display name.</param>
    /// <returns><c>true</c> if the rename was applied, <c>false</c> when the name is empty,
    /// longer than <see cref="MaxNameLength"/>, unchanged, or the controller has no entry.</returns>
    public bool RenameController(string? mac, string? devicePath, string? newName)
    {
        string trimmed = newName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength)
        {
            return false;
        }

        ControllerInfo? info = FindController(NormalizeMac(mac), NormalizePath(devicePath));
        if (info is null || string.Equals(info.Name, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        info.Name = trimmed;
        Save();
        return true;
    }

    /// <summary>
    /// Updates the stored profile name on every controller entry after a profile is renamed,
    /// so controllers keep their assignment. Persists only when something changed.
    /// </summary>
    /// <param name="oldName">The previous profile name.</param>
    /// <param name="newName">The new profile name.</param>
    public void UpdateProfileName(string oldName, string newName)
    {
        bool changed = false;
        foreach (ControllerInfo info in Settings.Controllers)
        {
            if (string.Equals(info.ProfileName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                info.ProfileName = newName;
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>
    /// Clears the stored profile name on every controller entry referencing a deleted
    /// profile, so those controllers fall back to the default profile. The controller
    /// entries themselves (name, identifiers) are kept. Persists only when something changed.
    /// </summary>
    /// <param name="profileName">The name of the deleted profile.</param>
    public void RemoveProfileReferences(string profileName)
    {
        bool changed = false;
        foreach (ControllerInfo info in Settings.Controllers)
        {
            if (string.Equals(info.ProfileName, profileName, StringComparison.OrdinalIgnoreCase))
            {
                info.ProfileName = string.Empty;
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>
    /// Finds a controller entry by normalized MAC address first, falling back to the
    /// normalized HID device path, or <c>null</c> when nothing matches.
    /// </summary>
    private ControllerInfo? FindController(string normalizedMac, string normalizedPath)
    {
        foreach (ControllerInfo info in Settings.Controllers)
        {
            if (!string.IsNullOrEmpty(normalizedMac)
                && string.Equals(NormalizeMac(info.MacAddress), normalizedMac, StringComparison.Ordinal))
            {
                return info;
            }
        }

        foreach (ControllerInfo info in Settings.Controllers)
        {
            if (!string.IsNullOrEmpty(normalizedPath)
                && string.Equals(NormalizePath(info.DevicePath), normalizedPath, StringComparison.Ordinal))
            {
                return info;
            }
        }

        return null;
    }

    /// <summary>
    /// Normalizes a MAC address to uppercase trimmed form so lookups are case-insensitive.
    /// </summary>
    private static string NormalizeMac(string? mac) => mac?.Trim().ToUpperInvariant() ?? string.Empty;

    /// <summary>
    /// Normalizes an HID device path by trimming surrounding whitespace.
    /// </summary>
    private static string NormalizePath(string? devicePath) => devicePath?.Trim() ?? string.Empty;
}