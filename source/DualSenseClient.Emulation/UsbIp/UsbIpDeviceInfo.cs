using System.Buffers.Binary;
using System.Text;

namespace DualSenseClient.Emulation.UsbIp;

/// <summary>
/// Transfer speed of an exported device, carried in the speed field of a device entry (usbip_usb_device).
/// </summary>
public enum UsbIpDeviceSpeed
{
    /// <summary>Speed unknown to the client (0).</summary>
    Unknown = 0,

    /// <summary>Low speed, 1.5 Mbit/s (1).</summary>
    Low = 1,

    /// <summary>Full speed, 12 Mbit/s (2).</summary>
    Full = 2,

    /// <summary>High speed, 480 Mbit/s (3).</summary>
    High = 3
}

/// <summary>
/// Interface descriptor data of one exported interface (usbip_usb_interface). Device list entries carry
/// one 4-byte record per interface: class, subclass, protocol and one zero padding byte.
/// </summary>
public readonly struct UsbIpInterfaceInfo
{
    /// <summary>Interface class (bInterfaceClass).</summary>
    public byte Class { get; init; }

    /// <summary>Interface subclass (bInterfaceSubClass).</summary>
    public byte SubClass { get; init; }

    /// <summary>Interface protocol (bInterfaceProtocol).</summary>
    public byte Protocol { get; init; }
}

/// <summary>
/// USB/IP device entry (usbip_usb_device) of the handshake replies. The kernel protocol documentation
/// tables place the entry at offset 0x0C of an OP_REP_DEVLIST message and at offset 0x08 of an
/// OP_REP_IMPORT message; every numeric field is network (big-endian) byte order and strings are
/// zero-padded. OP_REP_DEVLIST appends one 4-byte record per interface (see <see cref="WriteDevListEntry"/>),
/// while OP_REP_IMPORT carries the plain 312-byte base entry without records (see <see cref="WriteBaseEntry"/>).
/// </summary>
public readonly struct UsbIpDeviceInfo
{
    /// <summary>Size in bytes of the base device entry: the shape OP_REP_IMPORT carries and OP_REP_DEVLIST extends.</summary>
    public const int EntrySize = 312;

    /// <summary>Size in bytes of one interface record appended to a device list entry.</summary>
    public const int InterfaceRecordSize = 4;

    /// <summary>Initializes a device entry with an empty bus id and no interface records.</summary>
    public UsbIpDeviceInfo()
    {
    }

    /// <summary>
    /// Bus id of the exported device (busid): a zero-terminated string of at most 31 UTF-8 bytes,
    /// zero-padded to the 32-byte field. Clients use it to reference the device in OP_REQ_IMPORT.
    /// </summary>
    public string BusId { get; init; } = string.Empty;

    /// <summary>Bus number of the device (busnum).</summary>
    public uint BusNum { get; init; }

    /// <summary>Device number of the device (devnum); ((busnum &lt;&lt; 16) | devnum) forms the devid of the URB header.</summary>
    public uint DevNum { get; init; }

    /// <summary>Transfer speed of the device (speed).</summary>
    public UsbIpDeviceSpeed Speed { get; init; }

    /// <summary>Vendor id of the device descriptor (idVendor).</summary>
    public ushort IdVendor { get; init; }

    /// <summary>Product id of the device descriptor (idProduct).</summary>
    public ushort IdProduct { get; init; }

    /// <summary>Device release number of the device descriptor (bcdDevice).</summary>
    public ushort BcdDevice { get; init; }

    /// <summary>Device class of the device descriptor (bDeviceClass).</summary>
    public byte DeviceClass { get; init; }

    /// <summary>Device subclass of the device descriptor (bDeviceSubClass).</summary>
    public byte DeviceSubClass { get; init; }

    /// <summary>Device protocol of the device descriptor (bDeviceProtocol).</summary>
    public byte DeviceProtocol { get; init; }

    /// <summary>Configuration value of the device descriptor (bConfigurationValue).</summary>
    public byte ConfigurationValue { get; init; }

    /// <summary>
    /// One record per interface of the device (bNumInterfaces records in a device list entry);
    /// empty when the device exposes no interfaces.
    /// </summary>
    public IReadOnlyList<UsbIpInterfaceInfo> Interfaces { get; init; } = Array.Empty<UsbIpInterfaceInfo>();

    /// <summary>
    /// Returns the size in bytes of an OP_REP_DEVLIST device entry: the base entry plus one
    /// 4-byte record per interface.
    /// </summary>
    /// <param name="interfaceCount">Number of interface records that follow the base entry.</param>
    /// <returns>The entry size in bytes.</returns>
    public static int DevListEntrySize(int interfaceCount) => EntrySize + InterfaceRecordSize * interfaceCount;

    /// <summary>
    /// Writes the 312-byte base entry (the OP_REP_IMPORT shape, no interface records) to the first
    /// <see cref="EntrySize"/> bytes of <paramref name="destination"/>. The path field is written as
    /// zero bytes because the exported device has no host path, and bNumConfigurations is written as 1
    /// because the exposed devices carry a single configuration.
    /// </summary>
    /// <param name="destination">Buffer holding at least <see cref="EntrySize"/> bytes.</param>
    /// <returns>The number of bytes written (<see cref="EntrySize"/>).</returns>
    /// <exception cref="ArgumentException">The buffer is too small, the bus id does not fit into the
    /// 32-byte zero-terminated field, or there are more interfaces than the count field holds.</exception>
    public int WriteBaseEntry(Span<byte> destination)
    {
        if (destination.Length < EntrySize)
        {
            throw new ArgumentException($"A device entry requires {EntrySize} bytes; got {destination.Length}.", nameof(destination));
        }

        if (Interfaces.Count > byte.MaxValue)
        {
            throw new ArgumentException($"The entry carries at most {byte.MaxValue} interface records; got {Interfaces.Count}.", nameof(Interfaces));
        }

        Span<byte> entry = destination[..EntrySize];
        entry[..UsbIpHandshake.PathSize].Clear(); // 0x000: path
        WritePaddedString(entry.Slice(UsbIpHandshake.PathSize, UsbIpHandshake.BusIdSize), BusId, nameof(BusId)); // 0x100: busid
        BinaryPrimitives.WriteUInt32BigEndian(entry[288..], BusNum); // 0x120: busnum
        BinaryPrimitives.WriteUInt32BigEndian(entry[292..], DevNum); // 0x124: devnum
        BinaryPrimitives.WriteUInt32BigEndian(entry[296..], (uint)Speed); // 0x128: speed
        BinaryPrimitives.WriteUInt16BigEndian(entry[300..], IdVendor); // 0x12C: idVendor
        BinaryPrimitives.WriteUInt16BigEndian(entry[302..], IdProduct); // 0x12E: idProduct
        BinaryPrimitives.WriteUInt16BigEndian(entry[304..], BcdDevice); // 0x130: bcdDevice
        entry[306] = DeviceClass; // 0x132: bDeviceClass
        entry[307] = DeviceSubClass; // 0x133: bDeviceSubClass
        entry[308] = DeviceProtocol; // 0x134: bDeviceProtocol
        entry[309] = ConfigurationValue; // 0x135: bConfigurationValue
        entry[310] = 1; // 0x136: bNumConfigurations
        entry[311] = (byte)Interfaces.Count; // 0x137: bNumInterfaces
        return EntrySize;
    }

    /// <summary>
    /// Writes an OP_REP_DEVLIST device entry: the base entry written by <see cref="WriteBaseEntry"/>
    /// followed by one 4-byte record per interface (class, subclass, protocol, zero pad).
    /// </summary>
    /// <param name="destination">Buffer holding at least <see cref="DevListEntrySize(int)"/> bytes.</param>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="ArgumentException">The buffer is too small, the bus id does not fit into the
    /// 32-byte zero-terminated field, or there are more interfaces than the count field holds.</exception>
    public int WriteDevListEntry(Span<byte> destination)
    {
        int entrySize = DevListEntrySize(Interfaces.Count);
        if (destination.Length < entrySize)
        {
            throw new ArgumentException(
                $"A device list entry with {Interfaces.Count} interface(s) requires {entrySize} bytes; got {destination.Length}.",
                nameof(destination));
        }

        WriteBaseEntry(destination[..EntrySize]);
        int offset = EntrySize;
        foreach (UsbIpInterfaceInfo interfaceInfo in Interfaces)
        {
            destination[offset] = interfaceInfo.Class;
            destination[offset + 1] = interfaceInfo.SubClass;
            destination[offset + 2] = interfaceInfo.Protocol;
            destination[offset + 3] = 0; // padding
            offset += InterfaceRecordSize;
        }

        return entrySize;
    }

    private static void WritePaddedString(Span<byte> field, string value, string parameterName)
    {
        field.Clear();
        if (Encoding.UTF8.GetByteCount(value) > field.Length - 1)
        {
            throw new ArgumentException($"\"{value}\" does not fit into a zero-terminated {field.Length}-byte field.", parameterName);
        }

        Encoding.UTF8.GetBytes(value, field);
    }
}