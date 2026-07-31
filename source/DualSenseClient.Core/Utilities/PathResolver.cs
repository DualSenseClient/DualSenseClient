using DualSenseClient.Logging;

namespace DualSenseClient.Core.Utilities;

/// <summary>
/// Provides helper methods for converting relative paths to absolute paths
/// based on the application's runtime base directory.
/// </summary>
public static class PathResolver
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("PathResolver");

    /// <summary>
    /// The resolved base directory path for the application
    /// </summary>
    private static readonly string _baseDirectory = ResolveBaseDirectory();

    /// <summary>
    /// Gets the resolved base directory path for the application.
    /// </summary>
    public static string BaseDirectory => _baseDirectory;

    /// <summary>
    /// Resolves the most appropriate base directory for the application at runtime.
    /// </summary>
    /// <remarks>
    /// Attempts resolution in order: <c>AppContext.BaseDirectory</c>, executable directory,
    /// <c>AppDomain.CurrentDomain.BaseDirectory</c>, and finally the current working directory.
    /// Paths located within the system temp directory are skipped to avoid single-file deployment issues.
    /// </remarks>
    /// <returns>
    /// An absolute path representing the application's base directory.
    /// </returns>
    private static string ResolveBaseDirectory()
    {
        string baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDirectory) && !IsTempDirectory(baseDirectory))
        {
            _log.Debug($"Base directory resolved from AppContext.BaseDirectory: '{baseDirectory}'");
            return baseDirectory;
        }

        string? exePath = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(exePath))
        {
            _log.Debug($"Base directory resolved from executable path: '{exePath}'");
            return exePath;
        }

        string appDomainDir = AppDomain.CurrentDomain.BaseDirectory;
        if (!string.IsNullOrEmpty(appDomainDir) && !IsTempDirectory(appDomainDir))
        {
            _log.Debug($"Base directory resolved from AppDomain.BaseDirectory: '{appDomainDir}'");
            return appDomainDir;
        }

        string fallbackDir = Directory.GetCurrentDirectory();
        _log.Debug($"Base directory resolved from current working directory: '{fallbackDir}'");
        return fallbackDir;

        static bool IsTempDirectory(string path)
        {
            string tempPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
            string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            return normalizedPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Converts a relative path into an absolute path using the application's base directory.
    /// </summary>
    /// <param name="path">A path relative to the application's base directory.</param>
    /// <returns>The absolute path.</returns>
    public static string GetFullPath(string path) => Path.IsPathRooted(path) ? path : Path.Combine(_baseDirectory, path);

    /// <summary>
    /// Combines multiple relative path segments into a single absolute path
    /// using the application's base directory.
    /// </summary>
    /// <param name="relativePaths">An ordered set of relative path segments.</param>
    /// <returns>The resulting absolute path.</returns>
    public static string GetFullPath(params string[] relativePaths) => Path.Combine(new[] { _baseDirectory }.Concat(relativePaths).ToArray());
}