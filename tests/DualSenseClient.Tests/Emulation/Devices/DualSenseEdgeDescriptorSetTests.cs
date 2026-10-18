using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.DualSense;

namespace DualSenseClient.Tests.Emulation.Devices;

[TestFixture]
public sealed class DualSenseEdgeDescriptorSetTests
{
    /// <summary>Product id of the Edge persona, the reference's non-DS-mode idProduct write.</summary>
    private const ushort EdgeProductId = 0x0DF2;

    /// <summary>Product id of the base DualSense persona, the identity default the Edge factory supersedes.</summary>
    private const ushort BaseProductId = 0x0CE6;

    /// <summary>Product string the reference serves in Edge mode (tud_descriptor_string_cb).</summary>
    private const string EdgeProductName = "DualSense Edge Wireless Controller";

    /// <summary>
    /// Polling interval byte of the HID endpoints: the reference's runtime-effective value 2 for its
    /// default 500 Hz polling mode, which it overrides into its descriptor array (bInterval array bytes are 0x01).
    /// The reference derives it from polling_rate_mode alone, so the Edge path resolves to the same value.
    /// </summary>
    private const byte HidInterruptInterval = 0x02;

    /// <summary>Length of the Edge HID report descriptor (wDescriptorLength 437, pinned by the reference's static_assert).</summary>
    private const int HidReportDescriptorLength = 437;

    /// <summary>Length of the base HID report descriptor, served by the base persona instead.</summary>
    private const int BaseHidReportDescriptorLength = 321;

    /// <summary>Length of the bring-up configuration descriptor: header + HID block (the reference's audio function is omitted).</summary>
    private const int ConfigurationDescriptorLength = 41;

    /// <summary>Index in the base report descriptor of report 2's output Report Count, 0x2F (47) to 0x3F (63) in the Edge descriptor.</summary>
    private const int BaseOutputReportCountIndex = 109;

    /// <summary>Index in the base report descriptor of report 0xF2's feature Report Count, 0x0F (15) to 0x34 (52) in the Edge descriptor.</summary>
    private const int BaseFeatureReportCountIndex = 269;

    /// <summary>
    /// Index in the base report descriptor where the Edge descriptor inserts its 116 bytes of Edge-only
    /// feature reports: right after report 0xF5's attribute, at the start of the report 0xF6 block.
    /// </summary>
    private const int EdgeFeatureReportsIndex = 288;

    /// <summary>Number of bytes the Edge report descriptor inserts at <see cref="EdgeFeatureReportsIndex"/> (437 - 321).</summary>
    private const int EdgeFeatureReportsLength = 116;

    [Test]
    public void Create_DefaultIdentity_ServesTheEdgeProductIdUnlikeTheBaseSet()
    {
        UsbDescriptorSet edge = DualSenseEdgeDescriptorSet.Create(new DualSenseIdentity());
        UsbDescriptorSet dualSense = DualSenseDescriptorSet.Create(new DualSenseIdentity());

        Assert.That(edge.IdProduct, Is.EqualTo(EdgeProductId));
        Assert.That(dualSense.IdProduct, Is.EqualTo(BaseProductId));
        Assert.That(edge.IdProduct, Is.Not.EqualTo(dualSense.IdProduct));
    }

    [Test]
    public void Create_DefaultIdentity_DeviceDescriptorMatchesReferenceBytes()
    {
        UsbDescriptorSet descriptors = DualSenseEdgeDescriptorSet.Create(new DualSenseIdentity());

        // The reference device descriptor of the no-serial build with wake off (the device descriptor
        // plus the runtime writes): USB 2.0, class defined per interface, the
        // Edge ids (the callback resolves idProduct 0x0DF2 whenever ds_mode() is false), release 0x0100 and
        // manufacturer/product string indexes.
        Assert.That(descriptors.DeviceDescriptor.ToArray(), Is.EqualTo(new byte[]
        {
            0x12, // bLength: 18
            0x01, // bDescriptorType: DEVICE
            0x00, 0x02, // bcdUSB: 2.0 (the reference presents 2.1 only with wake on)
            0x00, // bDeviceClass: defined by the interface (the serial build uses the IAD class instead)
            0x00, // bDeviceSubClass
            0x00, // bDeviceProtocol
            0x40, // bMaxPacketSize0: 64 (CFG_TUD_ENDPOINT0_SIZE)
            0x4C, 0x05, // idVendor: 0x054C (Sony)
            0xF2, 0x0D, // idProduct: 0x0DF2 (Edge; the base persona serves 0x0CE6)
            0x00, 0x01, // bcdDevice: 0x0100
            0x01, // iManufacturer
            0x02, // iProduct
            0x00, // iSerialNumber: zero unless the USB-serial option is enabled
            0x01 // bNumConfigurations: 1
        }));
    }

    [Test]
    public void Create_DefaultIdentity_ParsesSingleHidInterface()
    {
        UsbDescriptorSet descriptors = DualSenseEdgeDescriptorSet.Create(new DualSenseIdentity());

        // The bring-up configuration omits the audio function, so the HID interface is interface 0; the
        // configuration is the base one byte for byte (only the HID class descriptor's report length differs).
        Assert.That(descriptors.ConfigDescriptor.Length, Is.EqualTo(ConfigurationDescriptorLength));
        Assert.That(descriptors.Interfaces, Has.Count.EqualTo(1));
        UsbInterfaceInfo interfaceInfo = descriptors.Interfaces[0];
        Assert.That(interfaceInfo.Number, Is.Zero);
        Assert.That(interfaceInfo.Class, Is.EqualTo((byte)0x03));
        Assert.That(interfaceInfo.SubClass, Is.Zero);
        Assert.That(interfaceInfo.Protocol, Is.Zero);
        Assert.That(interfaceInfo.Endpoints, Has.Count.EqualTo(2));
    }

    [Test]
    public void Create_DefaultIdentity_ConfigDescriptorMatchesReferenceBytes()
    {
        UsbDescriptorSet descriptors = DualSenseEdgeDescriptorSet.Create(new DualSenseIdentity());

        // The reference configuration descriptor with the audio function and the runtime keyboard interface
        // omitted (src/usb_descriptors.cpp descriptor_configuration). Exactly three documented deviations from
        // the reference array: the HID interface is renumbered 3 to 0 for the audio-less bring-up
        // configuration, both endpoint intervals carry the dongle's runtime override (2) instead of the array
        // bytes (1), and the HID class descriptor declares the Edge report length (0x01B5, the runtime write
        // for ds_mode() false) instead of the array's base length (0x0141).
        Assert.That(descriptors.ConfigDescriptor.ToArray(), Is.EqualTo(new byte[]
        {
            0x09, // bLength
            0x02, // bDescriptorType: CONFIGURATION
            0x29, 0x00, // wTotalLength: 41 (header + HID block only)
            0x01, // bNumInterfaces: 1 (the audio interfaces are omitted)
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
            0xB5, 0x01, // wDescriptorLength: 437 (the Edge runtime value; the base persona declares 0x0141)
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            0x84, // bEndpointAddress: IN EP4
            0x03, // bmAttributes: interrupt
            0x40, 0x00, // wMaxPacketSize: 64
            0x02, // bInterval: 2 -- runtime override for the default 500 Hz polling mode (array byte 0x01)
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            0x03, // bEndpointAddress: OUT EP3
            0x03, // bmAttributes: interrupt
            0x40, 0x00, // wMaxPacketSize: 64
            0x02 // bInterval: 2 -- runtime override for the default 500 Hz polling mode (array byte 0x01)
        }));
    }

    [Test]
    public void Create_DefaultIdentity_ParsesInterruptInEndpoint()
    {
        UsbDescriptorSet descriptors = DualSenseEdgeDescriptorSet.Create(new DualSenseIdentity());
        UsbEndpointInfo endpoint = descriptors.Interfaces[0].Endpoints[0];

        Assert.That(endpoint.Address, Is.EqualTo((byte)0x84)); // IN, endpoint 4 (reference)
        Assert.That(endpoint.Type, Is.EqualTo(UsbEndpointType.Interrupt));
        Assert.That(endpoint.MaxPacketSize, Is.EqualTo(64));
        Assert.That(endpoint.Interval, Is.EqualTo(HidInterruptInterval));
    }

    [Test]
    public void Create_DefaultIdentity_ParsesInterruptOutEndpoint()
    {
        UsbDescriptorSet descriptors = DualSenseEdgeDescriptorSet.Create(new DualSenseIdentity());
        UsbEndpointInfo endpoint = descriptors.Interfaces[0].Endpoints[1];

        Assert.That(endpoint.Address, Is.EqualTo((byte)0x03)); // OUT, endpoint 3 (reference)
        Assert.That(endpoint.Type, Is.EqualTo(UsbEndpointType.Interrupt));
        Assert.That(endpoint.MaxPacketSize, Is.EqualTo(64));
        Assert.That(endpoint.Interval, Is.EqualTo(HidInterruptInterval));
    }

    [Test]
    public void Create_DefaultIdentity_ExposesTheEdgeHidReportDescriptor()
    {
        UsbDescriptorSet descriptors = DualSenseEdgeDescriptorSet.Create(new DualSenseIdentity());

        Assert.That(descriptors.HidReportDescriptor, Is.Not.Null);
        ReadOnlyMemory<byte> report = descriptors.HidReportDescriptor.Value;
        Assert.That(report.Length, Is.EqualTo(HidReportDescriptorLength)); // not the 321-byte base descriptor (0x0141)
        Assert.That(report.Span[0], Is.EqualTo(0x05)); // Usage Page item
        Assert.That(report.Span[1], Is.EqualTo(0x01)); // Usage Page (Generic Desktop)
        Assert.That(report.Span[6], Is.EqualTo(0x85)); // Report ID item
        Assert.That(report.Span[7], Is.EqualTo(0x01)); // Report ID (1): the input report of every persona
        Assert.That(report.Span[^1], Is.EqualTo(0xC0)); // End Collection

        // The HID class descriptor (after the 9-byte configuration header and the 9-byte interface
        // descriptor) must declare exactly the served report descriptor length.
        ReadOnlySpan<byte> config = descriptors.ConfigDescriptor.Span;
        Assert.That(config[18], Is.EqualTo((byte)0x09));
        Assert.That(config[19], Is.EqualTo((byte)0x21));
        Assert.That(config[25] | (config[26] << 8), Is.EqualTo(HidReportDescriptorLength));
    }

    [Test]
    public void Create_DefaultIdentity_ExposesTheEdgeProductString()
    {
        UsbDescriptorSet descriptors = DualSenseEdgeDescriptorSet.Create(new DualSenseIdentity());
        ReadOnlySpan<byte> device = descriptors.DeviceDescriptor.Span;

        Assert.That(device[14], Is.EqualTo((byte)1)); // iManufacturer
        Assert.That(device[15], Is.EqualTo((byte)2)); // iProduct
        Assert.That(device[16], Is.Zero); // iSerialNumber: the reference serves no serial string by default
        Assert.That(descriptors.TryGetString(1, out string manufacturer), Is.True);
        Assert.That(manufacturer, Is.EqualTo("Sony Interactive Entertainment"));
        Assert.That(descriptors.TryGetString(2, out string product), Is.True);
        Assert.That(product, Is.EqualTo(EdgeProductName)); // the reference's Edge-mode product string
        Assert.That(descriptors.TryGetString(3, out string serial), Is.False);
        Assert.That(serial, Is.Empty);
    }

    [Test]
    public void Create_DefaultIdentity_ReportDescriptorDiffersFromBaseExactlyWhereTheReferenceDiffers()
    {
        byte[] baseReport = DualSenseDescriptorSet.Create(new DualSenseIdentity()).HidReportDescriptor!.Value.ToArray();
        byte[] edgeReport = DualSenseEdgeDescriptorSet.Create(new DualSenseIdentity()).HidReportDescriptor!.Value.ToArray();

        // desc_hid_report_dse differs from desc_hid_report_ds in exactly three places (derived from the
        // reference arrays with comments stripped): the two report counts below are changed in place, and
        // 116 bytes of Edge-only feature reports are inserted at EdgeFeatureReportsIndex. Every other byte
        // of the 321-byte prefix and of the trailing 33 bytes is shared with the base persona.
        Assert.That(baseReport, Has.Length.EqualTo(BaseHidReportDescriptorLength));
        Assert.That(edgeReport, Has.Length.EqualTo(HidReportDescriptorLength));

        for (int i = 0; i < EdgeFeatureReportsIndex; i++)
        {
            if (i == BaseOutputReportCountIndex || i == BaseFeatureReportCountIndex)
            {
                continue; // the two documented in-place count changes, asserted below
            }

            Assert.That(edgeReport[i], Is.EqualTo(baseReport[i]), $"Report descriptor byte {i} must match the base descriptor.");
        }

        Assert.That(baseReport[BaseOutputReportCountIndex], Is.EqualTo(0x2F)); // report 2 output count: 47
        Assert.That(edgeReport[BaseOutputReportCountIndex], Is.EqualTo(0x3F)); // report 2 output count: 63 (Edge)
        Assert.That(baseReport[BaseFeatureReportCountIndex], Is.EqualTo(0x0F)); // report 0xF2 feature count: 15
        Assert.That(edgeReport[BaseFeatureReportCountIndex], Is.EqualTo(0x34)); // report 0xF2 feature count: 52 (Edge)

        // The insertion anchor: the shared suffix starts with the report 0xF6 block in both descriptors.
        Assert.That(baseReport[EdgeFeatureReportsIndex], Is.EqualTo(0x85));
        Assert.That(baseReport[EdgeFeatureReportsIndex + 1], Is.EqualTo(0xF6));

        // The 116 inserted bytes: the Edge-only profile and function feature reports, report IDs 0x60-0x65,
        // 0x68 and 0x70-0x7B. Only the first block restates the Report Count (63); the following blocks keep
        // it, so each of their Feature items still spans 63 bytes.
        Assert.That(edgeReport[EdgeFeatureReportsIndex..(EdgeFeatureReportsIndex + EdgeFeatureReportsLength)], Is.EqualTo(new byte[]
        {
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
            0xB1, 0x02 // Feature (Data,Var)
        }));

        Assert.That(
            edgeReport[(EdgeFeatureReportsIndex + EdgeFeatureReportsLength)..],
            Is.EqualTo(baseReport[EdgeFeatureReportsIndex..]));
    }

    [Test]
    public void Create_NonEdgeIdentity_CannotProduceANonEdgeDescriptorWithTheEdgeProductId()
    {
        // The reference selects the product id, the HID report descriptor and the product string from
        // ds_mode() alone, so the Edge product id 0x0DF2 never travels with the base persona's 321-byte
        // report descriptor or base product string: handing the Edge factory a plain base identity
        // (idProduct 0x0CE6, base product string) must still serve the complete Edge descriptor set.
        DualSenseIdentity baseIdentity = new DualSenseIdentity();
        UsbDescriptorSet edge = DualSenseEdgeDescriptorSet.Create(baseIdentity);
        UsbDescriptorSet dualSense = DualSenseDescriptorSet.Create(baseIdentity);

        Assert.That(baseIdentity.IdProduct, Is.EqualTo(BaseProductId)); // the identity handed over is the base one
        Assert.That(edge.IdProduct, Is.EqualTo(EdgeProductId));
        Assert.That(edge.HidReportDescriptor!.Value.Length, Is.EqualTo(HidReportDescriptorLength));
        Assert.That(edge.HidReportDescriptor!.Value.ToArray(), Is.Not.EqualTo(dualSense.HidReportDescriptor!.Value.ToArray()));

        Assert.That(edge.TryGetString(2, out string product), Is.True);
        Assert.That(product, Is.EqualTo(EdgeProductName));
        Assert.That(product, Is.Not.EqualTo(baseIdentity.Product));

        // The HID class descriptor declares the Edge report length too, never the base 0x0141.
        ReadOnlySpan<byte> config = edge.ConfigDescriptor.Span;
        Assert.That(config[25] | (config[26] << 8), Is.EqualTo(HidReportDescriptorLength));
        Assert.That(
            config[25] | (config[26] << 8),
            Is.Not.EqualTo(dualSense.ConfigDescriptor.Span[25] | (dualSense.ConfigDescriptor.Span[26] << 8)));
    }

    [Test]
    public void Create_CustomSerial_AddsSerialStringAndDeviceIndex()
    {
        DualSenseIdentity identity = new DualSenseIdentity
        {
            SerialNumber = "Emulation-Edge-0001"
        };

        UsbDescriptorSet descriptors = DualSenseEdgeDescriptorSet.Create(identity);

        Assert.That(descriptors.DeviceDescriptor.Span[16], Is.EqualTo((byte)3)); // iSerialNumber
        Assert.That(descriptors.TryGetString(3, out string serial), Is.True);
        Assert.That(serial, Is.EqualTo("Emulation-Edge-0001"));
        Assert.That(descriptors.TryGetString(2, out string product), Is.True);
        Assert.That(product, Is.EqualTo(EdgeProductName));
    }
}