using DualSenseClient.Core.Models;
using DualSenseClient.Logging;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

public class SettingsModelTests
{
    [Test]
    public void Settings_Defaults_DebugIsDebugSettings()
    {
        DualSenseClient.Settings.Settings settings = new DualSenseClient.Settings.Settings();
        Assert.That(settings.Debug, Is.Not.Null);
        Assert.That(settings.Debug, Is.InstanceOf<DebugSettings>());
    }

    [Test]
    public void Settings_Defaults_UiIsUiSettings()
    {
        DualSenseClient.Settings.Settings settings = new DualSenseClient.Settings.Settings();
        Assert.That(settings.Ui, Is.Not.Null);
        Assert.That(settings.Ui, Is.InstanceOf<UiSettings>());
    }

    [Test]
    public void DebugSettings_Default_LogLevelIsInfo()
    {
        DebugSettings debug = new DebugSettings();
        Assert.That(debug.LogLevel, Is.EqualTo(LogLevel.Info));
    }

    [Test]
    public void DebugSettings_CanSetLogLevel()
    {
        DebugSettings debug = new DebugSettings();
        debug.LogLevel = LogLevel.Trace;
        Assert.That(debug.LogLevel, Is.EqualTo(LogLevel.Trace));
    }

    [Test]
    public void UiSettings_Default_LanguageIsEnglish()
    {
        UiSettings ui = new UiSettings();
        Assert.That(ui.Language, Is.EqualTo("en"));
    }

    [Test]
    public void UiSettings_Default_ThemeIsSystem()
    {
        UiSettings ui = new UiSettings();
        Assert.That(ui.Theme, Is.EqualTo(Theme.System));
    }

    [Test]
    public void UiSettings_CanSetLanguage()
    {
        UiSettings ui = new UiSettings();
        ui.Language = "de";
        Assert.That(ui.Language, Is.EqualTo("de"));
    }

    [Test]
    public void UiSettings_CanSetTheme()
    {
        UiSettings ui = new UiSettings();
        ui.Theme = Theme.Dark;
        Assert.That(ui.Theme, Is.EqualTo(Theme.Dark));
    }

    [Test]
    public void UiSettings_Default_CloseToTrayIsTrue()
    {
        UiSettings ui = new UiSettings();
        Assert.That(ui.CloseToTray, Is.True);
    }

    [Test]
    public void UiSettings_Default_StartInTrayIsFalse()
    {
        UiSettings ui = new UiSettings();
        Assert.That(ui.StartInTray, Is.False);
    }

    [Test]
    public void UiSettings_CanSetCloseToTray()
    {
        UiSettings ui = new UiSettings();
        ui.CloseToTray = false;
        Assert.That(ui.CloseToTray, Is.False);
    }

    [Test]
    public void UiSettings_CanSetStartInTray()
    {
        UiSettings ui = new UiSettings();
        ui.StartInTray = true;
        Assert.That(ui.StartInTray, Is.True);
    }

    [Test]
    public void UiSettings_Default_ShowBatteryPercentageIsTrue()
    {
        UiSettings ui = new UiSettings();
        Assert.That(ui.ShowBatteryPercentage, Is.True);
    }

    [Test]
    public void UiSettings_CanSetShowBatteryPercentage()
    {
        UiSettings ui = new UiSettings();
        ui.ShowBatteryPercentage = false;
        Assert.That(ui.ShowBatteryPercentage, Is.False);
    }
}