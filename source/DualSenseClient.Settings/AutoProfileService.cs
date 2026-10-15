using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseClient.Core.Utilities;
using DualSenseClient.Logging;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Settings;

/// <summary>
/// Service for managing foreground-app auto profile rules with JSON file persistence.
/// Rules map a focused program (exe path and optional window title) to a profile,
/// emulation mode and variant, trigger effects, and hiding per controller. The rules themselves are stored here; the per-controller
/// bindings they temporarily override live in <see cref="ControllerInfoService"/>.
/// </summary>
/// <remarks>
/// This is a separate settings service from <see cref="SettingsService"/>: it persists to
/// <c>auto_profiles.json</c> (in the same folder as <c>config.json</c>) and reuses
/// <see cref="JsonFileStore{T}"/> for lenient deserialization, backups, and thread-safe access.
/// </remarks>
public sealed class AutoProfileService
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private readonly DualSenseClientLogger _log = DualSenseClientLogger.For("AutoProfiles");

    /// <summary>
    /// The JSON file store backing this service.
    /// </summary>
    private readonly JsonFileStore<AutoProfileSettings> _store;

    /// <summary>
    /// Synchronizes access to rule load/save operations.
    /// </summary>
    private readonly Lock _lock = new Lock();

    /// <summary>
    /// Whether rules have been loaded from disk at least once.
    /// </summary>
    private bool _loaded;

    /// <summary>
    /// Gets the currently loaded auto profile settings.
    /// Loads them from persistent storage if not yet initialized.
    /// </summary>
    public AutoProfileSettings Settings
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
    /// Occurs when auto profile rules have been saved to persistent storage.
    /// </summary>
    public event EventHandler? AutoProfilesChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoProfileService"/> class.
    /// Rules are stored at <c>{PathResolver.BaseDirectory}/Config/auto_profiles.json</c>.
    /// </summary>
    public AutoProfileService() : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoProfileService"/> class
    /// with custom JSON serialization options.
    /// </summary>
    /// <param name="jsonOptions">
    /// Custom JSON serialization options.
    /// If <c>null</c>, default options with <see cref="JsonStringEnumConverter"/> are used.
    /// </param>
    /// <param name="autoProfilesPath">
    /// The full path to the auto profiles JSON file.
    /// If <c>null</c>, defaults to <c>{PathResolver.BaseDirectory}/Config/auto_profiles.json</c>.
    /// </param>
    public AutoProfileService(JsonSerializerOptions? jsonOptions = null, string? autoProfilesPath = null)
    {
        string path = autoProfilesPath
                      ?? PathResolver.GetFullPath("Config", "auto_profiles.json");
        _store = new JsonFileStore<AutoProfileSettings>(path, jsonOptions)
        {
            WriteDefaultsWhenMissing = true,
            BackupBeforeSave = true
        };
        _log.Debug($"Auto profile store initialized at '{path}'");
    }

    /// <summary>
    /// Loads auto profile rules from persistent storage.
    /// Falls back to defaults if the file does not exist or is invalid.
    /// Legacy 0-255 trigger values are scaled once into the official 0-9 / 0-8 ranges.
    /// </summary>
    public void Load()
    {
        lock (_lock)
        {
            _log.Info($"Loading auto profiles from '{_store.FilePath}'");
            _store.Load();
            NormalizeTriggerRanges();
            _loaded = true;
        }
    }

    /// <summary>
    /// Scales legacy trigger values into the official ranges. Rules saved with the old
    /// 0-255 layout (resistance/trigger/automatic) carry positions up to 255 and forces
    /// up to 255; official modes use position 0-9 and strength 0-8. Values already in
    /// range pass through untouched, so this is idempotent.
    /// </summary>
    private void NormalizeTriggerRanges()
    {
        foreach (AutoProfileRule rule in _store.Item.Rules)
        {
            NormalizeTriggerSettings(rule.LeftTrigger);
            NormalizeTriggerSettings(rule.RightTrigger);
        }
    }

    private static void NormalizeTriggerSettings(TriggerEffectSettings? settings)
    {
        if (settings is null)
        {
            return;
        }

#pragma warning disable CS0618 // Legacy aliases are intentional for old rule files.
        if (settings.Mode == AutoProfileTriggerMode.Resistance)
        {
            settings.Mode = AutoProfileTriggerMode.Feedback;
        }
        else if (settings.Mode == AutoProfileTriggerMode.Trigger)
        {
            settings.Mode = AutoProfileTriggerMode.Weapon;
        }
        else if (settings.Mode == AutoProfileTriggerMode.Automatic)
        {
            settings.Mode = AutoProfileTriggerMode.Vibration;
        }
#pragma warning restore CS0618

        if (settings.Start > 9)
        {
            settings.Start = (int)Math.Round(settings.Start * 9.0 / 255);
        }

        if (settings.End > 9)
        {
            settings.End = (int)Math.Round(settings.End * 9.0 / 255);
        }

        if (settings.Force > 8)
        {
            settings.Force = (int)Math.Round(settings.Force * 8.0 / 255);
        }

        settings.Start = Math.Clamp(settings.Start, 0, 9);
        settings.End = Math.Clamp(settings.End, 0, 9);
        settings.Force = Math.Clamp(settings.Force, 0, 8);
        settings.Frequency = Math.Clamp(settings.Frequency, 0, 255);
        settings.SnapForce = Math.Clamp(settings.SnapForce, 0, 8);
        settings.FirstFoot = Math.Clamp(settings.FirstFoot, 0, 6);
        settings.SecondFoot = Math.Clamp(settings.SecondFoot, 0, 7);
        settings.AmplitudeB = Math.Clamp(settings.AmplitudeB, 0, 7);
        settings.Period = Math.Clamp(settings.Period, 0, 255);
        settings.EndStrength = Math.Clamp(settings.EndStrength, 0, 8);
    }

    /// <summary>
    /// Saves the current auto profile settings to persistent storage.
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            _log.Debug($"Saving auto profiles to '{_store.FilePath}'");
            _store.Save();
            AutoProfilesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Captures a stable rule list for the background watcher.
    /// The settings list can be edited from the UI thread while the watcher evaluates it.
    /// </summary>
    public AutoProfileRule[] GetSnapshot()
    {
        lock (_lock)
        {
            return Settings.Rules.ToArray();
        }
    }

    /// <summary>
    /// Finds the first actionable rule matching the foreground program for a controller
    /// (rules are evaluated in list order), or <c>null</c> when nothing matches.
    /// Rules leaving profile, emulation, and hiding unchanged never match, so they cannot
    /// shadow lower rules.
    /// </summary>
    /// <param name="foregroundExe">Full executable path of the foreground process.</param>
    /// <param name="foregroundTitle">Title of the foreground window.</param>
    /// <param name="mac">The controller's Bluetooth MAC address, or <c>null</c>/empty when unavailable.</param>
    /// <param name="devicePath">The controller's HID device path, or <c>null</c>/empty when unavailable.</param>
    public AutoProfileRule? FindMatch(string? foregroundExe, string? foregroundTitle, string? mac, string? devicePath)
    {
        if (!Settings.Enabled)
        {
            return null;
        }

        foreach (AutoProfileRule rule in GetSnapshot())
        {
            if (!rule.IsActionless && rule.MatchesController(mac, devicePath) && rule.IsMatch(foregroundExe, foregroundTitle))
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>
    /// Updates the stored profile name on every rule after a profile is renamed,
    /// so rules keep applying it. Persists only when something changed.
    /// </summary>
    /// <param name="oldName">The previous profile name.</param>
    /// <param name="newName">The new profile name.</param>
    public void UpdateProfileName(string oldName, string newName)
    {
        bool changed = false;
        foreach (AutoProfileRule rule in Settings.Rules)
        {
            if (string.Equals(rule.ProfileName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                rule.ProfileName = newName;
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>
    /// Clears the stored profile name on every rule referencing a deleted profile,
    /// so those rules leave the profile unchanged. Persists only when something changed.
    /// </summary>
    /// <param name="profileName">The name of the deleted profile.</param>
    public void RemoveProfileReferences(string profileName)
    {
        bool changed = false;
        foreach (AutoProfileRule rule in Settings.Rules)
        {
            if (string.Equals(rule.ProfileName, profileName, StringComparison.OrdinalIgnoreCase))
            {
                rule.ProfileName = string.Empty;
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>
    /// Adds a rule and persists the change. The added rule is returned.
    /// </summary>
    public AutoProfileRule AddRule(AutoProfileRule rule)
    {
        Settings.Rules.Add(rule);
        Save();
        return rule;
    }

    /// <summary>
    /// Removes a rule and persists the change.
    /// </summary>
    /// <returns><c>true</c> when the rule was present and removed.</returns>
    public bool RemoveRule(AutoProfileRule rule)
    {
        bool removed = Settings.Rules.Remove(rule);
        if (removed)
        {
            Save();
        }

        return removed;
    }
}