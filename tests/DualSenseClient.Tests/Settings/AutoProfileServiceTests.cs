using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

public class AutoProfileServiceTests
{
    private string _tempDir = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"AutoProfileServiceTests_{Guid.NewGuid():N}");
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

    private string RulesPath
    {
        get
        {
            return Path.Combine(_tempDir, "Config", "auto_profiles.json");
        }
    }

    private AutoProfileService CreateService() => new AutoProfileService(autoProfilesPath: RulesPath);

    [Test]
    public void Load_MissingFile_FallsBackToDefaults()
    {
        AutoProfileService service = CreateService();

        Assert.Multiple(() =>
        {
            Assert.That(service.Settings.Enabled, Is.True);
            Assert.That(service.Settings.Rules, Is.Empty);
        });
    }

    [Test]
    public void Save_RoundTripsRules()
    {
        AutoProfileService service = CreateService();
        service.AddRule(new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            WindowTitle = "Menu",
            ProfileName = "Game",
            EmulationMode = EmulationMode.Xbox360
        });

        AutoProfileService reloaded = CreateService();

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Settings.Rules, Has.Count.EqualTo(1));
            Assert.That(reloaded.Settings.Rules[0].ExePattern, Is.EqualTo("game\\.exe"));
            Assert.That(reloaded.Settings.Rules[0].ProfileName, Is.EqualTo("Game"));
            Assert.That(reloaded.Settings.Rules[0].EmulationMode, Is.EqualTo(EmulationMode.Xbox360));
        });
    }

    [Test]
    public void FindMatch_ReturnsFirstControllerSpecificMatch()
    {
        AutoProfileService service = CreateService();
        AutoProfileRule global = new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            ProfileName = "Global"
        };
        AutoProfileRule specific = new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            ControllerMac = "AA:BB:CC:DD:EE:FF",
            ProfileName = "Specific"
        };
        service.AddRule(global);
        service.AddRule(specific);

        Assert.Multiple(() =>
        {
            Assert.That(service.FindMatch(@"C:\game.exe", "Title", "AA:BB:CC:DD:EE:FF", "path")?.ProfileName, Is.EqualTo("Global"));
            Assert.That(service.FindMatch(@"C:\other.exe", "Title", "AA:BB:CC:DD:EE:FF", "path"), Is.Null);
        });
    }

    [Test]
    public void FindMatch_Disabled_ReturnsNull()
    {
        AutoProfileService service = CreateService();
        service.AddRule(new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            ProfileName = "Game"
        });
        service.Settings.Enabled = false;

        Assert.That(service.FindMatch(@"C:\game.exe", "Title", null, null), Is.Null);
    }

    [Test]
    public void FindMatch_SkipsActionlessRules()
    {
        AutoProfileService service = CreateService();
        service.AddRule(new AutoProfileRule
        {
            ExePattern = "game\\.exe"
        });
        service.AddRule(new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            ProfileName = "Game"
        });

        Assert.That(service.FindMatch(@"C:\game.exe", "Title", null, null)?.ProfileName, Is.EqualTo("Game"));
    }

    [Test]
    public void UpdateProfileName_RepointsRules()
    {
        AutoProfileService service = CreateService();
        service.AddRule(new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            ProfileName = "Game"
        });
        service.AddRule(new AutoProfileRule
        {
            ExePattern = "other\\.exe",
            ProfileName = "Other"
        });

        service.UpdateProfileName("Game", "Racing");

        Assert.Multiple(() =>
        {
            Assert.That(service.Settings.Rules[0].ProfileName, Is.EqualTo("Racing"));
            Assert.That(service.Settings.Rules[1].ProfileName, Is.EqualTo("Other"));
            Assert.That(service.FindMatch(@"C:\game.exe", "Title", null, null)?.ProfileName, Is.EqualTo("Racing"));
        });
    }

    [Test]
    public void RemoveProfileReferences_ClearsDeletedProfile()
    {
        AutoProfileService service = CreateService();
        service.AddRule(new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            ProfileName = "Game"
        });

        service.RemoveProfileReferences("Game");

        Assert.Multiple(() =>
        {
            Assert.That(service.Settings.Rules[0].ProfileName, Is.Empty);
            Assert.That(service.Settings.Rules[0].IsActionless, Is.True);
        });
    }

    [Test]
    public void Rule_ProfileOnly_LeavesEmulationUnchanged()
    {
        AutoProfileService service = CreateService();
        service.AddRule(new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            ProfileName = "Game"
        });

        AutoProfileRule? match = service.FindMatch(@"C:\game.exe", "Title", null, null);

        Assert.Multiple(() =>
        {
            Assert.That(match, Is.Not.Null);
            Assert.That(match!.ProfileName, Is.EqualTo("Game"));
            Assert.That(match.EmulationMode, Is.Null);
        });
    }

    [Test]
    public void Rule_EmulationOnly_LeavesProfileUnchanged()
    {
        AutoProfileService service = CreateService();
        service.AddRule(new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            EmulationMode = EmulationMode.Xbox360
        });

        AutoProfileService reloaded = CreateService();
        AutoProfileRule? match = reloaded.FindMatch(@"C:\game.exe", "Title", null, null);

        Assert.Multiple(() =>
        {
            Assert.That(match, Is.Not.Null);
            Assert.That(match!.ProfileName, Is.Empty);
            Assert.That(match.EmulationMode, Is.EqualTo(EmulationMode.Xbox360));
        });
    }
}