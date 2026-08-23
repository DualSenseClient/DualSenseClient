namespace DualSenseClient.Controllers;

/// <summary>
/// Identifies libVIIPER virtual DualSense/DualSense Edge devices so they are never
/// tracked as physical controllers.
/// </summary>
/// <remarks>
/// <para>
/// Virtual devices present the same VID/PID as real hardware and answer the pairing
/// info feature report (0x09), so they can only be told apart by their Bluetooth MAC:
/// </para>
/// <list type="bullet">
/// <item>Devices created by this app carry the locally-administered
/// <see cref="OwnershipMacPrefix"/> ("02:D5") ownership prefix, stamped at creation via
/// <see cref="DualSenseClient.VIIPER.DualSense.DSMetaState.MACAddress"/>. The 0x02 first
/// byte marks a locally-administered unicast address, which real controllers — always
/// carrying a Sony-assigned universal address — can never have.</item>
/// <item>libVIIPER and standalone VIIPER instances expose the
/// library's hardcoded default addresses (<c>A5:FA:9C:CF:92:00</c> for DualSense,
/// <c>A5:FE:9C:CF:92:00</c> for DualSense Edge; VIIPER device/dualsense/const.go), matched
/// exactly.</item>
/// </list>
/// </remarks>
public static class VirtualDeviceFilter
{
    /// <summary>
    /// The MAC prefix stamped onto every virtual controller this app creates.
    /// </summary>
    public const string OwnershipMacPrefix = "02:D5";

    /// <summary>
    /// The hardcoded default client MAC addresses of libVIIPER's virtual DualSense and
    /// DualSense Edge devices.
    /// </summary>
    private static readonly HashSet<string> LegacyDefaultMacs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "A5:FA:9C:CF:92:00",
        "A5:FE:9C:CF:92:00"
    };

    /// <summary>
    /// Whether the given pairing-info client MAC belongs to a known virtual controller.
    /// Matching is case-insensitive; <c>null</c>, empty, or whitespace (MAC unavailable)
    /// never matches.
    /// </summary>
    public static bool IsKnownVirtualMac(string? clientMac)
    {
        if (string.IsNullOrWhiteSpace(clientMac))
        {
            return false;
        }

        return clientMac.StartsWith(OwnershipMacPrefix, StringComparison.OrdinalIgnoreCase)
               || LegacyDefaultMacs.Contains(clientMac);
    }

    /// <summary>
    /// Generates a random MAC with the <see cref="OwnershipMacPrefix"/> for a new virtual
    /// controller, formatted XX:XX:XX:XX:XX:XX like <see cref="DualSenseClient.Controllers.DualSense.Feature.PairingInfo.ClientMac"/>.
    /// </summary>
    public static string CreateOwnershipMac()
    {
        byte[] tail = new byte[4];
        Random.Shared.NextBytes(tail);
        return $"{OwnershipMacPrefix}:{string.Join(":", tail.Select(b => b.ToString("X2")))}";
    }
}