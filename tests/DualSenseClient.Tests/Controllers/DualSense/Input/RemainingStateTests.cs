using DualSenseClient.Controllers.DualSense.Input;

namespace DualSenseClient.Tests.Controllers.DualSense.Input;

public class RemainingStateTests
{
    [Test]
    public void AdaptiveTriggerStatus_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.AdaptiveTriggers.R2Status, Is.EqualTo(1));
        Assert.That(report.AdaptiveTriggers.L2Status, Is.EqualTo(2));
        Assert.That(report.AdaptiveTriggers.HostTimestamp, Is.EqualTo(99999));
        Assert.That(report.AdaptiveTriggers.Status2, Is.EqualTo(3));
    }

    [Test]
    public void DeviceTimestamp_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.DeviceTimestamp.Value, Is.EqualTo(67890));
    }

    [Test]
    public void ConnectionStatus_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Connection.Headphone, Is.True);
        Assert.That(report.Connection.Mic, Is.True);
        Assert.That(report.Connection.MicMuted, Is.False);
        Assert.That(report.Connection.UsbData, Is.True);
        Assert.That(report.Connection.UsbPower, Is.False);
    }
}