using DualSenseClient.Emulation.Windows;

namespace DualSenseClient.Tests.Emulation.Windows;

/// <summary>
/// Tests of <see cref="VhciDeviceLocator"/>: candidate selection through the injectable
/// enumeration seam (the first non-blank candidate wins, blanks are skipped, enumeration
/// failures return false instead of throwing) and the env-gated live case that finds and opens
/// the real usbip-win2 VHCI device interface on this machine.
/// </summary>
[TestFixture]
public sealed class VhciDeviceLocatorTests
{
    /// <summary>Device interface path of the usbip-win2 VHCI device class, as observed on this machine.</summary>
    private const string VhciDevicePath = @"\\?\ROOT#USB#0000#{b4030c06-dc5f-4fcc-87eb-e5515a0935c0}";

    /// <summary>Device interface path of the generic USB host controller class, used as a second candidate.</summary>
    private const string HostControllerDevicePath = @"\\?\ROOT#USB#0000#{3abf6f2d-71c4-462a-8a92-1e6861e6af27}";

    [Test]
    public void TryFind_TwoCandidatePaths_ReturnsFirst()
    {
        VhciDeviceLocator locator = new VhciDeviceLocator(() => new[]
        {
            VhciDevicePath, HostControllerDevicePath
        });

        bool found = locator.TryFind(out string devicePath);

        Assert.That(found, Is.True);
        Assert.That(devicePath, Is.EqualTo(VhciDevicePath));
    }

    [Test]
    public void TryFind_BlankCandidatesBeforeRealPath_SkipsBlanksAndTrimsResult()
    {
        VhciDeviceLocator locator = new VhciDeviceLocator(() => new[]
        {
            string.Empty, "   ", $"  {VhciDevicePath}  "
        });

        bool found = locator.TryFind(out string devicePath);

        Assert.That(found, Is.True);
        Assert.That(devicePath, Is.EqualTo(VhciDevicePath));
    }

    [Test]
    public void TryFind_NoCandidates_ReturnsFalseWithEmptyPath()
    {
        VhciDeviceLocator locator = new VhciDeviceLocator(Array.Empty<string>);

        bool found = locator.TryFind(out string devicePath);

        Assert.That(found, Is.False);
        Assert.That(devicePath, Is.Empty);
    }

    [Test]
    public void TryFind_ProviderThrows_ReturnsFalseInsteadOfThrowing()
    {
        VhciDeviceLocator locator = new VhciDeviceLocator(() => throw new InvalidOperationException("Synthetic enumeration failure"));

        bool found = locator.TryFind(out string devicePath);

        Assert.That(found, Is.False);
        Assert.That(devicePath, Is.Empty);
    }

    [Test]
    public void TryFind_RealDriverOnThisMachine_FindsOpenablePath()
    {
        if (Environment.GetEnvironmentVariable("EMULATION_INTEGRATION") != "1")
        {
            Assert.Ignore("Set EMULATION_INTEGRATION=1 to run USB/IP integration tests");
        }

        VhciDeviceLocator locator = new VhciDeviceLocator();

        bool found = locator.TryFind(out string devicePath);

        TestContext.Out.WriteLine($"VHCI device path: '{devicePath}'");
        Assert.That(found, Is.True, "The usbip-win2 VHCI device interface should be present on this machine.");
        Assert.That(devicePath, Does.StartWith(@"\\?\"));
        // Opening proves the enumerated path is a live device interface handle (CreateFile
        // semantics) - the handle future tasks send IOCTLs to.
        using FileStream stream = new FileStream(devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Assert.That(stream.SafeFileHandle.IsInvalid, Is.False);
    }
}