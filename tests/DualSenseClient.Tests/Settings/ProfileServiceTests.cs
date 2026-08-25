using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

public class ProfileServiceTests
{
    private string _tempDir = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ProfileServiceTests_{Guid.NewGuid():N}");
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

    private string ProfilesPath
    {
        get
        {
            return Path.Combine(_tempDir, "Config", "profiles.json");
        }
    }

    private ProfileService CreateService() => new ProfileService(profilesPath: ProfilesPath);

    [Test]
    public void Constructor_CreatesProfilesDirectory()
    {
        string path = Path.Combine(_tempDir, "nested", "profiles.json");
        ProfileService service = new ProfileService(profilesPath: path);
        _ = service.Settings;
        Assert.That(File.Exists(path), Is.True);
    }

    [Test]
    public void Load_MissingFile_FallsBackToDefaults()
    {
        ProfileService service = CreateService();
        Assert.That(service.Settings.Profiles, Has.Count.EqualTo(1));
        Assert.That(service.Settings.Profiles[0].Name, Is.EqualTo(ProfileService.DefaultProfileName));
    }

    [Test]
    public void Load_MissingFile_SeedsDefaultProfileWithBlueLightbar()
    {
        ProfileService service = CreateService();
        Profile? profile = service.GetProfile(ProfileService.DefaultProfileName);
        Assert.Multiple(() =>
        {
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile!.Lightbar.Red, Is.EqualTo(0));
            Assert.That(profile.Lightbar.Green, Is.EqualTo(0));
            Assert.That(profile.Lightbar.Blue, Is.EqualTo(255));
            Assert.That(profile.MicLed.Mode, Is.EqualTo(0));
            Assert.That(profile.PlayerLeds.Mask, Is.EqualTo(0));
        });
    }

    [Test]
    public void Load_CorruptFile_FallsBackToDefaults()
    {
        string path = Path.Combine(_tempDir, "profiles.json");
        File.WriteAllText(path, "not valid json {{{");
        ProfileService service = new ProfileService(profilesPath: path);
        Assert.That(service.Settings.Profiles, Has.Count.EqualTo(1));
        Assert.That(service.Settings.Profiles[0].Name, Is.EqualTo(ProfileService.DefaultProfileName));
    }

    [Test]
    public void Load_ExistingFileWithoutDefault_SeedsDefaultProfile()
    {
        string path = Path.Combine(_tempDir, "profiles.json");
        File.WriteAllText(path, """{"profiles":[{"name":"Night Mode"}]}""");
        ProfileService service = new ProfileService(profilesPath: path);
        Assert.Multiple(() =>
        {
            Assert.That(service.GetProfile("Night Mode"), Is.Not.Null);
            Assert.That(service.GetProfile(ProfileService.DefaultProfileName), Is.Not.Null);
            Assert.That(service.Settings.Profiles, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void CreateProfile_AddsAndPersists()
    {
        ProfileService service = CreateService();
        Profile profile = service.CreateProfile();
        Assert.That(profile.Name, Is.EqualTo("Profile"));
        Assert.That(File.Exists(ProfilesPath), Is.True);

        ProfileService reloaded = CreateService();
        Assert.That(reloaded.Settings.Profiles, Has.Count.EqualTo(2));
        Assert.That(reloaded.GetProfile("Profile"), Is.Not.Null);
    }

    [Test]
    public void CreateProfile_DerivesUniqueName_WhenBaseTaken()
    {
        ProfileService service = CreateService();
        service.CreateProfile();
        Profile second = service.CreateProfile();
        Profile third = service.CreateProfile();
        Assert.Multiple(() =>
        {
            Assert.That(second.Name, Is.EqualTo("Profile 2"));
            Assert.That(third.Name, Is.EqualTo("Profile 3"));
        });
    }

    [Test]
    public void GetProfile_IsCaseInsensitive()
    {
        ProfileService service = CreateService();
        Profile created = service.CreateProfile("Night Mode");
        Assert.That(service.GetProfile("night mode"), Is.SameAs(created));
        Assert.That(service.GetProfile("Missing"), Is.Null);
    }

    [Test]
    public void RenameProfile_UpdatesProfileAndPersists()
    {
        ProfileService service = CreateService();
        service.CreateProfile("Night Mode");

        bool renamed = service.RenameProfile("Night Mode", "Dark Mode");

        Assert.Multiple(() =>
        {
            Assert.That(renamed, Is.True);
            Assert.That(service.GetProfile("Dark Mode"), Is.Not.Null);
            Assert.That(service.GetProfile("Night Mode"), Is.Null);
        });
    }

    [Test]
    public void RenameProfileInMemory_DoesNotPersist()
    {
        ProfileService service = CreateService();
        service.CreateProfile("Night Mode");

        bool renamed = service.RenameProfileInMemory("Night Mode", "Dark Mode");

        Assert.Multiple(() =>
        {
            Assert.That(renamed, Is.True);
            Assert.That(service.GetProfile("Dark Mode"), Is.Not.Null);
        });
        Assert.That(File.ReadAllText(ProfilesPath), Does.Not.Contain("Dark Mode"));
    }

    [Test]
    public void RenameProfile_EmptyOrDuplicate_ReturnsFalse()
    {
        ProfileService service = CreateService();
        service.CreateProfile("One");
        service.CreateProfile("Two");
        Assert.Multiple(() =>
        {
            Assert.That(service.RenameProfile("One", "  "), Is.False);
            Assert.That(service.RenameProfile("One", "Two"), Is.False);
            Assert.That(service.RenameProfile("Missing", "Whatever"), Is.False);
            Assert.That(service.RenameProfile("One", "One"), Is.False);
        });
    }

    [Test]
    public void DeleteProfile_RemovesProfile()
    {
        ProfileService service = CreateService();
        service.CreateProfile("Night Mode");

        bool deleted = service.DeleteProfile("Night Mode");

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(service.GetProfile("Night Mode"), Is.Null);
        });
    }

    [Test]
    public void DeleteProfile_MissingName_ReturnsFalse()
    {
        ProfileService service = CreateService();
        Assert.That(service.DeleteProfile("Missing"), Is.False);
    }

    [Test]
    public void DeleteProfile_DefaultProfile_IsReSeeded()
    {
        ProfileService service = CreateService();
        Assert.That(service.GetProfile(ProfileService.DefaultProfileName), Is.Not.Null);

        bool deleted = service.DeleteProfile(ProfileService.DefaultProfileName);

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.True);
            Assert.That(service.GetProfile(ProfileService.DefaultProfileName), Is.Not.Null);
        });
    }

    [Test]
    public void Save_FiresProfilesChangedEvent()
    {
        ProfileService service = CreateService();
        bool eventFired = false;
        service.ProfilesChanged += (_, _) => eventFired = true;
        service.CreateProfile();
        Assert.That(eventFired, Is.True);
    }

    [Test]
    public void RoundTrip_PreservesProfileData()
    {
        ProfileService service = CreateService();
        Profile profile = service.CreateProfile("Night Mode");
        profile.Lightbar.Red = 0xAA;
        profile.Lightbar.Green = 0xBB;
        profile.Lightbar.Blue = 0xCC;
        profile.MicLed.Mode = 2;
        profile.PlayerLeds.Mask = 0x07;
        service.Save();

        ProfileService reloaded = CreateService();
        Profile? loaded = reloaded.GetProfile("Night Mode");
        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Lightbar.Red, Is.EqualTo(0xAA));
            Assert.That(loaded.Lightbar.Green, Is.EqualTo(0xBB));
            Assert.That(loaded.Lightbar.Blue, Is.EqualTo(0xCC));
            Assert.That(loaded.MicLed.Mode, Is.EqualTo(2));
            Assert.That(loaded.PlayerLeds.Mask, Is.EqualTo(0x07));
        });
    }

    [Test]
    public void DuplicateProfile_CopiesLightsAndPersists()
    {
        ProfileService service = CreateService();
        Profile source = service.CreateProfile("Night Mode");
        source.Lightbar.Red = 0x12;
        source.Lightbar.Green = 0x34;
        source.Lightbar.Blue = 0x56;
        source.MicLed.Mode = 1;
        source.PlayerLeds.Mask = 0x05;
        service.Save();

        Profile? copy = service.DuplicateProfile("Night Mode");

        Assert.Multiple(() =>
        {
            Assert.That(copy, Is.Not.Null);
            Assert.That(copy!.Name, Is.EqualTo("Night Mode Copy"));
            Assert.That(copy.Lightbar.Red, Is.EqualTo(0x12));
            Assert.That(copy.Lightbar.Green, Is.EqualTo(0x34));
            Assert.That(copy.Lightbar.Blue, Is.EqualTo(0x56));
            Assert.That(copy.MicLed.Mode, Is.EqualTo(1));
            Assert.That(copy.PlayerLeds.Mask, Is.EqualTo(0x05));
            Assert.That(service.GetProfile("Night Mode"), Is.Not.Null);
        });

        ProfileService reloaded = CreateService();
        Assert.That(reloaded.GetProfile("Night Mode Copy"), Is.Not.Null);
    }

    [Test]
    public void DuplicateProfile_DerivesUniqueName_WhenCopyNameTaken()
    {
        ProfileService service = CreateService();
        service.CreateProfile("Night Mode");
        service.CreateProfile("Night Mode Copy");

        Profile? copy = service.DuplicateProfile("Night Mode");

        Assert.That(copy?.Name, Is.EqualTo("Night Mode Copy 2"));
    }

    [Test]
    public void DuplicateProfile_MissingName_ReturnsNull()
    {
        ProfileService service = CreateService();
        Assert.That(service.DuplicateProfile("Missing"), Is.Null);
    }
}