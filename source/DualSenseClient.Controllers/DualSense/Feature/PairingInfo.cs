namespace DualSenseClient.Controllers.DualSense.Feature;

/// <summary>
/// Pairing information parsed from feature report 0x09 (§6.1).
/// Contains the controller (client) and host Bluetooth MAC addresses.
/// All values are read from the raw report buffer at construction time.
/// </summary>
public readonly struct PairingInfo
{
    /// <summary>
    /// The raw 0x09 feature report buffer this struct reads from.
    /// </summary>
    private readonly byte[] _raw;

    /// <summary>
    /// Creates a new pairing info view over a raw feature report buffer.
    /// </summary>
    /// <param name="raw">The raw 0x09 feature report buffer.</param>
    public PairingInfo(byte[] raw)
    {
        _raw = raw;
    }

    /// <summary>
    /// Whether the buffer holds a valid 0x09 pairing info report. Requires at least
    /// the 16 bytes covering both MAC addresses.
    /// </summary>
    public bool IsValid => _raw.Length >= 16 && _raw[0] == 0x09;

    /// <summary>
    /// Controller (client) Bluetooth MAC address (bytes 1-6, little-endian), formatted
    /// as XX:XX:XX:XX:XX:XX, or empty when the report is invalid.
    /// </summary>
    public string ClientMac => IsValid ? FormatMac(1) : string.Empty;

    /// <summary>
    /// Host Bluetooth MAC address (bytes 10-15, little-endian), formatted as
    /// XX:XX:XX:XX:XX:XX, or empty when the report is invalid.
    /// </summary>
    public string HostMac => IsValid ? FormatMac(10) : string.Empty;

    /// <summary>
    /// Formats the 6 MAC bytes at <paramref name="offset"/> as XX:XX:XX:XX:XX:XX.
    /// MAC bytes are stored least-significant first, so they are reversed before formatting.
    /// </summary>
    /// <param name="offset">Byte offset of the MAC address.</param>
    private string FormatMac(int offset)
    {
        byte[] mac = _raw[offset..(offset + 6)];
        Array.Reverse(mac);
        return string.Join(":", mac.Select(b => b.ToString("X2")));
    }
}