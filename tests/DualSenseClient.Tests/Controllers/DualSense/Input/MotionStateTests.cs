using DualSenseClient.Controllers.DualSense.Input;

namespace DualSenseClient.Tests.Controllers.DualSense.Input;

public class MotionStateTests
{
    [Test]
    public void MotionState_Gyroscope_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Motion.GyroX, Is.EqualTo(1000));
        Assert.That(report.Motion.GyroY, Is.EqualTo(-1000));
        Assert.That(report.Motion.GyroZ, Is.EqualTo(0));
    }

    [Test]
    public void MotionState_Accelerometer_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Motion.AccelX, Is.EqualTo(8192));
        Assert.That(report.Motion.AccelY, Is.EqualTo(0));
        Assert.That(report.Motion.AccelZ, Is.EqualTo(-8192));
    }

    [Test]
    public void MotionState_Timestamp_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Motion.Timestamp, Is.EqualTo(12345));
    }

    [Test]
    public void MotionState_Temperature_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Motion.Temperature, Is.EqualTo(35));
    }
}