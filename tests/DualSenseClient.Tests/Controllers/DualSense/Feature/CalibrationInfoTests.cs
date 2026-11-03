using DualSenseClient.Controllers.DualSense.Feature;

namespace DualSenseClient.Tests.Controllers.DualSense.Feature;

public class CalibrationInfoTests
{
    [Test]
    public void CalibrationInfo_ParsesEverySignedWord()
    {
        CalibrationInfo info = new CalibrationInfo(CreateReport());

        Assert.Multiple(() =>
        {
            Assert.That(info.IsValid, Is.True);
            Assert.That(info.GyroPitchBias, Is.EqualTo(1));
            Assert.That(info.GyroYawBias, Is.EqualTo(2));
            Assert.That(info.GyroRollBias, Is.EqualTo(3));
            Assert.That(info.GyroPitchPlus, Is.EqualTo(16384));
            Assert.That(info.GyroPitchMinus, Is.EqualTo(-16384));
            Assert.That(info.GyroYawPlus, Is.EqualTo(16000));
            Assert.That(info.GyroYawMinus, Is.EqualTo(-16000));
            Assert.That(info.GyroRollPlus, Is.EqualTo(15000));
            Assert.That(info.GyroRollMinus, Is.EqualTo(-15000));
            Assert.That(info.GyroSpeedPlus, Is.EqualTo(500));
            Assert.That(info.GyroSpeedMinus, Is.EqualTo(500));
            Assert.That(info.AccelXPlus, Is.EqualTo(8192));
            Assert.That(info.AccelXMinus, Is.EqualTo(-8192));
            Assert.That(info.AccelYPlus, Is.EqualTo(8100));
            Assert.That(info.AccelYMinus, Is.EqualTo(-8100));
            Assert.That(info.AccelZPlus, Is.EqualTo(8000));
            Assert.That(info.AccelZMinus, Is.EqualTo(-8000));
        });
    }

    [Test]
    public void CalibrationInfo_ShortReport_IsInvalid()
    {
        CalibrationInfo info = new CalibrationInfo(new byte[10]);

        Assert.That(info.IsValid, Is.False);
    }

    [Test]
    public void CalibrationInfo_WrongReportId_IsInvalid()
    {
        byte[] report = CreateReport();
        report[0] = 0x20;

        CalibrationInfo info = new CalibrationInfo(report);

        Assert.That(info.IsValid, Is.False);
    }

    private static byte[] CreateReport()
    {
        short[] values =
        [
            1, 2, 3,
            16384, -16384,
            16000, -16000,
            15000, -15000,
            500, 500,
            8192, -8192,
            8100, -8100,
            8000, -8000
        ];

        byte[] report = new byte[41];
        report[0] = 0x05;
        for (int i = 0; i < values.Length; i++)
        {
            report[1 + i * 2] = (byte)values[i];
            report[2 + i * 2] = (byte)(values[i] >> 8);
        }

        return report;
    }
}