using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using DualSenseClient.Logging;

namespace DualSenseClient.Core.Utilities;

/// <summary>
/// Which asset updates this machine: file name, the zip entry replacing the
/// executable (<c>null</c> when the asset itself is the executable), and whether
/// the result needs the Unix executable bit (zips lose it).
/// </summary>
public sealed record UpdateAsset(string FileName, string? Entry, bool MakeExecutable);

/// <summary>
/// Which distributable updates this machine.
/// </summary>
public enum UpdateTarget
{
    /// <summary>
    /// Windows zip containing the single-file exe.
    /// </summary>
    Windows,

    /// <summary>
    /// Linux zip containing the single-file binary.
    /// </summary>
    Linux,

    /// <summary>
    /// Self-contained Linux AppImage.
    /// </summary>
    AppImage
}

/// <summary>
/// Downloads a release asset and swaps it over the running executable.
/// Only the main binary is replaced: native libraries are embedded resources and
/// <c>Config/</c> next to it is left untouched. Windows allows renaming (but not
/// overwriting) a running exe, which is what the swap relies on.
/// </summary>
public static class UpdateInstaller
{
    /// <summary>
    /// Logger instance
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("UpdateInstaller");

    /// <summary>
    /// Suffix of the <c>.sha256</c> sidecar published next to each asset.
    /// </summary>
    private const string HashSuffix = ".sha256";

    /// <summary>
    /// Suffix of the backup kept after swapping, deleted on the next launch.
    /// </summary>
    private const string BackupSuffix = ".old";

    /// <summary>
    /// <see cref="HttpClient"/> used to download updates
    /// </summary>
    private static readonly HttpClient _http = new HttpClient
    {
        // Downloads outlive the short API timeout; the caller cancels instead.
        Timeout = Timeout.InfiniteTimeSpan
    };

    /// <summary>
    /// Constructor that sets UserAgent for <see cref="_http"/>
    /// </summary>
    static UpdateInstaller()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DualSenseClient");
    }

    /// <summary>
    /// Detects the update target for the running process.
    /// </summary>
    public static UpdateTarget Detect(string? processPath) =>
        OperatingSystem.IsWindows() ? UpdateTarget.Windows
        : IsAppImage(processPath) ? UpdateTarget.AppImage
        : UpdateTarget.Linux;

    /// <summary>
    /// The <see cref="UpdateAsset"/> updating the given target.
    /// </summary>
    public static UpdateAsset Target(UpdateTarget target) =>
        target switch
        {
            UpdateTarget.Windows => new UpdateAsset("DualSenseClient.zip", "DualSenseClient.exe", false),
            UpdateTarget.AppImage => new UpdateAsset("DualSenseClient.AppImage", null, true),
            _ => new UpdateAsset("DualSenseClient-linux.zip", "DualSenseClient", true)
        };

    /// <summary>
    /// Whether the running process is an AppImage (runtime env var or file suffix).
    /// </summary>
    public static bool IsAppImage(string? processPath) =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE"))
        || (processPath?.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destPath"/>.
    /// </summary>
    public static async Task DownloadAsync(string url, string destPath, CancellationToken token = default) =>
        await DownloadAsync(url, destPath, null, token);

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destPath"/>, reporting 0-100
    /// when the server sends a <c>Content-Length</c>. No reports when it does not.
    /// </summary>
    public static async Task DownloadAsync(string url, string destPath, IProgress<double>? progress, CancellationToken token = default)
    {
        using HttpResponseMessage response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(token);
        await using FileStream file = File.Create(destPath);
        byte[] buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await source.ReadAsync(buffer, token)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, n), token);
            read += n;
            if (total is > 0)
            {
                progress?.Report((double)read / total.Value * 100);
            }
        }
    }

    /// <summary>
    /// Verifies <paramref name="filePath"/> against the <c>.sha256</c> sidecar published
    /// next to the asset. Returns <c>true</c> on match, <c>false</c> on mismatch or when
    /// the sidecar cannot be fetched, and <c>true</c> only when the sidecar is absent
    /// (404: older releases without one).
    /// </summary>
    public static async Task<bool> VerifyHashAsync(string filePath, string assetUrl, HttpClient? http = null, CancellationToken token = default)
    {
        string expected;
        try
        {
            string sidecar = await (http ?? _http).GetStringAsync(assetUrl + HashSuffix, token);
            expected = sidecar.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _log.Warning($"No {HashSuffix} sidecar for '{assetUrl}'");
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not fetch {HashSuffix} sidecar for '{assetUrl}', refusing update");
            _log.LogExceptionDetails(ex);
            return false;
        }

        using SHA256 sha = SHA256.Create();
        await using FileStream file = File.OpenRead(filePath);
        byte[] actual = await sha.ComputeHashAsync(file, token);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expected));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts one entry from a zip archive.
    /// </summary>
    /// <exception cref="FileNotFoundException">The entry is not in the archive.</exception>
    public static void ExtractEntry(string zipPath, string entryName, string destPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry entry = archive.GetEntry(entryName)
                                ?? throw new FileNotFoundException($"'{entryName}' not found in update archive.");
        entry.ExtractToFile(destPath, true);
    }

    /// <summary>
    /// Adds the Unix executable bit. No-op on Windows.
    /// </summary>
    public static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    /// <summary>
    /// Renames the running executable aside and moves the new file into its place.
    /// The old process image keeps running until it exits. Restores the backup
    /// when the second move fails, so the install is never left without a binary.
    /// </summary>
    public static void SwapExecutable(string newFilePath, string exePath)
    {
        string backup = exePath + BackupSuffix;
        try
        {
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Could not delete stale update backup '{backup}'");
            _log.LogExceptionDetails(ex);
        }

        File.Move(exePath, backup);
        try
        {
            File.Move(newFilePath, exePath);
        }
        catch (Exception ex)
        {
            _log.Error($"Could not swap update into '{exePath}', restoring backup");
            _log.LogExceptionDetails(ex);
            try
            {
                File.Move(backup, exePath);
            }
            catch (Exception restoreEx)
            {
                _log.Error($"Could not restore update backup '{backup}'");
                _log.LogExceptionDetails(restoreEx);
            }

            throw;
        }
    }

    /// <summary>
    /// Deletes the backup left by the previous update. Best-effort, never throws.
    /// </summary>
    public static void CleanupOld(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        try
        {
            string backup = exePath + BackupSuffix;
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"Could not delete update backup '{exePath + BackupSuffix}'");
            _log.LogExceptionDetails(ex);
        }
    }
}