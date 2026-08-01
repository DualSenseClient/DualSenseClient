using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.DualSense.Utilities;

namespace DualSenseClient.Tests.Controllers.DualSense.Output;

public class OutputReportTests
{
    private static SetStateData SamplePayload()
    {
        return new SetStateData
        {
            RumbleRight = 0x11,
            RumbleLeft = 0x22,
            PlayerLeds = PlayerLedMask.Led3,
            LedRed = 0x10,
            LedGreen = 0x20,
            LedBlue = 0x30
        };
    }

    [Test]
    public void ForUsb_ReportIdPayloadAndLength()
    {
        OutputReport report = OutputReport.ForUsb(SamplePayload());
        byte[] payloadRaw = new byte[SetStateData.PayloadSize];
        SamplePayload().CopyTo(payloadRaw, 0);

        Assert.Multiple(() =>
        {
            Assert.That(report.Length, Is.EqualTo(48));
            Assert.That(report.Raw[0], Is.EqualTo(0x02));
            Assert.That(report.Raw[1..], Is.EqualTo(payloadRaw));
            Assert.That(report.Raw[1 + 2], Is.EqualTo(0x11));
        });
    }

    [Test]
    public void ForBluetooth_ReportIdPayloadAndLength()
    {
        OutputReport report = OutputReport.ForBluetooth(SamplePayload(), 3);
        byte[] payloadRaw = new byte[SetStateData.PayloadSize];
        SamplePayload().CopyTo(payloadRaw, 0);

        Assert.Multiple(() =>
        {
            Assert.That(report.Length, Is.EqualTo(78));
            Assert.That(report.Raw[0], Is.EqualTo(0x31));
            Assert.That(report.Raw[2], Is.EqualTo(0x10));
            Assert.That(report.Raw[3..50], Is.EqualTo(payloadRaw));
            Assert.That(report.Raw[2], Is.EqualTo(0x10));
        });
    }

    [Test]
    public void ForBluetooth_SequenceTag_UsesHighNibble()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OutputReport.ForBluetooth(new SetStateData(), 0).Raw[1], Is.EqualTo(0x00));
            Assert.That(OutputReport.ForBluetooth(new SetStateData(), 1).Raw[1], Is.EqualTo(0x10));
            Assert.That(OutputReport.ForBluetooth(new SetStateData(), 5).Raw[1], Is.EqualTo(0x50));
            Assert.That(OutputReport.ForBluetooth(new SetStateData(), 15).Raw[1], Is.EqualTo(0xF0));
            Assert.That(OutputReport.ForBluetooth(new SetStateData(), 16).Raw[1], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void ForBluetooth_ReservedBytesAreZero()
    {
        OutputReport report = OutputReport.ForBluetooth(SamplePayload(), 0);

        Assert.That(report.Raw[50..74], Is.All.EqualTo(0));
    }

    [Test]
    public void ForBluetooth_CrcMatchesComputedSeed()
    {
        OutputReport report = OutputReport.ForBluetooth(SamplePayload(), 0);

        uint expected = DualSenseCRC32.Compute(report.Raw, 0, 74);
        uint actual = (uint)(report.Raw[74]
                             | (report.Raw[75] << 8)
                             | (report.Raw[76] << 16)
                             | (report.Raw[77] << 24));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(report.Raw[74], Is.EqualTo((byte)expected));
            Assert.That(report.Raw[77], Is.EqualTo((byte)(expected >> 24)));
        });
    }
}