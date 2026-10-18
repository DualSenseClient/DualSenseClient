using DualSenseClient.Emulation.Core;

// Descriptor data: the reference implementation serves the 18-byte device descriptor (USB 2.0,
// vendor class 0xFF/0xFF/0xFF, bMaxPacketSize0 8, bcdDevice 0x0114, string indexes 1/2/3) and the
// 153-byte XUSB_DESCRIPTOR_SIZE configuration blob; its interface 0 is the bring-up configuration
// served here: vendor class 0xFF, subclass 0x5D, protocol 0x01, a 17-byte 0x21 class block and the
// interrupt IN/OUT endpoint pair at 32-byte packets with intervals 4 and 8. The identity defaults
// are the reference constructor defaults (idVendor 0x045E, idProduct 0x028E). The string values
// are the ones a behavior-oracle reference descriptor set serves: "©Microsoft Corporation" at index
// 1, "Controller" at index 2 (the real controller's value) and "296013F" at index 3, a captured
// hardware serial. The reference device descriptor pins iManufacturer/iProduct/iSerialNumber at
// 1/2/3 but its kernel PDO serves no string responses, so the string values cannot come from it.

namespace DualSenseClient.Emulation.Devices.Xbox360;

/// <summary>
/// Identity of the Xbox 360 wired controller persona (Microsoft 045E:028E): the device and string
/// descriptor values that describe one virtual controller. The defaults reproduce the reference
/// identity: the ids of the reference target defaults, the bcdDevice and string indexes of its
/// device descriptor and the string values of the behavior-oracle descriptor set. Every value is
/// settable at construction. The descriptor set is built with <see cref="Xbox360DescriptorSet.Create"/>.
/// </summary>
public readonly struct Xbox360Identity
{
    /// <summary>Initializes an identity with the reference Xbox 360 wired controller defaults.</summary>
    public Xbox360Identity()
    {
    }

    /// <summary>Vendor id served in the device descriptor (idVendor); defaults to 0x045E (Microsoft).</summary>
    public ushort IdVendor { get; init; } = 0x045E;

    /// <summary>
    /// Product id served in the device descriptor (idProduct); defaults to 0x028E (Xbox 360 wired
    /// controller), the product the Xbox 360 controller driver (xusb22) binds.
    /// </summary>
    public ushort IdProduct { get; init; } = 0x028E;

    /// <summary>Device release number served in the device descriptor (bcdDevice); defaults to 0x0114, the reference value.</summary>
    public ushort BcdDevice { get; init; } = 0x0114;

    /// <summary>
    /// Manufacturer string (string descriptor index 1 by default); defaults to "©Microsoft Corporation",
    /// the value a real wired controller serves. A null or empty value omits the manufacturer string
    /// descriptor and sets iManufacturer to zero.
    /// </summary>
    public string Manufacturer { get; init; } = "©Microsoft Corporation";

    /// <summary>
    /// Product string (string descriptor index 2 by default); defaults to "Controller", the real
    /// controller's product string. The reference's "Virtual Xbox 360 Controller" is its Windows
    /// device description, not a USB string, so it is not served. A null or empty value omits the product
    /// string descriptor and sets iProduct to zero.
    /// </summary>
    public string Product { get; init; } = "Controller";

    /// <summary>
    /// Serial number string (string descriptor index 3 by default); defaults to "296013F", the value
    /// the behavior-oracle descriptor set serves (a captured hardware serial). The reference device
    /// descriptor always advertises iSerialNumber 3, so the default keeps a matching string; a null or empty
    /// value omits the serial string descriptor and sets iSerialNumber to zero.
    /// </summary>
    public string SerialNumber { get; init; } = "296013F";
}

/// <summary>
/// Descriptor set of the Xbox 360 wired controller persona (Microsoft 045E:028E) in its bring-up
/// configuration: one vendor-class interface (class 0xFF, subclass 0x5D, protocol 0x01) with the
/// reference's 17-byte 0x21 class block and an interrupt IN and an interrupt OUT endpoint, both at
/// 32-byte packets (intervals 4 and 8). The persona is not HID: the interface serves no HID report
/// descriptor and the request engine serves only the standard requests. The descriptor bytes are
/// transcribed from the reference implementation.
/// The bring-up configuration drops the reference's interfaces 1-3 (headset audio, chatpad and the
/// 0x13 vendor interface) exactly like the DualShock 4 persona drops its audio function; the M3
/// composite restores the full 153-byte XUSB_DESCRIPTOR_SIZE configuration.
/// </summary>
public static class Xbox360DescriptorSet
{
    /// <summary>
    /// Creates the descriptor set of the Xbox 360 persona from an identity: the device descriptor
    /// carries the identity's ids, release number and string indexes, and the string table carries
    /// its manufacturer, product and (when non-empty) serial number strings.
    /// </summary>
    /// <param name="identity">Identity of the persona; use <c>new Xbox360Identity()</c> for the reference defaults.</param>
    /// <returns>The parsed descriptor set served for the device's control requests.</returns>
    public static UsbDescriptorSet Create(Xbox360Identity identity)
    {
        List<string> strings = [];
        byte manufacturerIndex = AddString(strings, identity.Manufacturer);
        byte productIndex = AddString(strings, identity.Product);
        byte serialIndex = AddString(strings, identity.SerialNumber);

        return UsbDescriptorSet.Create(
            CreateDeviceDescriptor(identity, manufacturerIndex, productIndex, serialIndex),
            CreateConfigurationDescriptor(),
            null,
            strings);
    }

    /// <summary>
    /// Appends one string descriptor value and returns its string descriptor index (list index plus one);
    /// a null or empty value is omitted and yields index zero (no string), so an omitted serial keeps
    /// the device descriptor's iSerialNumber zero.
    /// </summary>
    /// <param name="strings">String descriptor values collected so far, in descriptor index order.</param>
    /// <param name="value">String value to add.</param>
    /// <returns>The string descriptor index of <paramref name="value"/>, or zero when it is null or empty.</returns>
    private static byte AddString(List<string> strings, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        strings.Add(value);
        return (byte)strings.Count;
    }

    /// <summary>
    /// Creates the 18-byte device descriptor (USB 2.0 specification, section 9.6.1) with the
    /// reference's values: USB 2.0, vendor-specific class 0xFF/0xFF/0xFF, endpoint-0 packet size 8,
    /// the identity's ids and release number, and the string descriptor indexes.
    /// </summary>
    /// <param name="identity">Identity carrying the vendor/product ids and the release number.</param>
    /// <param name="manufacturerIndex">String descriptor index of the manufacturer, or zero when none is served.</param>
    /// <param name="productIndex">String descriptor index of the product, or zero when none is served.</param>
    /// <param name="serialIndex">String descriptor index of the serial number, or zero when none is served.</param>
    /// <returns>The device descriptor bytes.</returns>
    private static byte[] CreateDeviceDescriptor(Xbox360Identity identity, byte manufacturerIndex, byte productIndex, byte serialIndex)
    {
        return
        [
            0x12, // bLength: 18
            0x01, // bDescriptorType: DEVICE
            0x00, 0x02, // bcdUSB: USB 2.0
            0xFF, // bDeviceClass: vendor specific (reference)
            0xFF, // bDeviceSubClass: vendor specific (reference)
            0xFF, // bDeviceProtocol: vendor specific (reference)
            0x08, // bMaxPacketSize0: 8 (reference value)
            (byte)identity.IdVendor, (byte)(identity.IdVendor >> 8), // idVendor
            (byte)identity.IdProduct, (byte)(identity.IdProduct >> 8), // idProduct
            (byte)identity.BcdDevice, (byte)(identity.BcdDevice >> 8), // bcdDevice
            manufacturerIndex, // iManufacturer
            productIndex, // iProduct
            serialIndex, // iSerialNumber: the reference pins 3, zero when the serial string is omitted
            0x01 // bNumConfigurations: 1
        ];
    }

    /// <summary>
    /// Creates the 49-byte configuration descriptor blob of the bring-up configuration: the
    /// configuration descriptor, the vendor-class gamepad interface (interface 0), the reference's
    /// 17-byte 0x21 class block and the interrupt IN and interrupt OUT endpoint descriptors
    /// (USB 2.0, sections 9.6.3 to 9.6.6). Sizes, addresses, attributes, power and intervals follow
    /// the reference's interface 0.
    /// </summary>
    /// <returns>The configuration descriptor bytes.</returns>
    private static byte[] CreateConfigurationDescriptor()
    {
        int totalLength = 9 + 9 + 17 + 7 + 7; // 49: header + interface + class block + interrupt IN + interrupt OUT

        return
        [
            0x09, // bLength: 9
            0x02, // bDescriptorType: CONFIGURATION
            (byte)totalLength, (byte)(totalLength >> 8), // wTotalLength: 49
            0x01, // bNumInterfaces: 1 (the reference serves 4; the bring-up keeps the gamepad interface)
            0x01, // bConfigurationValue: 1
            0x00, // iConfiguration: none
            0xA0, // bmAttributes: remote wakeup (reference value)
            0xFA, // bMaxPower: 500 mA (250 * 2 mA)
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x00, // bInterfaceNumber: 0 (the reference serves the gamepad interface as 0)
            0x00, // bAlternateSetting: 0
            0x02, // bNumEndpoints: 2 (IN + OUT)
            0xFF, // bInterfaceClass: vendor specific
            0x5D, // bInterfaceSubClass: gamepad (reference 0x5D)
            0x01, // bInterfaceProtocol: gamepad (reference 0x01)
            0x00, // iInterface: none
            0x11, // bLength: 17
            0x21, // bDescriptorType: 0x21 (the reference's class block of the gamepad interface)
            0x00, 0x01, // bcdHID: 1.00
            0x01, // bCountryCode
            0x25, // bNumDescriptors: 37 (reference value, though the 17-byte block is truncated after four entries)
            0x81, // bDescriptorType[0]: 0x81
            0x14, 0x00, // wDescriptorLength[0]: 20 (the input packet size)
            0x00, // bDescriptorType[1]: 0x00
            0x00, 0x00, // wDescriptorLength[1]: 0
            0x13, // bDescriptorType[2]: 0x13
            0x01, 0x08, // wDescriptorLength[2]: 2049
            0x00, // bDescriptorType[3]: 0x00
            0x00, // (the reference block ends here; its declared lengths overrun the block)
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            0x81, // bEndpointAddress: IN EP1
            0x03, // bmAttributes: interrupt
            0x20, 0x00, // wMaxPacketSize: 32 (reference value; the input packet is 20 bytes)
            0x04, // bInterval: 4 (full-speed frames, reference value)
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            0x01, // bEndpointAddress: OUT EP1
            0x03, // bmAttributes: interrupt
            0x20, 0x00, // wMaxPacketSize: 32 (reference value; the rumble packet is 8 bytes)
            0x08 // bInterval: 8 (full-speed frames, reference value)
        ];
    }
}