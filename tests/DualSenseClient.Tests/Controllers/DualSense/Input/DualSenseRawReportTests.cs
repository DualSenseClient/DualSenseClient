using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Controllers.Emulation.UsbIp;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense.Input;

public class DualSenseRawReportTests
{
    [Test]
    public void ShouldUseRawMapping_NullOrDefault_ReturnsTrue()
    {
        Assert.That(EmulationDualSenseController.ShouldUseRawMapping(null), Is.True);
        Assert.That(EmulationDualSenseController.ShouldUseRawMapping(VirtualInputMapper.DualSenseDefaultTable), Is.True);
    }

    [Test]
    public void ShouldUseRawMapping_CustomTable_ReturnsFalse()
    {
        ButtonMappingTable custom = VirtualInputMapper.DualSenseTable(
            [
                new DualSenseClient.Settings.Sections.ButtonMappingEntry
                {
                    Keys = ["Cross"],
                    Targets = ["Circle"]
                }
            ],
            null);

        Assert.That(EmulationDualSenseController.ShouldUseRawMapping(custom), Is.False);
    }

    [Test]
    public void UsbBuffer_ReturnsVerbatim64Bytes()
    {
        byte[] hid = new byte[64];
        hid[0] = 0x01;
        for (int i = 1; i < 64; i++)
        {
            hid[i] = (byte)i;
        }

        bool ok = DualSenseRawReport.TryBuildUsbRaw64(hid, 64, ConnectionType.Usb, out byte[]? usbRaw);

        Assert.That(ok, Is.True);
        Assert.That(usbRaw, Is.Not.Null);
        Assert.That(usbRaw!.Length, Is.EqualTo(64));
        Assert.That(usbRaw[0], Is.EqualTo(0x01));
        Assert.That(usbRaw[63], Is.EqualTo(63));
    }

    [Test]
    public void BluetoothBuffer_StripsToUsbRaw64()
    {
        // BT HID: [0x31, flags, 63B payload] -> USB: [0x01 + 63B payload]
        byte[] hid = new byte[78];
        hid[0] = 0x31;
        hid[1] = 0x10;
        for (int i = 0; i < 63; i++)
        {
            hid[2 + i] = (byte)(0xA0 + i);
        }

        bool ok = DualSenseRawReport.TryBuildUsbRaw64(hid, 78, ConnectionType.Bluetooth, out byte[]? usbRaw);

        Assert.That(ok, Is.True);
        Assert.That(usbRaw!.Length, Is.EqualTo(64));
        Assert.That(usbRaw[0], Is.EqualTo(0x01));
        Assert.That(usbRaw[1], Is.EqualTo(0xA0));
        Assert.That(usbRaw[63], Is.EqualTo((byte)(0xA0 + 62)));
    }

    [Test]
    public void WrongReportId_ReturnsFalse()
    {
        byte[] hid = new byte[64];
        hid[0] = 0x02;

        Assert.That(DualSenseRawReport.TryBuildUsbRaw64(hid, 64, ConnectionType.Usb, out _), Is.False);
    }

    [Test]
    public void ShortBuffer_ReturnsFalse()
    {
        byte[] hid = new byte[10];
        hid[0] = 0x01;

        Assert.That(DualSenseRawReport.TryBuildUsbRaw64(hid, 10, ConnectionType.Usb, out _), Is.False);
    }
}