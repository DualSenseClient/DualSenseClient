using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.Profiles;
using DualSenseClient.Hid;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.Profiles;

[TestFixture]
public sealed class ProfileActivationTests
{
    private string _tempDir = null!;
    private ProfileService _profiles = null!;
    private ControllerInfoService _controllers = null!;
    private ControllerTracker _tracker = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ProfileActivationTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _profiles = new ProfileService(profilesPath: Path.Combine(_tempDir, "profiles.json"));
        _controllers = new ControllerInfoService(controllersPath: Path.Combine(_tempDir, "controllers.json"));
        _tracker = new ControllerTracker();
    }

    [TearDown]
    public void TearDown()
    {
        _tracker.Dispose();
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Test]
    public void Activate_NoBinding_AppliesDefaultProfile()
    {
        Profile defaultProfile = _profiles.GetProfile(ProfileService.DefaultProfileName)!;
        defaultProfile.Lightbar.Red = 10;
        defaultProfile.Lightbar.Green = 20;
        defaultProfile.Lightbar.Blue = 30;
        using DualSenseDevice device = CreateDevice();
        _tracker.TrackController(device);
        ProfileActivation activation = new ProfileActivation(_profiles, _controllers, _tracker);

        activation.Activate(device);

        Assert.That(device.CurrentLightbarColor, Is.EqualTo(((byte)10, (byte)20, (byte)30)));
    }

    [Test]
    public void Bind_PersistsBindingAndAppliesProfile()
    {
        Profile profile = AddProfile("Night", 40, 50, 60);
        using DualSenseDevice device = CreateDevice();
        _tracker.TrackController(device);
        using ProfileActivation activation = new ProfileActivation(_profiles, _controllers, _tracker);

        activation.Bind(device, profile);

        Assert.Multiple(() =>
        {
            Assert.That(_controllers.GetBoundProfileName(device.Key), Is.EqualTo("Night"));
            Assert.That(device.CurrentLightbarColor, Is.EqualTo(((byte)40, (byte)50, (byte)60)));
        });
    }

    [Test]
    public void RenameProfile_UpdatesBindingAndProfile()
    {
        Profile profile = AddProfile("Night", 40, 50, 60);
        using DualSenseDevice device = CreateDevice();
        _tracker.TrackController(device);
        using ProfileActivation activation = new ProfileActivation(_profiles, _controllers, _tracker);
        activation.Bind(device, profile);

        bool renamed = activation.RenameProfile("Night", "Evening");

        Assert.Multiple(() =>
        {
            Assert.That(renamed, Is.True);
            Assert.That(_profiles.GetProfile("Evening"), Is.Not.Null);
            Assert.That(_profiles.GetProfile("Night"), Is.Null);
            Assert.That(_controllers.GetBoundProfileName(device.Key), Is.EqualTo("Evening"));
        });
    }

    [Test]
    public void DeleteProfile_ClearsBindingAndFallsBackToDefault()
    {
        Profile profile = AddProfile("Night", 40, 50, 60);
        using DualSenseDevice device = CreateDevice();
        _tracker.TrackController(device);
        using ProfileActivation activation = new ProfileActivation(_profiles, _controllers, _tracker);
        activation.Bind(device, profile);

        bool deleted = activation.DeleteProfile("Night");

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(_controllers.GetBoundProfileName(device.Key), Is.Null);
            Assert.That(activation.GetActiveProfile(device)?.Name, Is.EqualTo(ProfileService.DefaultProfileName));
        });
    }

    [Test]
    public void ProfilesChanged_ReactivatesTrackedControllers()
    {
        Profile profile = AddProfile("Night", 40, 50, 60);
        using DualSenseDevice device = CreateDevice();
        _tracker.TrackController(device);
        using ProfileActivation activation = new ProfileActivation(_profiles, _controllers, _tracker);
        activation.Bind(device, profile);

        profile.Lightbar.Red = 70;
        profile.Lightbar.Green = 80;
        profile.Lightbar.Blue = 90;
        _profiles.Save();

        Assert.That(device.CurrentLightbarColor, Is.EqualTo(((byte)70, (byte)80, (byte)90)));
    }

    private Profile AddProfile(string name, byte red, byte green, byte blue)
    {
        Profile profile = new Profile
        {
            Name = name,
            Lightbar =
            {
                Red = red,
                Green = green,
                Blue = blue
            }
        };
        _profiles.Settings.Profiles.Add(profile);
        _profiles.Save();
        return profile;
    }

    private static DualSenseDevice CreateDevice() => new DualSenseDevice(new SilentHidDevice(), new StubHidDeviceInfo());

    private sealed class SilentHidDevice : IHidDevice
    {
        public ushort VendorId
        {
            get
            {
                return 0x054C;
            }
        }

        public ushort ProductId
        {
            get
            {
                return 0x0CE6;
            }
        }

        public string DevicePath
        {
            get
            {
                return "test";
            }
        }

        public bool IsConnected
        {
            get
            {
                return true;
            }
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count) => count;

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) => [];

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "Test";

        public void Dispose()
        {
        }
    }

    private sealed class StubHidDeviceInfo : IHidDeviceInfo
    {
        public string Path
        {
            get
            {
                return "test";
            }
        }

        public ushort VendorId
        {
            get
            {
                return 0x054C;
            }
        }

        public ushort ProductId
        {
            get
            {
                return 0x0CE6;
            }
        }

        public string ProductName
        {
            get
            {
                return "DualSense Test";
            }
        }

        public string Manufacturer
        {
            get
            {
                return "Sony";
            }
        }

        public int InterfaceNumber
        {
            get
            {
                return 0;
            }
        }

        public ushort UsagePage
        {
            get
            {
                return 1;
            }
        }

        public HidUsageId Usage
        {
            get
            {
                return HidUsageId.GamePad;
            }
        }

        public ConnectionType BusType
        {
            get
            {
                return ConnectionType.Usb;
            }
        }
    }
}