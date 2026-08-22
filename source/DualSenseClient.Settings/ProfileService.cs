using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseClient.Core.Utilities;
using DualSenseClient.Logging;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Settings;

/// <summary>
/// Service for managing controller profiles with JSON file persistence.
/// Stores named profiles (lightbar color, microphone LED mode, and player LED layout).
/// Controller-to-profile assignments are stored separately by
/// <see cref="ControllerInfoService"/>.
/// </summary>
/// <remarks>
/// This is a separate settings service from <see cref="SettingsService"/>: it persists to
/// <c>profiles.json</c> (in the same folder as <c>config.json</c>) and reuses
/// <see cref="JsonFileStore{T}"/> for lenient deserialization, backups, and thread-safe access.
/// </remarks>
public sealed class ProfileService
{
    /// <summary>
    /// Name of the default profile used by controllers without an explicit binding.
    /// </summary>
    public const string DefaultProfileName = ProfileSettings.DefaultProfileName;

    /// <summary>
    /// Logger instance.
    /// </summary>
    private readonly DualSenseClientLogger _log = DualSenseClientLogger.For("Profiles");

    /// <summary>
    /// The JSON file store backing this service.
    /// </summary>
    private readonly JsonFileStore<ProfileSettings> _store;

    /// <summary>
    /// Synchronizes access to profile load/save operations.
    /// </summary>
    private readonly Lock _lock = new Lock();

    /// <summary>
    /// Whether profiles have been loaded from disk at least once.
    /// </summary>
    private bool _loaded;

    /// <summary>
    /// Gets the currently loaded profile settings.
    /// Loads them from persistent storage if not yet initialized.
    /// </summary>
    public ProfileSettings Settings
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
    /// Occurs when profiles have been saved to persistent storage.
    /// </summary>
    public event EventHandler? ProfilesChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileService"/> class.
    /// Profiles are stored at <c>{PathResolver.BaseDirectory}/Config/profiles.json</c>.
    /// </summary>
    public ProfileService() : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileService"/> class
    /// with custom JSON serialization options.
    /// </summary>
    /// <param name="jsonOptions">
    /// Custom JSON serialization options.
    /// If <c>null</c>, default options with <see cref="JsonStringEnumConverter"/> are used.
    /// </param>
    /// <param name="profilesPath">
    /// The full path to the profiles JSON file.
    /// If <c>null</c>, defaults to <c>{PathResolver.BaseDirectory}/Config/profiles.json</c>.
    /// </param>
    public ProfileService(JsonSerializerOptions? jsonOptions = null, string? profilesPath = null)
    {
        string path = profilesPath
                      ?? PathResolver.GetFullPath("Config", "profiles.json");
        _store = new JsonFileStore<ProfileSettings>(path, jsonOptions)
        {
            WriteDefaultsWhenMissing = true,
            BackupBeforeSave = true
        };
        _log.Debug($"Profile store initialized at '{path}'");
    }

    /// <summary>
    /// Loads profiles from persistent storage.
    /// Falls back to defaults if the file does not exist or is invalid.
    /// </summary>
    public void Load()
    {
        lock (_lock)
        {
            _log.Info($"Loading profiles from '{_store.FilePath}'");
            _store.Load();
            _loaded = true;
            EnsureDefaultProfile();
        }
    }

    /// <summary>
    /// Inserts the <see cref="DefaultProfileName"/> profile at the front of the list when it
    /// is missing, so a baseline profile is always available for unbound controllers.
    /// </summary>
    private void EnsureDefaultProfile()
    {
        if (Settings.Profiles.Any(p => string.Equals(p.Name, DefaultProfileName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Settings.Profiles.Insert(0, new Profile
        {
            Name = DefaultProfileName
        });
        _log.Debug("Seeded default profile");
    }

    /// <summary>
    /// Saves the current profile settings to persistent storage.
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            _log.Debug($"Saving profiles to '{_store.FilePath}'");
            _store.Save();
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets a profile by name (case-insensitive), or <c>null</c> if not found.
    /// </summary>
    /// <param name="name">The profile name to look up.</param>
    public Profile? GetProfile(string name)
    {
        return Settings.Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a new profile with a unique name derived from <paramref name="baseName"/>
    /// (e.g. "Profile", "Profile 2") and persists it. The new profile is returned.
    /// </summary>
    /// <param name="baseName">The base name for the new profile.</param>
    public Profile CreateProfile(string baseName = "Profile")
    {
        Profile profile = new Profile
        {
            Name = GetUniqueProfileName(baseName)
        };
        Settings.Profiles.Add(profile);
        Save();
        return profile;
    }

    /// <summary>
    /// Creates a copy of an existing profile (lightbar color, microphone LED mode, and
    /// player LED layout) under a unique name derived from the source ("Name Copy",
    /// "Name Copy 2", ...) and persists it. The copy is returned, or <c>null</c> when the
    /// source profile does not exist.
    /// </summary>
    /// <param name="name">The name of the profile to duplicate.</param>
    public Profile? DuplicateProfile(string name)
    {
        Profile? source = GetProfile(name);
        if (source is null)
        {
            return null;
        }

        Profile copy = new Profile
        {
            Name = GetUniqueProfileName($"{source.Name} Copy"),
            Lightbar =
            {
                Red = source.Lightbar.Red,
                Green = source.Lightbar.Green,
                Blue = source.Lightbar.Blue
            },
            MicLed =
            {
                Mode = source.MicLed.Mode
            },
            PlayerLeds =
            {
                Mask = source.PlayerLeds.Mask
            }
        };
        Settings.Profiles.Add(copy);
        Save();
        return copy;
    }

    /// <summary>
    /// Renames a profile in memory without persisting. Callers that want the rename
    /// persisted should call <see cref="RenameProfile"/> instead, or schedule their own save.
    /// </summary>
    /// <param name="oldName">The current profile name.</param>
    /// <param name="newName">The desired profile name.</param>
    /// <returns><c>true</c> if the rename was applied, <c>false</c> when the name is
    /// empty or already taken by another profile.</returns>
    public bool RenameProfileInMemory(string oldName, string newName)
    {
        string trimmed = newName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        Profile? profile = GetProfile(oldName);
        if (profile is null || string.Equals(profile.Name, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (GetProfile(trimmed) is not null)
        {
            return false;
        }

        profile.Name = trimmed;

        return true;
    }

    /// <summary>
    /// Renames a profile.
    /// </summary>
    /// <param name="oldName">The current profile name.</param>
    /// <param name="newName">The desired profile name.</param>
    /// <returns><c>true</c> if the rename was applied, <c>false</c> when the name is
    /// empty or already taken by another profile.</returns>
    public bool RenameProfile(string oldName, string newName)
    {
        if (!RenameProfileInMemory(oldName, newName))
        {
            return false;
        }

        Save();
        return true;
    }

    /// <summary>
    /// Deletes a profile. The <see cref="DefaultProfileName"/> profile is re-seeded when it
    /// is deleted so a fallback profile is always available for controllers using a
    /// deleted profile. Callers should also notify <see cref="ControllerInfoService"/>
    /// to clear controller assignments referencing the deleted profile.
    /// </summary>
    /// <param name="name">The profile name to delete.</param>
    /// <returns><c>true</c> if the profile was deleted, <c>false</c> if it did not exist.</returns>
    public bool DeleteProfile(string name)
    {
        Profile? profile = GetProfile(name);
        if (profile is null)
        {
            return false;
        }

        Settings.Profiles.Remove(profile);
        EnsureDefaultProfile();
        Save();
        return true;
    }

    /// <summary>
    /// Derives a unique profile name from <paramref name="baseName"/> by appending a
    /// counter when the base name is already in use.
    /// </summary>
    private string GetUniqueProfileName(string baseName)
    {
        string candidate = string.IsNullOrWhiteSpace(baseName) ? "Profile" : baseName.Trim();
        if (Settings.Profiles.All(p => !string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            return candidate;
        }

        int suffix = 2;
        while (Settings.Profiles.Any(p => string.Equals(p.Name, $"{candidate} {suffix}", StringComparison.OrdinalIgnoreCase)))
        {
            suffix++;
        }

        return $"{candidate} {suffix}";
    }
}