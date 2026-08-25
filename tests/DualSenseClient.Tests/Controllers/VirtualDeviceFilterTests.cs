using DualSenseClient.Controllers;

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
}