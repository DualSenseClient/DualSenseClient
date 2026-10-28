using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Settings;

public class LightbarSettingsTests
{
    [Test]
    public void Defaults_MatchDesign()
    {
        LightbarSettings settings = new LightbarSettings();

        Assert.That(settings.Red, Is.EqualTo(0));
        Assert.That(settings.Green, Is.EqualTo(0));
        Assert.That(settings.Blue, Is.EqualTo(255));
        Assert.That(settings.Mode, Is.EqualTo(LightbarMode.SingleColor));
        Assert.That(settings.Brightness, Is.EqualTo(100));
        Assert.That(settings.Speed, Is.EqualTo(50));
        Assert.That(settings.RandomColor, Is.False);
        Assert.That(settings.Colors, Has.Count.EqualTo(6));
        Assert.That(
            settings.Colors.Select(c => (c.Red, c.Green, c.Blue)),
            Is.EqualTo(new[]
            {
                ((byte)255, (byte)40, (byte)40), ((byte)255, (byte)140, (byte)0), ((byte)255, (byte)220, (byte)0), ((byte)40, (byte)220, (byte)80),
                ((byte)40, (byte)120, (byte)255), ((byte)180, (byte)60, (byte)255)
            }));
        Assert.That(settings.BreathMin, Is.EqualTo(0));
        Assert.That(settings.BreathMax, Is.EqualTo(100));
        Assert.That(settings.AudioLowColor.Red, Is.EqualTo(0));
        Assert.That(settings.AudioLowColor.Green, Is.EqualTo(80));
        Assert.That(settings.AudioLowColor.Blue, Is.EqualTo(255));
        Assert.That(settings.AudioMidColor.Red, Is.EqualTo(255));
        Assert.That(settings.AudioMidColor.Green, Is.EqualTo(180));
        Assert.That(settings.AudioMidColor.Blue, Is.EqualTo(0));
        Assert.That(settings.AudioHighColor.Red, Is.EqualTo(255));
        Assert.That(settings.AudioHighColor.Green, Is.EqualTo(40));
        Assert.That(settings.AudioHighColor.Blue, Is.EqualTo(40));
        Assert.That(settings.AudioSensitivity, Is.EqualTo(50));
        Assert.That(settings.BatteryPlayerLeds, Is.False);

        LightbarSettings other = new LightbarSettings();
        Assert.That(other.Colors, Is.Not.SameAs(settings.Colors));
    }

    [Test]
    public void Clone_DeepCopiesPaletteAndAudioColors()
    {
        LightbarSettings source = new LightbarSettings();

        LightbarSettings clone = source.Clone();
        clone.Colors[0].Red = 1;
        clone.AudioLowColor.Red = 2;
        clone.Mode = LightbarMode.Strobe;

        Assert.That(source.Colors[0].Red, Is.EqualTo(255));
        Assert.That(source.AudioLowColor.Red, Is.EqualTo(0));
        Assert.That(source.Mode, Is.EqualTo(LightbarMode.SingleColor));
        Assert.That(clone.Colors, Is.Not.SameAs(source.Colors));
    }
}