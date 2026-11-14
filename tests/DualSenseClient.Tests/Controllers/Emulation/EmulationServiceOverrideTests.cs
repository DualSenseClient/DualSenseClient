using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.Emulation;

/// <summary>
/// Unit tests of the temporary-override copy behind
/// <see cref="EmulationService.SetTemporaryOverride"/>: an auto-profile override must carry the
/// stored microphone settings and the microphone passthrough switch, so applying a temporary
/// emulation mode never disables or reconfigures the virtual microphone.
/// </summary>
[TestFixture]
public sealed class EmulationServiceOverrideTests
{
    [Test]
    public void TemporaryOverride_PreservesMicrophoneSettings()
    {
        EmulationSettings stored = new EmulationSettings
        {
            Microphone = new MicrophoneSettings
            {
                Source = MicrophoneSource.Headphone,
                Level = 42
            },
            Forward = new ForwardSettings
            {
                MicrophoneEnabled = false
            }
        };

        EmulationSettings copy = EmulationService.CloneWithTemporaryOverride(
            stored, EmulationMode.DualSense, DualSenseVariant.Edge, DualShock4Variant.V2,
            EmulationAudioSource.SystemOutput, string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(copy.Microphone.Source, Is.EqualTo(MicrophoneSource.Headphone));
            Assert.That(copy.Microphone.Level, Is.EqualTo(42));
            Assert.That(copy.Forward.MicrophoneEnabled, Is.False);
        });
    }
}