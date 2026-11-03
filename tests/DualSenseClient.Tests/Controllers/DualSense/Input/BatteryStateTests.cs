using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Input;

namespace DualSenseClient.Tests.Controllers.DualSense.Input;

public class BatteryStateTests
{
    [Test]
    public void BatteryState_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Battery.Raw, Is.EqualTo(0x18));
        Assert.That(report.Battery.RawLevel, Is.EqualTo(8));
        Assert.That(report.Battery.PowerState, Is.EqualTo(BatteryPowerState.Charging));
    }

    [Test]
    public void BatteryState_WhenLevelUnknown_PercentageIsNull()
    {
        byte[] buffer = new byte[64];
        buffer[0] = 0x01;
        buffer[53] = 0x0B;

        InputReport report = InputReportTestData.CreateReport(buffer);

        Assert.That(report.Battery.Percentage, Is.Null);
    }

    [Test]
    public void BatteryState_WhenChargingComplete_DisplayPercentageIs100()
    {
        byte[] buffer = new byte[64];
        buffer[0] = 0x01;
        buffer[53] = 0x20;

        InputReport report = InputReportTestData.CreateReport(buffer);

        Assert.That(report.Battery.DisplayPercentage, Is.EqualTo(100));
    }

    [Test]
    public void BatteryState_WhenCharging_ReportsBandMidpoint()
    {
        byte[] buffer = new byte[64];
        buffer[0] = 0x01;
        buffer[53] = 0x18; // level 8, charging

        InputReport report = InputReportTestData.CreateReport(buffer);

        // The protocol reports the midpoint of each 10% band for both charging and
        // discharging: 8 * 10 + 5 = 85%.
        Assert.That(report.Battery.Percentage, Is.EqualTo(85));
    }
}