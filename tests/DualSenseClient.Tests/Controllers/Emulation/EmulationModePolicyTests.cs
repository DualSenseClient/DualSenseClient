using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Hid;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.Emulation;

/// <summary>
/// Tests of the connection rules behind the emulation mode picker.
/// </summary>
[TestFixture]
public sealed class EmulationModePolicyTests
{
    [Test]
    public void GetAvailableModes_Usb_OmitsDualSense()
    {
        IReadOnlyList<EmulationMode> modes = EmulationModePolicy.GetAvailableModes(ConnectionType.Usb);

        Assert.That(modes, Is.EqualTo(new[]
        {
            EmulationMode.Off, EmulationMode.Xbox360, EmulationMode.DualShock4
        }));
    }

    [Test]
    public void GetAvailableModes_Bluetooth_IncludesDualSense()
    {
        IReadOnlyList<EmulationMode> modes = EmulationModePolicy.GetAvailableModes(ConnectionType.Bluetooth);

        Assert.That(modes, Is.EqualTo(new[]
        {
            EmulationMode.Off, EmulationMode.Xbox360, EmulationMode.DualShock4, EmulationMode.DualSense
        }));
    }
}