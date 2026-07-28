using DualSenseClient.Core.Models;
using DualSenseClient.Logging;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

public class SettingsModelTests
{
    [Test]
    public void Settings_Defaults_DebugIsDebugSettings()
    {
        DualSenseClient.Settings.Settings settings = new();
        Assert.That(settings.Debug, Is.Not.Null);
        Assert.That(settings.Debug, Is.InstanceOf<DebugSettings>());
    }

    [Test]
    public void Settings_Defaults_UiIsUiSettings()
    {
        DualSenseClient.Settings.Settings settings = new();
        Assert.That(settings.Ui, Is.Not.Null);
        Assert.That(settings.Ui, Is.InstanceOf<UiSettings>());
    }

    [Test]
    public void DebugSettings_Default_LogLevelIsInfo()
    {
        DebugSettings debug = new();
        Assert.That(debug.LogLevel, Is.EqualTo(LogLevel.Info));
    }

    [Test]
    public void DebugSettings_CanSetLogLevel()
    {
        DebugSettings debug = new();
        debug.LogLevel = LogLevel.Trace;
        Assert.That(debug.LogLevel, Is.EqualTo(LogLevel.Trace));
    }

    [Test]
    public void UiSettings_Default_LanguageIsEnglish()
    {
        UiSettings ui = new();
        Assert.That(ui.Language, Is.EqualTo("en"));
    }

    [Test]
    public void UiSettings_Default_ThemeIsSystem()
    {
        UiSettings ui = new();
        Assert.That(ui.Theme, Is.EqualTo(Theme.System));
    }

    [Test]
    public void UiSettings_CanSetLanguage()
    {
        UiSettings ui = new();
        ui.Language = "de";
        Assert.That(ui.Language, Is.EqualTo("de"));
    }

    [Test]
    public void UiSettings_CanSetTheme()
    {
        UiSettings ui = new();
        ui.Theme = Theme.Dark;
        Assert.That(ui.Theme, Is.EqualTo(Theme.Dark));
    }
}