using System.Buffers.Binary;
using System.Text;

namespace DualSenseClient.Emulation.UsbIp;

/// <summary>
/// Codec of the message-level USB/IP handshake: the op_common header shared by OP_REQ_DEVLIST,
/// OP_REP_DEVLIST, OP_REQ_IMPORT and OP_REP_IMPORT together with the device list and import replies.
/// Layouts follow the kernel protocol documentation
/// (https://www.kernel.org/doc/html/latest/usb/usbip_protocol.html); every protocol field is network
/// (big-endian) byte order and strings are zero-padded.
/// </summary>
public static class UsbIpHandshake
{
    /// <summary>Size in bytes of the path field of a device entry (SYSFS_PATH_MAX).</summary>
    public const int PathSize = 256;

    /// <summary>Size in bytes of the busid field of a device entry and of an OP_REQ_IMPORT message (SYSFS_BUS_ID_SIZE).</summary>
    public const int BusIdSize = 32;

    /// <summary>
    /// Reads the op_common header (version, code, status) from the first
    /// <see cref="UsbIpProtocol.OpCommonSize"/> bytes of <paramref name="source"/>.
    /// </summary>
    /// <param name="source">Buffer holding at least 8 bytes.</param>
    /// <param name="version">USB/IP version of the message (0x0111 for protocol v1.1.1).</param>
    /// <param name="operation">Command or reply code: one of the UsbIpProtocol.Op* constants.</param>
    /// <param name="status">Status field: 0 for OK (unused in requests), a positive error code in import replies.</param>
    /// <exception cref="ArgumentException"><paramref name="source"/> is shorter than 8 bytes.</exception>
    public static void ReadOpCommon(ReadOnlySpan<byte> source, out ushort version, out ushort operation, out int status)
    {
        if (source.Length < UsbIpProtocol.OpCommonSize)
        {
            throw new ArgumentException($"An op_common header requires {UsbIpProtocol.OpCommonSize} bytes; got {source.Length}.", nameof(source));
        }

        version = BinaryPrimitives.ReadUInt16BigEndian(source); // 0x00
        operation = BinaryPrimitives.ReadUInt16BigEndian(source[2..]); // 0x02
        status = BinaryPrimitives.ReadInt32BigEndian(source[4..]); // 0x04
    }

    /// <summary>
    /// Writes an op_common header to the first <see cref="UsbIpProtocol.OpCommonSize"/> bytes of
    /// <paramref name="destination"/>. The version field is always written as
    /// <see cref="UsbIpProtocol.Version"/>.
    /// </summary>
    /// <param name="destination">Buffer holding at least 8 bytes.</param>
    /// <param name="operation">Command or reply code: one of the UsbIpProtocol.Op* constants.</param>
    /// <param name="status">Status field: 0 for OK, a positive error code in import replies.</param>
    /// <returns>The number of bytes written (8).</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than 8 bytes.</exception>
    public static int WriteOpCommon(Span<byte> destination, ushort operation, int status)
    {
        if (destination.Length < UsbIpProtocol.OpCommonSize)
        {
            throw new ArgumentException($"An op_common header requires {UsbIpProtocol.OpCommonSize} bytes; got {destination.Length}.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, UsbIpProtocol.Version); // 0x00
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], operation); // 0x02
        BinaryPrimitives.WriteInt32BigEndian(destination[4..], status); // 0x04
        return UsbIpProtocol.OpCommonSize;
    }

    /// <summary>
    /// Reads the busid field of an OP_REQ_IMPORT message: the message consists of the op_common header
    /// followed by a 32-byte zero-padded busid string naming the device to import, as listed by
    /// OP_REP_DEVLIST.
    /// </summary>
    /// <param name="source">Buffer holding the complete OP_REQ_IMPORT message (at least 40 bytes).</param>
    /// <param name="busId">Decoded busid: the UTF-8 bytes up to the first zero byte, or the whole field when it has no terminator.</param>
    /// <exception cref="ArgumentException"><paramref name="source"/> is shorter than 40 bytes.</exception>
    public static void ReadImportBusId(ReadOnlySpan<byte> source, out string busId)
    {
        int messageSize = UsbIpProtocol.OpCommonSize + BusIdSize;
        if (source.Length < messageSize)
        {
            throw new ArgumentException($"An OP_REQ_IMPORT message requires {messageSize} bytes; got {source.Length}.", nameof(source));
        }

        ReadOnlySpan<byte> field = source.Slice(UsbIpProtocol.OpCommonSize, BusIdSize);
        int terminatorIndex = field.IndexOf((byte)0);
        int length = terminatorIndex < 0 ? BusIdSize : terminatorIndex;
        busId = Encoding.UTF8.GetString(field[..length]);
    }

    /// <summary>
    /// Writes an OP_REP_DEVLIST reply: the op_common header, the number of exported devices and one
    /// <see cref="UsbIpDeviceInfo.WriteDevListEntry"/> per device.
    /// </summary>
    /// <param name="destination">Buffer holding at least the total size of the reply.</param>
    /// <param name="devices">Devices to export, in wire order.</param>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="ArgumentException">The buffer is too small or a device entry cannot be written.</exception>
    public static int WriteDevListReply(Span<byte> destination, IReadOnlyList<UsbIpDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        int replySize = UsbIpProtocol.OpCommonSize + 4; // op_common + number of exported devices
        foreach (UsbIpDeviceInfo device in devices)
        {
            replySize += UsbIpDeviceInfo.DevListEntrySize(device.Interfaces.Count);
        }

        if (destination.Length < replySize)
        {
            throw new ArgumentException(
                $"An OP_REP_DEVLIST reply for {devices.Count} device(s) requires {replySize} bytes; got {destination.Length}.",
                nameof(destination));
        }

        WriteOpCommon(destination, UsbIpProtocol.OpReplyDevList, UsbIpStatus.Success);
        BinaryPrimitives.WriteUInt32BigEndian(destination[UsbIpProtocol.OpCommonSize..], (uint)devices.Count); // 0x08
        int offset = UsbIpProtocol.OpCommonSize + 4;
        foreach (UsbIpDeviceInfo device in devices)
        {
            offset += device.WriteDevListEntry(destination[offset..]); // 0x0C onwards
        }

        return replySize;
    }

    /// <summary>
    /// Writes an OP_REP_IMPORT reply: the op_common header followed by the imported device's 312-byte
    /// base entry when the status is <see cref="UsbIpStatus.Success"/> ("0 for OK, 1 for error" in the
    /// kernel documentation); on any other status the reply ends at the header.
    /// </summary>
    /// <param name="destination">Buffer holding the whole reply.</param>
    /// <param name="status">Status: <see cref="UsbIpStatus.Success"/> when the device was found, 1 otherwise.</param>
    /// <param name="device">The imported device entry; ignored when <paramref name="status"/> is not success.</param>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="ArgumentException">The buffer is too small, or the status is success but <paramref name="device"/> is null.</exception>
    public static int WriteImportReply(Span<byte> destination, int status, UsbIpDeviceInfo? device)
    {
        bool succeeded = status == UsbIpStatus.Success;
        if (succeeded && device is null)
        {
            throw new ArgumentException("An OK import reply must carry the imported device entry.", nameof(device));
        }

        int replySize = succeeded ? UsbIpProtocol.OpCommonSize + UsbIpDeviceInfo.EntrySize : UsbIpProtocol.OpCommonSize;
        if (destination.Length < replySize)
        {
            throw new ArgumentException($"An OP_REP_IMPORT reply requires {replySize} bytes; got {destination.Length}.", nameof(destination));
        }

        WriteOpCommon(destination, UsbIpProtocol.OpReplyImport, status);
        if (succeeded && device is UsbIpDeviceInfo importedDevice)
        {
            importedDevice.WriteBaseEntry(destination[UsbIpProtocol.OpCommonSize..]); // 0x08 onwards
        }

        return replySize;
    }
}