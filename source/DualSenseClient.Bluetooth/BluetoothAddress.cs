using System.Globalization;

namespace DualSenseClient.Bluetooth;

/// <summary>
/// Converts between textual MAC addresses and their 48-bit numeric value.
/// </summary>
public static class BluetoothAddress
{
    /// <summary>
    /// Parses a MAC address into its 48-bit numeric value. Accepts the formats
    /// XX:XX:XX:XX:XX:XX, XX-XX-XX-XX-XX-XX, and XXXXXXXXXXXX, case-insensitive.
    /// </summary>
    /// <param name="macAddress">The MAC address string to parse.</param>
    /// <returns>The numeric address, or <c>null</c> if the input is not a valid 6-byte MAC.</returns>
    public static ulong? TryParse(string? macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress))
        {
            return null;
        }

        string hex = macAddress.Replace(":", string.Empty).Replace("-", string.Empty).Trim();
        if (hex.Length != 12 || !hex.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        return ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}