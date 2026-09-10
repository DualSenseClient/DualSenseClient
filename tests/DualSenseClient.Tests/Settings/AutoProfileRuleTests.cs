using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

public class AutoProfileRuleTests
{
    [Test]
    public void IsMatch_ExactRule_IsCaseInsensitive()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            ExePattern = @"SlayTheSpire2\.exe$",
            WindowTitle = "Slay the Spire 2"
        };

        Assert.That(rule.IsMatch(@"C:\Games\Slay the Spire 2\SlayTheSpire2.exe", "SLAY THE SPIRE 2"), Is.True);
    }

    [Test]
    public void IsMatch_ExeRegex_MatchExpectedBoundaries()
    {
        AutoProfileRule startsWith = new AutoProfileRule
        {
            ExePattern = @"^C:\\Games\\",
            WindowTitle = "^Ghost of"
        };
        AutoProfileRule contains = new AutoProfileRule
        {
            ExePattern = @"Tsushima\.exe",
            WindowTitle = "Director's Cut"
        };
        AutoProfileRule endsWith = new AutoProfileRule
        {
            ExePattern = @"Tsushima\.exe$",
            WindowTitle = "Gameplay$"
        };

        const string path = @"C:\Games\Ghost of Tsushima\Tsushima.exe";
        Assert.Multiple(() =>
        {
            Assert.That(startsWith.IsMatch(path, "Ghost of Tsushima - Gameplay"), Is.True);
            Assert.That(contains.IsMatch(path, "Ghost of Tsushima Director's Cut"), Is.True);
            Assert.That(endsWith.IsMatch(path, "Ghost of Tsushima - Gameplay"), Is.True);
        });
    }

    [Test]
    public void IsMatch_BlankRule_NeverMatches()
    {
        AutoProfileRule rule = new AutoProfileRule();

        Assert.That(rule.IsMatch(@"C:\Windows\explorer.exe", "Desktop"), Is.False);
    }

    [Test]
    public void IsMatch_ExeOnly_TitleIgnored()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            ExePattern = "game\\.exe"
        };

        Assert.Multiple(() =>
        {
            Assert.That(rule.IsMatch(@"C:\Games\game.exe", "Anything"), Is.True);
            Assert.That(rule.IsMatch(@"C:\Games\other.exe", "Anything"), Is.False);
        });
    }

    [Test]
    public void IsMatch_BothSet_RequiresBoth()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            WindowTitle = "Menu"
        };

        Assert.Multiple(() =>
        {
            Assert.That(rule.IsMatch(@"C:\Games\game.exe", "Menu"), Is.True);
            Assert.That(rule.IsMatch(@"C:\Games\game.exe", "Gameplay"), Is.False);
            Assert.That(rule.IsMatch(@"C:\Games\other.exe", "Menu"), Is.False);
        });
    }

    [Test]
    public void MatchesController_NoSelector_MatchesEveryController()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            ExePattern = "game\\.exe"
        };

        Assert.Multiple(() =>
        {
            Assert.That(rule.AppliesToAllControllers, Is.True);
            Assert.That(rule.MatchesController("AA:BB:CC:DD:EE:FF", @"\\?\hid#1"), Is.True);
            Assert.That(rule.MatchesController(null, null), Is.True);
        });
    }

    [Test]
    public void MatchesController_MacSelector_MatchesCaseInsensitively()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            ControllerMac = "aa:bb:cc:dd:ee:ff"
        };

        Assert.Multiple(() =>
        {
            Assert.That(rule.AppliesToAllControllers, Is.False);
            Assert.That(rule.MatchesController("AA:BB:CC:DD:EE:FF", @"\\?\hid#1"), Is.True);
            Assert.That(rule.MatchesController("11:22:33:44:55:66", @"\\?\hid#1"), Is.False);
        });
    }

    [Test]
    public void MatchesController_PathSelector_FallsBackWhenMacDiffers()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            ControllerPath = @"\\?\hid#1"
        };

        Assert.Multiple(() =>
        {
            Assert.That(rule.MatchesController("AA:BB:CC:DD:EE:FF", @"\\?\hid#1"), Is.True);
            Assert.That(rule.MatchesController("AA:BB:CC:DD:EE:FF", @"\\?\hid#2"), Is.False);
        });
    }

    [Test]
    public void DisplayName_PrefersNameOverProgramPath()
    {
        AutoProfileRule unnamed = new AutoProfileRule
        {
            ExePattern = @"C:\game.exe"
        };
        AutoProfileRule named = new AutoProfileRule
        {
            Name = "Racing",
            ExePattern = @"C:\game.exe"
        };
        AutoProfileRule titleOnly = new AutoProfileRule
        {
            WindowTitle = "Menu"
        };

        Assert.Multiple(() =>
        {
            Assert.That(unnamed.DisplayName, Is.EqualTo(@"C:\game.exe"));
            Assert.That(named.DisplayName, Is.EqualTo("Racing"));
            Assert.That(titleOnly.DisplayName, Is.EqualTo("Menu"));
        });
    }

    [Test]
    public void Details_ShowsFullProgramPath()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            Name = "Racing",
            ExePattern = @"C:\Games\game.exe",
            WindowTitle = "Menu"
        };

        Assert.That(rule.Details, Is.EqualTo(@"C:\Games\game.exe" + "\n" + "Menu"));
    }

    [Test]
    public void IsMatch_TitleRegex_SupportsAlternation()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            WindowTitle = "Main Menu|Pause Menu"
        };

        Assert.Multiple(() =>
        {
            Assert.That(rule.IsMatch(@"C:\game.exe", "Main Menu - Slot 1"), Is.True);
            Assert.That(rule.IsMatch(@"C:\game.exe", "pause menu"), Is.True);
            Assert.That(rule.IsMatch(@"C:\game.exe", "Settings"), Is.False);
        });
    }

    [Test]
    public void IsMatch_TitleRegex_SupportsAnchorsAndClasses()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            WindowTitle = @"^Level \d+$"
        };

        Assert.Multiple(() =>
        {
            Assert.That(rule.IsMatch(@"C:\game.exe", "Level 12"), Is.True);
            Assert.That(rule.IsMatch(@"C:\game.exe", "Level 12 - Bonus"), Is.False);
            Assert.That(rule.IsMatch(@"C:\game.exe", "My Level 12"), Is.False);
        });
    }

    [Test]
    public void IsMatch_TitleRegex_InvalidPattern_FallsBackToLiteral()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            WindowTitle = "Menu (Main"
        };

        Assert.Multiple(() =>
        {
            Assert.That(rule.IsMatch(@"C:\game.exe", "Menu (Main)"), Is.True);
            Assert.That(rule.IsMatch(@"C:\game.exe", "Settings"), Is.False);
        });
    }

    [Test]
    public void IsMatch_ExeRegex_InvalidPattern_FallsBackToLiteral()
    {
        // A raw picked path is not a valid expression (\g is not a valid escape),
        // so it matches literally instead of breaking the rule.
        AutoProfileRule rule = new AutoProfileRule
        {
            ExePattern = @"C:\Games\game.exe"
        };

        Assert.Multiple(() =>
        {
            Assert.That(rule.IsMatch(@"C:\Games\game.exe", "Anything"), Is.True);
            Assert.That(rule.IsMatch(@"C:\Games\other.exe", "Anything"), Is.False);
        });
    }
}