using System.Buffers.Binary;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.UsbIp;

namespace DualSenseClient.Tests.Emulation.UsbIp;

[TestFixture]
public sealed class UsbIpHeaderTests
{
    [Test]
    public void ReadSubmit_KernelDocCmdIntrInCapture_ParsesAllFields()
    {
        // Verbatim capture from the USB/IP kernel protocol documentation (CmdIntrIN example).
        byte[] bytes =
        [
            0x00, 0x00, 0x00, 0x01, // command = SUBMIT
            0x00, 0x00, 0x0D, 0x05, // seqnum
            0x00, 0x01, 0x00, 0x0F, // devid
            0x00, 0x00, 0x00, 0x01, // direction = IN
            0x00, 0x00, 0x00, 0x01, // ep = 1
            0x00, 0x00, 0x02, 0x00, // transfer_flags
            0x00, 0x00, 0x00, 0x40, // transfer_buffer_length = 64
            0xFF, 0xFF, 0xFF, 0xFF, // start_frame (unreliable for non-ISO)
            0x00, 0x00, 0x00, 0x00, // number_of_packets (unreliable for non-ISO)
            0x00, 0x00, 0x00, 0x04, // interval
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 // setup
        ];

        UsbIpHeader header = UsbIpHeader.ReadSubmit(bytes);

        Assert.That(header.Command, Is.EqualTo(UsbIpProtocol.CommandSubmit));
        Assert.That(header.SeqNum, Is.EqualTo(0x0D05u));
        Assert.That(header.DevId, Is.EqualTo(0x0001000Fu));
        Assert.That(header.Direction, Is.EqualTo(UsbIpDirection.In));
        Assert.That(header.Ep, Is.EqualTo(1u));
        Assert.That(header.TransferFlags, Is.EqualTo(0x00000200u));
        Assert.That(header.TransferBufferLength, Is.EqualTo(64u));
        Assert.That(header.StartFrame, Is.EqualTo(uint.MaxValue));
        Assert.That(header.NumberOfPackets, Is.Zero);
        Assert.That(header.Interval, Is.EqualTo(4u));
        Assert.That(header.Setup, Is.EqualTo(default(UsbSetupPacket)));
    }

    [Test]
    public void ReadSubmit_ThenWriteSubmit_BulkOutHeader_RoundTripsByteExact()
    {
        byte[] bytes =
        [
            0x00, 0x00, 0x00, 0x01, // command = SUBMIT
            0x00, 0x00, 0x00, 0x07, // seqnum = 7
            0x00, 0x00, 0x00, 0x01, // devid = 1
            0x00, 0x00, 0x00, 0x00, // direction = OUT
            0x00, 0x00, 0x00, 0x02, // ep = 2
            0x00, 0x00, 0x00, 0x40, // transfer_flags = URB_ZERO_PACKET
            0x00, 0x00, 0x02, 0x00, // transfer_buffer_length = 512
            0xFF, 0xFF, 0xFF, 0xFF, // start_frame (raw value preserved)
            0xFF, 0xFF, 0xFF, 0xFF, // number_of_packets (raw value preserved)
            0x00, 0x00, 0x00, 0x00, // interval
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 // setup
        ];

        UsbIpHeader header = UsbIpHeader.ReadSubmit(bytes);
        byte[] written = new byte[UsbIpProtocol.HeaderSize];
        header.WriteSubmit(written);

        Assert.That(header.StartFrame, Is.EqualTo(uint.MaxValue));
        Assert.That(header.NumberOfPackets, Is.EqualTo(uint.MaxValue));
        Assert.That(written, Is.EqualTo(bytes));
    }

    [Test]
    public void ReadSubmit_ThenWriteSubmit_ControlHeaderWithSetup_RoundTripsByteExact()
    {
        byte[] bytes =
        [
            0x00, 0x00, 0x00, 0x01, // command = SUBMIT
            0x00, 0x00, 0x00, 0x09, // seqnum = 9
            0x00, 0x00, 0x00, 0x01, // devid = 1
            0x00, 0x00, 0x00, 0x01, // direction = IN
            0x00, 0x00, 0x00, 0x00, // ep = 0 (control)
            0x00, 0x00, 0x00, 0x00, // transfer_flags
            0x00, 0x00, 0x00, 0x12, // transfer_buffer_length = wLength = 18
            0x00, 0x00, 0x00, 0x00, // start_frame
            0x00, 0x00, 0x00, 0x00, // number_of_packets
            0x00, 0x00, 0x00, 0x00, // interval
            0x81, 0x06, 0x00, 0x01, 0x00, 0x00, 0x12, 0x00 // setup = GET_DESCRIPTOR(DEVICE), raw little-endian USB bytes
        ];

        UsbIpHeader header = UsbIpHeader.ReadSubmit(bytes);
        byte[] written = new byte[UsbIpProtocol.HeaderSize];
        header.WriteSubmit(written);

        Assert.That(written, Is.EqualTo(bytes));
        Assert.That(header.Setup.RequestType, Is.EqualTo((byte)0x81));
        Assert.That(header.Setup.Request, Is.EqualTo((byte)0x06));
        Assert.That(header.Setup.Value, Is.EqualTo((ushort)0x0100));
        Assert.That(header.Setup.Index, Is.Zero);
        Assert.That(header.Setup.Length, Is.EqualTo((ushort)18));
    }

    [Test]
    public void WriteRetSubmit_CompletedTransfer_WritesStatusAndActualLengthAtSpecOffsets()
    {
        UsbIpHeader header = UsbIpHeader.CreateRetSubmit(5, UsbIpStatus.Stall, 64);

        byte[] written = new byte[UsbIpProtocol.HeaderSize];
        header.WriteRetSubmit(written);

        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(written), Is.EqualTo(UsbIpProtocol.CommandRetSubmit));
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(written[4..]), Is.EqualTo(5u));
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(written[8..]), Is.Zero); // devid
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(written[12..]), Is.Zero); // direction
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(written[16..]), Is.Zero); // ep
        Assert.That(written[20..24], Is.EqualTo(new byte[]
        {
            0xFF, 0xFF, 0xFF, 0xE0
        })); // status = -32, big-endian
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(written[20..]), Is.EqualTo(UsbIpStatus.Stall));
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(written[24..]), Is.EqualTo(64u)); // actual_length
        Assert.That(written[28..], Is.All.Zero); // start_frame, number_of_packets, error_count, padding
    }

    [Test]
    public void CreateRetSubmitIso_RoundTripsStartFramePacketsErrorCount()
    {
        const uint seqNum = 11;
        const int status = UsbIpStatus.Success;
        const uint actualLength = 188;
        const uint startFrame = 0x00001234;
        const uint numberOfPackets = 2;
        const uint errorCount = 1;

        UsbIpHeader header = UsbIpHeader.CreateRetSubmit(seqNum, status, actualLength, startFrame, numberOfPackets, errorCount);

        byte[] written = new byte[UsbIpProtocol.HeaderSize];
        header.WriteRetSubmit(written);

        Assert.That(header.Command, Is.EqualTo(UsbIpProtocol.CommandRetSubmit));
        Assert.That(header.SeqNum, Is.EqualTo(seqNum));
        Assert.That(header.Status, Is.EqualTo(status));
        Assert.That(header.ActualLength, Is.EqualTo(actualLength));
        Assert.That(header.StartFrame, Is.EqualTo(startFrame));
        Assert.That(header.NumberOfPackets, Is.EqualTo(numberOfPackets));
        Assert.That(header.ErrorCount, Is.EqualTo(errorCount));
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(written[28..]), Is.EqualTo(startFrame)); // 0x1C
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(written[32..]), Is.EqualTo(numberOfPackets)); // 0x20
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(written[36..]), Is.EqualTo(errorCount)); // 0x24
        Assert.That(written[40..], Is.All.Zero); // 0x28: padding
    }

    [Test]
    public void WriteRetUnlink_AnsweringUnlinkRequest_CarriesRequestSeqNumAndStatus()
    {
        byte[] request =
        [
            0x00, 0x00, 0x00, 0x02, // command = UNLINK
            0x00, 0x00, 0x00, 0x06, // seqnum = 6
            0x00, 0x00, 0x00, 0x01, // devid = 1
            0x00, 0x00, 0x00, 0x00, // direction = OUT
            0x00, 0x00, 0x00, 0x00, // ep
            0x00, 0x00, 0x00, 0x05, // unlink_seqnum = 5 (the URB to unlink)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // padding
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // padding
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 // padding
        ];

        UsbIpHeader requestHeader = UsbIpHeader.ReadUnlink(request);
        UsbIpHeader reply = UsbIpHeader.CreateRetUnlink(requestHeader.SeqNum, UsbIpStatus.NoEntry);

        byte[] written = new byte[UsbIpProtocol.HeaderSize];
        reply.WriteRetUnlink(written);

        Assert.That(requestHeader.Command, Is.EqualTo((uint)UsbIpCommand.Unlink));
        Assert.That(requestHeader.UnlinkSeqNum, Is.EqualTo(5u));
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(written), Is.EqualTo(UsbIpProtocol.CommandRetUnlink));
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(written[4..]), Is.EqualTo(requestHeader.SeqNum));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(written[20..]), Is.EqualTo(UsbIpStatus.NoEntry)); // status = -2
        Assert.That(written[8..20], Is.All.Zero);
        Assert.That(written[24..], Is.All.Zero);
    }

    [Test]
    public void CreateSubmit_GetDescriptorControlRequest_AppliesProtocolDefaults()
    {
        UsbSetupPacket setup = new UsbSetupPacket
        {
            RequestType = 0x81,
            Request = 0x06,
            Value = 0x0100,
            Index = 0x0000,
            Length = 18
        };

        UsbIpHeader header = UsbIpHeader.CreateSubmit(
            7,
            1,
            UsbIpDirection.In,
            0,
            18,
            setup: setup);

        Assert.That(header.Command, Is.EqualTo(UsbIpProtocol.CommandSubmit));
        Assert.That(header.NumberOfPackets, Is.EqualTo(uint.MaxValue)); // required by the protocol for non-ISO transfers
        Assert.That(header.TransferFlags, Is.Zero);
        Assert.That(header.StartFrame, Is.Zero);
        Assert.That(header.Interval, Is.Zero);
        Assert.That(header.Setup, Is.EqualTo(setup));
    }

    [Test]
    public void UsbSetupPacket_ReadThenWrite_GetDescriptorRequest_RoundTripsByteExact()
    {
        byte[] bytes = [0x81, 0x06, 0x00, 0x01, 0x09, 0x00, 0x12, 0x00]; // GET_DESCRIPTOR, wValue 0x0100, wIndex 9, wLength 18

        UsbSetupPacket setup = UsbSetupPacket.Read(bytes);
        byte[] written = new byte[8]; // setup packets are 8 bytes on the wire
        setup.Write(written);

        Assert.That(setup.RequestType, Is.EqualTo((byte)0x81));
        Assert.That(setup.Request, Is.EqualTo((byte)0x06));
        Assert.That(setup.Value, Is.EqualTo((ushort)0x0100));
        Assert.That(setup.Index, Is.EqualTo((ushort)9));
        Assert.That(setup.Length, Is.EqualTo((ushort)18));
        Assert.That(written, Is.EqualTo(bytes));
    }

    [Test]
    public void ReadSubmit_BufferShorterThanHeaderSize_ThrowsArgumentException()
    {
        byte[] bytes = new byte[UsbIpProtocol.HeaderSize - 1];

        Assert.That(() => UsbIpHeader.ReadSubmit(bytes), Throws.ArgumentException);
    }

    [Test]
    public void WriteSubmit_DestinationShorterThanHeaderSize_ThrowsArgumentException()
    {
        UsbIpHeader header = UsbIpHeader.CreateSubmit(1, 1, UsbIpDirection.In, 1, 64);
        byte[] destination = new byte[UsbIpProtocol.HeaderSize - 1];

        Assert.That(() => header.WriteSubmit(destination), Throws.ArgumentException);
    }
}