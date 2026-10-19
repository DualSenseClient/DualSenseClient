using DualSenseClient.Controllers;
using DualSenseClient.Emulation.Devices.DualSense;
using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Tests.Controllers;

public class VirtualDeviceFilterTests
{
    [Test]
    public void IsKnownVirtualMac_OwnershipPrefix_Matches() => Assert.That(VirtualDeviceFilter.IsKnownVirtualMac("02:D5:AA:BB:CC:DD"), Is.True);

    [Test]
    public void IsKnownVirtualMac_OwnershipPrefixLowercase_Matches() => Assert.That(VirtualDeviceFilter.IsKnownVirtualMac("02:d5:a1:b2:c3:d4"), Is.True);

    [Test]
    public void IsKnownVirtualMac_LegacyDualSenseDefault_Matches() => Assert.That(VirtualDeviceFilter.IsKnownVirtualMac("A5:FA:9C:CF:92:00"), Is.True);

    [Test]
    public void IsKnownVirtualMac_LegacyEdgeDefault_Matches() => Assert.That(VirtualDeviceFilter.IsKnownVirtualMac("A5:FE:9C:CF:92:00"), Is.True);

    [Test]
    public void IsKnownVirtualMac_LegacyDefaultLowercase_Matches() => Assert.That(VirtualDeviceFilter.IsKnownVirtualMac("a5:fa:9c:cf:92:00"), Is.True);

    [Test]
    public void IsKnownVirtualMac_RealControllerOui_DoesNotMatch() => Assert.That(VirtualDeviceFilter.IsKnownVirtualMac("D0:BC:C1:17:55:63"), Is.False);

    [Test]
    public void IsKnownVirtualMac_PrefixLookalike_DoesNotMatch()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VirtualDeviceFilter.IsKnownVirtualMac("02:D6:AA:BB:CC:DD"), Is.False);
            Assert.That(VirtualDeviceFilter.IsKnownVirtualMac("02:FA:9C:CF:92:00"), Is.False);
            Assert.That(VirtualDeviceFilter.IsKnownVirtualMac("A5:FB:9C:CF:92:00"), Is.False);
        });
    }

    [Test]
    public void IsKnownVirtualMac_EmulationDefaultIdentity_Matches() => Assert.That(VirtualDeviceFilter.IsKnownVirtualMac("00:00:00:00:00:00"), Is.True);

    [Test]
    public void IsKnownVirtualMac_EmulationDefaultNearMisses_DoNotMatch()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VirtualDeviceFilter.IsKnownVirtualMac("00:00:00:00:00:01"), Is.False);
            Assert.That(VirtualDeviceFilter.IsKnownVirtualMac("00:00:00:00:00"), Is.False);
            Assert.That(VirtualDeviceFilter.IsKnownVirtualMac("00:00"), Is.False);
        });
    }

    [Test]
    public void IsKnownVirtualMac_NullOrEmptyOrWhitespace_DoesNotMatch()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VirtualDeviceFilter.IsKnownVirtualMac(null), Is.False);
            Assert.That(VirtualDeviceFilter.IsKnownVirtualMac(string.Empty), Is.False);
            Assert.That(VirtualDeviceFilter.IsKnownVirtualMac("   "), Is.False);
        });
    }

    [Test]
    public void CreateOwnershipMac_HasOwnershipPrefixAndValidFormat()
    {
        string mac = VirtualDeviceFilter.CreateOwnershipMac();

        Assert.Multiple(() =>
        {
            Assert.That(mac, Does.StartWith(VirtualDeviceFilter.OwnershipMacPrefix));
            Assert.That(mac, Does.Match(@"^02:D5(:[0-9A-F]{2}){4}$"));
        });
    }

    [Test]
    public void CreateOwnershipMac_IsRecognizedAsVirtual()
    {
        for (int i = 0; i < 16; i++)
        {
            Assert.That(VirtualDeviceFilter.IsKnownVirtualMac(VirtualDeviceFilter.CreateOwnershipMac()), Is.True);
        }
    }

    /// <summary>
    /// Format-linkage test (not wire evidence): pins that the default MAC bytes an Emulation
    /// DualSense or DualSense Edge persona identity carries format to the pairing-info client MAC
    /// string the scanner hands to <see cref="VirtualDeviceFilter.IsKnownVirtualMac"/>. The 0x09
    /// report serves the address reversed and <c>PairingInfo</c> reverses it back, so the scanner
    /// sees the identity's address-order bytes formatted in order. Guards the filter against a
    /// change of the library default.
    /// </summary>
    [Test]
    public void EmulationDualSenseDefaultIdentity_ScannerMacFormat_IsRecognizedAsVirtual()
    {
        string scannerMac = FormatAddressOrder(new DualSenseIdentity().MacAddress);

        Assert.That(VirtualDeviceFilter.IsKnownVirtualMac(scannerMac), Is.True);
    }

    /// <summary>
    /// Format-linkage test (not wire evidence): the DualShock 4 persona currently serves no
    /// feature reports (all ids stall) and the client never wraps DS4, so its identity default
    /// does not reach the scanner over a wire today; this pins only that the shared all-zero
    /// default formats to a string the filter recognizes if it ever does (the identity is kept
    /// for the later-milestone 0x12/0x81 reports).
    /// </summary>
    [Test]
    public void EmulationDualShock4DefaultIdentity_ScannerMacFormat_IsRecognizedAsVirtual()
    {
        string scannerMac = FormatAddressOrder(new DualShock4Identity().MacAddress);

        Assert.That(VirtualDeviceFilter.IsKnownVirtualMac(scannerMac), Is.True);
    }

    /// <summary>
    /// Formats six MAC bytes in address order as XX:XX:XX:XX:XX:XX, the string form the scanner
    /// compares (<c>PairingInfo.ClientMac</c>).
    /// </summary>
    /// <param name="mac">MAC bytes in address order.</param>
    private static string FormatAddressOrder(byte[] mac) => string.Join(":", mac.Select(b => b.ToString("X2")));
}