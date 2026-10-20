using System.Buffers.Binary;

namespace DualSenseClient.Emulation.Core;

/// <summary>
/// Transfer type of a USB endpoint, decoded from the low two bits of the endpoint attributes
/// (bmAttributes; USB 2.0 specification, table 9-13). The values match the bmAttributes bit pattern.
/// </summary>
public enum UsbEndpointType : byte
{
    /// <summary>Control transfer type (bmAttributes bits 1..0 = 0b00).</summary>
    Control = 0,

    /// <summary>Isochronous transfer type (bmAttributes bits 1..0 = 0b01).</summary>
    Isochronous = 1,

    /// <summary>Bulk transfer type (bmAttributes bits 1..0 = 0b10).</summary>
    Bulk = 2,

    /// <summary>Interrupt transfer type (bmAttributes bits 1..0 = 0b11).</summary>
    Interrupt = 3
}

/// <summary>
/// Endpoint descriptor data (USB 2.0 specification, section 9.6.6) of one endpoint belonging to an
/// interface of a parsed <see cref="UsbDescriptorSet"/>.
/// </summary>
public readonly struct UsbEndpointInfo
{
    /// <summary>Mask of the transfer type bits (bmAttributes bits 1..0).</summary>
    private const byte TypeMask = 0x03;

    /// <summary>Endpoint address: endpoint number in bits 3..0 and direction in bit 7 (bEndpointAddress).</summary>
    public byte Address { get; init; }

    /// <summary>Transfer type and synchronization attributes of the endpoint (bmAttributes).</summary>
    public byte Attributes { get; init; }

    /// <summary>Transfer type decoded from the low two bits of <see cref="Attributes"/>.</summary>
    public UsbEndpointType Type
    {
        get { return (UsbEndpointType)(Attributes & TypeMask); }
    }

    /// <summary>Maximum packet size of the endpoint in bytes (wMaxPacketSize).</summary>
    public ushort MaxPacketSize { get; init; }

    /// <summary>Polling interval of the endpoint (bInterval).</summary>
    public byte Interval { get; init; }
}

/// <summary>
/// Interface descriptor data (USB 2.0 specification, section 9.6.5) with the endpoints that follow it
/// in the configuration descriptor. One entry is parsed per interface descriptor, so an interface
/// number that declares several alternate settings appears once per setting; <see cref="Number"/>
/// and <see cref="AlternateSetting"/> identify one setting.
/// </summary>
public readonly struct UsbInterfaceInfo
{
    /// <summary>Initializes an interface entry with no endpoints and every field zero.</summary>
    public UsbInterfaceInfo()
    {
    }

    /// <summary>Interface number (bInterfaceNumber).</summary>
    public byte Number { get; init; }

    /// <summary>Alternate setting of the interface (bAlternateSetting).</summary>
    public byte AlternateSetting { get; init; }

    /// <summary>Interface class (bInterfaceClass).</summary>
    public byte Class { get; init; }

    /// <summary>Interface subclass (bInterfaceSubClass).</summary>
    public byte SubClass { get; init; }

    /// <summary>Interface protocol (bInterfaceProtocol).</summary>
    public byte Protocol { get; init; }

    /// <summary>Endpoints of this alternate setting in descriptor order; empty when it declares none.</summary>
    public IReadOnlyList<UsbEndpointInfo> Endpoints { get; init; } = [];
}

/// <summary>
/// Descriptor set of one USB device: the raw device and configuration descriptor blobs plus the
/// interfaces and endpoints parsed from the configuration blob. The blobs are retained as views
/// (not copied) and the configuration blob is modelled as the region up to its wTotalLength.
/// Immutable; create instances with <see cref="Create"/>.
/// </summary>
public sealed class UsbDescriptorSet
{
    /// <summary>Size in bytes of the device descriptor (USB 2.0 specification, section 9.6.1).</summary>
    private const int DeviceDescriptorSize = 18;

    /// <summary>Size in bytes of the configuration descriptor header (USB 2.0 specification, section 9.6.3).</summary>
    private const int ConfigDescriptorSize = 9;

    /// <summary>Size in bytes of an interface descriptor (USB 2.0 specification, section 9.6.5).</summary>
    private const int InterfaceDescriptorSize = 9;

    /// <summary>Size in bytes of an endpoint descriptor (USB 2.0 specification, section 9.6.6).</summary>
    private const int EndpointDescriptorSize = 7;

    /// <summary>Size in bytes of the bLength and bDescriptorType header every descriptor starts with.</summary>
    private const int DescriptorHeaderSize = 2;

    /// <summary>Descriptor type of the device descriptor (DEVICE).</summary>
    private const byte DeviceDescriptorType = 0x01;

    /// <summary>Descriptor type of the configuration descriptor (CONFIGURATION).</summary>
    private const byte ConfigDescriptorType = 0x02;

    /// <summary>Descriptor type of an interface descriptor (INTERFACE).</summary>
    private const byte InterfaceDescriptorType = 0x04;

    /// <summary>Descriptor type of an endpoint descriptor (ENDPOINT).</summary>
    private const byte EndpointDescriptorType = 0x05;

    private UsbDescriptorSet()
    {
    }

    /// <summary>Device descriptor blob (bLength = 18); the descriptor served for GET_DESCRIPTOR(DEVICE).</summary>
    public ReadOnlyMemory<byte> DeviceDescriptor { get; private init; }

    /// <summary>Configuration descriptor blob: the configuration descriptor followed by every subordinate descriptor up to its wTotalLength.</summary>
    public ReadOnlyMemory<byte> ConfigDescriptor { get; private init; }

    /// <summary>HID report descriptor served for the device's HID interface, or <see langword="null"/> when the device has none.</summary>
    public ReadOnlyMemory<byte>? HidReportDescriptor { get; private init; }

    /// <summary>Vendor id of the device descriptor (idVendor).</summary>
    public ushort IdVendor { get; private init; }

    /// <summary>Product id of the device descriptor (idProduct).</summary>
    public ushort IdProduct { get; private init; }

    /// <summary>Device release number of the device descriptor (bcdDevice).</summary>
    public ushort BcdDevice { get; private init; }

    /// <summary>Device class of the device descriptor (bDeviceClass; 0 when the class is defined per interface).</summary>
    public byte DeviceClass { get; private init; }

    /// <summary>Device subclass of the device descriptor (bDeviceSubClass).</summary>
    public byte DeviceSubClass { get; private init; }

    /// <summary>Device protocol of the device descriptor (bDeviceProtocol).</summary>
    public byte DeviceProtocol { get; private init; }

    /// <summary>Number of configurations of the device descriptor (bNumConfigurations).</summary>
    public byte NumConfigurations { get; private init; }

    /// <summary>Configuration value of the configuration descriptor (bConfigurationValue).</summary>
    public byte ConfigurationValue { get; private init; }

    /// <summary>Interfaces of the configuration in descriptor order; one entry per interface descriptor, including every alternate setting.</summary>
    public IReadOnlyList<UsbInterfaceInfo> Interfaces { get; private init; } = [];

    /// <summary>Distinct interface numbers of the configuration in ascending order.</summary>
    public IReadOnlyList<byte> InterfaceNumbers { get; private init; } = [];

    /// <summary>Number of distinct interfaces of the configuration (bNumInterfaces).</summary>
    public int InterfaceCount
    {
        get
        {
            return InterfaceNumbers.Count;
        }
    }

    /// <summary>String descriptor values; the list index plus one is the string descriptor index.</summary>
    private string[] Strings { get; init; } = [];

    /// <summary>
    /// Validates the descriptor blobs and creates the descriptor set of one device.
    /// </summary>
    /// <param name="deviceDescriptor">Device descriptor blob (USB 2.0 specification, section 9.6.1); the first 18 bytes form the descriptor.</param>
    /// <param name="configDescriptor">Configuration descriptor blob (section 9.6.3): the configuration descriptor followed by every interface, class-specific and endpoint descriptor up to its wTotalLength.</param>
    /// <param name="hidReportDescriptor">HID report descriptor served for the device's HID interface, or <see langword="null"/> when the device has none.</param>
    /// <param name="strings">Strings of the device's string descriptors, where the list index plus one is the string descriptor index; index 0 is the language id descriptor served by the request engine and is not part of the list.</param>
    /// <returns>The parsed, immutable descriptor set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="strings"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">A blob is malformed: a declared length does not fit its buffer, the descriptor chain does not fill the configuration descriptor's wTotalLength, a descriptor is shorter than its kind requires, a descriptor header is wrong, or a distinct interface number declares no alternate setting 0.</exception>
    public static UsbDescriptorSet Create(
        ReadOnlyMemory<byte> deviceDescriptor,
        ReadOnlyMemory<byte> configDescriptor,
        ReadOnlyMemory<byte>? hidReportDescriptor,
        IReadOnlyList<string> strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        ReadOnlySpan<byte> device = deviceDescriptor.Span;
        ValidateDeviceDescriptor(device);
        List<UsbInterfaceInfo> interfaces = WalkConfiguration(configDescriptor.Span, out int totalLength, out byte configurationValue);
        List<byte> interfaceNumbers = [];
        foreach (UsbInterfaceInfo interfaceInfo in interfaces)
        {
            if (!interfaceNumbers.Contains(interfaceInfo.Number))
            {
                interfaceNumbers.Add(interfaceInfo.Number);
            }
        }

        interfaceNumbers.Sort();

        return new UsbDescriptorSet
        {
            DeviceDescriptor = deviceDescriptor.Slice(0, DeviceDescriptorSize),
            ConfigDescriptor = configDescriptor.Slice(0, totalLength),
            HidReportDescriptor = hidReportDescriptor,
            IdVendor = BinaryPrimitives.ReadUInt16LittleEndian(device[8..]),
            IdProduct = BinaryPrimitives.ReadUInt16LittleEndian(device[10..]),
            BcdDevice = BinaryPrimitives.ReadUInt16LittleEndian(device[12..]),
            DeviceClass = device[4],
            DeviceSubClass = device[5],
            DeviceProtocol = device[6],
            NumConfigurations = device[17],
            ConfigurationValue = configurationValue,
            Interfaces = interfaces.ToArray(),
            InterfaceNumbers = interfaceNumbers.ToArray(),
            Strings = strings.ToArray()
        };
    }

    /// <summary>
    /// Returns the string of a string descriptor index. The list passed to <see cref="Create"/> holds
    /// the strings in descriptor index order starting at index 1, so index 0 (the language id
    /// descriptor) and indexes beyond the list are unknown.
    /// </summary>
    /// <param name="index">String descriptor index referenced by iManufacturer, iProduct or iSerialNumber.</param>
    /// <param name="value">The string at <paramref name="index"/>; <see cref="string.Empty"/> when the index is unknown.</param>
    /// <returns><see langword="true"/> when the index maps to a string; otherwise <see langword="false"/>.</returns>
    public bool TryGetString(int index, out string value)
    {
        if (index >= 1 && index <= Strings.Length)
        {
            value = Strings[index - 1];
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>Returns whether the configuration declares an interface with <paramref name="number"/>.</summary>
    /// <param name="number">Interface number (bInterfaceNumber) to look up.</param>
    /// <returns><see langword="true"/> when the interface number is declared; otherwise <see langword="false"/>.</returns>
    public bool HasInterface(byte number)
    {
        for (int i = 0; i < InterfaceNumbers.Count; i++)
        {
            if (InterfaceNumbers[i] == number)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns every alternate setting of interface <paramref name="number"/> in descriptor order.
    /// The returned list is a snapshot; the descriptor set itself keeps no active setting.
    /// </summary>
    /// <param name="number">Interface number (bInterfaceNumber) to look up.</param>
    /// <returns>The alternate settings of the interface; empty when the interface is not declared.</returns>
    public IReadOnlyList<UsbInterfaceInfo> GetAlternates(byte number)
    {
        List<UsbInterfaceInfo> alternates = [];
        for (int i = 0; i < Interfaces.Count; i++)
        {
            UsbInterfaceInfo interfaceInfo = Interfaces[i];
            if (interfaceInfo.Number == number)
            {
                alternates.Add(interfaceInfo);
            }
        }

        return alternates.ToArray();
    }

    /// <summary>Looks up one alternate setting of an interface.</summary>
    /// <param name="number">Interface number (bInterfaceNumber) to look up.</param>
    /// <param name="alternateSetting">Alternate setting (bAlternateSetting) to look up.</param>
    /// <param name="interfaceInfo">Receives the interface entry when found; otherwise the default entry.</param>
    /// <returns><see langword="true"/> when the interface declares the alternate setting; otherwise <see langword="false"/>.</returns>
    public bool TryGetInterface(byte number, byte alternateSetting, out UsbInterfaceInfo interfaceInfo)
    {
        for (int i = 0; i < Interfaces.Count; i++)
        {
            UsbInterfaceInfo candidate = Interfaces[i];
            if (candidate.Number == number && candidate.AlternateSetting == alternateSetting)
            {
                interfaceInfo = candidate;
                return true;
            }
        }

        interfaceInfo = default;
        return false;
    }

    /// <summary>
    /// Validates the 18-byte device descriptor header.
    /// </summary>
    /// <param name="descriptor">Device descriptor blob; only the first 18 bytes are inspected.</param>
    /// <exception cref="InvalidDataException">The blob is shorter than 18 bytes or the header declares another length or type.</exception>
    private static void ValidateDeviceDescriptor(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < DeviceDescriptorSize)
        {
            throw new InvalidDataException($"A device descriptor blob requires {DeviceDescriptorSize} bytes; got {descriptor.Length}.");
        }

        if (descriptor[0] != DeviceDescriptorSize)
        {
            throw new InvalidDataException($"Device descriptor bLength must be {DeviceDescriptorSize}; got {descriptor[0]}.");
        }

        if (descriptor[1] != DeviceDescriptorType)
        {
            throw new InvalidDataException($"Device descriptor bDescriptorType must be 0x{DeviceDescriptorType:X2}; got 0x{descriptor[1]:X2}.");
        }
    }

    /// <summary>
    /// Walks the configuration descriptor chain up to wTotalLength and collects every interface
    /// descriptor with the endpoints that follow it, including all alternate settings. Every
    /// descriptor must fit the declared total length so the chain terminates exactly on it.
    /// </summary>
    /// <param name="descriptor">Configuration descriptor blob.</param>
    /// <param name="totalLength">The declared wTotalLength of the blob.</param>
    /// <param name="configurationValue">The bConfigurationValue of the configuration descriptor.</param>
    /// <returns>The interfaces of the configuration in descriptor order, one entry per interface descriptor.</returns>
    /// <exception cref="InvalidDataException">The blob is truncated, a descriptor header is malformed, or a distinct interface number declares no alternate setting 0.</exception>
    private static List<UsbInterfaceInfo> WalkConfiguration(ReadOnlySpan<byte> descriptor, out int totalLength, out byte configurationValue)
    {
        if (descriptor.Length < ConfigDescriptorSize)
        {
            throw new InvalidDataException($"A configuration descriptor blob requires at least {ConfigDescriptorSize} bytes; got {descriptor.Length}.");
        }

        if (descriptor[0] < ConfigDescriptorSize)
        {
            throw new InvalidDataException($"Configuration descriptor bLength must be at least {ConfigDescriptorSize}; got {descriptor[0]}.");
        }

        if (descriptor[1] != ConfigDescriptorType)
        {
            throw new InvalidDataException($"Configuration descriptor bDescriptorType must be 0x{ConfigDescriptorType:X2}; got 0x{descriptor[1]:X2}.");
        }

        totalLength = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[2..]);
        if (totalLength < ConfigDescriptorSize || totalLength > descriptor.Length)
        {
            throw new InvalidDataException($"Configuration descriptor wTotalLength is {totalLength} for a {descriptor.Length}-byte blob.");
        }

        configurationValue = descriptor[5];

        List<UsbInterfaceInfo> interfaces = [];
        List<UsbEndpointInfo>? currentEndpoints = null;
        byte number = 0;
        byte alternateSetting = 0;
        byte interfaceClass = 0;
        byte subClass = 0;
        byte protocol = 0;
        bool anyInterface = false;

        int offset = 0;
        while (offset < totalLength)
        {
            int remaining = totalLength - offset;
            if (remaining < DescriptorHeaderSize)
            {
                throw new InvalidDataException($"The descriptor chain leaves {remaining} byte(s) at offset {offset} before wTotalLength is reached.");
            }

            int length = descriptor[offset];
            if (length < DescriptorHeaderSize || length > remaining)
            {
                throw new InvalidDataException($"Descriptor at offset {offset} declares bLength {length} for {remaining} remaining byte(s).");
            }

            ReadOnlySpan<byte> body = descriptor.Slice(offset, length);
            switch (body[1])
            {
                case InterfaceDescriptorType:
                    if (length < InterfaceDescriptorSize)
                    {
                        throw new InvalidDataException(
                            $"Interface descriptor at offset {offset} declares bLength {length}; at least {InterfaceDescriptorSize} is required.");
                    }

                    AddCurrentInterface(interfaces, currentEndpoints, number, alternateSetting, interfaceClass, subClass, protocol);
                    anyInterface = true;
                    number = body[2];
                    alternateSetting = body[3];
                    interfaceClass = body[5];
                    subClass = body[6];
                    protocol = body[7];
                    currentEndpoints = [];
                    break;

                case EndpointDescriptorType:
                    if (length < EndpointDescriptorSize)
                    {
                        throw new InvalidDataException(
                            $"Endpoint descriptor at offset {offset} declares bLength {length}; at least {EndpointDescriptorSize} is required.");
                    }

                    if (!anyInterface)
                    {
                        throw new InvalidDataException($"Endpoint descriptor at offset {offset} appears before any interface descriptor.");
                    }

                    if (currentEndpoints is not null)
                    {
                        currentEndpoints.Add(new UsbEndpointInfo
                        {
                            Address = body[2],
                            Attributes = body[3],
                            MaxPacketSize = BinaryPrimitives.ReadUInt16LittleEndian(body[4..]),
                            Interval = body[6]
                        });
                    }

                    break;
            }

            offset += length;
        }

        AddCurrentInterface(interfaces, currentEndpoints, number, alternateSetting, interfaceClass, subClass, protocol);
        ValidateAlternateSettings(interfaces);
        return interfaces;
    }

    /// <summary>
    /// Validates that every distinct interface number declares the mandatory alternate setting 0.
    /// </summary>
    /// <param name="interfaces">Interfaces parsed from the configuration descriptor.</param>
    /// <exception cref="InvalidDataException">A distinct interface number declares only non-zero alternate settings.</exception>
    private static void ValidateAlternateSettings(List<UsbInterfaceInfo> interfaces)
    {
        List<byte> numbers = [];
        List<byte> numbersWithAltZero = [];
        foreach (UsbInterfaceInfo interfaceInfo in interfaces)
        {
            if (!numbers.Contains(interfaceInfo.Number))
            {
                numbers.Add(interfaceInfo.Number);
            }

            if (interfaceInfo.AlternateSetting == 0 && !numbersWithAltZero.Contains(interfaceInfo.Number))
            {
                numbersWithAltZero.Add(interfaceInfo.Number);
            }
        }

        foreach (byte number in numbers)
        {
            if (!numbersWithAltZero.Contains(number))
            {
                throw new InvalidDataException($"Interface {number} declares no alternate setting 0.");
            }
        }
    }

    /// <summary>
    /// Appends the interface collected so far to <paramref name="interfaces"/>; does nothing when no
    /// interface is open.
    /// </summary>
    /// <param name="interfaces">Interfaces collected so far.</param>
    /// <param name="endpoints">Endpoints of the open interface, or <see langword="null"/> when none is open.</param>
    /// <param name="number">Interface number of the open interface.</param>
    /// <param name="alternateSetting">Alternate setting of the open interface.</param>
    /// <param name="interfaceClass">Interface class of the open interface.</param>
    /// <param name="subClass">Interface subclass of the open interface.</param>
    /// <param name="protocol">Interface protocol of the open interface.</param>
    private static void AddCurrentInterface(List<UsbInterfaceInfo> interfaces, List<UsbEndpointInfo>? endpoints, byte number, byte alternateSetting,
        byte interfaceClass, byte subClass, byte protocol)
    {
        if (endpoints is null)
        {
            return;
        }

        interfaces.Add(new UsbInterfaceInfo
        {
            Number = number,
            AlternateSetting = alternateSetting,
            Class = interfaceClass,
            SubClass = subClass,
            Protocol = protocol,
            Endpoints = endpoints.ToArray()
        });
    }
}