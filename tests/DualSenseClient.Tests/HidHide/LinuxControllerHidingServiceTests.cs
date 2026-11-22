using DualSenseClient.HidHide;
using DualSenseClient.Tests.HidHide.TestSupport;

namespace DualSenseClient.Tests.HidHide;

/// <summary>
/// Tests of the Linux hiding service using a fake socket client.
/// </summary>
[TestFixture]
public sealed class LinuxControllerHidingServiceTests
{
    /// <summary>
    /// Controller ID used by the tests.
    /// </summary>
    private const string ControllerId = "d0:bc:c1:17:55:63";

    /// <summary>
    /// Temporary sysfs/group root.
    /// </summary>
    private string _tempDir = null!;

    /// <summary>
    /// Temporary group database.
    /// </summary>
    private string _groupFile = null!;

    /// <summary>
    /// Temporary sysfs root.
    /// </summary>
    private string _sysfsRoot = null!;

    /// <summary>
    /// Creates an isolated temp directory and group file.
    /// </summary>
    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LinuxControllerHidingServiceTests_{Guid.NewGuid():N}");
        _sysfsRoot = Path.Combine(_tempDir, "sys");
        _groupFile = Path.Combine(_tempDir, "group");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_groupFile, "root:x:0:\ndualsenseclient:x:4242:goose\n");
    }

    /// <summary>
    /// Removes the temp directory.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Resolves a controller ID through the sysfs tree.
    /// </summary>
    [Test]
    public void TryGetInstanceId_ReturnsMac()
    {
        WriteUevent("d0:bc:c1:17:55:63");
        (LinuxControllerHidingService service, _) = CreateService();

        Assert.That(service.TryGetInstanceId("/dev/hidraw2", out string id), Is.True);
        Assert.That(id, Is.EqualTo(ControllerId));
    }

    /// <summary>
    /// A missing uevent leaves the controller unresolvable.
    /// </summary>
    [Test]
    public void TryGetInstanceId_MissingUevent_ReturnsFalse()
    {
        (LinuxControllerHidingService service, _) = CreateService();

        Assert.That(service.TryGetInstanceId("/dev/hidraw2", out string id), Is.False);
        Assert.That(id, Is.Empty);
    }

    /// <summary>
    /// Availability requires the effective GID and a successful connect.
    /// </summary>
    [Test]
    public void IsAvailable_WhenGroupMatchesAndClientConnects_ReturnsTrue()
    {
        Assume.That(OperatingSystem.IsLinux());
        (LinuxControllerHidingService service, FakeLinuxHidingClient client) = CreateService();

        Assert.That(service.IsAvailable, Is.True);
        Assert.That(client.ConnectCalls, Is.EqualTo(1));
    }

    /// <summary>
    /// A process not running with the hiding group never connects.
    /// </summary>
    [Test]
    public void IsAvailable_WhenEffectiveGidDiffers_ReturnsFalseAndDoesNotConnect()
    {
        Assume.That(OperatingSystem.IsLinux());
        (LinuxControllerHidingService service, FakeLinuxHidingClient client) = CreateService(1000);

        Assert.That(service.IsAvailable, Is.False);
        Assert.That(client.ConnectCalls, Is.EqualTo(0));
    }

    /// <summary>
    /// A missing group is reported as unavailable.
    /// </summary>
    [Test]
    public void IsAvailable_WhenGroupMissing_ReturnsFalseAndDoesNotConnect()
    {
        Assume.That(OperatingSystem.IsLinux());
        File.WriteAllText(_groupFile, "root:x:0:\n");
        (LinuxControllerHidingService service, FakeLinuxHidingClient client) = CreateService();

        Assert.That(service.IsAvailable, Is.False);
        Assert.That(client.ConnectCalls, Is.EqualTo(0));
    }

    /// <summary>
    /// Hiding sends the ID and records the state.
    /// </summary>
    [Test]
    public void SetControllerHidden_HidesAndUpdatesCache()
    {
        Assume.That(OperatingSystem.IsLinux());
        (LinuxControllerHidingService service, FakeLinuxHidingClient client) = CreateService();

        service.SetControllerHidden(ControllerId, true);

        Assert.That(client.HideCalls, Is.EqualTo(new[]
        {
            ControllerId
        }));
    }

    /// <summary>
    /// Repeating the same state does not send a second request.
    /// </summary>
    [Test]
    public void SetControllerHidden_SameState_DoesNotCallClient()
    {
        Assume.That(OperatingSystem.IsLinux());
        (LinuxControllerHidingService service, FakeLinuxHidingClient client) = CreateService();

        service.SetControllerHidden(ControllerId, true);
        service.SetControllerHidden(ControllerId, true);

        Assert.That(client.HideCalls.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// An invalid ID is ignored without opening a connection.
    /// </summary>
    [Test]
    public void SetControllerHidden_InvalidId_DoesNotConnect()
    {
        Assume.That(OperatingSystem.IsLinux());
        (LinuxControllerHidingService service, FakeLinuxHidingClient client) = CreateService();

        service.SetControllerHidden("bad id", true);

        Assert.That(client.ConnectCalls, Is.EqualTo(0));
        Assert.That(client.HideCalls, Is.Empty);
    }

    /// <summary>
    /// A failed hide request does not enter the cache.
    /// </summary>
    [Test]
    public void SetControllerHidden_WhenClientFails_DoesNotAddToCache()
    {
        Assume.That(OperatingSystem.IsLinux());
        (LinuxControllerHidingService service, FakeLinuxHidingClient client) = CreateService();
        client.SetHiddenResult = false;

        service.SetControllerHidden(ControllerId, true);

        Assert.That(client.HideCalls, Is.Empty);
        Assert.That(service.IsControllerHidden(ControllerId), Is.False);
    }

    /// <summary>
    /// Status is read from the helper, not only from the local cache.
    /// </summary>
    [Test]
    public void IsControllerHidden_UsesClientStatus()
    {
        Assume.That(OperatingSystem.IsLinux());
        (LinuxControllerHidingService service, FakeLinuxHidingClient client) = CreateService();
        client.StatusHidden = true;

        Assert.That(service.IsControllerHidden(ControllerId), Is.True);
        Assert.That(client.StatusCalls, Does.Contain(ControllerId));
    }

    /// <summary>
    /// An invalid ID is never queried.
    /// </summary>
    [Test]
    public void IsControllerHidden_InvalidId_ReturnsFalseWithoutConnecting()
    {
        Assume.That(OperatingSystem.IsLinux());
        (LinuxControllerHidingService service, FakeLinuxHidingClient client) = CreateService();

        Assert.That(service.IsControllerHidden("bad id"), Is.False);
        Assert.That(client.ConnectCalls, Is.EqualTo(0));
    }

    /// <summary>
    /// Re-establishing self visibility reconnects and re-sends cached hides.
    /// </summary>
    [Test]
    public void EnsureSelfVisible_ReconnectsAndResendsCachedHides()
    {
        Assume.That(OperatingSystem.IsLinux());
        (LinuxControllerHidingService service, FakeLinuxHidingClient client) = CreateService();
        service.SetControllerHidden(ControllerId, true);
        client.Disconnect();

        service.EnsureSelfVisible();

        Assert.That(client.HideCalls.Count(id => id == ControllerId), Is.EqualTo(2));
    }

    /// <summary>
    /// A transient status failure does not forget a cached hide.
    /// </summary>
    [Test]
    public void IsControllerHidden_WhenStatusFails_KeepsCachedHide()
    {
        Assume.That(OperatingSystem.IsLinux());
        (LinuxControllerHidingService service, FakeLinuxHidingClient client) = CreateService();
        service.SetControllerHidden(ControllerId, true);

        client.StatusResult = false;

        Assert.That(service.IsControllerHidden(ControllerId), Is.True);
    }

    /// <summary>
    /// A helper restart is detected and cached hides are re-sent.
    /// </summary>
    [Test]
    public void IsControllerHidden_AfterHelperRestart_RehidesCachedController()
    {
        Assume.That(OperatingSystem.IsLinux());
        (LinuxControllerHidingService service, FakeLinuxHidingClient client) = CreateService();
        service.SetControllerHidden(ControllerId, true);

        client.Restart();

        Assert.That(service.IsControllerHidden(ControllerId), Is.True);
        Assert.That(client.HideCalls.Count(id => id == ControllerId), Is.EqualTo(2));
    }

    /// <summary>
    /// Disposing the service disposes the client once.
    /// </summary>
    [Test]
    public void Dispose_DisposesClientOnce()
    {
        (LinuxControllerHidingService service, FakeLinuxHidingClient client) = CreateService();

        service.Dispose();
        service.Dispose();

        Assert.That(client.DisposeCalls, Is.EqualTo(1));
    }

    /// <summary>
    /// Creates a service with a fake client and the temporary paths.
    /// </summary>
    /// <param name="effectiveGid">Effective GID reported by the test process.</param>
    /// <param name="connectResult">Result of the initial connect.</param>
    /// <returns>The service and its fake client.</returns>
    private (LinuxControllerHidingService Service, FakeLinuxHidingClient Client) CreateService(uint effectiveGid = 4242, bool connectResult = true)
    {
        FakeLinuxHidingClient client = new FakeLinuxHidingClient
        {
            ConnectResult = connectResult
        };
        LinuxHidingOptions options = new LinuxHidingOptions
        {
            SysfsRoot = _sysfsRoot,
            GroupFile = _groupFile,
            GroupName = "dualsenseclient",
            SocketPath = Path.Combine(_tempDir, "hiding.sock")
        };
        return (new LinuxControllerHidingService(options, client, () => effectiveGid), client);
    }

    /// <summary>
    /// Writes a minimal hidraw uevent for the test controller.
    /// </summary>
    /// <param name="uniq">Value of HID_UNIQ.</param>
    private void WriteUevent(string uniq)
    {
        string ueventDir = Path.Combine(_sysfsRoot, "class", "hidraw", "hidraw2", "device");
        Directory.CreateDirectory(ueventDir);
        File.WriteAllText(Path.Combine(ueventDir, "uevent"),
            $"HID_ID=0003:0000054C:00000CE6\nHID_UNIQ={uniq}\n");
    }
}