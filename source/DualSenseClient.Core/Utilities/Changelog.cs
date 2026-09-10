using System.Net.Http.Json;
using System.Text.Json;
using DualSenseClient.Logging;

namespace DualSenseClient.Core.Utilities;

/// <summary>
/// Reads the aggregated release notes published to the <c>data</c> branch by
/// <c>.github/workflows/update_changelog.yml</c> and selects the entries newer
/// than the running build. Any failure (offline, missing file) yields <c>null</c>
/// so the caller skips the what's-new popup.
/// </summary>
public static class Changelog
{
    /// <summary>
    /// URL of the aggregated releases file on the <c>data</c> branch.
    /// </summary>
    private const string _dataUrl = "https://raw.githubusercontent.com/DualSenseClient/DualSenseClient/data/changelog.json";

    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("Changelog");

    /// <summary>
    /// HttpClient used for fetching the aggregated changelog.
    /// </summary>
    private static readonly HttpClient _http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Constructor that adds Headers to our HttpClient.
    /// </summary>
    static Changelog()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DualSenseClient");
    }

    /// <summary>
    /// One release from the aggregated file, newest first.
    /// </summary>
    /// <param name="Tag">The release tag (e.g. <c>v1.0.0</c> or <c>v1.0.0-3.abc1234</c>).</param>
    /// <param name="Name">The release title.</param>
    /// <param name="PublishedAt">When the release was published.</param>
    /// <param name="Prerelease">Whether this is a nightly pre-release.</param>
    /// <param name="Body">The release notes markdown.</param>
    public sealed record Entry(string Tag, string Name, DateTimeOffset PublishedAt, bool Prerelease, string Body);

    /// <summary>
    /// Fetches the aggregated changelog and returns the entries newer than
    /// <paramref name="previousVersion"/> (an <see cref="AppInfo.VersionWithCommit"/>
    /// string), newest first. Returns <c>null</c> when there is nothing to show
    /// or the file could not be read.
    /// </summary>
    public static async Task<IReadOnlyList<Entry>?> GetChangesSinceAsync(string previousVersion, bool nightly, CancellationToken token = default)
    {
        try
        {
            using JsonDocument doc = await _http.GetFromJsonAsync<JsonDocument>(_dataUrl, token)
                                     ?? throw new InvalidOperationException("Empty changelog response.");
            List<Entry> entries = ReadEntries(doc.RootElement);
            IReadOnlyList<Entry> newer = SelectNewer(entries, previousVersion, nightly);
            return newer.Count == 0 ? null : newer;
        }
        catch (Exception ex)
        {
            _log.Error("Changelog fetch failed");
            _log.LogExceptionDetails(ex);
            return null;
        }
    }

    /// <summary>
    /// Selects the entries newer than <paramref name="previousVersion"/>, newest first.
    /// Stable compares versions; nightly compares the tag's trailing commit sha.
    /// Falls back to the newest entry when the previous version is unknown.
    /// Updating to a stable release never lists nightly entries, even with the
    /// nightly channel enabled: the stable notes already cover those changes.
    /// </summary>
    public static IReadOnlyList<Entry> SelectNewer(IReadOnlyList<Entry> entries, string previousVersion, bool nightly)
    {
        List<Entry> channel = entries.Where(e => nightly || !e.Prerelease).ToList();
        if (channel.Count == 0)
        {
            return [];
        }

        (Version? version, string sha) = ParseSeen(previousVersion);
        List<Entry> newer;
        if (nightly)
        {
            if (!string.IsNullOrEmpty(sha) && channel.FindIndex(e => e.Tag.EndsWith("." + sha, StringComparison.OrdinalIgnoreCase)) is int index and >= 0)
            {
                newer = channel.Take(index).ToList();
            }
            else
            {
                newer = channel.Take(1).ToList();
            }
        }
        else if (version is not null)
        {
            newer = channel.Where(e => Version.TryParse(e.Tag.TrimStart('v'), out Version? v) && v > version).ToList();
        }
        else
        {
            newer = channel.Take(1).ToList();
        }

        if (newer.Count > 0 && !newer[0].Prerelease)
        {
            newer = newer.Where(e => !e.Prerelease).ToList();
        }

        return newer;
    }

    /// <summary>
    /// Splits a <see cref="AppInfo.VersionWithCommit"/> string (e.g. <c>v1.0.0 (abc1234)</c>)
    /// into its version and commit sha. Debug builds and plain versions yield an empty sha.
    /// </summary>
    private static (Version? version, string sha) ParseSeen(string seen)
    {
        if (string.IsNullOrWhiteSpace(seen))
        {
            return (null, string.Empty);
        }

        string s = seen.Trim().TrimStart('v', 'V');
        string sha = string.Empty;
        int paren = s.IndexOf('(');
        if (paren >= 0)
        {
            int end = s.IndexOf(')', paren);
            if (end > paren)
            {
                sha = s.Substring(paren + 1, end - paren - 1).Trim();
            }

            s = s[..paren].Trim();
        }

        if (sha.Equals("DEBUG", StringComparison.OrdinalIgnoreCase))
        {
            sha = string.Empty;
        }

        return (Version.TryParse(s, out Version? version) ? version : null, sha);
    }

    /// <summary>
    /// Reads the aggregated file into entries, skipping malformed items.
    /// </summary>
    private static List<Entry> ReadEntries(JsonElement root)
    {
        List<Entry> entries = [];
        if (root.ValueKind != JsonValueKind.Array)
        {
            return entries;
        }

        foreach (JsonElement item in root.EnumerateArray())
        {
            if (item.TryGetProperty("tag", out JsonElement tag) && tag.GetString() is { Length: > 0 } tagName)
            {
                string name = item.TryGetProperty("name", out JsonElement n) && n.GetString() is { Length: > 0 } title ? title : tagName;
                bool prerelease = item.TryGetProperty("prerelease", out JsonElement p) && p.ValueKind == JsonValueKind.True;
                string body = item.TryGetProperty("body", out JsonElement b) && b.GetString() is { Length: > 0 } notes ? notes : string.Empty;
                DateTimeOffset published = item.TryGetProperty("publishedAt", out JsonElement d)
                                           && DateTimeOffset.TryParse(d.GetString(), out DateTimeOffset at)
                    ? at
                    : DateTimeOffset.MinValue;
                entries.Add(new Entry(tagName, name, published, prerelease, body));
            }
        }

        return entries;
    }
}