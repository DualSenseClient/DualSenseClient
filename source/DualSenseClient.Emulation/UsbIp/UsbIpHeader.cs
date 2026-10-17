using System.Buffers.Binary;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Emulation.UsbIp;

/// <summary>
/// Fixed-size (48 byte) USB/IP URB header (usbip_header) shared by
/// USBIP_CMD_SUBMIT, USBIP_CMD_UNLINK, USBIP_RET_SUBMIT and USBIP_RET_UNLINK.
/// Every protocol field is network (big-endian) byte order and status values are
/// signed 32-bit; the trailing 8 setup bytes are raw little-endian USB data.
/// The offsets noted next to the field accesses refer to the tables of
/// https://www.kernel.org/doc/html/latest/usb/usbip_protocol.html
/// </summary>
public readonly struct UsbIpHeader
{
    /// <summary>Offset of the setup bytes (submit) or padding (return) at the end of the header.</summary>
    private const int SetupOffset = 40;

    /// <summary>Number of setup bytes (submit) or padding bytes (return) at the end of the header.</summary>
    private const int SetupSize = 8;

    /// <summary>Command code (0x00): one of the USBIP_CMD_* or USBIP_RET_* values.</summary>
    public uint Command { get; init; }

    /// <summary>Sequence number (0x04) identifying the URB on this connection.</summary>
    public uint SeqNum { get; init; }

    /// <summary>Device id (0x08): ((busnum &lt;&lt; 16) | devnum) for client requests, 0 for server replies.</summary>
    public uint DevId { get; init; }

    /// <summary>Transfer direction (0x0C): only used by the client, 0 in server replies.</summary>
    public UsbIpDirection Direction { get; init; }

    /// <summary>Endpoint number (0x10): only used by the client, 0 in server replies.</summary>
    public uint Ep { get; init; }

    /// <summary>Transfer flags (0x14) of a USBIP_CMD_SUBMIT; zero otherwise.</summary>
    public uint TransferFlags { get; init; }

    /// <summary>Number of bytes of the transfer buffer (0x18) of a USBIP_CMD_SUBMIT; zero otherwise.</summary>
    public uint TransferBufferLength { get; init; }

    /// <summary>Initial frame for isochronous transfers (0x1C); zero otherwise.</summary>
    public uint StartFrame { get; init; }

    /// <summary>Number of isochronous packets (0x20); the protocol uses 0xFFFFFFFF for non-ISO transfers.</summary>
    public uint NumberOfPackets { get; init; }

    /// <summary>Maximum time for the request on the server-side host controller (0x24); zero otherwise.</summary>
    public uint Interval { get; init; }

    /// <summary>Setup packet of a control transfer (0x28); zero for all other transfers.</summary>
    public UsbSetupPacket Setup { get; init; }

    /// <summary>Completion status (0x14) of a USBIP_RET_SUBMIT or USBIP_RET_UNLINK; zero otherwise.</summary>
    public int Status { get; init; }

    /// <summary>Number of bytes actually transferred (0x18) of a USBIP_RET_SUBMIT; zero otherwise.</summary>
    public uint ActualLength { get; init; }

    /// <summary>Error count (0x24) of a USBIP_RET_SUBMIT; zero otherwise.</summary>
    public uint ErrorCount { get; init; }

    /// <summary>Sequence number of the URB to unlink (0x14) of a USBIP_CMD_UNLINK; zero otherwise.</summary>
    public uint UnlinkSeqNum { get; init; }

    /// <summary>
    /// Reads a USBIP_CMD_SUBMIT header from the first 48 bytes of <paramref name="source"/>.
    /// </summary>
    /// <param name="source">Buffer holding at least 48 bytes.</param>
    /// <returns>The parsed header with all submit fields populated.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> is shorter than 48 bytes.</exception>
    public static UsbIpHeader ReadSubmit(ReadOnlySpan<byte> source)
    {
        EnsureHeaderSize(source.Length, nameof(source));
        (uint command, uint seqNum, uint devId, UsbIpDirection direction, uint ep) = ReadBasicHeader(source);

        return new UsbIpHeader
        {
            Command = command,
            SeqNum = seqNum,
            DevId = devId,
            Direction = direction,
            Ep = ep,
            TransferFlags = BinaryPrimitives.ReadUInt32BigEndian(source[20..]), // 0x14
            TransferBufferLength = BinaryPrimitives.ReadUInt32BigEndian(source[24..]), // 0x18
            StartFrame = BinaryPrimitives.ReadUInt32BigEndian(source[28..]), // 0x1C
            NumberOfPackets = BinaryPrimitives.ReadUInt32BigEndian(source[32..]), // 0x20
            Interval = BinaryPrimitives.ReadUInt32BigEndian(source[36..]), // 0x24
            Setup = UsbSetupPacket.Read(source[SetupOffset..]) // 0x28
        };
    }

    /// <summary>
    /// Reads a USBIP_CMD_UNLINK header from the first 48 bytes of <paramref name="source"/>.
    /// </summary>
    /// <param name="source">Buffer holding at least 48 bytes.</param>
    /// <returns>The parsed header with the unlink sequence number populated.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> is shorter than 48 bytes.</exception>
    public static UsbIpHeader ReadUnlink(ReadOnlySpan<byte> source)
    {
        EnsureHeaderSize(source.Length, nameof(source));
        (uint command, uint seqNum, uint devId, UsbIpDirection direction, uint ep) = ReadBasicHeader(source);

        return new UsbIpHeader
        {
            Command = command,
            SeqNum = seqNum,
            DevId = devId,
            Direction = direction,
            Ep = ep,
            UnlinkSeqNum = BinaryPrimitives.ReadUInt32BigEndian(source[20..]) // 0x14
        };
    }

    /// <summary>
    /// Creates a USBIP_CMD_SUBMIT header for a transfer submitted by the client.
    /// </summary>
    /// <param name="seqNum">Sequence number identifying the URB on the connection.</param>
    /// <param name="devId">Device id as sent by the client: ((busnum &lt;&lt; 16) | devnum).</param>
    /// <param name="direction">Transfer direction.</param>
    /// <param name="ep">Endpoint number.</param>
    /// <param name="transferBufferLength">Number of bytes of the transfer buffer.</param>
    /// <param name="transferFlags">Transfer flags; zero when omitted.</param>
    /// <param name="startFrame">Initial frame for isochronous transfers; zero when omitted.</param>
    /// <param name="numberOfPackets">Number of isochronous packets; defaults to 0xFFFFFFFF for non-ISO transfers.</param>
    /// <param name="interval">Maximum time for the request on the server-side host controller; zero when omitted.</param>
    /// <param name="setup">Setup packet of a control transfer; zero when omitted.</param>
    /// <returns>The submit header.</returns>
    public static UsbIpHeader CreateSubmit(
        uint seqNum,
        uint devId,
        UsbIpDirection direction,
        uint ep,
        uint transferBufferLength,
        uint transferFlags = 0,
        uint startFrame = 0,
        uint numberOfPackets = uint.MaxValue,
        uint interval = 0,
        UsbSetupPacket setup = default)
    {
        return new UsbIpHeader
        {
            Command = UsbIpProtocol.CommandSubmit,
            SeqNum = seqNum,
            DevId = devId,
            Direction = direction,
            Ep = ep,
            TransferFlags = transferFlags,
            TransferBufferLength = transferBufferLength,
            StartFrame = startFrame,
            NumberOfPackets = numberOfPackets,
            Interval = interval,
            Setup = setup
        };
    }

    /// <summary>
    /// Creates a USBIP_RET_SUBMIT header answering a submitted URB. The reply only
    /// carries the sequence number, status and transferred byte count; the remaining
    /// fields are zero as the protocol requires for server-side replies. The
    /// <see cref="NumberOfPackets"/> default of 0 is the ISO/unspecified shape: non-ISO RET callers
    /// (the server) overwrite it with 0xFFFFFFFF per the kernel protocol documentation.
    /// </summary>
    /// <param name="seqNum">Sequence number of the submitted URB being answered.</param>
    /// <param name="status">Completion status: zero on success, a negative errno otherwise.</param>
    /// <param name="actualLength">Number of bytes actually transferred.</param>
    /// <returns>The ret submit header.</returns>
    public static UsbIpHeader CreateRetSubmit(uint seqNum, int status, uint actualLength)
    {
        return new UsbIpHeader
        {
            Command = UsbIpProtocol.CommandRetSubmit,
            SeqNum = seqNum,
            Status = status,
            ActualLength = actualLength
        };
    }

    /// <summary>
    /// Creates a USBIP_RET_UNLINK header answering an unlink request. The reply only
    /// carries the sequence number and status; the remaining fields are zero as the
    /// protocol requires for server-side replies.
    /// </summary>
    /// <param name="seqNum">Sequence number of the USBIP_CMD_UNLINK being answered.</param>
    /// <param name="status">Completion status: zero on success, a negative errno otherwise.</param>
    /// <returns>The ret unlink header.</returns>
    public static UsbIpHeader CreateRetUnlink(uint seqNum, int status)
    {
        return new UsbIpHeader
        {
            Command = UsbIpProtocol.CommandRetUnlink,
            SeqNum = seqNum,
            Status = status
        };
    }

    /// <summary>
    /// Writes a USBIP_CMD_SUBMIT header to the first 48 bytes of <paramref name="destination"/>.
    /// The command field is always written as USBIP_CMD_SUBMIT.
    /// </summary>
    /// <param name="destination">Buffer holding at least 48 bytes.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than 48 bytes.</exception>
    public void WriteSubmit(Span<byte> destination)
    {
        EnsureHeaderSize(destination.Length, nameof(destination));

        WriteBasicHeader(destination, UsbIpProtocol.CommandSubmit);
        BinaryPrimitives.WriteUInt32BigEndian(destination[20..], TransferFlags); // 0x14
        BinaryPrimitives.WriteUInt32BigEndian(destination[24..], TransferBufferLength); // 0x18
        BinaryPrimitives.WriteUInt32BigEndian(destination[28..], StartFrame); // 0x1C
        BinaryPrimitives.WriteUInt32BigEndian(destination[32..], NumberOfPackets); // 0x20
        BinaryPrimitives.WriteUInt32BigEndian(destination[36..], Interval); // 0x24
        Setup.Write(destination.Slice(SetupOffset, SetupSize)); // 0x28
    }

    /// <summary>
    /// Writes a USBIP_RET_SUBMIT header to the first 48 bytes of <paramref name="destination"/>.
    /// The command field is always written as USBIP_RET_SUBMIT and the 8 bytes after
    /// error_count are written as zero padding; the remaining fields are written as stored,
    /// which keeps them zero for headers created with <see cref="CreateRetSubmit"/>.
    /// </summary>
    /// <param name="destination">Buffer holding at least 48 bytes.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than 48 bytes.</exception>
    public void WriteRetSubmit(Span<byte> destination)
    {
        EnsureHeaderSize(destination.Length, nameof(destination));

        WriteBasicHeader(destination, UsbIpProtocol.CommandRetSubmit);
        BinaryPrimitives.WriteInt32BigEndian(destination[20..], Status); // 0x14
        BinaryPrimitives.WriteUInt32BigEndian(destination[24..], ActualLength); // 0x18
        BinaryPrimitives.WriteUInt32BigEndian(destination[28..], StartFrame); // 0x1C
        BinaryPrimitives.WriteUInt32BigEndian(destination[32..], NumberOfPackets); // 0x20
        BinaryPrimitives.WriteUInt32BigEndian(destination[36..], ErrorCount); // 0x24
        destination.Slice(SetupOffset, SetupSize).Clear(); // 0x28: padding
    }

    /// <summary>
    /// Writes a USBIP_RET_UNLINK header to the first 48 bytes of <paramref name="destination"/>.
    /// The command field is always written as USBIP_RET_UNLINK and the 24 bytes after
    /// status are written as zero padding.
    /// </summary>
    /// <param name="destination">Buffer holding at least 48 bytes.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than 48 bytes.</exception>
    public void WriteRetUnlink(Span<byte> destination)
    {
        EnsureHeaderSize(destination.Length, nameof(destination));

        WriteBasicHeader(destination, UsbIpProtocol.CommandRetUnlink);
        BinaryPrimitives.WriteInt32BigEndian(destination[20..], Status); // 0x14
        destination[24..UsbIpProtocol.HeaderSize].Clear(); // 0x18: padding
    }

    private static (uint Command, uint SeqNum, uint DevId, UsbIpDirection Direction, uint Ep) ReadBasicHeader(ReadOnlySpan<byte> source)
    {
        return (
            BinaryPrimitives.ReadUInt32BigEndian(source), // 0x00
            BinaryPrimitives.ReadUInt32BigEndian(source[4..]), // 0x04
            BinaryPrimitives.ReadUInt32BigEndian(source[8..]), // 0x08
            (UsbIpDirection)BinaryPrimitives.ReadUInt32BigEndian(source[12..]), // 0x0C
            BinaryPrimitives.ReadUInt32BigEndian(source[16..])); // 0x10
    }

    private void WriteBasicHeader(Span<byte> destination, uint command)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, command); // 0x00
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], SeqNum); // 0x04
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..], DevId); // 0x08
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..], (uint)Direction); // 0x0C
        BinaryPrimitives.WriteUInt32BigEndian(destination[16..], Ep); // 0x10
    }

    private static void EnsureHeaderSize(int actualSize, string parameterName)
    {
        if (actualSize < UsbIpProtocol.HeaderSize)
        {
            throw new ArgumentException($"A USB/IP URB header requires {UsbIpProtocol.HeaderSize} bytes; got {actualSize}.", parameterName);
        }
    }
}