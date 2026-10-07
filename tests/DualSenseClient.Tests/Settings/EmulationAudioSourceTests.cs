using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

/// <summary>
/// Covers the forwarded-audio source model: the new source/application fields, the
/// legacy <c>audio_from_output</c> alias, and their JSON round-trips.
/// </summary>
[TestFixture]
public sealed class EmulationAudioSourceTests
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    [Test]
    public void ForwardSettings_DefaultSource_IsTheControllerEndpoint()
    {
        ForwardSettings settings = new ForwardSettings();

        Assert.Multiple(() =>
        {
            Assert.That(settings.AudioSource, Is.EqualTo(EmulationAudioSource.ControllerEndpoint));
            Assert.That(settings.AudioApp, Is.Empty);
            Assert.That(settings.AudioFromSystemOutput, Is.False);
        });
    }

    [Test]
    public void ForwardSettings_LegacyAlias_TrueSelectsTheSystemOutput()
    {
        ForwardSettings settings = new ForwardSettings
        {
            AudioFromSystemOutput = true
        };

        Assert.Multiple(() =>
        {
            Assert.That(settings.AudioSource, Is.EqualTo(EmulationAudioSource.SystemOutput));
            Assert.That(settings.AudioFromSystemOutput, Is.True);
        });
    }

    [Test]
    public void ForwardSettings_LegacyAlias_FalseNeverDowngradesTheApplicationSource()
    {
        // The alias is an upgrade-only compatibility shim: a stored file carrying both
        // fields must not let the legacy false value clobber the newer source.
        ForwardSettings settings = new ForwardSettings
        {
            AudioSource = EmulationAudioSource.Application,
            AudioApp = @"C:\Games\game.exe",
            AudioFromSystemOutput = false
        };

        Assert.Multiple(() =>
        {
            Assert.That(settings.AudioSource, Is.EqualTo(EmulationAudioSource.Application));
            Assert.That(settings.AudioApp, Is.EqualTo(@"C:\Games\game.exe"));
            Assert.That(settings.AudioFromSystemOutput, Is.False);
        });
    }

    [Test]
    public void ForwardSettings_LegacyJson_TrueSelectsTheSystemOutput()
    {
        ForwardSettings? settings = JsonSerializer.Deserialize<ForwardSettings>("{\"audio_from_output\":true}", Options);

        Assert.That(settings?.AudioSource, Is.EqualTo(EmulationAudioSource.SystemOutput));
    }

    [Test]
    public void ForwardSettings_LegacyJson_FalseSelectsTheControllerEndpoint()
    {
        ForwardSettings? settings = JsonSerializer.Deserialize<ForwardSettings>("{\"audio_from_output\":false}", Options);

        Assert.Multiple(() =>
        {
            Assert.That(settings?.AudioSource, Is.EqualTo(EmulationAudioSource.ControllerEndpoint));
            Assert.That(settings?.AudioFromSystemOutput, Is.False);
        });
    }

    [TestCase("{\"audio_from_output\":false,\"audio_source\":\"Application\"}")]
    [TestCase("{\"audio_source\":\"Application\",\"audio_from_output\":false}")]
    public void ForwardSettings_LegacyJson_DoesNotClobberTheNewerSource(string json)
    {
        ForwardSettings? settings = JsonSerializer.Deserialize<ForwardSettings>(json, Options);

        Assert.That(settings?.AudioSource, Is.EqualTo(EmulationAudioSource.Application));
    }

    [Test]
    public void ForwardSettings_RoundTripsTheApplicationSourceAndPath()
    {
        ForwardSettings settings = new ForwardSettings
        {
            AudioSource = EmulationAudioSource.Application,
            AudioApp = @"C:\Games\game.exe"
        };

        string json = JsonSerializer.Serialize(settings, Options);
        ForwardSettings? roundTripped = JsonSerializer.Deserialize<ForwardSettings>(json, Options);

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped?.AudioSource, Is.EqualTo(EmulationAudioSource.Application));
            Assert.That(roundTripped?.AudioApp, Is.EqualTo(@"C:\Games\game.exe"));
            Assert.That(roundTripped?.AudioFromSystemOutput, Is.False);
        });
    }

    [Test]
    public void AutoProfileRule_LegacyJson_TrueSelectsTheSystemOutput()
    {
        AutoProfileRule? rule = JsonSerializer.Deserialize<AutoProfileRule>(
            "{\"exe\":\"game\\\\.exe\",\"audio_from_output\":true}", Options);

        Assert.Multiple(() =>
        {
            Assert.That(rule?.AudioSource, Is.EqualTo(EmulationAudioSource.SystemOutput));
            Assert.That(rule?.AudioFromSystemOutput, Is.True);
        });
    }

    [Test]
    public void AutoProfileRule_LegacyJson_FalseSelectsTheControllerEndpoint()
    {
        AutoProfileRule? rule = JsonSerializer.Deserialize<AutoProfileRule>(
            "{\"exe\":\"game\\\\.exe\",\"audio_from_output\":false}", Options);

        Assert.Multiple(() =>
        {
            Assert.That(rule?.AudioSource, Is.EqualTo(EmulationAudioSource.ControllerEndpoint));
            Assert.That(rule?.AudioFromSystemOutput, Is.False);
        });
    }

    [TestCase("{\"audio_from_output\":false,\"audio_source\":\"Application\"}")]
    [TestCase("{\"audio_source\":\"Application\",\"audio_from_output\":false}")]
    public void AutoProfileRule_LegacyJson_DoesNotClobberTheNewerSource(string json)
    {
        AutoProfileRule? rule = JsonSerializer.Deserialize<AutoProfileRule>(json, Options);

        Assert.That(rule?.AudioSource, Is.EqualTo(EmulationAudioSource.Application));
    }

    [Test]
    public void AutoProfileRule_ApplicationSource_IsNotActionless()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            AudioSource = EmulationAudioSource.Application,
            AudioApp = @"C:\Games\game.exe"
        };

        Assert.Multiple(() =>
        {
            Assert.That(rule.IsActionless, Is.False);
            Assert.That(rule.AudioFromSystemOutput, Is.False, "the application source is not the system output");
        });
    }

    [Test]
    public void AutoProfileRule_NullSource_IsActionless()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            ExePattern = "game\\.exe"
        };

        Assert.Multiple(() =>
        {
            Assert.That(rule.AudioSource, Is.Null);
            Assert.That(rule.AudioFromSystemOutput, Is.Null);
            Assert.That(rule.IsActionless, Is.True);
        });
    }

    [Test]
    public void AutoProfileRule_RoundTripsTheApplicationSourceAndPath()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            AudioSource = EmulationAudioSource.Application,
            AudioApp = @"C:\Games\game.exe"
        };

        string json = JsonSerializer.Serialize(rule, Options);
        AutoProfileRule? roundTripped = JsonSerializer.Deserialize<AutoProfileRule>(json, Options);

        Assert.Multiple(() =>
        {
            Assert.That(roundTripped?.AudioSource, Is.EqualTo(EmulationAudioSource.Application));
            Assert.That(roundTripped?.AudioApp, Is.EqualTo(@"C:\Games\game.exe"));
        });
    }

    [Test]
    public void ForwardSettings_DefaultMode_IsPlayingApps()
    {
        ForwardSettings settings = new ForwardSettings();

        Assert.That(settings.AudioAppMode, Is.EqualTo(EmulationAudioAppMode.PlayingApps));
    }

    [Test]
    public void ForwardSettings_RoundTripsTheApplicationMode()
    {
        ForwardSettings settings = new ForwardSettings
        {
            AudioSource = EmulationAudioSource.Application,
            AudioApp = @"C:\Games\game.exe",
            AudioAppMode = EmulationAudioAppMode.ExecutableFile
        };

        string json = JsonSerializer.Serialize(settings, Options);
        ForwardSettings? roundTripped = JsonSerializer.Deserialize<ForwardSettings>(json, Options);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"audio_app_mode\":\"ExecutableFile\""));
            Assert.That(roundTripped?.AudioAppMode, Is.EqualTo(EmulationAudioAppMode.ExecutableFile));
        });
    }

    [Test]
    public void AutoProfileRule_DefaultMode_IsNullAndKeepsTheRuleActionless()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            ExePattern = "game\\.exe"
        };

        Assert.Multiple(() =>
        {
            Assert.That(rule.AudioAppMode, Is.Null);
            Assert.That(rule.IsActionless, Is.True);
        });
    }

    [Test]
    public void AutoProfileRule_ModeOnly_IsActionless()
    {
        // The mode is only a picker preference: a rule that sets nothing else must not
        // shadow a lower rule that does something.
        AutoProfileRule rule = new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            AudioAppMode = EmulationAudioAppMode.ExecutableFile
        };

        Assert.Multiple(() =>
        {
            Assert.That(rule.AudioAppMode, Is.EqualTo(EmulationAudioAppMode.ExecutableFile));
            Assert.That(rule.IsActionless, Is.True);
        });
    }

    [Test]
    public void AutoProfileRule_ApplicationSourceWithMode_IsNotActionless()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            AudioSource = EmulationAudioSource.Application,
            AudioApp = @"C:\Games\game.exe",
            AudioAppMode = EmulationAudioAppMode.ExecutableFile
        };

        Assert.That(rule.IsActionless, Is.False);
    }

    [Test]
    public void AutoProfileRule_RoundTripsTheApplicationMode()
    {
        AutoProfileRule rule = new AutoProfileRule
        {
            ExePattern = "game\\.exe",
            AudioSource = EmulationAudioSource.Application,
            AudioApp = @"C:\Games\game.exe",
            AudioAppMode = EmulationAudioAppMode.ExecutableFile
        };

        string json = JsonSerializer.Serialize(rule, Options);
        AutoProfileRule? roundTripped = JsonSerializer.Deserialize<AutoProfileRule>(json, Options);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"audio_app_mode\":\"ExecutableFile\""));
            Assert.That(roundTripped?.AudioAppMode, Is.EqualTo(EmulationAudioAppMode.ExecutableFile));
        });
    }
}