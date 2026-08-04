using System.Reflection;

namespace DualSenseClient.Core.Utilities;

/// <summary>
/// Provides application version and build metadata (git commit SHA) read from the
/// entry assembly. Shared across the application (e.g. the settings page header and
/// the update checker), so version info is always read from one place.
/// </summary>
/// <remarks>
/// The commit SHA is embedded in <see cref="AssemblyInformationalVersionAttribute"/>
/// (e.g. <c>1.0.0+0123456789abcdef...</c>) by the .NET SDK when building from a git
/// repository. When it is absent, the commit properties are empty and
/// <see cref="VersionWithCommit"/> falls back to the plain version.
/// </remarks>
public static class AppInfo
{
    /// <summary>
    /// Length of the shortened commit SHA shown in version strings.
    /// </summary>
    private const int ShortShaLength = 7;

    /// <summary>
    /// The assembly the version metadata is read from: the application entry assembly,
    /// or the calling assembly when no entry assembly is available (e.g. in tests).
    /// </summary>
    private static readonly Assembly _assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

    /// <summary>
    /// Gets the application version (e.g. <c>1.0.0</c>).
    /// </summary>
    public static Version Version { get; } = _assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// Gets the full git commit SHA of the build, or empty when unavailable.
    /// </summary>
    public static string CommitSha { get; } = ReadCommitSha();

    /// <summary>
    /// Gets the shortened (7-character) git commit SHA of the build, or empty when unavailable.
    /// </summary>
    public static string CommitShaShort { get; } =
        CommitSha.Length > ShortShaLength ? CommitSha[..ShortShaLength] : CommitSha;

    /// <summary>
    /// Gets the version string including the build commit, e.g. <c>v1.0.0 (abc1234)</c>.
    /// Debug builds show <c>DEBUG</c> instead of the commit; when no commit is embedded
    /// only the plain version is returned.
    /// </summary>
    public static string VersionWithCommit { get; } = BuildVersionWithCommit();

    /// <summary>
    /// Reads the full git commit SHA from the assembly's informational version
    /// (the part after the <c>+</c>, e.g. <c>1.0.0+0123456...</c>).
    /// </summary>
    private static string ReadCommitSha()
    {
        AssemblyInformationalVersionAttribute? attribute = _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        string? informationalVersion = attribute?.InformationalVersion;
        if (string.IsNullOrEmpty(informationalVersion) || !informationalVersion.Contains('+'))
        {
            return string.Empty;
        }

        return informationalVersion.Split('+')[1];
    }

    /// <summary>
    /// Formats <see cref="Version"/> together with the build commit:
    /// <c>v{major}.{minor}.{build} ({commit})</c> in release builds, <c>(DEBUG)</c> in
    /// debug builds, or just the version when no commit is embedded.
    /// </summary>
    private static string BuildVersionWithCommit()
    {
        string baseVersion = $"v{Version.Major}.{Version.Minor}.{Version.Build}";
        if (string.IsNullOrEmpty(CommitSha))
        {
            return baseVersion;
        }

#if DEBUG
        return $"{baseVersion} (DEBUG)";
#else
        return $"{baseVersion} ({CommitShaShort})";
#endif
    }
}