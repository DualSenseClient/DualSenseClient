using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.UsbIp;

namespace DualSenseClient.Tests.Emulation.UsbIp;

[TestFixture]
public sealed class IsoPacketDescriptorTests
{
    [Test]
    public void Read_GoldenDescriptor_ParsesFields()
    {
        byte[] bytes =
        [
            0x00, 0x00, 0x00, 0x00, // offset = 0
            0x00, 0x00, 0x00, 0xC0, // length = 192
            0x00, 0x00, 0x00, 0xBC, // actual_length = 188
            0x00, 0x00, 0x00, 0x00 // status = 0
        ];

        IsoPacketDescriptor descriptor = IsoPacketDescriptor.Read(bytes);

        Assert.That(descriptor.Offset, Is.Zero);
        Assert.That(descriptor.Length, Is.EqualTo(192u));
        Assert.That(descriptor.ActualLength, Is.EqualTo(188u));
        Assert.That(descriptor.Status, Is.Zero);
    }

    [Test]
    public void Write_DescriptorFields_ProducesGoldenBytes()
    {
        IsoPacketDescriptor descriptor = new IsoPacketDescriptor
        {
            Offset = 0,
            Length = 192,
            ActualLength = 188,
            Status = 0
        };
        byte[] written = new byte[IsoPacketDescriptor.WireSize];

        descriptor.Write(written);

        byte[] expected =
        [
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0xC0,
            0x00, 0x00, 0x00, 0xBC,
            0x00, 0x00, 0x00, 0x00
        ];
        Assert.That(written, Is.EqualTo(expected));
    }

    [Test]
    public void Read_ShortSpan_ThrowsArgumentException()
    {
        byte[] bytes = new byte[IsoPacketDescriptor.WireSize - 1];

        Assert.That(() => IsoPacketDescriptor.Read(bytes), Throws.ArgumentException);
    }

    [Test]
    public void Write_ShortSpan_ThrowsArgumentException()
    {
        IsoPacketDescriptor descriptor = new IsoPacketDescriptor
        {
            Offset = 0,
            Length = 192,
            ActualLength = 188,
            Status = 0
        };
        byte[] destination = new byte[IsoPacketDescriptor.WireSize - 1];

        Assert.That(() => descriptor.Write(destination), Throws.ArgumentException);
    }

    [Test]
    public void UsbTransfer_IsoFieldsDefaultToZero()
    {
        UsbTransfer transfer = new UsbTransfer();

        Assert.That(transfer.StartFrame, Is.Zero);
        Assert.That(transfer.NumberOfPackets, Is.Zero);
        Assert.That(transfer.Interval, Is.Zero);
        Assert.That(transfer.ErrorCount, Is.Zero);
        Assert.That(transfer.IsoPackets, Is.Null);
    }
}