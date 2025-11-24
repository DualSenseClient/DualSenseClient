using System.Reflection;
using System.Text.Json.Serialization;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Settings.Models;

namespace DualSenseClient.Core.Settings;

public class ApplicationSettingsStore
{
    [JsonPropertyName("controllers")]
    public ControllerSettings Controllers { get; set; } = new ControllerSettings();

    [JsonPropertyName("ui")]
    public UiSettings Ui { get; set; } = new UiSettings();

    [JsonPropertyName("debug")]
    public DebuggingSettings Debug { get; set; } = new DebuggingSettings();

    [JsonPropertyName("update_check")]
    public UpdateCheckSettings UpdateCheck { get; set; } = new UpdateCheckSettings();

    public string GetVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        if (UpdateCheck.NightlyBuild)
        {
            try
            {
                // Return informational version for nightly release
                AssemblyInformationalVersionAttribute? informationalVersionAttribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (informationalVersionAttribute?.InformationalVersion != null)
                {
                    return informationalVersionAttribute.InformationalVersion.Split('+')[0];
                }
            }
            catch (Exception ex)
            {
                Logger.Error<SettingsManager>("Failed to fetch nightly version");
                Logger.LogExceptionDetails<SettingsManager>(ex);
                return "0.0.0";
            }
        }

        try
        {
            // Return assembly version for stable release
            // Get first 3 components, use 0 for missing parts
            Version? version = assembly.GetName().Version;
            return version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch (Exception ex)
        {
            Logger.Error<SettingsManager>("Failed to fetch stable version");
            Logger.LogExceptionDetails<SettingsManager>(ex);
            return "0.0.0";
        }
    }

    public string GetVersionWithCommit()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        // Try to get the informational version which includes the git commit
        AssemblyInformationalVersionAttribute? informationalVersionAttribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        string? informationalVersion = informationalVersionAttribute?.InformationalVersion;

        // If we have an informational version with commit hash (includes +)
        Version? version = assembly.GetName().Version;
        if (version != null && !string.IsNullOrEmpty(informationalVersion) && informationalVersion.Contains('+'))
        {
#if DEBUG
            string commit = "DEBUG";
#else
            string commit = informationalVersion.Split('+')[0];
#endif
            return $"v{version.Major}.{version.Minor}.{version.Build} ({commit})";
        }

        // If there's no commit info, just return the normal version
        return GetVersion();
    }
}