using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// Settings for GitHub release update checks (stable vs nightly channel
/// and the daily auto-check).
/// </summary>
public class UpdateSettings
{
    /// <summary>
    /// Gets or sets whether the application automatically checks GitHub for updates
    /// once per calendar day during startup.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>false</c>.
    /// </remarks>
    [JsonPropertyName("autoCheck")]
    public bool AutomaticCheck { get; set; }

    /// <summary>
    /// Gets or sets whether update checks target nightly (pre-release) builds
    /// instead of stable releases. Off means stable, on means nightly.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>false</c> (stable).
    /// </remarks>
    [JsonPropertyName("nightly")]
    public bool NightlyVersion { get; set; }

    /// <summary>
    /// Gets or sets the calendar day of the last update check, like the daily log
    /// rotation in <c>FileLogSink</c>. Compared against <see cref="DateOnly.Today"/>.
    /// </summary>
    [JsonPropertyName("lastUpdateCheck")]
    public DateOnly LastUpdateCheck { get; set; }

    /// <summary>
    /// Gets or sets the version string (<see cref="Core.Utilities.AppInfo.VersionWithCommit"/>)
    /// that last ran. Used as the base for the post-update changelog.
    /// Empty on first run (stamped silently).
    /// </summary>
    [JsonPropertyName("lastSeenVersion")]
    public string LastSeenVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether an in-app update was installed and its changelog
    /// still has to be shown. Set when applying an update, cleared when the
    /// what's-new popup is handled on the next launch.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>false</c>.
    /// </remarks>
    [JsonPropertyName("pendingChangelog")]
    public bool PendingChangelog { get; set; }
}