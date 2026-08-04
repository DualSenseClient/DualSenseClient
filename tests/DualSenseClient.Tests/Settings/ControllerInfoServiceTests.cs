using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

public class ControllerInfoServiceTests
{
    private string _tempDir = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ControllerInfoServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // cleanup best-effort
        }
    }

    private string ControllersPath => Path.Combine(_tempDir, "Config", "controllers.json");

    private ControllerInfoService CreateService() => new ControllerInfoService(controllersPath: ControllersPath);

    [Test]
    public void Constructor_CreatesControllersDirectory()
    {
        string path = Path.Combine(_tempDir, "nested", "controllers.json");
        ControllerInfoService service = new ControllerInfoService(controllersPath: path);
        _ = service.Settings;
        Assert.That(File.Exists(path), Is.True);
    }

    [Test]
    public void Load_MissingFile_FallsBackToDefaults()
    {
        ControllerInfoService service = CreateService();
        Assert.That(service.Settings.Controllers, Is.Empty);
    }

    [Test]
    public void RegisterController_AddsEntryWithName()
    {
        ControllerInfoService service = CreateService();
        service.RegisterController("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1", "DualSense");

        ControllerInfo? info = service.GetControllerInfo("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1");
        Assert.Multiple(() =>
        {
            Assert.That(info, Is.Not.Null);
            Assert.That(info!.Name, Is.EqualTo("DualSense"));
            Assert.That(info.MacAddress, Is.EqualTo("AA:BB:CC:DD:EE:FF"));
            Assert.That(info.DevicePath, Is.EqualTo(@"\\?\HID#VID_054C#1"));
        });
        Assert.That(File.Exists(ControllersPath), Is.True);
    }

    [Test]
    public void RegisterController_DoesNotOverwriteExistingName()
    {
        ControllerInfoService service = CreateService();
        service.RegisterController("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1", "DualSense");
        service.RenameController("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1", "My Controller");
        service.RegisterController("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1", "DualSense");

        Assert.That(service.GetDisplayName("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1", "fallback"), Is.EqualTo("My Controller"));
    }

    [Test]
    public void RegisterController_MergesIdentifiers_OnReconnect()
    {
        ControllerInfoService service = CreateService();
        // Registered over Bluetooth (MAC only), later seen with both identifiers.
        service.RegisterController("AA:BB:CC:DD:EE:FF", string.Empty, "DualSense");
        service.RegisterController("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1", "DualSense");

        Assert.That(service.Settings.Controllers, Has.Count.EqualTo(1));
        ControllerInfo? info = service.GetControllerInfo("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1");
        Assert.Multiple(() =>
        {
            Assert.That(info, Is.Not.Null);
            Assert.That(info!.MacAddress, Is.EqualTo("AA:BB:CC:DD:EE:FF"));
            Assert.That(info.DevicePath, Is.EqualTo(@"\\?\HID#VID_054C#1"));
        });
    }

    [Test]
    public void RegisterController_EmptyIdentifiers_IsIgnored()
    {
        ControllerInfoService service = CreateService();
        service.RegisterController(string.Empty, string.Empty, "DualSense");
        Assert.That(service.Settings.Controllers, Is.Empty);
    }

    [Test]
    public void GetDisplayName_ReturnsCustomNameOrFallback()
    {
        ControllerInfoService service = CreateService();
        Assert.That(service.GetDisplayName("AA:BB:CC:DD:EE:FF", string.Empty, "product"), Is.EqualTo("product"));

        service.RegisterController("AA:BB:CC:DD:EE:FF", string.Empty, "product");
        Assert.That(service.GetDisplayName("AA:BB:CC:DD:EE:FF", string.Empty, "product"), Is.EqualTo("product"));

        service.RenameController("AA:BB:CC:DD:EE:FF", string.Empty, "My Controller");
        Assert.That(service.GetDisplayName("AA:BB:CC:DD:EE:FF", string.Empty, "product"), Is.EqualTo("My Controller"));
    }

    [Test]
    public void SetControllerProfile_BindsByMac()
    {
        ControllerInfoService service = CreateService();
        service.SetControllerProfile("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1", "Night Mode");

        Assert.That(service.GetBoundProfileName("AA:BB:CC:DD:EE:FF", string.Empty), Is.EqualTo("Night Mode"));
        Assert.That(service.GetBoundProfileName("11:22:33:44:55:66", string.Empty), Is.Null);
        Assert.That(service.GetBoundProfileName(string.Empty, @"\\?\HID#VID_054C#1"), Is.EqualTo("Night Mode"));
    }

    [Test]
    public void SetControllerProfile_NormalizesMacCaseAndWhitespace()
    {
        ControllerInfoService service = CreateService();
        service.SetControllerProfile("  aa:bb:cc:dd:ee:ff ", string.Empty, "Night Mode");
        Assert.That(service.GetBoundProfileName("AA:BB:CC:DD:EE:FF", string.Empty), Is.EqualTo("Night Mode"));
    }

    [Test]
    public void SetControllerProfile_ClearsBinding_WhenProfileNull()
    {
        ControllerInfoService service = CreateService();
        service.SetControllerProfile("AA:BB:CC:DD:EE:FF", string.Empty, "Night Mode");
        service.SetControllerProfile("AA:BB:CC:DD:EE:FF", string.Empty, null);

        Assert.That(service.GetBoundProfileName("AA:BB:CC:DD:EE:FF", string.Empty), Is.Null);
    }

    [Test]
    public void SetControllerProfile_BindsByDevicePath_WhenMacUnavailable()
    {
        ControllerInfoService service = CreateService();
        service.SetControllerProfile(string.Empty, @"\\?\HID#VID_054C#1", "Night Mode");

        Assert.That(service.GetBoundProfileName(string.Empty, @"\\?\HID#VID_054C#1"), Is.EqualTo("Night Mode"));
        Assert.That(service.GetBoundProfileName("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1"), Is.EqualTo("Night Mode"));
    }

    [Test]
    public void SetControllerProfile_ReplacesProfile_OnSameController()
    {
        ControllerInfoService service = CreateService();
        service.SetControllerProfile("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1", "One");
        service.SetControllerProfile("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1", "Two");

        Assert.Multiple(() =>
        {
            Assert.That(service.GetBoundProfileName("AA:BB:CC:DD:EE:FF", string.Empty), Is.EqualTo("Two"));
            Assert.That(service.GetBoundProfileName(string.Empty, @"\\?\HID#VID_054C#1"), Is.EqualTo("Two"));
            Assert.That(service.Settings.Controllers, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void GetBoundProfileName_PrefersMacOverDevicePath()
    {
        ControllerInfoService service = CreateService();
        // Simulates an edge case where two entries share a device path: the MAC-bound
        // entry must win when a MAC is available, and the path entry is the fallback.
        service.Settings.Controllers.Add(new ControllerInfo
        {
            MacAddress = string.Empty,
            DevicePath = @"\\?\HID#shared",
            ProfileName = "Path-Only"
        });
        service.Settings.Controllers.Add(new ControllerInfo
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
            DevicePath = @"\\?\HID#shared",
            ProfileName = "Mac-Bound"
        });

        Assert.Multiple(() =>
        {
            Assert.That(service.GetBoundProfileName("AA:BB:CC:DD:EE:FF", @"\\?\HID#shared"), Is.EqualTo("Mac-Bound"));
            Assert.That(service.GetBoundProfileName(string.Empty, @"\\?\HID#shared"), Is.EqualTo("Path-Only"));
        });
    }

    [Test]
    public void SetControllerProfile_EmptyIdentifiers_IsIgnored()
    {
        ControllerInfoService service = CreateService();
        service.SetControllerProfile(string.Empty, string.Empty, "Night Mode");
        Assert.That(service.Settings.Controllers, Is.Empty);
    }

    [Test]
    public void RenameController_RenamesAndPersists()
    {
        ControllerInfoService service = CreateService();
        service.RegisterController("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1", "DualSense");

        bool renamed = service.RenameController("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1", "  My Controller  ");

        Assert.Multiple(() =>
        {
            Assert.That(renamed, Is.True);
            Assert.That(service.GetDisplayName("AA:BB:CC:DD:EE:FF", string.Empty, "fallback"), Is.EqualTo("My Controller"));
        });

        ControllerInfoService reloaded = CreateService();
        Assert.That(reloaded.GetDisplayName("AA:BB:CC:DD:EE:FF", string.Empty, "fallback"), Is.EqualTo("My Controller"));
    }

    [Test]
    public void RenameController_EmptyOrUnknown_ReturnsFalse()
    {
        ControllerInfoService service = CreateService();
        Assert.Multiple(() =>
        {
            Assert.That(service.RenameController("AA:BB:CC:DD:EE:FF", string.Empty, "  "), Is.False);
            Assert.That(service.RenameController("AA:BB:CC:DD:EE:FF", string.Empty, "Whatever"), Is.False);
        });
    }

    [Test]
    public void RenameController_ExceedingMaxLength_ReturnsFalse()
    {
        ControllerInfoService service = CreateService();
        service.RegisterController("AA:BB:CC:DD:EE:FF", string.Empty, "DualSense");

        string tooLong = new string('x', ControllerInfoService.MaxNameLength + 1);
        Assert.That(service.RenameController("AA:BB:CC:DD:EE:FF", string.Empty, tooLong), Is.False);
        Assert.That(service.GetDisplayName("AA:BB:CC:DD:EE:FF", string.Empty, "fallback"), Is.EqualTo("DualSense"));
    }

    [Test]
    public void RenameController_MaxLengthName_IsAccepted()
    {
        ControllerInfoService service = CreateService();
        service.RegisterController("AA:BB:CC:DD:EE:FF", string.Empty, "DualSense");

        string maxLength = new string('x', ControllerInfoService.MaxNameLength);
        Assert.That(service.RenameController("AA:BB:CC:DD:EE:FF", string.Empty, maxLength), Is.True);
        Assert.That(service.GetDisplayName("AA:BB:CC:DD:EE:FF", string.Empty, "fallback"), Is.EqualTo(maxLength));
    }

    [Test]
    public void UpdateProfileName_ReassignsControllersAndPersists()
    {
        ControllerInfoService service = CreateService();
        service.SetControllerProfile("AA:BB:CC:DD:EE:FF", string.Empty, "Night Mode");
        service.SetControllerProfile("11:22:33:44:55:66", string.Empty, "Night Mode");
        service.SetControllerProfile("77:88:99:AA:BB:CC", string.Empty, "Other");

        service.UpdateProfileName("Night Mode", "Dark Mode");

        Assert.Multiple(() =>
        {
            Assert.That(service.GetBoundProfileName("AA:BB:CC:DD:EE:FF", string.Empty), Is.EqualTo("Dark Mode"));
            Assert.That(service.GetBoundProfileName("11:22:33:44:55:66", string.Empty), Is.EqualTo("Dark Mode"));
            Assert.That(service.GetBoundProfileName("77:88:99:AA:BB:CC", string.Empty), Is.EqualTo("Other"));
        });

        ControllerInfoService reloaded = CreateService();
        Assert.That(reloaded.GetBoundProfileName("AA:BB:CC:DD:EE:FF", string.Empty), Is.EqualTo("Dark Mode"));
    }

    [Test]
    public void RemoveProfileReferences_ClearsBindingsButKeepsControllers()
    {
        ControllerInfoService service = CreateService();
        service.RegisterController("AA:BB:CC:DD:EE:FF", string.Empty, "DualSense");
        service.SetControllerProfile("AA:BB:CC:DD:EE:FF", string.Empty, "Night Mode");

        service.RemoveProfileReferences("Night Mode");

        Assert.Multiple(() =>
        {
            Assert.That(service.GetBoundProfileName("AA:BB:CC:DD:EE:FF", string.Empty), Is.Null);
            Assert.That(service.GetDisplayName("AA:BB:CC:DD:EE:FF", string.Empty, "fallback"), Is.EqualTo("DualSense"));
        });

        ControllerInfoService reloaded = CreateService();
        Assert.That(reloaded.Settings.Controllers, Has.Count.EqualTo(1));
    }

    [Test]
    public void Save_FiresControllersChangedEvent()
    {
        ControllerInfoService service = CreateService();
        bool eventFired = false;
        service.ControllersChanged += (_, _) => eventFired = true;
        service.SetControllerProfile("AA:BB:CC:DD:EE:FF", string.Empty, "Night Mode");
        Assert.That(eventFired, Is.True);
    }
}