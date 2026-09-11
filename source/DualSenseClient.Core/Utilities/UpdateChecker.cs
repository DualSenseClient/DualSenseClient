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
    /// Fetches the newest release for the channel. The tag comes from the aggregated
    /// changelog (newest-first, rebuilt on every release) and the release itself is
    /// then fetched by tag, so this never depends on releases API ordering. Falls back
    /// to the releases list when the changelog is unavailable. Returns <c>null</c>
    /// when unavailable (no releases, offline, rate-limited).
    /// </summary>
    public static async Task<ReleaseInfo?> GetLatestAsync(bool nightly, CancellationToken token = default)
    {
        try
        {
            if (await Changelog.GetLatestAsync(nightly, token) is { Tag.Length: > 0 } latest
                && await GetByTagAsync(latest.Tag, token) is { } byTag)
            {
                return byTag;
            }

            return await GetLatestFromListAsync(nightly, token);
        }
        catch (Exception ex)
        {
            _log.Error($"Update check failed");
            _log.LogExceptionDetails(ex);
            return null;
        }
    }

    /// <summary>
    /// Fetches a single release by tag (<c>/releases/tags/{tag}</c>). Returns
    /// <c>null</c> when unavailable (deleted release, offline, rate-limited).
    /// </summary>
    private static async Task<ReleaseInfo?> GetByTagAsync(string tag, CancellationToken token)
    {
        try
        {
            using JsonDocument? doc = await _http.GetFromJsonAsync<JsonDocument>($"repos/{_repository}/releases/tags/{tag}", token);
            return doc is null ? null : ReadRelease(doc.RootElement);
        }
        catch (Exception ex)
        {
            _log.Error($"Update check tag lookup failed");
            _log.LogExceptionDetails(ex);
            return null;
        }
    }

    /// <summary>
    /// Latest-release lookup against the releases list: <c>/releases/latest</c> for
    /// stable, highest nightly build number (falling back to the first non-draft
    /// prerelease) for nightly. Used only when the changelog is unavailable.
    /// </summary>
    private static async Task<ReleaseInfo?> GetLatestFromListAsync(bool nightly, CancellationToken token)
    {
        if (!nightly)
        {
            using JsonDocument doc = await _http.GetFromJsonAsync<JsonDocument>($"repos/{_repository}/releases/latest", token)
                                     ?? throw new InvalidOperationException("Empty release response.");
            return ReadRelease(doc.RootElement);
        }

        using JsonDocument list = await _http.GetFromJsonAsync<JsonDocument>($"repos/{_repository}/releases?per_page=100", token)
                                  ?? throw new InvalidOperationException("Empty releases response.");
        List<(string Tag, bool Prerelease, bool Draft)> metas = [];
        foreach (JsonElement release in list.RootElement.EnumerateArray())
        {
            string tagName = release.TryGetProperty("tag_name", out JsonElement tag) && tag.GetString() is { Length: > 0 } name ? name : string.Empty;
            bool prerelease = release.TryGetProperty("prerelease", out JsonElement pre) && pre.GetBoolean();
            bool draft = release.TryGetProperty("draft", out JsonElement state) && state.GetBoolean();
            metas.Add((tagName, prerelease, draft));
        }

        // Highest build number wins: the releases API offers no ordering
        // guarantee (v1.0.0-9 was once listed above v1.0.0-10).
        if (SelectLatestNightlyTag(metas) is { Length: > 0 } latest)
        {
            foreach (JsonElement release in list.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("tag_name", out JsonElement tag) && latest.Equals(tag.GetString(), StringComparison.Ordinal))
                {
                    return ReadRelease(release);
                }
            }
        }

        // Fallback for tags outside v{Version}-{N}.{sha}: first prerelease in API order.
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
    /// Selects the newest nightly tag: highest build number <c>N</c> in
    /// <c>v{Version}-{N}.{sha}</c> (highest <c>Version</c> wins first).
    /// Drafts and non-prereleases are skipped. Returns <c>null</c> when no tag
    /// parses, in which case callers fall back to API order.
    /// </summary>
    public static string? SelectLatestNightlyTag(IEnumerable<(string Tag, bool Prerelease, bool Draft)> releases)
    {
        string? best = null;
        Version? bestVersion = null;
        int bestN = -1;
        foreach ((string tag, bool prerelease, bool draft) in releases)
        {
            if (!prerelease || draft || !TryParseNightlyTag(tag, out Version? version, out int n))
            {
                continue;
            }

            if (best is null || version > bestVersion || (version == bestVersion && n > bestN))
            {
                best = tag;
                bestVersion = version;
                bestN = n;
            }
        }

        return best;
    }

    /// <summary>
    /// Parses a nightly tag (<c>v{Version}-{N}.{sha}</c>) into its version and build
    /// number. Returns <c>false</c> for anything else (stable tags, unexpected schemes).
    /// </summary>
    public static bool TryParseNightlyTag(string tag, out Version? version, out int build)
    {
        version = null;
        build = -1;
        string t = tag.Trim().TrimStart('v', 'V');
        int dash = t.IndexOf('-');
        if (dash < 0 || !Version.TryParse(t[..dash], out version))
        {
            version = null;
            return false;
        }

        string rest = t[(dash + 1)..];
        int dot = rest.IndexOf('.');
        if (!int.TryParse(dot < 0 ? rest : rest[..dot], out build))
        {
            version = null;
            build = -1;
            return false;
        }

        return true;
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