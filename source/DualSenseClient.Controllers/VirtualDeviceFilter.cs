namespace DualSenseClient.Controllers;

/// <summary>
/// Identifies virtual DualSense/DualSense Edge devices of both emulation engines (libVIIPER and
/// Emulation) so they are never tracked as physical controllers.
/// </summary>
/// <remarks>
/// <para>
/// Virtual devices present the same VID/PID as real hardware and answer the pairing
/// info feature report (0x09), so they can only be told apart by their Bluetooth MAC:
/// </para>
/// <list type="bullet">
/// <item>Devices created by this app carry the locally-administered
/// <see cref="OwnershipMacPrefix"/> ("02:D5") ownership prefix, stamped at creation in both
/// engines: libVIIPER via <see cref="DualSenseClient.VIIPER.DualSense.DSMetaState.MACAddress"/>
/// and Emulation via the persona identity's MAC. The 0x02 first
/// byte marks a locally-administered unicast address, which real controllers — always
/// carrying a Sony-assigned universal address — can never have.</item>
/// <item>libVIIPER and standalone VIIPER instances expose the
/// library's hardcoded default addresses (<c>A5:FA:9C:CF:92:00</c> for DualSense,
/// <c>A5:FE:9C:CF:92:00</c> for DualSense Edge; VIIPER device/dualsense/const.go), matched
/// exactly.</item>
/// <item>An Emulation DualSense or DualSense Edge persona carrying the default identity serves the
/// all-zero controller MAC <c>00:00:00:00:00:00</c> over its pairing-info report
/// (<see cref="DualSenseClient.Emulation.Devices.DualSense.DualSenseIdentity.MacAddress"/> default),
/// matched exactly. Real controllers always carry a Sony-assigned nonzero address, so the zero
/// value cannot belong to physical hardware, and a failed pairing read yields no comparable value
/// and never matches.</item>
/// </list>
/// <para>
/// The other Emulation personas need no value here: the DualShock 4 persona's identity carries the
/// same all-zero default, but the client recognizes and wraps only DualSense/Edge
/// (<see cref="ControllerFactory"/>), and the DS4 feature surface currently serves no reports
/// (all ids stall), so no DS4 MAC can reach this filter; the Xbox 360 persona is vendor-class
/// (0xFF) with no HID interface, so the HID scanner never enumerates it.
/// </para>
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
    /// The all-zero controller MAC an Emulation DualSense or DualSense Edge persona serves over its
    /// pairing-info report (0x09) when it carries the default identity. Matched exactly: the value
    /// is valid as a whole but never as a prefix.
    /// </summary>
    private const string EmulationDefaultIdentityMac = "00:00:00:00:00:00";

    /// <summary>
    /// Whether the given pairing-info client MAC belongs to a known virtual controller: a device
    /// created by this app, a libVIIPER/standalone VIIPER default, or an Emulation persona carrying
    /// its default identity. Matching is case-insensitive; <c>null</c>, empty, or whitespace
    /// (MAC unavailable) never matches.
    /// </summary>
    public static bool IsKnownVirtualMac(string? clientMac)
    {
        if (string.IsNullOrWhiteSpace(clientMac))
        {
            return false;
        }

        return clientMac.StartsWith(OwnershipMacPrefix, StringComparison.OrdinalIgnoreCase)
               || LegacyDefaultMacs.Contains(clientMac)
               || string.Equals(clientMac, EmulationDefaultIdentityMac, StringComparison.OrdinalIgnoreCase);
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