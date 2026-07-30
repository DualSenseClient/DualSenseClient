using DualSenseClient.Controllers.DualSense.Input;

namespace DualSenseClient.Tests.Controllers.DualSense.Input;

public class TouchpadStateTests
{
    [Test]
    public void TouchpadState_Touch1_WhenActive_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Touchpad.Touch1.TrackingId, Is.EqualTo(5));
        Assert.That(report.Touchpad.Touch1.IsActive, Is.True);
        Assert.That(report.Touchpad.Touch1.X, Is.EqualTo(100));
        Assert.That(report.Touchpad.Touch1.Y, Is.EqualTo(200));
    }

    [Test]
    public void TouchpadState_Touch2_WhenInactive_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Touchpad.Touch2.TrackingId, Is.EqualTo(10));
        Assert.That(report.Touchpad.Touch2.IsActive, Is.False);
        Assert.That(report.Touchpad.Touch2.X, Is.EqualTo(0));
        Assert.That(report.Touchpad.Touch2.Y, Is.EqualTo(0));
    }
}