using System.Net.Http.Json;
using System.Text.Json;
using DualSenseClient.Logging;

namespace DualSenseClient.Core.Utilities;

/// <summary>
/// Checks GitHub releases for newer builds. Release layout mirrors
/// <c>.github/workflows/create_release.yml</c>: stable tags are <c>v{Version}</c>
/// (non-prerelease), nightly tags are <c>v{Version}-{N}.{sha7}</c> (prerelease).
/// </summary>
public static class UpdateChecker
{
    /// <summary>
    /// The repository update checks target.
    /// </summary>
    private const string _repository = "DualSenseClient/DualSenseClient";

    /// <summary>
    /// Logger instance
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("UpdateChecker");

    /// <summary>
    /// HttpClient used for checking for updates
    /// </summary>
    private static readonly HttpClient _http = new HttpClient
    {
        BaseAddress = new Uri("https://api.github.com/"),
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Constructor that adds Headers to our HttpClient
    /// </summary>
    static UpdateChecker()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DualSenseClient");
    }

    /// <summary>
    /// A release found on GitHub.
    /// </summary>
    /// <param name="Tag">The release tag (e.g. <c>v1.0.0</c> or <c>v1.0.0-3.abc1234</c>).</param>
    /// <param name="Url">The release page URL.</param>
    /// <param name="Assets">Download URLs by asset file name.</param>
    public sealed record ReleaseInfo(string Tag, string Url, IReadOnlyDictionary<string, string> Assets)
    {
        /// <summary>
        /// Download URL of the asset that updates this machine, or <c>null</c> when missing.
        /// </summary>
        public string? GetAssetUrl(UpdateTarget target)
        {
            string asset = UpdateInstaller.Target(target).FileName;
            return Assets.GetValueOrDefault(asset);
        }
    }

    /// <summary>
    /// Fetches the newest release for the channel: <c>/releases/latest</c> for stable,
    /// first non-draft prerelease of <c>/releases</c> for nightly. Returns <c>null</c>
    /// when unavailable (no releases, offline, rate-limited).
    /// </summary>
    public static async Task<ReleaseInfo?> GetLatestAsync(bool nightly, CancellationToken token = default)
    {
        try
        {
            if (!nightly)
            {
                using JsonDocument doc = await _http.GetFromJsonAsync<JsonDocument>($"repos/{_repository}/releases/latest", token)
                                         ?? throw new InvalidOperationException("Empty release response.");
                return ReadRelease(doc.RootElement);
            }

            using JsonDocument list = await _http.GetFromJsonAsync<JsonDocument>($"repos/{_repository}/releases?per_page=30", token)
                                      ?? throw new InvalidOperationException("Empty releases response.");
            foreach (JsonElement release in list.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("prerelease", out JsonElement pre) && pre.GetBoolean()
                                                                              && (!release.TryGetProperty("draft", out JsonElement draft) ||
                                                                                  !draft.GetBoolean()))
                {
                    return ReadRelease(release);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _log.Error($"Update check failed");
            _log.LogExceptionDetails(ex);
            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="remoteTag"/> is newer than the running build. Stable compares
    /// versions; nightly compares the tag's trailing commit sha against the build commit.
    /// A build without an embedded commit (dev) is always offered the latest nightly.
    /// </summary>
    public static bool IsNewer(string remoteTag, Version localVersion, string localShaShort, bool nightly)
    {
        if (string.IsNullOrWhiteSpace(remoteTag))
        {
            return false;
        }

        if (!nightly)
        {
            return Version.TryParse(remoteTag.TrimStart('v'), out Version? remote) && remote > localVersion;
        }

        if (string.IsNullOrEmpty(localShaShort))
        {
            return true;
        }

        int dot = remoteTag.LastIndexOf('.');
        return dot < 0 || !remoteTag[(dot + 1)..].Equals(localShaShort, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads JSON response and converts it into <see cref="ReleaseInfo"/>
    /// </summary>
    /// <param name="release"></param>
    /// <returns></returns>
    private static ReleaseInfo? ReadRelease(JsonElement release)
    {
        if (!release.TryGetProperty("tag_name", out JsonElement tag) || tag.GetString() is not { Length: > 0 } tagName)
        {
            return null;
        }

        string url = release.TryGetProperty("html_url", out JsonElement link) && link.GetString() is { Length: > 0 } href
            ? href
            : $"https://github.com/{_repository}/releases/tag/{tagName}";

        Dictionary<string, string> assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (release.TryGetProperty("assets", out JsonElement assetList) && assetList.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement asset in assetList.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out JsonElement name) && name.GetString() is { Length: > 0 } fileName
                                                                       && asset.TryGetProperty("browser_download_url", out JsonElement download)
                                                                       && download.GetString() is { Length: > 0 } downloadUrl)
                {
                    assets[fileName] = downloadUrl;
                }
            }
        }

        return new ReleaseInfo(tagName, url, assets);
    }
}