using DualSenseClient.Emulation.Core;

// Descriptor data derived from DS4Dongle (MIT), Copyright (c) 2026 awalol
// (src/usb_descriptors.cpp: desc_device, the HID block of descriptor_configuration and desc_hid_report_ds4).
// The DualShock 4 v1 variant is derived from ViGEmBus (BSD-3), Copyright (c) ViGEm project:
// sys/Ds4Pdo.hpp EmulationTargetDS4 constructor defaults (idVendor 0x054C, idProduct 0x05C4),
// sys/Ds4Pdo.cpp UsbGetDeviceDescriptorType (bcdUSB 0x0200, interface-defined class, bMaxPacketSize0 0x40,
// bcdDevice 0x0100, string indexes 1/2/0), GetConfigurationDescriptorType (the 41-byte audio-less
// configuration blob: interface 0, wDescriptorLength 0x01D3) and UsbGetDescriptorFromInterface (the
// 467-byte report descriptor) plus UsbGetStringDescriptorType (index 1 "Sony Computer Entertainment",
// index 2 "Wireless Controller").

namespace DualSenseClient.Emulation.Devices.DualShock4;

/// <summary>
/// Descriptor set of a DualShock 4 variant in its bring-up configuration: one HID interface (interface 0)
/// with an interrupt IN and an interrupt OUT endpoint plus the variant's reference HID report descriptor
/// (507 bytes for <see cref="DualShock4Variant.V2"/>, Sony 054C:09CC, transcribed from DS4Dongle (MIT);
/// 467 bytes for <see cref="DualShock4Variant.V1"/>, Sony 054C:05C4, transcribed from ViGEmBus (BSD-3)).
/// The audio interfaces of the full persona are added in a later milestone.
/// </summary>
public static class DualShock4DescriptorSet
{
    /// <summary>
    /// Product id of the reference DualShock 4 v1 persona (the ViGEmBus target constructor default,
    /// sys/Ds4Pdo.hpp): the v1 variant pins it because the identity's default carries the v2 id.
    /// </summary>
    private const ushort V1ProductId = 0x05C4;

    /// <summary>
    /// Manufacturer string of the reference DualShock 4 v1 persona (string descriptor index 1, the
    /// ViGEmBus UsbGetStringDescriptorType value): the value differs from the v2 reference's string, so
    /// the v1 variant pins it.
    /// </summary>
    private const string V1ManufacturerName = "Sony Computer Entertainment";

    /// <summary>
    /// Creates the descriptor set of the DualShock 4 v2 persona from an identity: the device descriptor
    /// carries the identity's ids and release number, and the string table carries its manufacturer, product
    /// and (when non-empty) serial number strings.
    /// </summary>
    /// <param name="identity">Identity of the persona; use <c>new DualShock4Identity()</c> for the reference defaults.</param>
    /// <returns>The parsed descriptor set served for the device's control and HID requests.</returns>
    public static UsbDescriptorSet Create(DualShock4Identity identity) => Create(identity, DualShock4Variant.V2);

    /// <summary>
    /// Creates the descriptor set of a DualShock 4 variant from an identity. The variant pins the facts
    /// that differ between the two reference personas: <see cref="DualShock4Variant.V2"/> serves the
    /// 507-byte v2 reference report descriptor and takes the product id, manufacturer string, product string
    /// and serial number from the identity (its defaults are the reference v2 values, so
    /// <see cref="Create(DualShock4Identity)"/> keeps serving exactly the v2 bytes), while
    /// <see cref="DualShock4Variant.V1"/> serves the 467-byte v1 reference report descriptor and supersedes
    /// the identity's product id and manufacturer string with the v1 reference values 0x05C4 and
    /// "Sony Computer Entertainment" - the identity's defaults would otherwise serve v2 facts. Every
    /// other fact (vendor id, release number, product string, serial number and the layout of the
    /// bring-up configuration) is identity-driven for both variants, and both variants serve the same
    /// 64-byte input report 0x01 through <see cref="DualShock4Device"/>.
    /// </summary>
    /// <param name="identity">Identity of the persona; use <c>new DualShock4Identity()</c> for the reference defaults.</param>
    /// <param name="variant">Descriptor variant of the persona, which pins the variant-distinguishing reference facts.</param>
    /// <returns>The parsed descriptor set served for the device's control and HID requests.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="variant"/> is not a known <see cref="DualShock4Variant"/> (defensive: the enum is closed, so no caller can express one).</exception>
    public static UsbDescriptorSet Create(DualShock4Identity identity, DualShock4Variant variant)
    {
        ushort idProduct;
        string manufacturer;
        byte[] hidReportDescriptor;
        switch (variant)
        {
            case DualShock4Variant.V1:
                idProduct = V1ProductId;
                manufacturer = V1ManufacturerName;
                hidReportDescriptor = CreateV1HidReportDescriptor();
                break;

            case DualShock4Variant.V2:
                idProduct = identity.IdProduct;
                manufacturer = identity.Manufacturer;
                hidReportDescriptor = CreateV2HidReportDescriptor();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown DualShock 4 variant.");
        }

        List<string> strings = [];
        byte manufacturerIndex = AddString(strings, manufacturer);
        byte productIndex = AddString(strings, identity.Product);
        byte serialIndex = AddString(strings, identity.SerialNumber);

        return UsbDescriptorSet.Create(
            CreateDeviceDescriptor(identity, idProduct, manufacturerIndex, productIndex, serialIndex),
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
    /// per interface (both references' no-serial build), endpoint-0 packet size 64, the identity's vendor id
    /// and release number, the variant's product id and the string descriptor indexes. The two reference
    /// device descriptors agree on every value except the product id.
    /// </summary>
    /// <param name="identity">Identity carrying the vendor id and the release number.</param>
    /// <param name="idProduct">Product id of the variant (0x09CC v2, 0x05C4 v1).</param>
    /// <param name="manufacturerIndex">String descriptor index of the manufacturer, or zero when none is served.</param>
    /// <param name="productIndex">String descriptor index of the product, or zero when none is served.</param>
    /// <param name="serialIndex">String descriptor index of the serial number, or zero when none is served.</param>
    /// <returns>The device descriptor bytes.</returns>
    private static byte[] CreateDeviceDescriptor(DualShock4Identity identity, ushort idProduct, byte manufacturerIndex, byte productIndex,
        byte serialIndex)
    {
        return
        [
            0x12, // bLength: 18
            0x01, // bDescriptorType: DEVICE
            0x00, 0x02, // bcdUSB: USB 2.0
            0x00, // bDeviceClass: defined by the interface
            0x00, // bDeviceSubClass
            0x00, // bDeviceProtocol
            0x40, // bMaxPacketSize0: 64 (CFG_TUD_ENDPOINT0_SIZE in the v2 reference)
            (byte)identity.IdVendor, (byte)(identity.IdVendor >> 8), // idVendor
            (byte)idProduct, (byte)(idProduct >> 8), // idProduct: the variant's reference id (0x09CC v2, 0x05C4 v1)
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
    /// 9.6.6). The two reference blobs agree on every byte except the HID class descriptor's report length,
    /// so both variants serve the same 41 bytes up to it: the DS4Dongle v2 blob is renumbered (interface 3
    /// to 0) and carries its runtime interval override 5, while the ViGEmBus v1 blob already declares
    /// interface 0 and the interval bytes 5.
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
            (byte)totalLength, (byte)(totalLength >> 8), // wTotalLength: 41
            0x01, // bNumInterfaces: 1
            0x01, // bConfigurationValue: 1
            0x00, // iConfiguration: none
            0xC0, // bmAttributes: self-powered, no remote wakeup
            0xFA, // bMaxPower: 500 mA (250 * 2 mA)
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x00, // bInterfaceNumber: 0 (the v2 reference serves the HID interface as 3 alongside the audio function)
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
            (byte)hidReportDescriptorLength, (byte)(hidReportDescriptorLength >> 8), // wDescriptorLength: 507 (v2) or 467 (v1)
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            0x84, // bEndpointAddress: IN EP4
            0x03, // bmAttributes: interrupt
            0x40, 0x00, // wMaxPacketSize: 64
            0x05, // bInterval: 5 -- the reference's runtime override for the stock real DS4 HID interval (the v1 array byte)
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            0x03, // bEndpointAddress: OUT EP3
            0x03, // bmAttributes: interrupt
            0x40, 0x00, // wMaxPacketSize: 64
            0x05 // bInterval: 5 -- the v2 reference's runtime override for the stock real DS4 HID interval (the v1 array byte)
        ];
    }

    /// <summary>
    /// Creates the reference v2 HID report descriptor: the 507-byte gamepad report descriptor of a real
    /// DualShock 4 v2 (report IDs 1, 2, 4, 5, 8, 16-21, 128-137, 144-147, 160-180, 224 and 240-242),
    /// transcribed byte-for-byte from the v2 reference (the reference pins the length at 507).
    /// The 467-byte v1 descriptor is served by <see cref="CreateV1HidReportDescriptor"/> instead.
    /// </summary>
    /// <returns>The HID report descriptor bytes.</returns>
    private static byte[] CreateV2HidReportDescriptor()
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
            0x15, 0x00, // Logical Minimum (0)
            0x26, 0xFF, 0x00, // Logical Maximum (255)
            0x75, 0x08, // Report Size (8)
            0x95, 0x04, // Report Count (4)
            0x81, 0x02, // Input (Data,Var)
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
            0x29, 0x0E, // Usage Maximum (14)
            0x15, 0x00, // Logical Minimum (0)
            0x25, 0x01, // Logical Maximum (1)
            0x75, 0x01, // Report Size (1)
            0x95, 0x0E, // Report Count (14)
            0x81, 0x02, // Input (Data,Var)
            0x06, 0x00, 0xFF, // Usage Page (0xFF00)
            0x09, 0x20, // Usage (0x20)
            0x75, 0x06, // Report Size (6)
            0x95, 0x01, // Report Count (1)
            0x15, 0x00, // Logical Minimum (0)
            0x25, 0x7F, // Logical Maximum (127)
            0x81, 0x02, // Input (Data,Var)
            0x05, 0x01, // Usage Page (Generic Desktop)
            0x09, 0x33, // Usage (Rx)
            0x09, 0x34, // Usage (Ry)
            0x15, 0x00, // Logical Minimum (0)
            0x26, 0xFF, 0x00, // Logical Maximum (255)
            0x75, 0x08, // Report Size (8)
            0x95, 0x02, // Report Count (2)
            0x81, 0x02, // Input (Data,Var)
            0x06, 0x00, 0xFF, // Usage Page (0xFF00)
            0x09, 0x21, // Usage (0x21)
            0x95, 0x36, // Report Count (54)
            0x81, 0x02, // Input (Data,Var)
            0x85, 0x05, // Report ID (5)
            0x09, 0x22, // Usage (0x22)
            0x95, 0x1F, // Report Count (31)
            0x91, 0x02, // Output (Data,Var)
            0x85, 0x04, // Report ID (4)
            0x09, 0x23, // Usage (0x23)
            0x95, 0x24, // Report Count (36)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x02, // Report ID (2)
            0x09, 0x24, // Usage (0x24)
            0x95, 0x24, // Report Count (36)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x08, // Report ID (8)
            0x09, 0x25, // Usage (0x25)
            0x95, 0x03, // Report Count (3)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x10, // Report ID (16)
            0x09, 0x26, // Usage (0x26)
            0x95, 0x04, // Report Count (4)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x11, // Report ID (17)
            0x09, 0x27, // Usage (0x27)
            0x95, 0x02, // Report Count (2)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x12, // Report ID (18)
            0x06, 0x02, 0xFF, // Usage Page (0xFF02)
            0x09, 0x21, // Usage (0x21)
            0x95, 0x0F, // Report Count (15)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x13, // Report ID (19)
            0x09, 0x22, // Usage (0x22)
            0x95, 0x16, // Report Count (22)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x14, // Report ID (20)
            0x06, 0x05, 0xFF, // Usage Page (0xFF05)
            0x09, 0x20, // Usage (0x20)
            0x95, 0x10, // Report Count (16)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x15, // Report ID (21)
            0x09, 0x21, // Usage (0x21)
            0x95, 0x2C, // Report Count (44)
            0xB1, 0x02, // Feature (Data,Var)
            0x06, 0x80, 0xFF, // Usage Page (0xFF80)
            0x85, 0x80, // Report ID (128)
            0x09, 0x20, // Usage (0x20)
            0x95, 0x06, // Report Count (6)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x81, // Report ID (129)
            0x09, 0x21, // Usage (0x21)
            0x95, 0x06, // Report Count (6)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x82, // Report ID (130)
            0x09, 0x22, // Usage (0x22)
            0x95, 0x05, // Report Count (5)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x83, // Report ID (131)
            0x09, 0x23, // Usage (0x23)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x84, // Report ID (132)
            0x09, 0x24, // Usage (0x24)
            0x95, 0x04, // Report Count (4)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x85, // Report ID (133)
            0x09, 0x25, // Usage (0x25)
            0x95, 0x06, // Report Count (6)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x86, // Report ID (134)
            0x09, 0x26, // Usage (0x26)
            0x95, 0x06, // Report Count (6)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x87, // Report ID (135)
            0x09, 0x27, // Usage (0x27)
            0x95, 0x23, // Report Count (35)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x88, // Report ID (136)
            0x09, 0x28, // Usage (0x28)
            0x95, 0x22, // Report Count (34)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x89, // Report ID (137)
            0x09, 0x29, // Usage (0x29)
            0x95, 0x02, // Report Count (2)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x90, // Report ID (144)
            0x09, 0x30, // Usage (0x30)
            0x95, 0x05, // Report Count (5)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x91, // Report ID (145)
            0x09, 0x31, // Usage (0x31)
            0x95, 0x03, // Report Count (3)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x92, // Report ID (146)
            0x09, 0x32, // Usage (0x32)
            0x95, 0x03, // Report Count (3)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x93, // Report ID (147)
            0x09, 0x33, // Usage (0x33)
            0x95, 0x0C, // Report Count (12)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA0, // Report ID (160)
            0x09, 0x40, // Usage (0x40)
            0x95, 0x06, // Report Count (6)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA1, // Report ID (161)
            0x09, 0x41, // Usage (0x41)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA2, // Report ID (162)
            0x09, 0x42, // Usage (0x42)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA3, // Report ID (163)
            0x09, 0x43, // Usage (0x43)
            0x95, 0x30, // Report Count (48)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA4, // Report ID (164)
            0x09, 0x44, // Usage (0x44)
            0x95, 0x0D, // Report Count (13)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA5, // Report ID (165)
            0x09, 0x45, // Usage (0x45)
            0x95, 0x15, // Report Count (21)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA6, // Report ID (166)
            0x09, 0x46, // Usage (0x46)
            0x95, 0x15, // Report Count (21)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF0, // Report ID (240)
            0x09, 0x47, // Usage (0x47)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF1, // Report ID (241)
            0x09, 0x48, // Usage (0x48)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF2, // Report ID (242)
            0x09, 0x49, // Usage (0x49)
            0x95, 0x0F, // Report Count (15)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA7, // Report ID (167)
            0x09, 0x4A, // Usage (0x4A)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA8, // Report ID (168)
            0x09, 0x4B, // Usage (0x4B)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA9, // Report ID (169)
            0x09, 0x4C, // Usage (0x4C)
            0x95, 0x08, // Report Count (8)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xAA, // Report ID (170)
            0x09, 0x4E, // Usage (0x4E)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xAB, // Report ID (171)
            0x09, 0x4F, // Usage (0x4F)
            0x95, 0x39, // Report Count (57)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xAC, // Report ID (172)
            0x09, 0x50, // Usage (0x50)
            0x95, 0x39, // Report Count (57)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xAD, // Report ID (173)
            0x09, 0x51, // Usage (0x51)
            0x95, 0x0B, // Report Count (11)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xAE, // Report ID (174)
            0x09, 0x52, // Usage (0x52)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xAF, // Report ID (175)
            0x09, 0x53, // Usage (0x53)
            0x95, 0x02, // Report Count (2)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xB0, // Report ID (176)
            0x09, 0x54, // Usage (0x54)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xB1, // Report ID (177)
            0x09, 0x55, // Usage (0x55)
            0x95, 0x02, // Report Count (2)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xB2, // Report ID (178)
            0x09, 0x56, // Usage (0x56)
            0x95, 0x02, // Report Count (2)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xE0, // Report ID (224)
            0x09, 0x57, // Usage (0x57)
            0x95, 0x02, // Report Count (2)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xB3, // Report ID (179)
            0x09, 0x55, // Usage (0x55)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xB4, // Report ID (180)
            0x09, 0x55, // Usage (0x55)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0xC0 // End Collection
        ];
    }

    /// <summary>
    /// Creates the reference v1 HID report descriptor: the 467-byte gamepad report descriptor the
    /// v1 reference serves for a DualShock 4 v1 (report IDs 1, 2, 4, 5, 8, 16-21, 128-137,
    /// 144-147, 160-176 and 240-242; the v2 reference descriptor appends the feature blocks 177-180 and
    /// 224 before its End Collection), transcribed byte-for-byte from the v1 reference (its
    /// configuration blob pins the length at wDescriptorLength 0x01D3). The 507-byte v2 descriptor is
    /// served by <see cref="CreateV2HidReportDescriptor"/> instead.
    /// </summary>
    /// <returns>The HID report descriptor bytes.</returns>
    private static byte[] CreateV1HidReportDescriptor()
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
            0x15, 0x00, // Logical Minimum (0)
            0x26, 0xFF, 0x00, // Logical Maximum (255)
            0x75, 0x08, // Report Size (8)
            0x95, 0x04, // Report Count (4)
            0x81, 0x02, // Input (Data,Var)
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
            0x29, 0x0E, // Usage Maximum (14)
            0x15, 0x00, // Logical Minimum (0)
            0x25, 0x01, // Logical Maximum (1)
            0x75, 0x01, // Report Size (1)
            0x95, 0x0E, // Report Count (14)
            0x81, 0x02, // Input (Data,Var)
            0x06, 0x00, 0xFF, // Usage Page (0xFF00)
            0x09, 0x20, // Usage (0x20)
            0x75, 0x06, // Report Size (6)
            0x95, 0x01, // Report Count (1)
            0x15, 0x00, // Logical Minimum (0)
            0x25, 0x7F, // Logical Maximum (127)
            0x81, 0x02, // Input (Data,Var)
            0x05, 0x01, // Usage Page (Generic Desktop)
            0x09, 0x33, // Usage (Rx)
            0x09, 0x34, // Usage (Ry)
            0x15, 0x00, // Logical Minimum (0)
            0x26, 0xFF, 0x00, // Logical Maximum (255)
            0x75, 0x08, // Report Size (8)
            0x95, 0x02, // Report Count (2)
            0x81, 0x02, // Input (Data,Var)
            0x06, 0x00, 0xFF, // Usage Page (0xFF00)
            0x09, 0x21, // Usage (0x21)
            0x95, 0x36, // Report Count (54)
            0x81, 0x02, // Input (Data,Var)
            0x85, 0x05, // Report ID (5)
            0x09, 0x22, // Usage (0x22)
            0x95, 0x1F, // Report Count (31)
            0x91, 0x02, // Output (Data,Var)
            0x85, 0x04, // Report ID (4)
            0x09, 0x23, // Usage (0x23)
            0x95, 0x24, // Report Count (36)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x02, // Report ID (2)
            0x09, 0x24, // Usage (0x24)
            0x95, 0x24, // Report Count (36)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x08, // Report ID (8)
            0x09, 0x25, // Usage (0x25)
            0x95, 0x03, // Report Count (3)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x10, // Report ID (16)
            0x09, 0x26, // Usage (0x26)
            0x95, 0x04, // Report Count (4)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x11, // Report ID (17)
            0x09, 0x27, // Usage (0x27)
            0x95, 0x02, // Report Count (2)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x12, // Report ID (18)
            0x06, 0x02, 0xFF, // Usage Page (0xFF02)
            0x09, 0x21, // Usage (0x21)
            0x95, 0x0F, // Report Count (15)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x13, // Report ID (19)
            0x09, 0x22, // Usage (0x22)
            0x95, 0x16, // Report Count (22)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x14, // Report ID (20)
            0x06, 0x05, 0xFF, // Usage Page (0xFF05)
            0x09, 0x20, // Usage (0x20)
            0x95, 0x10, // Report Count (16)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x15, // Report ID (21)
            0x09, 0x21, // Usage (0x21)
            0x95, 0x2C, // Report Count (44)
            0xB1, 0x02, // Feature (Data,Var)
            0x06, 0x80, 0xFF, // Usage Page (0xFF80)
            0x85, 0x80, // Report ID (128)
            0x09, 0x20, // Usage (0x20)
            0x95, 0x06, // Report Count (6)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x81, // Report ID (129)
            0x09, 0x21, // Usage (0x21)
            0x95, 0x06, // Report Count (6)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x82, // Report ID (130)
            0x09, 0x22, // Usage (0x22)
            0x95, 0x05, // Report Count (5)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x83, // Report ID (131)
            0x09, 0x23, // Usage (0x23)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x84, // Report ID (132)
            0x09, 0x24, // Usage (0x24)
            0x95, 0x04, // Report Count (4)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x85, // Report ID (133)
            0x09, 0x25, // Usage (0x25)
            0x95, 0x06, // Report Count (6)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x86, // Report ID (134)
            0x09, 0x26, // Usage (0x26)
            0x95, 0x06, // Report Count (6)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x87, // Report ID (135)
            0x09, 0x27, // Usage (0x27)
            0x95, 0x23, // Report Count (35)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x88, // Report ID (136)
            0x09, 0x28, // Usage (0x28)
            0x95, 0x22, // Report Count (34)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x89, // Report ID (137)
            0x09, 0x29, // Usage (0x29)
            0x95, 0x02, // Report Count (2)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x90, // Report ID (144)
            0x09, 0x30, // Usage (0x30)
            0x95, 0x05, // Report Count (5)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x91, // Report ID (145)
            0x09, 0x31, // Usage (0x31)
            0x95, 0x03, // Report Count (3)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x92, // Report ID (146)
            0x09, 0x32, // Usage (0x32)
            0x95, 0x03, // Report Count (3)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0x93, // Report ID (147)
            0x09, 0x33, // Usage (0x33)
            0x95, 0x0C, // Report Count (12)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA0, // Report ID (160)
            0x09, 0x40, // Usage (0x40)
            0x95, 0x06, // Report Count (6)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA1, // Report ID (161)
            0x09, 0x41, // Usage (0x41)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA2, // Report ID (162)
            0x09, 0x42, // Usage (0x42)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA3, // Report ID (163)
            0x09, 0x43, // Usage (0x43)
            0x95, 0x30, // Report Count (48)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA4, // Report ID (164)
            0x09, 0x44, // Usage (0x44)
            0x95, 0x0D, // Report Count (13)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA5, // Report ID (165)
            0x09, 0x45, // Usage (0x45)
            0x95, 0x15, // Report Count (21)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA6, // Report ID (166)
            0x09, 0x46, // Usage (0x46)
            0x95, 0x15, // Report Count (21)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF0, // Report ID (240)
            0x09, 0x47, // Usage (0x47)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF1, // Report ID (241)
            0x09, 0x48, // Usage (0x48)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xF2, // Report ID (242)
            0x09, 0x49, // Usage (0x49)
            0x95, 0x0F, // Report Count (15)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA7, // Report ID (167)
            0x09, 0x4A, // Usage (0x4A)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA8, // Report ID (168)
            0x09, 0x4B, // Usage (0x4B)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xA9, // Report ID (169)
            0x09, 0x4C, // Usage (0x4C)
            0x95, 0x08, // Report Count (8)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xAA, // Report ID (170)
            0x09, 0x4E, // Usage (0x4E)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xAB, // Report ID (171)
            0x09, 0x4F, // Usage (0x4F)
            0x95, 0x39, // Report Count (57)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xAC, // Report ID (172)
            0x09, 0x50, // Usage (0x50)
            0x95, 0x39, // Report Count (57)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xAD, // Report ID (173)
            0x09, 0x51, // Usage (0x51)
            0x95, 0x0B, // Report Count (11)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xAE, // Report ID (174)
            0x09, 0x52, // Usage (0x52)
            0x95, 0x01, // Report Count (1)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xAF, // Report ID (175)
            0x09, 0x53, // Usage (0x53)
            0x95, 0x02, // Report Count (2)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xB0, // Report ID (176)
            0x09, 0x54, // Usage (0x54)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0xC0 // End Collection
        ];
    }
}