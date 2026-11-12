using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Hid;

public class HidUsageFilterTests
{
    [Test]
    public void IsControllerUsage_AcceptsGenericDesktopGamepadAndJoystick()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HidDeviceEnumerator.IsControllerUsage(0x01, (ushort)HidUsageId.GamePad), Is.True);
            Assert.That(HidDeviceEnumerator.IsControllerUsage(0x01, (ushort)HidUsageId.Joystick), Is.True);
        });
    }

    [Test]
    public void IsControllerUsage_RejectsKeyboardConsumerAndReservedUsages()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HidDeviceEnumerator.IsControllerUsage(0x01, 0x06), Is.False, "keyboard");
            Assert.That(HidDeviceEnumerator.IsControllerUsage(0x0C, 0x01), Is.False, "consumer control");
            Assert.That(HidDeviceEnumerator.IsControllerUsage(0x01, 0x00), Is.False, "reserved");
            Assert.That(HidDeviceEnumerator.IsControllerUsage(0x00, (ushort)HidUsageId.GamePad), Is.False, "wrong page");
        });
    }
}