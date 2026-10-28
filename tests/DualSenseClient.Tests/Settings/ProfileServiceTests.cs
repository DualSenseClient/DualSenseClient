using DualSenseClient.Controllers.DualSense.Lightbar;
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
        profile.OverrideVirtualLights = true;
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
            Assert.That(loaded.OverrideVirtualLights, Is.True);
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
        source.OverrideVirtualLights = true;
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
            Assert.That(copy.OverrideVirtualLights, Is.True);
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

    [Test]
    public void Load_LegacyLightbarJson_DefaultsToSingleColorAtFullBrightness()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilesPath)!);
        File.WriteAllText(ProfilesPath, """{"profiles":[{"name":"Legacy","lightbar":{"red":10,"green":20,"blue":30}}]}""");

        ProfileService service = CreateService();
        Profile? legacy = service.GetProfile("Legacy");

        Assert.Multiple(() =>
        {
            Assert.That(legacy, Is.Not.Null);
            Assert.That(legacy!.Lightbar.Mode, Is.EqualTo(LightbarMode.SingleColor));
            Assert.That(legacy.Lightbar.Brightness, Is.EqualTo(100));
            Assert.That(legacy.Lightbar.Colors, Has.Count.EqualTo(6));
            Assert.That(legacy.Lightbar.AudioLowColor.Blue, Is.EqualTo(255));
            Assert.That(legacy.Lightbar.AudioPlayerLeds, Is.False);
            Assert.That(legacy.Lightbar.BatteryChargingAnimation, Is.True);
            Assert.That(legacy.Lightbar.BatteryPlayerLeds, Is.False);
            Assert.That(legacy.Lightbar.BatteryPlayerLedSpeed, Is.EqualTo(50));
            Assert.That(legacy.Lightbar.BatteryLevelPlayerLeds, Is.False);
            Assert.That(legacy.OverrideVirtualLights, Is.False);
        });
    }

    [Test]
    public void Load_OverrideVirtualLights_UsesJsonKey()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilesPath)!);
        File.WriteAllText(
            ProfilesPath,
            """{"profiles":[{"name":"Override","override_virtual_lights":true}]}""");

        Profile? profile = CreateService().GetProfile("Override");

        Assert.That(profile?.OverrideVirtualLights, Is.True);
    }

    [Test]
    public void RoundTrip_PreservesLightbarEffects()
    {
        ProfileService service = CreateService();
        Profile profile = service.CreateProfile("Effects");
        profile.Lightbar.Mode = LightbarMode.Breathing;
        profile.Lightbar.Brightness = 42;
        profile.Lightbar.Speed = 77;
        profile.Lightbar.RandomColor = true;
        profile.Lightbar.BreathMin = 10;
        profile.Lightbar.BreathMax = 90;
        profile.Lightbar.AudioSensitivity = 33;
        profile.Lightbar.AudioPlayerLeds = true;
        profile.Lightbar.BatteryChargingAnimation = false;
        profile.Lightbar.BatteryPlayerLeds = true;
        profile.Lightbar.BatteryPlayerLedSpeed = 80;
        profile.Lightbar.BatteryLevelPlayerLeds = true;
        profile.Lightbar.Colors =
        [
            new LightbarColorSetting
            {
                Red = 1,
                Green = 2,
                Blue = 3
            },
            new LightbarColorSetting
            {
                Red = 4,
                Green = 5,
                Blue = 6
            }
        ];
        profile.Lightbar.AudioLowColor = new LightbarColorSetting
        {
            Red = 7,
            Green = 8,
            Blue = 9
        };
        profile.Lightbar.AudioMidColor = new LightbarColorSetting
        {
            Red = 10,
            Green = 11,
            Blue = 12
        };
        profile.Lightbar.AudioHighColor = new LightbarColorSetting
        {
            Red = 13,
            Green = 14,
            Blue = 15
        };
        service.Save();

        Profile? loaded = CreateService().GetProfile("Effects");

        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.Lightbar.Mode, Is.EqualTo(LightbarMode.Breathing));
            Assert.That(loaded.Lightbar.Brightness, Is.EqualTo(42));
            Assert.That(loaded.Lightbar.Speed, Is.EqualTo(77));
            Assert.That(loaded.Lightbar.RandomColor, Is.True);
            Assert.That(
                loaded.Lightbar.Colors.Select(c => (c.Red, c.Green, c.Blue)),
                Is.EqualTo(new[]
                {
                    ((byte)1, (byte)2, (byte)3), ((byte)4, (byte)5, (byte)6)
                }));
            Assert.That(loaded.Lightbar.BreathMin, Is.EqualTo(10));
            Assert.That(loaded.Lightbar.BreathMax, Is.EqualTo(90));
            Assert.That(loaded.Lightbar.AudioLowColor.Red, Is.EqualTo(7));
            Assert.That(loaded.Lightbar.AudioLowColor.Green, Is.EqualTo(8));
            Assert.That(loaded.Lightbar.AudioLowColor.Blue, Is.EqualTo(9));
            Assert.That(loaded.Lightbar.AudioMidColor.Red, Is.EqualTo(10));
            Assert.That(loaded.Lightbar.AudioMidColor.Green, Is.EqualTo(11));
            Assert.That(loaded.Lightbar.AudioMidColor.Blue, Is.EqualTo(12));
            Assert.That(loaded.Lightbar.AudioHighColor.Red, Is.EqualTo(13));
            Assert.That(loaded.Lightbar.AudioHighColor.Green, Is.EqualTo(14));
            Assert.That(loaded.Lightbar.AudioHighColor.Blue, Is.EqualTo(15));
            Assert.That(loaded.Lightbar.AudioSensitivity, Is.EqualTo(33));
            Assert.That(loaded.Lightbar.AudioPlayerLeds, Is.True);
            Assert.That(loaded.Lightbar.BatteryChargingAnimation, Is.False);
            Assert.That(loaded.Lightbar.BatteryPlayerLeds, Is.True);
            Assert.That(loaded.Lightbar.BatteryPlayerLedSpeed, Is.EqualTo(80));
            Assert.That(loaded.Lightbar.BatteryLevelPlayerLeds, Is.True);
        });
    }

    [Test]
    public void Load_NullPaletteEntry_DropsItAndEvaluatesAndClones()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilesPath)!);
        File.WriteAllText(
            ProfilesPath,
            """{"profiles":[{"name":"Null Palette","lightbar":{"mode":1,"colors":[{"red":10,"green":20,"blue":30},null]}}]}""");

        Profile? profile = CreateService().GetProfile("Null Palette");

        Assert.That(profile, Is.Not.Null);
        Assert.That(
            profile!.Lightbar.Colors.Select(c => (c.Red, c.Green, c.Blue)),
            Is.EqualTo(new[]
            {
                ((byte)10, (byte)20, (byte)30)
            }));

        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(
            new LightbarEffectContext
            {
                Settings = profile.Lightbar
            });
        Assert.Multiple(() =>
        {
            Assert.That(result.Frame.Red, Is.EqualTo(10));
            Assert.That(result.Frame.Green, Is.EqualTo(20));
            Assert.That(result.Frame.Blue, Is.EqualTo(30));
            Assert.That(profile.Lightbar.Clone().Colors, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void DuplicateProfile_DeepCopiesLightbarEffects()
    {
        ProfileService service = CreateService();
        Profile source = service.CreateProfile("Effects");
        source.Lightbar.Mode = LightbarMode.MultiColor;
        source.Lightbar.Brightness = 25;
        source.Lightbar.Colors =
        [
            new LightbarColorSetting
            {
                Red = 11,
                Green = 12,
                Blue = 13
            }
        ];
        source.Lightbar.AudioLowColor = new LightbarColorSetting
        {
            Red = 21,
            Green = 22,
            Blue = 23
        };
        service.Save();

        Profile? copy = service.DuplicateProfile("Effects");
        Assert.That(copy, Is.Not.Null);

        copy!.Lightbar.Colors[0].Red = 200;
        copy.Lightbar.AudioLowColor.Red = 201;

        Assert.Multiple(() =>
        {
            Assert.That(copy.Lightbar.Mode, Is.EqualTo(LightbarMode.MultiColor));
            Assert.That(copy.Lightbar.Brightness, Is.EqualTo(25));
            Assert.That(copy.Lightbar.Colors[0].Green, Is.EqualTo(12));
            Assert.That(copy.Lightbar.AudioLowColor.Green, Is.EqualTo(22));
            Assert.That(source.Lightbar.Colors[0].Red, Is.EqualTo(11));
            Assert.That(source.Lightbar.AudioLowColor.Red, Is.EqualTo(21));
        });
    }

    [Test]
    public void Load_NullLightbar_DefaultsColorsAndAudioColors()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilesPath)!);
        File.WriteAllText(ProfilesPath, """{"profiles":[{"name":"Null Lightbar","lightbar":null}]}""");

        Profile? profile = CreateService().GetProfile("Null Lightbar");

        Assert.That(profile, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(profile!.Lightbar.Colors, Has.Count.EqualTo(6));
            Assert.That(profile.Lightbar.AudioLowColor.Blue, Is.EqualTo(255));
            Assert.That(profile.Lightbar.AudioMidColor.Green, Is.EqualTo(180));
            Assert.That(profile.Lightbar.AudioHighColor.Red, Is.EqualTo(255));
            Assert.That(profile.Lightbar.Clone().Colors, Has.Count.EqualTo(6));
        });
    }

    [Test]
    public void Load_NullPaletteList_DefaultsColors()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilesPath)!);
        File.WriteAllText(
            ProfilesPath,
            """{"profiles":[{"name":"Null Colors","lightbar":{"mode":1,"colors":null}}]}""");

        Profile? profile = CreateService().GetProfile("Null Colors");

        Assert.That(profile, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(profile!.Lightbar.Colors, Has.Count.EqualTo(6));
            Assert.That(profile.Lightbar.Clone().Colors, Has.Count.EqualTo(6));
        });
    }

    [Test]
    public void Load_NullAudioColors_DefaultsThem()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilesPath)!);
        File.WriteAllText(
            ProfilesPath,
            """{"profiles":[{"name":"Null Audio","lightbar":{"audio_low_color":null,"audio_mid_color":null,"audio_high_color":null}}]}""");

        Profile? profile = CreateService().GetProfile("Null Audio");

        Assert.That(profile, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(profile!.Lightbar.AudioLowColor.Blue, Is.EqualTo(255));
            Assert.That(profile.Lightbar.AudioMidColor.Green, Is.EqualTo(180));
            Assert.That(profile.Lightbar.AudioHighColor.Red, Is.EqualTo(255));
            Assert.That(profile.Lightbar.Clone().AudioLowColor.Green, Is.EqualTo(80));
        });
    }
}