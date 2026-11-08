using DualSenseClient.Core.Models;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

public class TouchpadMouseSettingsTests
{
    private string _tempDir = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"TouchpadMouseSettingsTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // cleanup best-effort
        }
    }

    private string ControllersPath
    {
        get
        {
            return Path.Combine(_tempDir, "Config", "controllers.json");
        }
    }

    private ControllerInfoService CreateService() => new ControllerInfoService(controllersPath: ControllersPath);

    [Test]
    public void TouchpadMouseSettings_Defaults_AreDisabledWithUnitSpeeds()
    {
        TouchpadMouseSettings settings = new TouchpadMouseSettings();
        Assert.Multiple(() =>
        {
            Assert.That(settings.Enabled, Is.False);
            Assert.That(settings.PointerSpeed, Is.EqualTo(1.0));
            Assert.That(settings.ScrollSpeed, Is.EqualTo(1.0));
            Assert.That(settings.NaturalScrolling, Is.False);
        });
    }

    [Test]
    public void ControllerInfo_Defaults_HasTouchpadMouseSection()
    {
        ControllerInfo info = new ControllerInfo();
        Assert.That(info.TouchpadMouse, Is.Not.Null);
        Assert.That(info.TouchpadMouse.Enabled, Is.False);
    }

    [Test]
    public void SaveTouchpadMouseSettings_PersistsAcrossServiceInstances()
    {
        ControllerInfoService service = CreateService();
        service.RegisterController("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1", "DualSense");
        ControllerKey key = ControllerKey.From("AA:BB:CC:DD:EE:FF", @"\\?\HID#VID_054C#1");

        TouchpadMouseSettings settings = service.GetTouchpadMouseSettings(key);
        settings.Enabled = true;
        settings.PointerSpeed = 1.75;
        settings.ScrollSpeed = 0.5;
        settings.NaturalScrolling = true;
        service.SaveTouchpadMouseSettings(key, settings);

        TouchpadMouseSettings stored = CreateService().GetTouchpadMouseSettings(key);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Enabled, Is.True);
            Assert.That(stored.PointerSpeed, Is.EqualTo(1.75));
            Assert.That(stored.ScrollSpeed, Is.EqualTo(0.5));
            Assert.That(stored.NaturalScrolling, Is.True);
        });
    }

    [Test]
    public void Load_ControllerEntryWithoutTouchpadSection_UsesDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ControllersPath)!);
        File.WriteAllText(ControllersPath, """
                                           { "controllers": [ { "name": "DualSense", "mac_address": "AA:BB:CC:DD:EE:FF" } ] }
                                           """);

        ControllerInfoService service = CreateService();
        TouchpadMouseSettings settings = service.GetTouchpadMouseSettings(
            ControllerKey.From("AA:BB:CC:DD:EE:FF", null));

        Assert.Multiple(() =>
        {
            Assert.That(settings.Enabled, Is.False);
            Assert.That(settings.PointerSpeed, Is.EqualTo(1.0));
            Assert.That(settings.ScrollSpeed, Is.EqualTo(1.0));
            Assert.That(settings.NaturalScrolling, Is.False);
        });
    }
}