using System.Reflection;
using System.Runtime.InteropServices;

namespace DualSenseClient.Hid.Interop;

/// <summary>
/// Resolves P/Invoke calls to hidapi against the native binary embedded in this
/// assembly (Windows), falling back to the system HIDAPI library
/// (Linux hidraw backend). Mirrors the embedded-resource pattern used for
/// libVIIPER without duplicating its implementation.
/// </summary>
internal static class HidNativeLibrary
{
    /// <summary>
    /// Name of the native library used by every <see cref="DllImportAttribute"/>
    /// in <see cref="HidApi"/>.
    /// </summary>
    private const string LibraryName = "hidapi";

    /// <summary>
    /// Embedded resource name of the Windows x64 native library.
    /// </summary>
    private const string WinX64Resource = "hidapi.win-x64.dll";

    /// <summary>
    /// Embedded resource name of the Linux x64 native library, when vendored.
    /// </summary>
    private const string LinuxX64Resource = "hidapi.linux-x64.so";

    /// <summary>
    /// Embedded resource name of the release tag written to native/version.txt by
    /// the scripts/fetch_native_libraries.py script.
    /// </summary>
    private const string VersionResource = "hidapi.version.txt";

    /// <summary>
    /// Prefix of the temporary native library files extracted by this resolver,
    /// used to clean up leftovers from previous runs.
    /// </summary>
    private const string TempFilePrefix = "DualSenseClient.Hid.";

    /// <summary>
    /// System library names probed on Linux, preferring the hidraw backend.
    /// Upstream HIDAPI ships no prebuilt Linux binaries, so the distro package
    /// (e.g. libhidapi-hidraw0) provides the library at runtime.
    /// </summary>
    private static readonly string[] LinuxCandidates =
    [
        "libhidapi-hidraw.so.0",
        "libhidapi-hidraw.so",
        "libhidapi.so.0",
        "libhidapi.so"
    ];

    /// <summary>
    /// Loaded handle to the native library, initialized on the first P/Invoke call.
    /// </summary>
    private static readonly Lazy<IntPtr> NativeLibraryHandle = new Lazy<IntPtr>(Load);

    /// <summary>
    /// Release tag of the embedded hidapi native library (e.g. "hidapi-0.15.0"),
    /// or null if no version information was embedded.
    /// </summary>
    public static string? NativeVersion { get; } = ReadVersion();

    /// <summary>
    /// Registers the resolver for this assembly so that the runtime asks us to resolve
    /// "hidapi" instead of searching the default locations.
    /// Called once from <see cref="HidApi"/>'s static constructor.
    /// </summary>
    public static void Register() => NativeLibrary.SetDllImportResolver(typeof(HidNativeLibrary).Assembly, Resolve);

    /// <summary>
    /// Resolves a native library to a handle, invoked by the runtime whenever an
    /// unmanaged library is needed.
    /// </summary>
    /// <param name="libraryName">Name of the library being resolved.</param>
    /// <param name="assembly">Assembly that triggered the resolution.</param>
    /// <param name="searchPath">DllImport search path hints (unused).</param>
    /// <returns>
    /// The loaded native library handle when <paramref name="libraryName"/> is hidapi,
    /// otherwise <see cref="IntPtr.Zero"/> to let the runtime fall back to default resolution.
    /// </returns>
    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) =>
        libraryName == LibraryName ? NativeLibraryHandle.Value : IntPtr.Zero;

    /// <summary>
    /// Loads the native library matching the current platform: the embedded resource
    /// when present, otherwise a system HIDAPI library on Linux. Returns
    /// <see cref="IntPtr.Zero"/> when nothing could be loaded so the runtime can
    /// attempt its default probing.
    /// </summary>
    /// <returns>The loaded native library handle, or <see cref="IntPtr.Zero"/>.</returns>
    private static IntPtr Load()
    {
        bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string resourceName = windows ? WinX64Resource : LinuxX64Resource;
        using (Stream? stream = typeof(HidNativeLibrary).Assembly.GetManifestResourceStream(resourceName))
        {
            if (stream is not null)
            {
                try
                {
                    return LoadEmbedded(stream, windows);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException)
                {
                }
            }
        }

        if (!windows)
        {
            foreach (string candidate in LinuxCandidates)
            {
                if (NativeLibrary.TryLoad(candidate, typeof(HidNativeLibrary).Assembly, DllImportSearchPath.SafeDirectories, out IntPtr handle))
                {
                    return handle;
                }
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Extracts the embedded native library to a temporary file and loads it into
    /// the process. Leftovers from previous runs are removed best-effort first.
    /// </summary>
    /// <param name="stream">Embedded resource stream of the native library.</param>
    /// <param name="windows">Whether the current platform is Windows.</param>
    /// <returns>The loaded native library handle.</returns>
    private static IntPtr LoadEmbedded(Stream stream, bool windows)
    {
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
    /// Reads the embedded version resource, whose content is the hidapi release
    /// tag written by scripts/fetch_native_libraries.py.
    /// </summary>
    /// <returns>The release tag, or null if the resource is missing.</returns>
    private static string? ReadVersion()
    {
        using Stream? stream = typeof(HidNativeLibrary).Assembly.GetManifestResourceStream(VersionResource);
        if (stream is null)
        {
            return null;
        }

        using StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}