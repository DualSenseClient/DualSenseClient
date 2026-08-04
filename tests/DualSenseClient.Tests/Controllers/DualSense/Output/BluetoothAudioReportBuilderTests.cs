using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.DualSense.Utilities;

namespace DualSenseClient.Tests.Controllers.DualSense.Output;

public class BluetoothAudioReportBuilderTests
{
    private static byte[] OpusFrame() => Enumerable.Repeat((byte)0xAB, BluetoothAudioReportBuilder.AudioPayloadSize).ToArray();

    private static byte[] HapticsPcm() => Enumerable.Repeat((byte)0x7F, BluetoothAudioReportBuilder.HapticsPayloadSize).ToArray();

    private static SetStateData SampleState()
    {
        return new SetStateData
        {
            RumbleRight = 0x11,
            RumbleLeft = 0x22,
            SpeakerVolume = 0x50,
            AudioControl = AudioControl.OutputPathSpeaker,
            LedRed = 0x10,
            LedGreen = 0x20,
            LedBlue = 0x30
        };
    }

    private static uint ReadCrc(byte[] report, int crcOffset)
        => (uint)(report[crcOffset]
                  | (report[crcOffset + 1] << 8)
                  | (report[crcOffset + 2] << 16)
                  | (report[crcOffset + 3] << 24));

    [Test]
    public void BuildInitPrime_ReportIdLengthAndHeader()
    {
        byte[] report = new BluetoothAudioReportBuilder().BuildInitPrime(SampleState());

        Assert.Multiple(() =>
        {
            Assert.That(report, Has.Length.EqualTo(142));
            Assert.That(report[0], Is.EqualTo(0x32));
            Assert.That(report[1], Is.EqualTo(0x10));
            Assert.That(report[2], Is.EqualTo(0x90));
            Assert.That(report[3], Is.EqualTo(0x3F));
        });
    }

    [Test]
    public void BuildInitPrime_EmbedsStateAtOffset4()
    {
        byte[] report = new BluetoothAudioReportBuilder().BuildInitPrime(SampleState());
        byte[] stateRaw = new byte[SetStateData.PayloadSize];
        SampleState().CopyTo(stateRaw, 0);

        Assert.That(report[4..(4 + SetStateData.PayloadSize)], Is.EqualTo(stateRaw));
    }

    [Test]
    public void BuildInitPrime_CrcCoversWholeReport()
    {
        byte[] report = new BluetoothAudioReportBuilder().BuildInitPrime(SampleState());

        uint expected = DualSenseCRC32.Compute(report, 0, 138);
        Assert.That(ReadCrc(report, 138), Is.EqualTo(expected));
    }

    [Test]
    public void BuildHapticsReport_ReportIdSessionBlockAndPcm()
    {
        byte[] report = new BluetoothAudioReportBuilder().BuildHapticsReport(HapticsPcm());

        Assert.Multiple(() =>
        {
            Assert.That(report, Has.Length.EqualTo(142));
            Assert.That(report[0], Is.EqualTo(0x32));
            Assert.That(report[2], Is.EqualTo(0x91));
            Assert.That(report[3], Is.EqualTo(0x07));
            Assert.That(report[4], Is.EqualTo(0xFE));
            Assert.That(report[9], Is.EqualTo(0xFF));
            Assert.That(report[11], Is.EqualTo(0x92));
            Assert.That(report[12], Is.EqualTo(64));
            Assert.That(report[13..77], Is.EqualTo(HapticsPcm()));
        });
    }

    [Test]
    public void BuildHapticsReport_RemainingBytesZero()
    {
        byte[] report = new BluetoothAudioReportBuilder().BuildHapticsReport(HapticsPcm());

        Assert.That(report[77..138], Is.All.EqualTo(0));
    }

    [Test]
    public void BuildHapticsReport_CrcCoversWholeReport()
    {
        byte[] report = new BluetoothAudioReportBuilder().BuildHapticsReport(HapticsPcm());

        uint expected = DualSenseCRC32.Compute(report, 0, 138);
        Assert.That(ReadCrc(report, 138), Is.EqualTo(expected));
    }

    [Test]
    public void BuildAudioReport_ReportIdSessionBlockAndOpus()
    {
        byte[] report = new BluetoothAudioReportBuilder().BuildAudioReport(OpusFrame(), BluetoothAudioRoute.Speaker);

        Assert.Multiple(() =>
        {
            Assert.That(report, Has.Length.EqualTo(334));
            Assert.That(report[0], Is.EqualTo(0x35));
            Assert.That(report[2], Is.EqualTo(0x91));
            Assert.That(report[3], Is.EqualTo(0x07));
            Assert.That(report[4], Is.EqualTo(0xFE));
            Assert.That(report[9], Is.EqualTo(0xFF));
            Assert.That(report[11], Is.EqualTo((byte)BluetoothAudioRoute.Speaker));
            Assert.That(report[12], Is.EqualTo(200));
            Assert.That(report[13..213], Is.EqualTo(OpusFrame()));
        });
    }

    [Test]
    public void BuildAudioReport_HeadsetRouteUses0x96()
    {
        byte[] report = new BluetoothAudioReportBuilder().BuildAudioReport(OpusFrame(), BluetoothAudioRoute.Headset);

        Assert.That(report[11], Is.EqualTo(0x96));
    }

    [Test]
    public void BuildAudioReport_RemainingBytesZero()
    {
        byte[] report = new BluetoothAudioReportBuilder().BuildAudioReport(OpusFrame(), BluetoothAudioRoute.Speaker);

        Assert.That(report[213..330], Is.All.EqualTo(0));
    }

    [Test]
    public void BuildAudioReport_CrcCoversWholeReport()
    {
        byte[] report = new BluetoothAudioReportBuilder().BuildAudioReport(OpusFrame(), BluetoothAudioRoute.Speaker);

        uint expected = DualSenseCRC32.Compute(report, 0, 330);
        Assert.That(ReadCrc(report, 330), Is.EqualTo(expected));
    }

    [Test]
    public void BuildCombinedReport_ReportIdStateSessionAndPackets()
    {
        byte[] report = new BluetoothAudioReportBuilder().BuildCombinedReport(SampleState(), OpusFrame(), HapticsPcm(), BluetoothAudioRoute.Speaker);
        byte[] stateRaw = new byte[SetStateData.PayloadSize];
        SampleState().CopyTo(stateRaw, 0);

        Assert.Multiple(() =>
        {
            Assert.That(report, Has.Length.EqualTo(398));
            Assert.That(report[0], Is.EqualTo(0x36));
            Assert.That(report[2], Is.EqualTo(0x90));
            Assert.That(report[3], Is.EqualTo(0x3F));
            Assert.That(report[4..(4 + SetStateData.PayloadSize)], Is.EqualTo(stateRaw));
            Assert.That(report[67], Is.EqualTo(0x91));
            Assert.That(report[68], Is.EqualTo(0x07));
            Assert.That(report[69], Is.EqualTo(0xFE));
            Assert.That(report[70..75], Is.EqualTo(new byte[] { 0x40, 0x40, 0x40, 0x40, 0x40 }));
            Assert.That(report[76], Is.EqualTo((byte)BluetoothAudioRoute.Speaker));
            Assert.That(report[77], Is.EqualTo(200));
            Assert.That(report[78..278], Is.EqualTo(OpusFrame()));
            Assert.That(report[278], Is.EqualTo(0x92));
            Assert.That(report[279], Is.EqualTo(64));
            Assert.That(report[280..344], Is.EqualTo(HapticsPcm()));
        });
    }

    [Test]
    public void BuildCombinedReport_HeadsetRouteUses0x96()
    {
        byte[] report = new BluetoothAudioReportBuilder().BuildCombinedReport(SampleState(), OpusFrame(), HapticsPcm(), BluetoothAudioRoute.Headset);

        Assert.That(report[76], Is.EqualTo(0x96));
    }

    [Test]
    public void BuildCombinedReport_RemainingBytesZero()
    {
        byte[] report = new BluetoothAudioReportBuilder().BuildCombinedReport(SampleState(), OpusFrame(), HapticsPcm(), BluetoothAudioRoute.Speaker);

        Assert.That(report[344..394], Is.All.EqualTo(0));
    }

    [Test]
    public void BuildCombinedReport_CrcCoversWholeReport()
    {
        byte[] report = new BluetoothAudioReportBuilder().BuildCombinedReport(SampleState(), OpusFrame(), HapticsPcm(), BluetoothAudioRoute.Speaker);

        uint expected = DualSenseCRC32.Compute(report, 0, 394);
        Assert.That(ReadCrc(report, 394), Is.EqualTo(expected));
    }

    [Test]
    public void BuildCombinedReport_AdvancesCountersOncePerReport()
    {
        BluetoothAudioReportBuilder builder = new BluetoothAudioReportBuilder();

        // Snapshot each report: the builder reuses one buffer per report type, so
        // retained arrays would otherwise alias the subsequent build.
        byte[] a = builder.BuildCombinedReport(SampleState(), OpusFrame(), HapticsPcm(), BluetoothAudioRoute.Speaker).ToArray();
        byte[] b = builder.BuildCombinedReport(SampleState(), OpusFrame(), HapticsPcm(), BluetoothAudioRoute.Headset).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(a[1], Is.EqualTo(0x00));
            Assert.That(a[75], Is.EqualTo(0x00));
            Assert.That(b[1], Is.EqualTo(0x10));
            Assert.That(b[75], Is.EqualTo(0x01));
        });
    }

    [Test]
    public void BuildCombinedReport_RejectsWrongPayloadSize()
    {
        BluetoothAudioReportBuilder builder = new BluetoothAudioReportBuilder();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => builder.BuildCombinedReport(SampleState(), new byte[10], HapticsPcm(), BluetoothAudioRoute.Speaker));
            Assert.Throws<ArgumentException>(() => builder.BuildCombinedReport(SampleState(), OpusFrame(), new byte[10], BluetoothAudioRoute.Speaker));
        });
    }

    [Test]
    public void SequenceTag_AdvancesWithStride16()
    {
        BluetoothAudioReportBuilder builder = new BluetoothAudioReportBuilder();

        // Snapshot each report: the builder reuses one buffer per report type, so
        // retained arrays would otherwise alias the subsequent builds.
        byte[] a = builder.BuildHapticsReport(HapticsPcm()).ToArray();
        byte[] b = builder.BuildHapticsReport(HapticsPcm()).ToArray();
        byte[] c = builder.BuildHapticsReport(HapticsPcm()).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(a[1], Is.EqualTo(0x00));
            Assert.That(b[1], Is.EqualTo(0x10));
            Assert.That(c[1], Is.EqualTo(0x20));
        });
    }

    [Test]
    public void SequenceTag_WrapsAfterLowNibble()
    {
        BluetoothAudioReportBuilder builder = new BluetoothAudioReportBuilder();

        byte last = 0;
        for (int i = 0; i < 17; i++)
        {
            last = builder.BuildHapticsReport(HapticsPcm())[1];
        }

        Assert.That(last, Is.EqualTo(0x00));
    }

    [Test]
    public void PacketCounter_IncrementsAcrossReportTypes()
    {
        BluetoothAudioReportBuilder builder = new BluetoothAudioReportBuilder();

        byte[] a = builder.BuildHapticsReport(HapticsPcm());
        byte[] b = builder.BuildAudioReport(OpusFrame(), BluetoothAudioRoute.Speaker);

        Assert.Multiple(() =>
        {
            Assert.That(a[10], Is.EqualTo(0x00));
            Assert.That(b[10], Is.EqualTo(0x01));
        });
    }

    [Test]
    public void Reset_RestartsSequenceAndCounter()
    {
        BluetoothAudioReportBuilder builder = new BluetoothAudioReportBuilder();
        builder.BuildHapticsReport(HapticsPcm());
        builder.BuildHapticsReport(HapticsPcm());
        builder.Reset();

        byte[] report = builder.BuildHapticsReport(HapticsPcm());

        Assert.Multiple(() =>
        {
            Assert.That(report[1], Is.EqualTo(0x00));
            Assert.That(report[10], Is.EqualTo(0x00));
        });
    }

    [Test]
    public void BuildHapticsReport_RejectsWrongPayloadSize()
    {
        BluetoothAudioReportBuilder builder = new BluetoothAudioReportBuilder();

        Assert.Throws<ArgumentException>(() => builder.BuildHapticsReport(new byte[10]));
    }

    [Test]
    public void BuildAudioReport_RejectsWrongPayloadSize()
    {
        BluetoothAudioReportBuilder builder = new BluetoothAudioReportBuilder();

        Assert.Throws<ArgumentException>(() => builder.BuildAudioReport(new byte[10], BluetoothAudioRoute.Speaker));
    }
}