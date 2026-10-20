using System.Buffers.Binary;

namespace DualSenseClient.Emulation.UsbIp;

/// <summary>
/// Isochronous packet descriptor (usbip_iso_packet_descriptor) carried after the URB header
/// of an isochronous USB/IP submit or return-submit message. The wire representation is
/// 16 bytes with all multi-byte fields in network (big-endian) byte order and a signed
/// 32-bit status.
/// </summary>
public readonly struct IsoPacketDescriptor
{
    /// <summary>Number of bytes of an isochronous packet descriptor on the wire.</summary>
    public const int WireSize = 16;

    /// <summary>Offset of the packet data in the transfer buffer.</summary>
    public uint Offset { get; init; }

    /// <summary>Number of bytes requested for the packet.</summary>
    public uint Length { get; init; }

    /// <summary>Number of bytes actually transferred for the packet.</summary>
    public uint ActualLength { get; init; }

    /// <summary>Completion status of the packet: zero on success, a negative errno otherwise.</summary>
    public int Status { get; init; }

    /// <summary>
    /// Reads an isochronous packet descriptor from the first 16 bytes of <paramref name="source"/>.
    /// </summary>
    /// <param name="source">Buffer holding at least 16 bytes.</param>
    /// <returns>The parsed packet descriptor.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> is shorter than 16 bytes.</exception>
    public static IsoPacketDescriptor Read(ReadOnlySpan<byte> source)
    {
        if (source.Length < WireSize)
        {
            throw new ArgumentException($"An isochronous packet descriptor requires {WireSize} bytes; got {source.Length}.", nameof(source));
        }

        return new IsoPacketDescriptor
        {
            Offset = BinaryPrimitives.ReadUInt32BigEndian(source),
            Length = BinaryPrimitives.ReadUInt32BigEndian(source[4..]),
            ActualLength = BinaryPrimitives.ReadUInt32BigEndian(source[8..]),
            Status = BinaryPrimitives.ReadInt32BigEndian(source[12..])
        };
    }

    /// <summary>
    /// Writes the isochronous packet descriptor to the first 16 bytes of <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">Buffer holding at least 16 bytes.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than 16 bytes.</exception>
    public void Write(Span<byte> destination)
    {
        if (destination.Length < WireSize)
        {
            throw new ArgumentException($"An isochronous packet descriptor requires {WireSize} bytes; got {destination.Length}.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination, Offset);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], Length);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..], ActualLength);
        BinaryPrimitives.WriteInt32BigEndian(destination[12..], Status);
    }
}