using DualSenseClient.Emulation.Core;

// Descriptor data of the reference device: the device descriptor, the HID block of the configuration
// descriptor and the 321-byte HID report descriptor.

namespace DualSenseClient.Emulation.Devices.DualSense;

/// <summary>
/// Descriptor set of the DualSense persona (Sony 054C:0CE6): the composite configuration with the
/// UAC1 audio function (audio control interface 0, speaker streaming interface 1, microphone streaming
/// interface 2) and one HID interface (interface 3) with an interrupt IN and an interrupt OUT endpoint
/// plus the reference 321-byte HID report descriptor. The 186-byte audio block is the reference block
/// of <see cref="UsbAudioClass"/>; the HID block keeps the P3 bytes and the reference's interface
/// number 3 (the composite builder does not renumber it).
/// The descriptor bytes are transcribed from the reference.
/// </summary>
public static class DualSenseDescriptorSet
{
    /// <summary>
    /// Creates the descriptor set of the DualSense persona from an identity: the device descriptor
    /// carries the identity's ids and release number, and the string table carries its manufacturer, product
    /// and (when non-empty) serial number strings.
    /// </summary>
    /// <param name="identity">Identity of the persona; use <c>new DualSenseIdentity()</c> for the reference defaults.</param>
    /// <returns>The parsed descriptor set served for the device's control and HID requests.</returns>
    public static UsbDescriptorSet Create(DualSenseIdentity identity)
    {
        List<string> strings = [];
        byte manufacturerIndex = AddString(strings, identity.Manufacturer);
        byte productIndex = AddString(strings, identity.Product);
        byte serialIndex = AddString(strings, identity.SerialNumber);

        byte[] hidReportDescriptor = CreateHidReportDescriptor();
        return UsbDescriptorSet.Create(
            CreateDeviceDescriptor(identity, manufacturerIndex, productIndex, serialIndex),
            CreateConfigurationDescriptor(hidReportDescriptor.Length),
            hidReportDescriptor,
            strings);
    }

    /// <summary>
    /// Appends one string descriptor value and returns its string descriptor index (list index plus one);
    /// a null or empty value is omitted and yields index zero (no string), matching the reference, where
    /// the serial string descriptor only exists when a serial number is configured.
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
    /// Creates the 18-byte device descriptor (USB 2.0 specification, section 9.6.1): USB 2.0, class defined
    /// per interface (the reference's no-serial build; it presents USB 2.1 only with wake on), endpoint-0
    /// packet size 64, the identity's ids and release number, and the string descriptor indexes.
    /// </summary>
    /// <param name="identity">Identity carrying the vendor/product ids and the release number.</param>
    /// <param name="manufacturerIndex">String descriptor index of the manufacturer, or zero when none is served.</param>
    /// <param name="productIndex">String descriptor index of the product, or zero when none is served.</param>
    /// <param name="serialIndex">String descriptor index of the serial number, or zero when none is served.</param>
    /// <returns>The device descriptor bytes.</returns>
    private static byte[] CreateDeviceDescriptor(DualSenseIdentity identity, byte manufacturerIndex, byte productIndex, byte serialIndex)
    {
        return
        [
            0x12, // bLength: 18
            0x01, // bDescriptorType: DEVICE
            0x00, 0x02, // bcdUSB: USB 2.0 (the reference's runtime value with wake off; 0x0210 with wake on)
            0x00, // bDeviceClass: defined by the interface (the reference's serial build uses the IAD class instead)
            0x00, // bDeviceSubClass
            0x00, // bDeviceProtocol
            0x40, // bMaxPacketSize0: 64 (CFG_TUD_ENDPOINT0_SIZE)
            (byte)identity.IdVendor, (byte)(identity.IdVendor >> 8), // idVendor
            (byte)identity.IdProduct, (byte)(identity.IdProduct >> 8), // idProduct
            (byte)identity.BcdDevice, (byte)(identity.BcdDevice >> 8), // bcdDevice
            manufacturerIndex, // iManufacturer
            productIndex, // iProduct
            serialIndex, // iSerialNumber: zero when no serial string is served
            0x01 // bNumConfigurations: 1
        ];
    }

    /// <summary>
    /// Creates the 227-byte composite configuration descriptor blob of the persona: the configuration
    /// header declaring four interfaces, the 186-byte UAC1 audio block (audio control interface 0,
    /// speaker streaming interface 1 and microphone streaming interface 2) and the 32-byte HID function
    /// block (interface 3 with the HID class descriptor and the interrupt IN and interrupt OUT endpoint
    /// descriptors; USB 2.0, sections 9.6.3 to 9.6.6). The composite header and the concatenation come
    /// from <see cref="UsbAudioClass.CreateCompositeConfiguration"/>; sizes, addresses, attributes,
    /// power and the report length follow the reference. The HID block keeps the P3 bytes with the
    /// reference's interface number 3 beside the audio function; both endpoint intervals carry the
    /// reference's runtime override instead of the array bytes.
    /// </summary>
    /// <param name="hidReportDescriptorLength">Length in bytes of the served HID report descriptor (wDescriptorLength).</param>
    /// <returns>The composite configuration descriptor bytes.</returns>
    private static byte[] CreateConfigurationDescriptor(int hidReportDescriptorLength)
    {
        return UsbAudioClass.CreateCompositeConfiguration(
            UsbAudioClass.CreateDualSenseAudioBlock(),
            CreateHidConfigurationBlock(hidReportDescriptorLength));
    }

    /// <summary>
    /// Creates the 32-byte HID function block of the composite configuration: the HID interface
    /// (interface 3, the reference's number beside the audio function), the HID class descriptor and
    /// the interrupt IN and interrupt OUT endpoint descriptors. Every byte matches the P3 HID block,
    /// including the runtime interval override (2) in place of the array bytes (1), except that the
    /// interface number is the reference's 3 rather than the audio-less bring-up's 0.
    /// </summary>
    /// <param name="hidReportDescriptorLength">Length in bytes of the served HID report descriptor (wDescriptorLength).</param>
    /// <returns>The HID block bytes.</returns>
    private static byte[] CreateHidConfigurationBlock(int hidReportDescriptorLength)
    {
        return
        [
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x03, // bInterfaceNumber: 3 (the reference serves the HID interface as 3 alongside the audio function)
            0x00, // bAlternateSetting: 0
            0x02, // bNumEndpoints: 2 (IN + OUT)
            0x03, // bInterfaceClass: HID
            0x00, // bInterfaceSubClass: none
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x09, // bLength: 9
            0x21, // bDescriptorType: HID
            0x11, 0x01, // bcdHID: 1.11
            0x00, // bCountryCode: not localized
            0x01, // bNumDescriptors: 1 report descriptor
            0x22, // bDescriptorType: REPORT
            (byte)hidReportDescriptorLength, (byte)(hidReportDescriptorLength >> 8), // wDescriptorLength: 321
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            0x84, // bEndpointAddress: IN EP4
            0x03, // bmAttributes: interrupt
            0x40, 0x00, // wMaxPacketSize: 64
            0x02, // bInterval: 2 -- runtime override for the reference's default 500 Hz polling mode (raw array byte 0x01)
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            0x03, // bEndpointAddress: OUT EP3
            0x03, // bmAttributes: interrupt
            0x40, 0x00, // wMaxPacketSize: 64
            0x02 // bInterval: 2 -- runtime override for the reference's default 500 Hz polling mode (raw array byte 0x01)
        ];
    }

    /// <summary>
    /// Creates the reference HID report descriptor: the 321-byte report descriptor the reference serves for a
    /// DualSense in DualSense mode (<c>desc_hid_report_ds</c>; report IDs 1, 2, 5, 8-12, 32-34, 128-133, 160,
    /// 224 and 240-242 plus 244-249), transcribed byte-for-byte from the reference (its
    /// static_assert pins the length at 321). The 437-byte Edge descriptor (<c>desc_hid_report_dse</c>) is
    /// served by the Edge persona instead.
    /// </summary>
    /// <returns>The HID report descriptor bytes.</returns>
    private static byte[] CreateHidReportDescriptor()
    {
        return
        [
            0x05, 0x01, // Usage Page (Generic Desktop)
            0x09, 0x05, // Usage (Game Pad)
            0xA1, 0x01, // Collection (Application)
            0x85, 0x01, // Report ID (1)
            0x09, 0x30, // Usage (X)
            0x09, 0x31, // Usage (Y)
            0x09, 0x32, // Usage (Z)
            0x09, 0x35, // Usage (Rz)
            0x09, 0x33, // Usage (Rx)
            0x09, 0x34, // Usage (Ry)
            0x15, 0x00, // Logical Minimum (0)
            0x26, 0xFF, 0x00, // Logical Maximum (255)
            0x75, 0x08, // Report Size (8)
            0x95, 0x06, // Report Count (6)
            0x81, 0x02, // Input (Data,Var)
            0x06, 0x00, 0xFF, // Usage Page (0xFF00)
            0x09, 0x20, // Usage (0x20)
            0x95, 0x01, // Report Count (1)
            0x81, 0x02, // Input (Data,Var)
            0x05, 0x01, // Usage Page (Generic Desktop)
            0x09, 0x39, // Usage (Hat switch)
            0x15, 0x00, // Logical Minimum (0)
            0x25, 0x07, // Logical Maximum (7)
            0x35, 0x00, // Physical Minimum (0)
            0x46, 0x3B, 0x01, // Physical Maximum (315)
            0x65, 0x14, // Unit (0x14)
            0x75, 0x04, // Report Size (4)
            0x95, 0x01, // Report Count (1)
            0x81, 0x42, // Input (Data,Var,Null)
            0x65, 0x00, // Unit (0x0)
            0x05, 0x09, // Usage Page (Button)
            0x19, 0x01, // Usage Minimum (1)
            0x29, 0x0F, // Usage Maximum (15)
            0x15, 0x00, // Logical Minimum (0)
            0x25, 0x01, // Logical Maximum (1)
            0x75, 0x01, // Report Size (1)
            0x95, 0x0F, // Report Count (15)
            0x81, 0x02, // Input (Data,Var)
            0x06, 0x00, 0xFF, // Usage Page (0xFF00)
            0x09, 0x21, // Usage (0x21)
            0x95, 0x0D, // Report Count (13)
            0x81, 0x02, // Input (Data,Var)
            0x06, 0x00, 0xFF, // Usage Page (0xFF00)
            0x09, 0x22, // Usage (0x22)
            0x15, 0x00, // Logical Minimum (0)
            0x26, 0xFF, 0x00, // Logical Maximum (255)
            0x75, 0x08, // Report Size (8)
            0x95, 0x34, // Report Count (52)
            0x81, 0x02, // Input (Data,Var)
            0x85, 0x02, // Report ID (2)
            0x09, 0x23, // Usage (0x23)
            0x95, 0x2F, // Report Count (47)
            0x91, 0x02, // Output (Data,Var)
            0x85, 0x05, // Report ID (5)
            0x09, 0x33, // Usage (0x33)
            0x95, 0x28, // Report Count (40)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x08, // Report ID (8)
            0x09, 0x34, // Usage (0x34)
            0x95, 0x2F, // Report Count (47)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x09, // Report ID (9)
            0x09, 0x24, // Usage (0x24)
            0x95, 0x13, // Report Count (19)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x0A, // Report ID (10)
            0x09, 0x25, // Usage (0x25)
            0x95, 0x1A, // Report Count (26)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x0B, // Report ID (11)
            0x09, 0x41, // Usage (0x41)
            0x95, 0x29, // Report Count (41)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x0C, // Report ID (12)
            0x09, 0x42, // Usage (0x42)
            0x95, 0x29, // Report Count (41)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x20, // Report ID (32)
            0x09, 0x26, // Usage (0x26)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x21, // Report ID (33)
            0x09, 0x27, // Usage (0x27)
            0x95, 0x04, // Report Count (4)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x22, // Report ID (34)
            0x09, 0x40, // Usage (0x40)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x80, // Report ID (128)
            0x09, 0x28, // Usage (0x28)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x81, // Report ID (129)
            0x09, 0x29, // Usage (0x29)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x82, // Report ID (130)
            0x09, 0x2A, // Usage (0x2A)
            0x95, 0x09, // Report Count (9)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x83, // Report ID (131)
            0x09, 0x2B, // Usage (0x2B)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x84, // Report ID (132)
            0x09, 0x2C, // Usage (0x2C)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x85, // Report ID (133)
            0x09, 0x2D, // Usage (0x2D)
            0x95, 0x02, // Report Count (2)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA0, // Report ID (160)
            0x09, 0x2E, // Usage (0x2E)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xE0, // Report ID (224)
            0x09, 0x2F, // Usage (0x2F)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF0, // Report ID (240)
            0x09, 0x30, // Usage (0x30)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF1, // Report ID (241)
            0x09, 0x31, // Usage (0x31)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF2, // Report ID (242)
            0x09, 0x32, // Usage (0x32)
            0x95, 0x0F, // Report Count (15)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF4, // Report ID (244)
            0x09, 0x35, // Usage (0x35)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF5, // Report ID (245)
            0x09, 0x36, // Usage (0x36)
            0x95, 0x03, // Report Count (3)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF6, // Report ID (246)
            0x09, 0x37, // Usage (0x37)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF7, // Report ID (247)
            0x09, 0x38, // Usage (0x38)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF8, // Report ID (248)
            0x09, 0x39, // Usage (0x39)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF9, // Report ID (249)
            0x09, 0x3A, // Usage (0x3A)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0xC0 // End Collection
        ];
    }
}