using System.Buffers.Binary;

namespace DualSenseClient.Emulation.Core;

/// <summary>
/// USB control transfer setup packet (USB 2.0 specification, section 9.3).
/// The wire representation is 8 bytes with all multi-byte fields little-endian.
/// </summary>
public readonly struct UsbSetupPacket
{
    /// <summary>Number of bytes of a setup packet on the wire.</summary>
    private const int WireSize = 8;

    /// <summary>Request characteristics: direction and type of the request (bmRequestType).</summary>
    public byte RequestType { get; init; }

    /// <summary>Request code (bRequest).</summary>
    public byte Request { get; init; }

    /// <summary>Request-specific value (wValue).</summary>
    public ushort Value { get; init; }

    /// <summary>Request-specific index (wIndex).</summary>
    public ushort Index { get; init; }

    /// <summary>Number of bytes to transfer in the data stage (wLength).</summary>
    public ushort Length { get; init; }

    /// <summary>
    /// Reads a setup packet from the first 8 bytes of <paramref name="source"/>.
    /// </summary>
    /// <param name="source">Buffer holding at least 8 bytes.</param>
    /// <returns>The parsed setup packet.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> is shorter than 8 bytes.</exception>
    public static UsbSetupPacket Read(ReadOnlySpan<byte> source)
    {
        if (source.Length < WireSize)
        {
            throw new ArgumentException($"A setup packet requires {WireSize} bytes; got {source.Length}.", nameof(source));
        }

        return new UsbSetupPacket
        {
            RequestType = source[0],
            Request = source[1],
            Value = BinaryPrimitives.ReadUInt16LittleEndian(source[2..]),
            Index = BinaryPrimitives.ReadUInt16LittleEndian(source[4..]),
            Length = BinaryPrimitives.ReadUInt16LittleEndian(source[6..])
        };
    }

    /// <summary>
    /// Writes the setup packet to the first 8 bytes of <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">Buffer holding at least 8 bytes.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than 8 bytes.</exception>
    public void Write(Span<byte> destination)
    {
        if (destination.Length < WireSize)
        {
            throw new ArgumentException($"A setup packet requires {WireSize} bytes; got {destination.Length}.", nameof(destination));
        }

        destination[0] = RequestType;
        destination[1] = Request;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], Value);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], Index);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], Length);
    }
}