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
    /// <c>AppDomain.CurrentDomain.BaseDirectory</c>, the XDG config directory on Linux, and
    /// finally the current working directory.
    /// Directories that are temporary or not writable are skipped to avoid single-file
    /// deployment and read-only install issues.
    /// </remarks>
    /// <returns>
    /// An absolute path representing the application's base directory.
    /// </returns>
    private static string ResolveBaseDirectory()
    {
        string baseDirectory = AppContext.BaseDirectory;
        if (IsUsableBaseDirectory(baseDirectory))
        {
            _log.Debug($"Base directory resolved from AppContext.BaseDirectory: '{baseDirectory}'");
            return baseDirectory;
        }

        string? exePath = Path.GetDirectoryName(Environment.ProcessPath);
        if (IsUsableBaseDirectory(exePath))
        {
            _log.Debug($"Base directory resolved from executable path: '{exePath}'");
            return exePath!;
        }

        string appDomainDir = AppDomain.CurrentDomain.BaseDirectory;
        if (IsUsableBaseDirectory(appDomainDir))
        {
            _log.Debug($"Base directory resolved from AppDomain.BaseDirectory: '{appDomainDir}'");
            return appDomainDir;
        }

        if (OperatingSystem.IsLinux())
        {
            string xdgBase = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(xdgBase))
            {
                string xdgDir = Path.Combine(xdgBase, "DualSenseClient");
                _log.Warning($"Base directory is not usable, falling back to XDG config directory: '{xdgDir}'");
                return xdgDir;
            }
        }

        string fallbackDir = Directory.GetCurrentDirectory();
        _log.Debug($"Base directory resolved from current working directory: '{fallbackDir}'");
        return fallbackDir;
    }

    /// <summary>
    /// Determines whether a path can serve as the application's base directory.
    /// </summary>
    /// <param name="path">The path to evaluate.</param>
    /// <returns>
    /// <c>true</c> if the path is non-empty, not located in the system temp directory,
    /// and writable; otherwise, <c>false</c>.
    /// </returns>
    private static bool IsUsableBaseDirectory(string? path) =>
        !string.IsNullOrEmpty(path) && !IsTempDirectory(path) && IsWritable(path);

    /// <summary>
    /// Determines whether the specified directory exists and can be written to.
    /// </summary>
    /// <param name="path">The directory to probe.</param>
    /// <returns><c>true</c> if a probe file can be created and deleted; otherwise, <c>false</c>.</returns>
    private static bool IsWritable(string path)
    {
        try
        {
            string probeFile = Path.Combine(path, $".write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probeFile, string.Empty);
            File.Delete(probeFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Determines whether the specified path is located within the system temp directory.
    /// </summary>
    /// <param name="path">The path to evaluate.</param>
    /// <returns><c>true</c> if the path starts with the system temp directory; otherwise, <c>false</c>.</returns>
    private static bool IsTempDirectory(string path)
    {
        string tempPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        return normalizedPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase);
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