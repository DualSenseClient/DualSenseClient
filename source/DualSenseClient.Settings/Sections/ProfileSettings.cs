using System.Text.Json.Serialization;

namespace DualSenseClient.Settings.Sections;

/// <summary>
/// The root settings class for the profile manager.
/// Persisted to <c>profiles.json</c> next to the application's <c>config.json</c>.
/// </summary>
public class ProfileSettings
{
    /// <summary>
    /// Name of the profile used by default for controllers without an explicit binding.
    /// </summary>
    public const string DefaultProfileName = "Default";

    /// <summary>
    /// Gets or sets all saved controller profiles.
    /// </summary>
    [JsonPropertyName("profiles")]
    public List<Profile> Profiles { get; set; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileSettings"/> class,
    /// seeding the <see cref="DefaultProfileName"/> profile (blue lightbar, everything
    /// else off) when it is missing so a baseline profile is always available.
    /// </summary>
    public ProfileSettings()
    {
        EnsureDefaultProfile();
    }

    /// <summary>
    /// Inserts the default profile at the front of <see cref="Profiles"/> when no profile
    /// named <see cref="DefaultProfileName"/> exists. The profile's section defaults already
    /// describe a blue lightbar (red 0, green 0, blue 255) with the mic LED and player LEDs off.
    /// </summary>
    private void EnsureDefaultProfile()
    {
        if (Profiles.Any(p => string.Equals(p.Name, DefaultProfileName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Profiles.Insert(0, new Profile { Name = DefaultProfileName });
    }
}