namespace DualSenseClient.Core.Models;

/// <summary>
/// The normalized identity of a controller: its Bluetooth MAC when available and its
/// HID device path. Used to key settings, match Auto-profile rules, and derive the
/// canonical identifier for Special Action enablement.
/// </summary>
public readonly record struct ControllerKey
{
    private ControllerKey(string mac, string path)
    {
        Mac = mac;
        Path = path;
    }

    /// <summary>
    /// The normalized Bluetooth MAC address, or an empty string when unavailable.
    /// </summary>
    public string Mac { get; }

    /// <summary>
    /// The normalized HID device path, or an empty string when unavailable.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Whether both identifiers are empty.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            return string.IsNullOrEmpty(Mac) && string.IsNullOrEmpty(Path);
        }
    }

    /// <summary>
    /// The canonical identifier for persistence: the MAC when present, otherwise the path.
    /// </summary>
    public string? Id
    {
        get
        {
            if (!string.IsNullOrEmpty(Mac))
            {
                return Mac;
            }

            return string.IsNullOrEmpty(Path) ? null : Path;
        }
    }

    /// <summary>
    /// Creates a key with normalized identifiers: the MAC is trimmed and uppercased,
    /// the path is trimmed.
    /// </summary>
    /// <param name="mac">Bluetooth MAC address, or <c>null</c>/empty when unavailable.</param>
    /// <param name="path">HID device path, or <c>null</c>/empty when unavailable.</param>
    /// <returns>The normalized key.</returns>
    public static ControllerKey From(string? mac, string? path) =>
        new ControllerKey(mac?.Trim().ToUpperInvariant() ?? string.Empty, path?.Trim() ?? string.Empty);

    /// <summary>
    /// Whether the given key identifies the same controller: the MACs match when both
    /// are present, otherwise the paths match.
    /// </summary>
    /// <param name="other">The key to compare with.</param>
    /// <returns><c>true</c> when either identifier matches.</returns>
    public bool Matches(ControllerKey other)
    {
        if (!string.IsNullOrEmpty(Mac) && string.Equals(Mac, other.Mac, StringComparison.Ordinal))
        {
            return true;
        }

        return !string.IsNullOrEmpty(Path) && string.Equals(Path, other.Path, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public override string ToString() => Id ?? string.Empty;
}