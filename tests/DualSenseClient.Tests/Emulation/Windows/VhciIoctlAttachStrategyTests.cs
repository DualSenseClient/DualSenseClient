using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Windows;
using Microsoft.Win32.SafeHandles;

namespace DualSenseClient.Tests.Emulation.Windows;

/// <summary>
/// Tests of <see cref="VhciIoctlAttachStrategy"/>: the native attach flow (locate the device
/// interface, open one handle per operation, send PLUGIN_HARDWARE with the exact request image,
/// remember the driver-assigned hub port), the detach flow (PLUGOUT_HARDWARE on the remembered
/// port) and the failure contract (return false with an actionable error carrying the install
/// guidance instead of throwing). All runs go through <see cref="FakeVhciDriver"/> and a
/// <see cref="VhciDeviceLocator"/> with a fake candidate provider, so the tests are hermetic and
/// need no usbip-win2 driver. Disposal is asserted through the handles the tests hand to the fake:
/// a closed handle proves the strategy disposed its per-operation handle.
/// </summary>
[TestFixture]
public sealed class VhciIoctlAttachStrategyTests
{
    private const string Host = "127.0.0.1";
    private const int Port = 3240;
    private const string BusId = "1-1";

    /// <summary>Exact service string the strategy must pass for <see cref="Port"/> (invariant "3240").</summary>
    private const string Service = "3240";

    private const string DevicePath = @"\\?\ROOT#USB#0000#{b4030c06-dc5f-4fcc-87eb-e5515a0935c0}";

    [Test]
    public void TryAttach_AttachableDevice_SendsExactRequestRemembersPortAndDisposesHandle()
    {
        SafeFileHandle handle = CreateValidHandle();
        FakeVhciDriver driver = new FakeVhciDriver
        {
            HandleToReturn = handle,
            ConnectResult = true,
            PortToReturn = 5
        };
        RecordingLogger logger = new RecordingLogger();
        VhciIoctlAttachStrategy strategy = CreateStrategy(driver, logger);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(driver.OpenedPaths, Is.EqualTo(new[]
        {
            DevicePath
        }));
        Assert.That(driver.ConnectRequests, Has.Count.EqualTo(1));
        // The recorded request is value-compared through its wire image (structs have no value equality).
        Assert.That(ToRequestBytes(driver.ConnectRequests[0]), Is.EqualTo(ToRequestBytes(new VhciAttachRequest(Host, Service, BusId))));
        Assert.That(logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Information), Is.True);
        Assert.That(handle.IsClosed, Is.True, "The per-operation handle must be disposed after the call.");
    }

    [Test]
    public void TryAttach_NoDeviceInterfaceFound_ReturnsFalseWithGuidanceAndNeverOpens()
    {
        FakeVhciDriver driver = new FakeVhciDriver();
        RecordingLogger logger = new RecordingLogger();
        VhciIoctlAttachStrategy strategy = CreateStrategy(driver, logger, CreateLocatorWithoutDevice());

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.False);
        Assert.That(error, Does.Contain("was not found"));
        Assert.That(error, Does.Contain("usbip-win2"));
        Assert.That(error, Does.Contain("USB/IP must be installed"));
        Assert.That(driver.OpenedPaths, Is.Empty, "The driver must not be touched when the device is missing.");
        Assert.That(logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Warning), Is.True);
    }

    [Test]
    public void TryAttach_OpenFailure_ReturnsFalseWithGuidance()
    {
        // The fake's default handle is invalid, so Open fails without any real device.
        FakeVhciDriver driver = new FakeVhciDriver();
        RecordingLogger logger = new RecordingLogger();
        VhciIoctlAttachStrategy strategy = CreateStrategy(driver, logger);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.False);
        Assert.That(error, Does.Contain("could not be opened"));
        Assert.That(error, Does.Contain("USB/IP must be installed"));
        Assert.That(driver.OpenedPaths, Is.EqualTo(new[]
        {
            DevicePath
        }));
        Assert.That(driver.ConnectRequests, Is.Empty);
        Assert.That(logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Warning), Is.True);
    }

    [Test]
    public void TryAttach_ConnectFailure_ReturnsFalseWithWin32ErrorAndGuidance()
    {
        SafeFileHandle handle = CreateValidHandle();
        FakeVhciDriver driver = new FakeVhciDriver
        {
            HandleToReturn = handle,
            ConnectResult = false,
            ConnectWin32Error = 31
        };
        RecordingLogger logger = new RecordingLogger();
        VhciIoctlAttachStrategy strategy = CreateStrategy(driver, logger);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.False);
        Assert.That(error, Does.Contain("Win32 error 31"));
        Assert.That(error, Does.Contain("usbip-win2"));
        Assert.That(error, Does.Contain("USB/IP must be installed"));
        Assert.That(logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Warning), Is.True);
        Assert.That(handle.IsClosed, Is.True, "The per-operation handle must be disposed after the call.");
    }

    [Test]
    public void TryAttach_ConnectFailure_LeavesNoRememberedPort()
    {
        SafeFileHandle handle = CreateValidHandle();
        FakeVhciDriver driver = new FakeVhciDriver
        {
            HandleToReturn = handle,
            ConnectResult = false,
            ConnectWin32Error = 31
        };
        RecordingLogger logger = new RecordingLogger();
        VhciIoctlAttachStrategy strategy = CreateStrategy(driver, logger);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? _);
        bool detached = strategy.TryDetach(BusId, out string? detachError);

        Assert.That(attached, Is.False);
        Assert.That(detached, Is.False);
        Assert.That(detachError, Does.Contain("no vhci port"));
        Assert.That(driver.OpenedPaths, Is.EqualTo(new[]
        {
            DevicePath
        }), "The failed detach must not touch the driver.");
    }

    [Test]
    public void TryAttach_AfterDetach_ReattachesAndUsesTheNewestPort()
    {
        SafeFileHandle firstAttachHandle = CreateValidHandle();
        FakeVhciDriver driver = new FakeVhciDriver
        {
            HandleToReturn = firstAttachHandle,
            ConnectResult = true,
            PortToReturn = 3,
            DisconnectResult = true
        };
        RecordingLogger logger = new RecordingLogger();
        VhciIoctlAttachStrategy strategy = CreateStrategy(driver, logger);

        bool firstAttach = strategy.TryAttach(Host, Port, BusId, out string? _);
        SafeFileHandle firstDetachHandle = CreateValidHandle();
        driver.HandleToReturn = firstDetachHandle;
        bool firstDetach = strategy.TryDetach(BusId, out string? _);

        SafeFileHandle secondAttachHandle = CreateValidHandle();
        driver.HandleToReturn = secondAttachHandle;
        driver.PortToReturn = 4;
        bool secondAttach = strategy.TryAttach(Host, Port, BusId, out string? reattachError);
        SafeFileHandle secondDetachHandle = CreateValidHandle();
        driver.HandleToReturn = secondDetachHandle;
        bool secondDetach = strategy.TryDetach(BusId, out string? secondDetachError);

        Assert.That(firstAttach, Is.True);
        Assert.That(firstDetach, Is.True);
        Assert.That(secondAttach, Is.True);
        Assert.That(reattachError, Is.Null);
        Assert.That(secondDetach, Is.True);
        Assert.That(secondDetachError, Is.Null);
        Assert.That(driver.ConnectRequests, Has.Count.EqualTo(2));
        Assert.That(driver.DisconnectedPorts, Is.EqualTo(new[]
        {
            3u, 4u
        }));
        Assert.That(firstAttachHandle.IsClosed, Is.True);
        Assert.That(firstDetachHandle.IsClosed, Is.True);
        Assert.That(secondAttachHandle.IsClosed, Is.True);
        Assert.That(secondDetachHandle.IsClosed, Is.True);
    }

    [Test]
    public void TryDetach_AfterSuccessfulAttach_DisconnectsRememberedPortAndDisposesEachHandle()
    {
        SafeFileHandle attachHandle = CreateValidHandle();
        FakeVhciDriver driver = new FakeVhciDriver
        {
            HandleToReturn = attachHandle,
            ConnectResult = true,
            PortToReturn = 5,
            DisconnectResult = true
        };
        RecordingLogger logger = new RecordingLogger();
        VhciIoctlAttachStrategy strategy = CreateStrategy(driver, logger);
        Assert.That(strategy.TryAttach(Host, Port, BusId, out string? _), Is.True);

        SafeFileHandle detachHandle = CreateValidHandle();
        driver.HandleToReturn = detachHandle;
        bool detached = strategy.TryDetach(BusId, out string? error);

        Assert.That(detached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(driver.DisconnectedPorts, Is.EqualTo(new[]
        {
            5u
        }));
        Assert.That(driver.OpenedPaths, Is.EqualTo(new[]
        {
            DevicePath, DevicePath
        }));
        Assert.That(logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Information), Is.True);
        Assert.That(attachHandle.IsClosed, Is.True);
        Assert.That(detachHandle.IsClosed, Is.True);
    }

    [Test]
    public void TryDetach_WithoutPriorAttach_ReturnsFalseWithGuidanceAndNeverTouchesDriver()
    {
        FakeVhciDriver driver = new FakeVhciDriver();
        RecordingLogger logger = new RecordingLogger();
        VhciIoctlAttachStrategy strategy = CreateStrategy(driver, logger);

        bool detached = strategy.TryDetach(BusId, out string? error);

        Assert.That(detached, Is.False);
        Assert.That(error, Does.Contain("no vhci port"));
        Assert.That(error, Does.Contain("attach it through this strategy first"));
        Assert.That(error, Does.Contain("USB/IP must be installed"));
        Assert.That(driver.OpenedPaths, Is.Empty);
        Assert.That(driver.DisconnectedPorts, Is.Empty);
        Assert.That(logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Warning), Is.True);
    }

    [Test]
    public void TryDetach_DisconnectFailure_ReturnsFalseWithWin32ErrorAndKeepsPortForRetry()
    {
        SafeFileHandle attachHandle = CreateValidHandle();
        FakeVhciDriver driver = new FakeVhciDriver
        {
            HandleToReturn = attachHandle,
            ConnectResult = true,
            PortToReturn = 7
        };
        RecordingLogger logger = new RecordingLogger();
        VhciIoctlAttachStrategy strategy = CreateStrategy(driver, logger);
        Assert.That(strategy.TryAttach(Host, Port, BusId, out string? _), Is.True);

        SafeFileHandle failedDetachHandle = CreateValidHandle();
        driver.HandleToReturn = failedDetachHandle;
        driver.DisconnectResult = false;
        driver.DisconnectWin32Error = 5;
        bool firstAttempt = strategy.TryDetach(BusId, out string? error);

        SafeFileHandle retryHandle = CreateValidHandle();
        driver.HandleToReturn = retryHandle;
        driver.DisconnectResult = true;
        bool retry = strategy.TryDetach(BusId, out string? retryError);

        Assert.That(firstAttempt, Is.False);
        Assert.That(error, Does.Contain("Win32 error 5"));
        Assert.That(error, Does.Contain("USB/IP must be installed"));
        Assert.That(retry, Is.True);
        Assert.That(retryError, Is.Null);
        Assert.That(driver.DisconnectedPorts, Is.EqualTo(new[]
        {
            7u, 7u
        }));
        Assert.That(failedDetachHandle.IsClosed, Is.True, "The per-operation handle must be disposed even when the IOCTL fails.");
    }

    [Test]
    public void TryDetach_DeviceVanishedAfterAttach_ReturnsFalseAndKeepsPortForRetry()
    {
        bool devicePresent = true;
        VhciDeviceLocator locator = new VhciDeviceLocator(() => devicePresent
            ? new[]
            {
                DevicePath
            }
            : Array.Empty<string>());
        SafeFileHandle attachHandle = CreateValidHandle();
        FakeVhciDriver driver = new FakeVhciDriver
        {
            HandleToReturn = attachHandle,
            ConnectResult = true,
            PortToReturn = 9,
            DisconnectResult = true
        };
        RecordingLogger logger = new RecordingLogger();
        VhciIoctlAttachStrategy strategy = CreateStrategy(driver, logger, locator);
        Assert.That(strategy.TryAttach(Host, Port, BusId, out string? _), Is.True);

        devicePresent = false;
        bool unavailableAttempt = strategy.TryDetach(BusId, out string? unavailableError);

        devicePresent = true;
        driver.HandleToReturn = CreateValidHandle();
        bool retry = strategy.TryDetach(BusId, out string? retryError);

        Assert.That(unavailableAttempt, Is.False);
        Assert.That(unavailableError, Does.Contain("was not found"));
        Assert.That(unavailableError, Does.Contain("USB/IP must be installed"));
        Assert.That(retry, Is.True);
        Assert.That(retryError, Is.Null);
        Assert.That(driver.DisconnectedPorts, Is.EqualTo(new[]
        {
            9u
        }));
        Assert.That(driver.OpenedPaths, Has.Count.EqualTo(2));
    }

    [Test]
    public void TryAttach_DriverThrows_ReturnsFalseWithErrorInsteadOfThrowing()
    {
        ThrowingVhciDriver driver = new ThrowingVhciDriver();
        RecordingLogger logger = new RecordingLogger();
        VhciIoctlAttachStrategy strategy = new VhciIoctlAttachStrategy(driver, logger, CreateLocatorWithDevice());

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.False);
        Assert.That(error, Does.Contain("Synthetic driver fault"));
        Assert.That(error, Does.Contain("USB/IP must be installed"));
        Assert.That(logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Warning), Is.True);
    }

    private static VhciIoctlAttachStrategy CreateStrategy(FakeVhciDriver driver, RecordingLogger logger) =>
        CreateStrategy(driver, logger, CreateLocatorWithDevice());

    private static VhciIoctlAttachStrategy CreateStrategy(FakeVhciDriver driver, RecordingLogger logger, VhciDeviceLocator locator) =>
        new VhciIoctlAttachStrategy(driver, logger, locator);

    private static VhciDeviceLocator CreateLocatorWithDevice() => new VhciDeviceLocator(() => new[]
    {
        DevicePath
    });

    private static VhciDeviceLocator CreateLocatorWithoutDevice() => new VhciDeviceLocator(() => Array.Empty<string>());

    /// <summary>
    /// Creates an open, valid handle for the fake. The value is never a real kernel handle (kernel
    /// handle values are 4-aligned), so disposing it is silent; <see cref="SafeFileHandle.IsClosed"/>
    /// then proves the strategy disposed the handle it got back.
    /// </summary>
    private static SafeFileHandle CreateValidHandle() => new SafeFileHandle(new IntPtr(1), true);

    private static byte[] ToRequestBytes(VhciAttachRequest request)
    {
        byte[] bytes = new byte[VhciAttachRequest.SizeInBytes];
        request.Write(bytes);
        return bytes;
    }

    /// <summary>
    /// <see cref="IVhciDriver"/> that always throws, proving the strategy's defensive catch maps an
    /// unexpected seam fault to the documented failure shape instead of letting it escape.
    /// </summary>
    private sealed class ThrowingVhciDriver : IVhciDriver
    {
        /// <inheritdoc/>
        public SafeFileHandle Open(string devicePath) => throw new InvalidOperationException("Synthetic driver fault");

        /// <inheritdoc/>
        public bool TryConnect(SafeFileHandle device, VhciAttachRequest request, out uint port, out int win32Error) =>
            throw new InvalidOperationException("Synthetic driver fault");

        /// <inheritdoc/>
        public bool TryDisconnect(SafeFileHandle device, uint port, out int win32Error) =>
            throw new InvalidOperationException("Synthetic driver fault");
    }
}