using System.Reflection;
using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER;

/// <summary>
/// Resolves P/Invoke calls to libVIIPER against the native binary embedded in this
/// assembly, so no additional files need to be copied next to the managed DLL.
/// </summary>
internal static class NativeLibraryResolver
{
    /// <summary>
    /// Name of the native library used by every <see cref="DllImportAttribute"/>
    /// in <see cref="LibVIIPER"/>.
    /// </summary>
    private const string LibraryName = "libVIIPER";

    /// <summary>
    /// Embedded resource name of the Windows x64 native library.
    /// </summary>
    private const string WinX64Resource = "libVIIPER.win-x64.dll";

    /// <summary>
    /// Embedded resource name of the Linux x64 native library.
    /// </summary>
    private const string LinuxX64Resource = "libVIIPER.linux-x64.so";

    /// <summary>
    /// Embedded resource name of the release tag written to native/version.txt by
    /// the scripts/fetch_native_libraries.py script.
    /// </summary>
    private const string VersionResource = "libVIIPER.version.txt";

    /// <summary>
    /// Prefix of the temporary native library files extracted by this resolver,
    /// used to clean up leftovers from previous runs.
    /// </summary>
    private const string TempFilePrefix = "DualSenseClient.VIIPER.";

    /// <summary>
    /// Loaded handle to the native library, initialized on the first P/Invoke call.
    /// </summary>
    private static readonly Lazy<IntPtr> NativeLibraryHandle = new Lazy<IntPtr>(Load);

    /// <summary>
    /// Release tag of the embedded libVIIPER native library (e.g. "dev-snapshot"),
    /// or null if no version information was embedded.
    /// </summary>
    public static string? NativeVersion { get; } = ReadVersion();

    /// <summary>
    /// Registers the resolver for this assembly so that the runtime asks us to resolve
    /// "libVIIPER" instead of searching the default locations.
    /// Called once from <see cref="LibVIIPER"/>'s static constructor.
    /// </summary>
    public static void Register() => NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);

    /// <summary>
    /// Resolves a native library to a handle, invoked by the runtime whenever an
    /// unmanaged library is needed.
    /// </summary>
    /// <param name="libraryName">Name of the library being resolved.</param>
    /// <param name="assembly">Assembly that triggered the resolution.</param>
    /// <param name="searchPath">DllImport search path hints (unused).</param>
    /// <returns>
    /// The loaded native library handle when <paramref name="libraryName"/> is libVIIPER,
    /// otherwise <see cref="IntPtr.Zero"/> to let the runtime fall back to default resolution.
    /// </returns>
    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) =>
        libraryName == LibraryName ? NativeLibraryHandle.Value : IntPtr.Zero;

    /// <summary>
    /// Extracts the native library matching the current platform from the assembly's
    /// embedded resources to a temporary file and loads it into the process. The
    /// temporary file is left in place on Windows because the loaded image keeps it
    /// memory-mapped (and therefore locked) for the process lifetime; leftovers from
    /// previous runs are removed best-effort first.
    /// </summary>
    /// <returns>The loaded native library handle, or <see cref="IntPtr.Zero"/> if the resource is missing.</returns>
    private static IntPtr Load()
    {
        bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string resourceName = windows ? WinX64Resource : LinuxX64Resource;
        using Stream? stream = typeof(NativeLibraryResolver).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return IntPtr.Zero;
        }

        CleanupStaleTempFiles();

        string extension = windows ? ".dll" : ".so";
        string tempPath = Path.Combine(Path.GetTempPath(), TempFilePrefix + Path.GetRandomFileName() + extension);
        using (FileStream file = File.Create(tempPath))
        {
            stream.CopyTo(file);
        }

        return NativeLibrary.Load(tempPath);
    }

    /// <summary>
    /// Best-effort removal of native library temp files extracted by previous runs,
    /// which are no longer locked once the extracting process has exited. Files still
    /// in use by a running instance are skipped silently.
    /// </summary>
    private static void CleanupStaleTempFiles()
    {
        try
        {
            foreach (string file in Directory.GetFiles(Path.GetTempPath(), TempFilePrefix + "*"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Reads the embedded version resource, whose content is the libVIIPER release
    /// tag written by scripts/fetch_native_libraries.py.
    /// </summary>
    /// <returns>The release tag, or null if the resource is missing.</returns>
    private static string? ReadVersion()
    {
        using Stream? stream = typeof(NativeLibraryResolver).Assembly.GetManifestResourceStream(VersionResource);
        if (stream is null)
        {
            return null;
        }

        using StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}