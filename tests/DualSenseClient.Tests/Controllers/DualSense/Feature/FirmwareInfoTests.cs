using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Feature;

namespace DualSenseClient.Tests.Controllers.DualSense.Feature;

public class FirmwareInfoTests
{
    [Test]
    public void FirmwareInfo_ParsesHardwareInfo()
    {
        FirmwareInfo info = new FirmwareInfo(CreateReport(0x01000208));

        Assert.That(info.HardwareInfo, Is.EqualTo(0x01000208));
        Assert.That(info.ModelRevision, Is.EqualTo(0x0208));
        Assert.That(info.HardwareGeneration, Is.EqualTo(DualSenseHardwareGeneration.Generation2));
        Assert.That(info.HasFullPlayerLedSupport, Is.True);
    }

    [Test]
    public void FirmwareInfo_Generation3_HasFullPlayerLedSupport()
    {
        FirmwareInfo info = new FirmwareInfo(CreateReport(0x01000308));

        Assert.That(info.HardwareGeneration, Is.EqualTo(DualSenseHardwareGeneration.Generation3));
        Assert.That(info.HasFullPlayerLedSupport, Is.True);
    }

    [Test]
    public void FirmwareInfo_Generation4_IsRestrictedToMirroredOnly()
    {
        FirmwareInfo info = new FirmwareInfo(CreateReport(0x01000408));

        Assert.That(info.HardwareGeneration, Is.EqualTo(DualSenseHardwareGeneration.Generation4));
        Assert.That(info.HasFullPlayerLedSupport, Is.False);
    }

    [Test]
    public void FirmwareInfo_UnknownGeneration_HasNoPlayerLedSupport()
    {
        FirmwareInfo info = new FirmwareInfo(new byte[16]);

        Assert.That(info.IsValid, Is.False);
        Assert.That(info.HardwareInfo, Is.EqualTo(0));
        Assert.That(info.HardwareGeneration, Is.EqualTo(DualSenseHardwareGeneration.Unknown));
        Assert.That(info.HasFullPlayerLedSupport, Is.False);
    }

    private static byte[] CreateReport(uint hwInfo)
    {
        byte[] raw = new byte[64];
        raw[0] = 0x20;
        raw[24] = (byte)hwInfo;
        raw[25] = (byte)(hwInfo >> 8);
        raw[26] = (byte)(hwInfo >> 16);
        raw[27] = (byte)(hwInfo >> 24);
        return raw;
    }
}