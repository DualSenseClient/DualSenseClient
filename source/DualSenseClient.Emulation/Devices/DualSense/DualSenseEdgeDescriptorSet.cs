using DualSenseClient.Emulation.Core;

// Descriptor data of the reference device: the device descriptor, the HID block of the configuration
// descriptor, the 437-byte Edge HID report descriptor and the Edge product string; the Edge path is
// the <c>ds_mode() == false</c> branch.

namespace DualSenseClient.Emulation.Devices.DualSense;

/// <summary>
/// Descriptor set of the DualSense Edge persona (Sony 054C:0DF2) in its bring-up configuration: one HID
/// interface (interface 0) with an interrupt IN and an interrupt OUT endpoint plus the reference 437-byte
/// Edge HID report descriptor. The reference resolves the Edge product id (0x0DF2 instead of 0x0CE6), the
/// HID report descriptor (437 instead of 321 bytes) and the product string ("DualSense Edge Wireless
/// Controller") from the single <c>ds_mode()</c> check, so this factory locks the three together: the Edge
/// product id never travels with the base persona's descriptor or product string. The audio function of the
/// full persona is added in a later milestone. The descriptor bytes are transcribed from the DS5Dongle
/// reference (MIT, Copyright (c) 2026 awalol).
/// </summary>
public static class DualSenseEdgeDescriptorSet
{
    /// <summary>Product id the reference serves in Edge mode (<c>ds_mode()</c> false overwrites idProduct with 0x0DF2).</summary>
    private const ushort EdgeProductId = 0x0DF2;

    /// <summary>Product string the reference serves in Edge mode (string descriptor index 2 when <c>ds_mode()</c> is false).</summary>
    private const string EdgeProductName = "DualSense Edge Wireless Controller";

    /// <summary>
    /// Creates the descriptor set of the DualSense Edge persona from an identity: the device descriptor
    /// carries the Edge product id 0x0DF2 (the identity's <see cref="DualSenseIdentity.IdProduct"/> is
    /// superseded, exactly as the reference resolves the id from its controller mode alone), the string
    /// table carries the identity's manufacturer and (when non-empty) serial number strings plus the
    /// reference's Edge product string, and the HID report descriptor is the 437-byte Edge one.
    /// </summary>
    /// <param name="identity">Identity of the persona; every value is served except the Edge product id and product string, which the persona fixes.</param>
    /// <returns>The parsed descriptor set served for the device's control and HID requests.</returns>
    public static UsbDescriptorSet Create(DualSenseIdentity identity)
    {
        List<string> strings = [];
        byte manufacturerIndex = AddString(strings, identity.Manufacturer);
        byte productIndex = AddString(strings, EdgeProductName);
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
    /// packet size 64, the identity's vendor id and release number, the Edge product id and the string
    /// descriptor indexes.
    /// </summary>
    /// <param name="identity">Identity carrying the vendor id and the release number.</param>
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
            (byte)identity.IdVendor, (byte)(identity.IdVendor >> 8), // idVendor (the reference serves 0x054C)
            (byte)(EdgeProductId & 0xFF), (byte)(EdgeProductId >> 8), // idProduct: 0x0DF2 (the reference's Edge value; the identity's base id is superseded)
            (byte)identity.BcdDevice, (byte)(identity.BcdDevice >> 8), // bcdDevice
            manufacturerIndex, // iManufacturer
            productIndex, // iProduct
            serialIndex, // iSerialNumber: zero when no serial string is served
            0x01 // bNumConfigurations: 1
        ];
    }

    /// <summary>
    /// Creates the 41-byte configuration descriptor blob of the bring-up configuration: the configuration
    /// descriptor, the HID interface (interface 0 because the audio function is omitted), the HID class
    /// descriptor and the interrupt IN and interrupt OUT endpoint descriptors (USB 2.0, sections 9.6.3 to
    /// 9.6.6). Sizes, addresses, attributes and power follow the reference. The HID block deviates from the
    /// reference array in exactly three documented ways: the interface number is renumbered (3 to 0) for the
    /// audio-less bring-up configuration, both endpoint intervals carry the dongle's runtime override instead
    /// of the array bytes, and the HID class descriptor declares the served report length with the Edge
    /// runtime value (0x01B5 for <c>ds_mode()</c> false) instead of the array's base value.
    /// </summary>
    /// <param name="hidReportDescriptorLength">Length in bytes of the served HID report descriptor (wDescriptorLength).</param>
    /// <returns>The configuration descriptor bytes.</returns>
    private static byte[] CreateConfigurationDescriptor(int hidReportDescriptorLength)
    {
        int totalLength = 9 + 9 + 9 + 7 + 7; // 41: header + HID interface + HID class + interrupt IN + interrupt OUT

        return
        [
            0x09, // bLength: 9
            0x02, // bDescriptorType: CONFIGURATION
            (byte)totalLength, (byte)(totalLength >> 8), // wTotalLength: 41 (the reference's base blob covers the audio function too)
            0x01, // bNumInterfaces: 1 (the reference serves 4: the audio function and the HID interface)
            0x01, // bConfigurationValue: 1
            0x00, // iConfiguration: none
            0xC0, // bmAttributes: self-powered, no remote wakeup (the reference's runtime value with wake off)
            0xFA, // bMaxPower: 500 mA (250 * 2 mA)
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x00, // bInterfaceNumber: 0 (the reference serves the HID interface as 3 alongside the audio function)
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
            (byte)hidReportDescriptorLength, (byte)(hidReportDescriptorLength >> 8), // wDescriptorLength: 437 (0x01B5; the base persona declares 321, 0x0141)
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
    /// Creates the reference Edge HID report descriptor: the 437-byte report descriptor the reference serves
    /// for a DualSense Edge (<c>desc_hid_report_dse</c>; the base persona's report IDs plus the Edge profile
    /// and function feature reports 96-101, 104 and 112-123), transcribed byte-for-byte from the
    /// reference (its static_assert pins the length at 437). The base 321-byte descriptor
    /// (<c>desc_hid_report_ds</c>) is served by <see cref="DualSenseDescriptorSet"/> instead.
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
            0x95, 0x3F, // Report Count (63) -- Edge: the base persona serves 47 (0x2F)
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
            0x95, 0x34, // Report Count (52) -- Edge: the base persona serves 15 (0x0F)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF4, // Report ID (244)
            0x09, 0x35, // Usage (0x35)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF5, // Report ID (245)
            0x09, 0x36, // Usage (0x36)
            0x95, 0x03, // Report Count (3)
            0xB1, 0x02, // Feature (Data,Var)
            // The Edge profile and function feature reports appear only in the Edge descriptor (0x60-0x62
            // are the profile writes that trigger the post-save cycle, 0x70-0x7B the profile snapshot
            // reports, 0x65 the firmware echo SET whose payload is the reference's report20).
            // Only the first block restates the Report Count; the following blocks inherit 63.
            0x85, 0x60, // Report ID (96)
            0x09, 0x41, // Usage (0x41)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x61, // Report ID (97)
            0x09, 0x42, // Usage (0x42)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x62, // Report ID (98)
            0x09, 0x43, // Usage (0x43)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x63, // Report ID (99)
            0x09, 0x44, // Usage (0x44)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x64, // Report ID (100)
            0x09, 0x45, // Usage (0x45)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x65, // Report ID (101)
            0x09, 0x46, // Usage (0x46)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x68, // Report ID (104)
            0x09, 0x47, // Usage (0x47)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x70, // Report ID (112)
            0x09, 0x48, // Usage (0x48)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x71, // Report ID (113)
            0x09, 0x49, // Usage (0x49)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x72, // Report ID (114)
            0x09, 0x4A, // Usage (0x4A)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x73, // Report ID (115)
            0x09, 0x4B, // Usage (0x4B)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x74, // Report ID (116)
            0x09, 0x4C, // Usage (0x4C)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x75, // Report ID (117)
            0x09, 0x4D, // Usage (0x4D)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x76, // Report ID (118)
            0x09, 0x4E, // Usage (0x4E)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x77, // Report ID (119)
            0x09, 0x4F, // Usage (0x4F)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x78, // Report ID (120)
            0x09, 0x50, // Usage (0x50)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x79, // Report ID (121)
            0x09, 0x51, // Usage (0x51)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x7A, // Report ID (122)
            0x09, 0x52, // Usage (0x52)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x7B, // Report ID (123)
            0x09, 0x53, // Usage (0x53)
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