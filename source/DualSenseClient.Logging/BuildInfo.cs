using System.Reflection;

namespace DualSenseClient.Logging;

/// <summary>
/// Provides diagnostic build provenance and runtime environment information.
/// Generates a multi-line banner string from assembly metadata attributes
/// that is typically printed at application startup.
/// </summary>
/// <remarks>
/// <para>
/// The banner includes the assembly name, version, build configuration,
/// optional commit SHA and branch from CI metadata, runtime framework
/// description, and operating system version.
/// </para>
/// <para>
/// CI systems can populate custom metadata by adding
/// <c>AssemblyMetadata</c> attributes to the entry assembly:
/// <code>
/// [assembly: AssemblyMetadata("CommitSha", "abc1234")]
/// [assembly: AssemblyMetadata("Branch", "main")]
/// </code>
/// </para>
/// </remarks>
public static class BuildInfo
{
    private static string? _banner;

    /// <summary>
    /// Gets a multi-line diagnostic banner describing the application build
    /// and runtime environment. The result is cached after the first access.
    /// </summary>
    public static string Banner
    {
        get
        {
            if (_banner is not null)
                return _banner;

            Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();
            AssemblyName name = assembly.GetName();

            string version = name.Version?.ToString() ?? "0.0.0.0";
            string config = assembly.GetCustomAttributes(typeof(AssemblyConfigurationAttribute), false)
                .Cast<AssemblyConfigurationAttribute>().FirstOrDefault()?.Configuration ?? "Unknown";
            string commitSha = assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false)
                .Cast<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "CommitSha")?.Value ?? "";
            string branch = assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false)
                .Cast<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "Branch")?.Value ?? "";

            List<string> lines = new(4)
            {
                $"{name.Name} v{version} ({config})"
            };

            if (!string.IsNullOrEmpty(commitSha) || !string.IsNullOrEmpty(branch))
            {
                string buildInfo = "";
                if (!string.IsNullOrEmpty(branch))
                    buildInfo += branch;
                if (!string.IsNullOrEmpty(commitSha))
                    buildInfo += $" @{commitSha[..Math.Min(commitSha.Length, 7)]}";
                lines.Add($"Build: {buildInfo}");
            }

            lines.Add($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            lines.Add($"OS: {Environment.OSVersion}");

            _banner = string.Join(Environment.NewLine, lines);
            return _banner;
        }
    }

    /// <summary>
    /// Logs each line of the startup banner at <see cref="LogLevel.Info"/> using
    /// the specified logger. Call this during application initialization to record
    /// build provenance in both console and file sinks.
    /// </summary>
    /// <param name="logger">The logger instance to write banner lines to.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logger"/> is null.
    /// </exception>
    public static void LogStartupBanner(DualSenseClientLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        foreach (string line in Banner.Split(Environment.NewLine))
        {
            logger.Info(line);
        }
    }
}