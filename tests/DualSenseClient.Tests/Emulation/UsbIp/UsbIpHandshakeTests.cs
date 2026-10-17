using System.Buffers.Binary;
using System.Text;
using DualSenseClient.Emulation.UsbIp;

namespace DualSenseClient.Tests.Emulation.UsbIp;

[TestFixture]
public sealed class UsbIpHandshakeTests
{
    [Test]
    public void WriteDevListReply_OneDeviceTwoInterfaces_WritesSpecifiedLayout()
    {
        UsbIpDeviceInfo device = CreateDevice("1-1", CreateTwoInterfaces());
        UsbIpDeviceInfo[] devices = [device];
        byte[] wire = new byte[8 + 4 + UsbIpDeviceInfo.EntrySize + 8];

        int writtenLength = UsbIpHandshake.WriteDevListReply(wire, devices);

        Assert.That(writtenLength, Is.EqualTo(8 + 4 + 312 + 8));
        Assert.That(wire[0..2], Is.EqualTo(new byte[]
        {
            0x01, 0x11
        })); // version 0x0111, big-endian
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(wire), Is.EqualTo(UsbIpProtocol.Version));
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(wire[2..]), Is.EqualTo((ushort)0x0005)); // op OP_REP_DEVLIST, big-endian
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(wire[4..]), Is.Zero); // status OK
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(wire[8..]), Is.EqualTo(1u)); // number of exported devices
        Assert.That(wire[12..268], Is.All.Zero); // path: no host path, written as zero bytes
        Assert.That(wire[268..271], Is.EqualTo(Encoding.ASCII.GetBytes("1-1"))); // busid at 8 + 4 + 256
        Assert.That(wire[271..300], Is.All.Zero); // busid unused bytes zero-padded
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(wire[300..]), Is.EqualTo(1u)); // busnum
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(wire[304..]), Is.EqualTo(1u)); // devnum
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(wire[308..]), Is.EqualTo((uint)UsbIpDeviceSpeed.High)); // speed
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(wire[312..]), Is.EqualTo((ushort)0x054C)); // idVendor
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(wire[314..]), Is.EqualTo((ushort)0x0CE6)); // idProduct
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(wire[316..]), Is.EqualTo((ushort)0x0100)); // bcdDevice
        Assert.That(wire[318], Is.Zero); // bDeviceClass
        Assert.That(wire[319], Is.Zero); // bDeviceSubClass
        Assert.That(wire[320], Is.Zero); // bDeviceProtocol
        Assert.That(wire[321], Is.EqualTo((byte)1)); // bConfigurationValue
        Assert.That(wire[322], Is.EqualTo((byte)1)); // bNumConfigurations
        Assert.That(wire[323], Is.EqualTo((byte)2)); // bNumInterfaces
        Assert.That(wire[324..328], Is.EqualTo(new byte[]
        {
            3, 0, 0, 0
        })); // interface 0: class, subclass, protocol, pad
        Assert.That(wire[328..332], Is.EqualTo(new byte[]
        {
            1, 2, 1, 0
        })); // interface 1: class, subclass, protocol, pad
    }

    [Test]
    public void WriteDevListReply_TwoDevices_CountsDevicesAndPacksEntries()
    {
        UsbIpDeviceInfo[] devices =
        [
            CreateDevice("1-1"),
            CreateDevice("2-1", CreateTwoInterfaces())
        ];
        byte[] wire = new byte[8 + 4 + 312 + 312 + 8];

        int writtenLength = UsbIpHandshake.WriteDevListReply(wire, devices);

        Assert.That(writtenLength, Is.EqualTo(8 + 4 + 312 + 312 + 8));
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(wire[8..]), Is.EqualTo(2u)); // number of exported devices
        Assert.That(wire[268], Is.EqualTo((byte)'1')); // first entry busid at 12 + 256
        Assert.That(wire[323], Is.Zero); // first entry bNumInterfaces at 12 + 311
        Assert.That(wire[580..583], Is.EqualTo(Encoding.ASCII.GetBytes("2-1"))); // second entry busid at 324 + 256
        Assert.That(wire[583..612], Is.All.Zero);
        Assert.That(wire[635], Is.EqualTo((byte)2)); // second entry bNumInterfaces at 324 + 311
        Assert.That(wire[636..640], Is.EqualTo(new byte[]
        {
            3, 0, 0, 0
        })); // interface records follow the base entry
        Assert.That(wire[640..644], Is.EqualTo(new byte[]
        {
            1, 2, 1, 0
        }));
    }

    [Test]
    public void DevListEntrySize_InterfaceCounts_AddBaseEntryAndOneRecordPerInterface()
    {
        Assert.That(UsbIpDeviceInfo.DevListEntrySize(0), Is.EqualTo(312));
        Assert.That(UsbIpDeviceInfo.DevListEntrySize(1), Is.EqualTo(316));
        Assert.That(UsbIpDeviceInfo.DevListEntrySize(2), Is.EqualTo(320));
        Assert.That(UsbIpDeviceInfo.DevListEntrySize(4), Is.EqualTo(328));
    }

    [Test]
    public void ReadOpCommon_DeviceListRequestHeader_ParsesVersionOperationAndStatus()
    {
        byte[] header = [0x01, 0x11, 0x80, 0x05, 0x00, 0x00, 0x00, 0x00]; // version, OP_REQ_DEVLIST, status 0

        UsbIpHandshake.ReadOpCommon(header, out ushort version, out ushort operation, out int status);

        Assert.That(version, Is.EqualTo((ushort)0x0111));
        Assert.That(operation, Is.EqualTo((ushort)0x8005));
        Assert.That(status, Is.Zero);
    }

    [Test]
    public void WriteOpCommon_ImportFailureReplyHeader_WritesVersionOperationAndStatusBigEndian()
    {
        byte[] written = new byte[8];

        int writtenLength = UsbIpHandshake.WriteOpCommon(written, UsbIpProtocol.OpReplyImport, 1);

        Assert.That(writtenLength, Is.EqualTo(8));
        Assert.That(written, Is.EqualTo(new byte[]
        {
            0x01, 0x11, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01
        }));

        UsbIpHandshake.ReadOpCommon(written, out ushort version, out ushort operation, out int status);

        Assert.That(version, Is.EqualTo((ushort)0x0111));
        Assert.That(operation, Is.EqualTo((ushort)0x0003));
        Assert.That(status, Is.EqualTo(1));
    }

    [Test]
    public void ReadImportBusId_NullPaddedBusIdField_ParsesBusId()
    {
        byte[] request = new byte[UsbIpProtocol.OpCommonSize + UsbIpHandshake.BusIdSize];
        UsbIpHandshake.WriteOpCommon(request, UsbIpProtocol.OpRequestImport, 0);
        Encoding.ASCII.GetBytes("1-1").CopyTo(request, UsbIpProtocol.OpCommonSize);

        UsbIpHandshake.ReadImportBusId(request, out string busId);

        Assert.That(busId, Is.EqualTo("1-1"));
    }

    [Test]
    public void ReadImportBusId_FieldWithoutNullTerminator_ParsesWholeField()
    {
        string expected = new string('a', UsbIpHandshake.BusIdSize);
        byte[] request = new byte[UsbIpProtocol.OpCommonSize + UsbIpHandshake.BusIdSize];
        Encoding.ASCII.GetBytes(expected).CopyTo(request, UsbIpProtocol.OpCommonSize);

        UsbIpHandshake.ReadImportBusId(request, out string busId);

        Assert.That(busId, Is.EqualTo(expected));
    }

    [Test]
    public void WriteImportReply_SuccessfulImport_WritesBaseEntryWithoutInterfaceRecords()
    {
        UsbIpDeviceInfo device = CreateDevice("1-1", CreateTwoInterfaces());
        byte[] reply = new byte[8 + 312];

        int writtenLength = UsbIpHandshake.WriteImportReply(reply, UsbIpStatus.Success, device);

        Assert.That(writtenLength, Is.EqualTo(8 + 312));
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(reply), Is.EqualTo(UsbIpProtocol.Version));
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(reply[2..]), Is.EqualTo(UsbIpProtocol.OpReplyImport));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(reply[4..]), Is.Zero); // status OK
        Assert.That(reply[264..267], Is.EqualTo(Encoding.ASCII.GetBytes("1-1"))); // busid at entry offset 256
        Assert.That(reply[319], Is.EqualTo((byte)2)); // bNumInterfaces still reported; no records follow

        UsbIpDeviceInfo[] devices = [device];
        byte[] devListReply = new byte[8 + 4 + 312 + 8];
        int devListLength = UsbIpHandshake.WriteDevListReply(devListReply, devices);

        Assert.That(devListLength, Is.EqualTo(332));
        Assert.That(reply[8..320], Is.EqualTo(devListReply[12..324])); // the same 312-byte base entry shape
    }

    [Test]
    public void WriteImportReply_MissingBusIdStatusOne_WritesStatusAndNoEntry()
    {
        byte[] reply = new byte[8];

        int writtenLength = UsbIpHandshake.WriteImportReply(reply, 1, null);

        Assert.That(writtenLength, Is.EqualTo(8));
        Assert.That(reply, Is.EqualTo(new byte[]
        {
            0x01, 0x11, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01
        }));
    }

    [Test]
    public void ReadOpCommon_BufferShorterThanHeader_ThrowsArgumentException()
    {
        byte[] header = new byte[7];

        Assert.That(() => UsbIpHandshake.ReadOpCommon(header, out ushort _, out ushort _, out int _), Throws.ArgumentException);
    }

    [Test]
    public void ReadImportBusId_BufferShorterThanImportRequest_ThrowsArgumentException()
    {
        byte[] request = new byte[8 + UsbIpHandshake.BusIdSize - 1];

        Assert.That(() => UsbIpHandshake.ReadImportBusId(request, out string _), Throws.ArgumentException);
    }

    [Test]
    public void WriteDevListReply_DestinationShorterThanReply_ThrowsArgumentException()
    {
        UsbIpDeviceInfo[] devices = [CreateDevice("1-1", CreateTwoInterfaces())];
        byte[] destination = new byte[8 + 4 + 312 + 8 - 1];

        Assert.That(() => UsbIpHandshake.WriteDevListReply(destination, devices), Throws.ArgumentException);
    }

    [Test]
    public void WriteImportReply_SuccessWithoutDeviceEntry_ThrowsArgumentException()
    {
        byte[] destination = new byte[8 + 312];

        Assert.That(() => UsbIpHandshake.WriteImportReply(destination, UsbIpStatus.Success, null), Throws.ArgumentException);
    }

    [Test]
    public void WriteBaseEntry_DestinationShorterThanEntrySize_ThrowsArgumentException()
    {
        UsbIpDeviceInfo device = CreateDevice("1-1");
        byte[] destination = new byte[312 - 1];

        Assert.That(() => device.WriteBaseEntry(destination), Throws.ArgumentException);
    }

    [Test]
    public void WriteDevListEntry_DestinationShorterThanEntrySize_ThrowsArgumentException()
    {
        UsbIpDeviceInfo device = CreateDevice("1-1", CreateTwoInterfaces());
        byte[] destination = new byte[312 + 8 - 1];

        Assert.That(() => device.WriteDevListEntry(destination), Throws.ArgumentException);
    }

    [Test]
    public void WriteBaseEntry_BusIdWithoutNullTerminatorSpace_ThrowsArgumentException()
    {
        UsbIpDeviceInfo device = CreateDevice(new string('a', 32));
        byte[] destination = new byte[312];

        Assert.That(() => device.WriteBaseEntry(destination), Throws.ArgumentException);
    }

    [Test]
    public void WriteBaseEntry_MoreInterfacesThanCountFieldHolds_ThrowsArgumentException()
    {
        UsbIpInterfaceInfo[] interfaces = new UsbIpInterfaceInfo[byte.MaxValue + 1];
        UsbIpDeviceInfo device = CreateDevice("1-1", interfaces);
        byte[] destination = new byte[UsbIpDeviceInfo.EntrySize];

        Assert.That(() => device.WriteBaseEntry(destination), Throws.ArgumentException);
    }

    private static UsbIpDeviceInfo CreateDevice(string busId, params UsbIpInterfaceInfo[] interfaces)
    {
        return new UsbIpDeviceInfo
        {
            BusId = busId,
            BusNum = 1,
            DevNum = 1,
            Speed = UsbIpDeviceSpeed.High,
            IdVendor = 0x054C,
            IdProduct = 0x0CE6,
            BcdDevice = 0x0100,
            DeviceClass = 0,
            DeviceSubClass = 0,
            DeviceProtocol = 0,
            ConfigurationValue = 1,
            Interfaces = interfaces
        };
    }

    private static UsbIpInterfaceInfo[] CreateTwoInterfaces()
    {
        return new UsbIpInterfaceInfo[]
        {
            new UsbIpInterfaceInfo
            {
                Class = 3,
                SubClass = 0,
                Protocol = 0
            }, // HID
            new UsbIpInterfaceInfo
            {
                Class = 1,
                SubClass = 2,
                Protocol = 1
            } // audio control
        };
    }
}