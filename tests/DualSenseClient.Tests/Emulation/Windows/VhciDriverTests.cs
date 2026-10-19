using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Windows;
using Microsoft.Win32.SafeHandles;

namespace DualSenseClient.Tests.Emulation.Windows;

/// <summary>
/// Tests of <see cref="VhciDriver"/>: the failure contract of the real P/Invoke seam (an invalid
/// handle is signalled, never thrown; failed IOCTLs return the Win32 error) and the env-gated live
/// case that locates the real usbip-win2 VHCI device on this machine and opens it. The seam is
/// also exercised through <see cref="FakeVhciDriver"/> to pin the interface shape consumers compile
/// against. No attach or detach IOCTL runs in this fixture.
/// </summary>
[TestFixture]
public sealed class VhciDriverTests
{
    [Test]
    public void Open_NonexistentDevicePath_ReturnsInvalidHandleWithoutThrowing()
    {
        VhciDriver driver = new VhciDriver();

        using SafeFileHandle handle = driver.Open(@"\\?\ROOT#USB#FFFF#{00000000-0000-0000-0000-000000000000}");

        Assert.That(handle, Is.Not.Null);
        Assert.That(handle.IsInvalid, Is.True);
    }

    [Test]
    public void TryConnect_InvalidHandle_ReturnsFalseWithWin32Error()
    {
        VhciDriver driver = new VhciDriver();
        VhciAttachRequest request = new VhciAttachRequest("127.0.0.1", "3240", "1-1");
        using SafeFileHandle handle = new SafeFileHandle(new IntPtr(-1), false);

        bool connected = driver.TryConnect(handle, request, out uint port, out int win32Error);

        Assert.That(connected, Is.False);
        Assert.That(port, Is.Zero);
        Assert.That(win32Error, Is.EqualTo(6)); // ERROR_INVALID_HANDLE
    }

    [Test]
    public void TryDisconnect_InvalidHandle_ReturnsFalseWithWin32Error()
    {
        VhciDriver driver = new VhciDriver();
        using SafeFileHandle handle = new SafeFileHandle(new IntPtr(-1), false);

        bool disconnected = driver.TryDisconnect(handle, 1, out int win32Error);

        Assert.That(disconnected, Is.False);
        Assert.That(win32Error, Is.EqualTo(6)); // ERROR_INVALID_HANDLE
    }

    [Test]
    public void Seam_FakeDriver_IsSubstitutableForTheRealDriver()
    {
        FakeVhciDriver fake = new FakeVhciDriver
        {
            ConnectResult = true,
            PortToReturn = 2,
            DisconnectResult = true
        };
        IVhciDriver driver = fake;
        using SafeFileHandle handle = driver.Open(@"\\?\test#device#path");
        VhciAttachRequest request = new VhciAttachRequest("127.0.0.1", "3240", "1-1");

        bool connected = driver.TryConnect(handle, request, out uint port, out int connectError);
        bool disconnected = driver.TryDisconnect(handle, port, out int disconnectError);

        Assert.That(connected, Is.True);
        Assert.That(port, Is.EqualTo(2));
        Assert.That(connectError, Is.Zero);
        Assert.That(disconnected, Is.True);
        Assert.That(disconnectError, Is.Zero);
        Assert.That(fake.OpenedPaths, Is.EqualTo(new[]
        {
            @"\\?\test#device#path"
        }));
        // The recorded request is value-compared through its wire image (structs have no value equality).
        Assert.That(fake.ConnectRequests, Has.Count.EqualTo(1));
        byte[] expectedRequestBytes = new byte[VhciAttachRequest.SizeInBytes];
        request.Write(expectedRequestBytes);
        byte[] recordedRequestBytes = new byte[VhciAttachRequest.SizeInBytes];
        fake.ConnectRequests[0].Write(recordedRequestBytes);
        Assert.That(recordedRequestBytes, Is.EqualTo(expectedRequestBytes));
        Assert.That(fake.DisconnectedPorts, Is.EqualTo(new[]
        {
            2u
        }));
    }

    [Test]
    public void Open_RealDriverOnThisMachine_ReturnsValidHandle()
    {
        if (Environment.GetEnvironmentVariable("EMULATION_INTEGRATION") != "1")
        {
            Assert.Ignore("Set EMULATION_INTEGRATION=1 to run USB/IP integration tests");
        }

        VhciDeviceLocator locator = new VhciDeviceLocator();
        bool found = locator.TryFind(out string devicePath);
        TestContext.Out.WriteLine($"VHCI device path: '{devicePath}'");
        Assert.That(found, Is.True, "The usbip-win2 VHCI device interface should be present on this machine.");

        VhciDriver driver = new VhciDriver();
        using SafeFileHandle handle = driver.Open(devicePath);

        TestContext.Out.WriteLine($"VHCI handle invalid: {handle.IsInvalid}");
        Assert.That(handle.IsInvalid, Is.False);
    }
}