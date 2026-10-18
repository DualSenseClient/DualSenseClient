using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Tests.Emulation.Devices;

/// <summary>
/// Descriptor-set tests of the DualShock 4 persona: the v2 goldens (the reference implementation, the
/// existing tests) and the v1 variant facts (the v1 reference). <see cref="DualShock4Variant"/>
/// is a closed enum, so an invalid variant/descriptor combination cannot be constructed and no test
/// asserts one; the variant-dependent behavior is covered by the v1/v2 comparisons below.
/// </summary>
[TestFixture]
public sealed class DualShock4DescriptorSetTests
{
    /// <summary>
    /// Polling interval byte of the HID endpoints: the reference's runtime-effective value 5, which it
    /// overrides into its descriptor array (stock real DS4 v2 HID interval; bInterval array bytes are 0x01).
    /// </summary>
    private const byte HidInterruptInterval = 0x05;

    /// <summary>Length of the reference HID report descriptor (wDescriptorLength 507, pinned by the reference's static_assert).</summary>
    private const int HidReportDescriptorLength = 507;

    /// <summary>
    /// Product id of the reference DualShock 4 v1 persona (the ViGEmBus target constructor default,
    /// sys/Ds4Pdo.hpp); the variant pins it because the identity's default is the v2 id.
    /// </summary>
    private const ushort V1ProductId = 0x05C4;

    /// <summary>
    /// Manufacturer string the ViGEmBus reference serves for the v1 persona (string descriptor index 1,
    /// sys/Ds4Pdo.cpp UsbGetStringDescriptorType); the value differs between the two references.
    /// </summary>
    private const string V1ManufacturerName = "Sony Computer Entertainment";

    /// <summary>
    /// Length of the v1 HID report descriptor: the ViGEmBus configuration blob declares it as
    /// wDescriptorLength 0x01D3 (sys/Ds4Pdo.cpp GetConfigurationDescriptorType).
    /// </summary>
    private const int V1HidReportDescriptorLength = 467;

    [Test]
    public void NewIdentity_Defaults_MatchTheReferenceDescriptorSet()
    {
        DualShock4Identity identity = new DualShock4Identity();

        Assert.That(identity.IdVendor, Is.EqualTo(0x054C));
        Assert.That(identity.IdProduct, Is.EqualTo(0x09CC));
        Assert.That(identity.BcdDevice, Is.EqualTo(0x0100));
        Assert.That(identity.Manufacturer, Is.EqualTo("Sony Interactive Entertainment"));
        Assert.That(identity.Product, Is.EqualTo("Wireless Controller"));
        Assert.That(identity.SerialNumber, Is.Empty);
        Assert.That(identity.MacAddress, Has.Length.EqualTo(6));
        Assert.That(identity.MacAddress, Is.All.Zero);
    }

    [Test]
    public void Create_DefaultIdentity_ParsesDeviceIdentifiers()
    {
        UsbDescriptorSet descriptors = DualShock4DescriptorSet.Create(new DualShock4Identity());

        Assert.That(descriptors.IdVendor, Is.EqualTo(0x054C));
        Assert.That(descriptors.IdProduct, Is.EqualTo(0x09CC));
        Assert.That(descriptors.BcdDevice, Is.EqualTo(0x0100));
        Assert.That(descriptors.DeviceClass, Is.Zero);
        Assert.That(descriptors.NumConfigurations, Is.EqualTo((byte)1));
        Assert.That(descriptors.ConfigurationValue, Is.EqualTo((byte)1));
    }

    [Test]
    public void Create_DefaultIdentity_ParsesSingleHidInterface()
    {
        UsbDescriptorSet descriptors = DualShock4DescriptorSet.Create(new DualShock4Identity());

        // The bring-up configuration omits the audio function, so the HID interface is interface 0.
        Assert.That(descriptors.ConfigDescriptor.Length, Is.EqualTo(41)); // wTotalLength: header + HID block
        Assert.That(descriptors.Interfaces, Has.Count.EqualTo(1));
        UsbInterfaceInfo interfaceInfo = descriptors.Interfaces[0];
        Assert.That(interfaceInfo.Number, Is.Zero);
        Assert.That(interfaceInfo.Class, Is.EqualTo((byte)0x03));
        Assert.That(interfaceInfo.SubClass, Is.Zero);
        Assert.That(interfaceInfo.Protocol, Is.Zero);
        Assert.That(interfaceInfo.Endpoints, Has.Count.EqualTo(2));
    }

    [Test]
    public void Create_DefaultIdentity_ParsesInterruptInEndpoint()
    {
        UsbDescriptorSet descriptors = DualShock4DescriptorSet.Create(new DualShock4Identity());
        UsbEndpointInfo endpoint = descriptors.Interfaces[0].Endpoints[0];

        Assert.That(endpoint.Address, Is.EqualTo((byte)0x84)); // IN, endpoint 4 (reference)
        Assert.That(endpoint.Type, Is.EqualTo(UsbEndpointType.Interrupt));
        Assert.That(endpoint.MaxPacketSize, Is.EqualTo(64));
        Assert.That(endpoint.Interval, Is.EqualTo(HidInterruptInterval));
    }

    [Test]
    public void Create_DefaultIdentity_ParsesInterruptOutEndpoint()
    {
        UsbDescriptorSet descriptors = DualShock4DescriptorSet.Create(new DualShock4Identity());
        UsbEndpointInfo endpoint = descriptors.Interfaces[0].Endpoints[1];

        Assert.That(endpoint.Address, Is.EqualTo((byte)0x03)); // OUT, endpoint 3 (reference)
        Assert.That(endpoint.Type, Is.EqualTo(UsbEndpointType.Interrupt));
        Assert.That(endpoint.MaxPacketSize, Is.EqualTo(64));
        Assert.That(endpoint.Interval, Is.EqualTo(HidInterruptInterval));
    }

    [Test]
    public void Create_DefaultIdentity_ExposesHidReportDescriptor()
    {
        UsbDescriptorSet descriptors = DualShock4DescriptorSet.Create(new DualShock4Identity());

        Assert.That(descriptors.HidReportDescriptor, Is.Not.Null);
        ReadOnlyMemory<byte> report = descriptors.HidReportDescriptor.Value;
        Assert.That(report.Length, Is.EqualTo(HidReportDescriptorLength));
        Assert.That(report.Span[0], Is.EqualTo(0x05)); // Usage Page item
        Assert.That(report.Span[1], Is.EqualTo(0x01)); // Usage Page (Generic Desktop)
        Assert.That(report.Span[^1], Is.EqualTo(0xC0)); // End Collection

        // The HID class descriptor (after the 9-byte configuration header and the 9-byte interface
        // descriptor) must declare exactly the served report descriptor length.
        ReadOnlySpan<byte> config = descriptors.ConfigDescriptor.Span;
        Assert.That(config[18], Is.EqualTo((byte)0x09));
        Assert.That(config[19], Is.EqualTo((byte)0x21));
        Assert.That(config[25] | (config[26] << 8), Is.EqualTo(HidReportDescriptorLength));
    }

    [Test]
    public void Create_DefaultIdentity_ExposesStringsIndexedFromOne()
    {
        UsbDescriptorSet descriptors = DualShock4DescriptorSet.Create(new DualShock4Identity());
        ReadOnlySpan<byte> device = descriptors.DeviceDescriptor.Span;

        Assert.That(device[14], Is.EqualTo((byte)1)); // iManufacturer
        Assert.That(device[15], Is.EqualTo((byte)2)); // iProduct
        Assert.That(device[16], Is.Zero); // iSerialNumber: the reference descriptor set has no serial string
        Assert.That(descriptors.TryGetString(1, out string manufacturer), Is.True);
        Assert.That(manufacturer, Is.EqualTo("Sony Interactive Entertainment"));
        Assert.That(descriptors.TryGetString(2, out string product), Is.True);
        Assert.That(product, Is.EqualTo("Wireless Controller"));
        Assert.That(descriptors.TryGetString(3, out string serial), Is.False);
        Assert.That(serial, Is.Empty);
    }

    [Test]
    public void Create_CustomProductName_ReflectsInStringTable()
    {
        DualShock4Identity identity = new DualShock4Identity
        {
            Product = "Emulation Custom Pad"
        };

        UsbDescriptorSet descriptors = DualShock4DescriptorSet.Create(identity);

        Assert.That(descriptors.DeviceDescriptor.Span[15], Is.EqualTo((byte)2)); // iProduct
        Assert.That(descriptors.TryGetString(2, out string product), Is.True);
        Assert.That(product, Is.EqualTo("Emulation Custom Pad"));
    }

    [Test]
    public void Create_CustomSerial_AddsSerialStringAndDeviceIndex()
    {
        DualShock4Identity identity = new DualShock4Identity
        {
            SerialNumber = "Emulation-0001"
        };

        UsbDescriptorSet descriptors = DualShock4DescriptorSet.Create(identity);

        Assert.That(descriptors.DeviceDescriptor.Span[16], Is.EqualTo((byte)3)); // iSerialNumber
        Assert.That(descriptors.TryGetString(3, out string serial), Is.True);
        Assert.That(serial, Is.EqualTo("Emulation-0001"));
        Assert.That(descriptors.TryGetString(2, out string product), Is.True);
        Assert.That(product, Is.EqualTo("Wireless Controller"));
    }

    [Test]
    public void Create_V1DefaultIdentity_ServesTheV1IdsUnlikeTheV2Set()
    {
        UsbDescriptorSet v1 = DualShock4DescriptorSet.Create(new DualShock4Identity(), DualShock4Variant.V1);
        UsbDescriptorSet v2 = DualShock4DescriptorSet.Create(new DualShock4Identity());

        // The variant pins the served product id: the default identity carries the v2 id 0x09CC, so the
        // v1 persona supersedes it with the v1 reference value 0x05C4.
        Assert.That(v1.IdVendor, Is.EqualTo(0x054C));
        Assert.That(v1.IdProduct, Is.EqualTo(V1ProductId));
        Assert.That(v2.IdProduct, Is.EqualTo(0x09CC));
        Assert.That(v1.IdProduct, Is.Not.EqualTo(v2.IdProduct));
    }

    [Test]
    public void Create_V1DefaultIdentity_DeviceDescriptorMatchesReferenceBytes()
    {
        UsbDescriptorSet descriptors = DualShock4DescriptorSet.Create(new DualShock4Identity(), DualShock4Variant.V1);

        // The v1 reference device descriptor: USB 2.0, class
        // defined per interface, endpoint-0 packet size 64, the v1 ids (the reference's constructor
        // defaults), release 0x0100 and the string indexes 1/2/0.
        Assert.That(descriptors.DeviceDescriptor.ToArray(), Is.EqualTo(new byte[]
        {
            0x12, // bLength: 18
            0x01, // bDescriptorType: DEVICE
            0x00, 0x02, // bcdUSB: USB 2.0
            0x00, // bDeviceClass: defined by the interface
            0x00, // bDeviceSubClass
            0x00, // bDeviceProtocol
            0x40, // bMaxPacketSize0: 64
            0x4C, 0x05, // idVendor: 0x054C (Sony)
            0xC4, 0x05, // idProduct: 0x05C4 (DualShock 4 v1; the v2 persona serves 0x09CC)
            0x00, 0x01, // bcdDevice: 0x0100
            0x01, // iManufacturer
            0x02, // iProduct
            0x00, // iSerialNumber: the v1 reference serves no serial string
            0x01 // bNumConfigurations: 1
        }));
    }

    [Test]
    public void Create_V1DefaultIdentity_ConfigDescriptorMatchesReferenceBytes()
    {
        UsbDescriptorSet descriptors = DualShock4DescriptorSet.Create(new DualShock4Identity(), DualShock4Variant.V1);

        // The ViGEmBus v1 configuration blob (sys/Ds4Pdo.cpp GetConfigurationDescriptorType) is already
        // the audio-less bring-up shape: interface 0, one HID interface and the interrupt IN (EP4) and
        // OUT (EP3) pair with interval 5. It equals the v2 set byte for byte except the HID class
        // descriptor's report length: 0x01D3 = 467 for v1, 0x01FB = 507 for v2.
        Assert.That(descriptors.ConfigDescriptor.ToArray(), Is.EqualTo(new byte[]
        {
            0x09, // bLength: 9
            0x02, // bDescriptorType: CONFIGURATION
            0x29, 0x00, // wTotalLength: 41
            0x01, // bNumInterfaces: 1
            0x01, // bConfigurationValue: 1
            0x00, // iConfiguration: none
            0xC0, // bmAttributes: self-powered, no remote wakeup
            0xFA, // bMaxPower: 500 mA (250 * 2 mA)
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x00, // bInterfaceNumber: 0 (the v1 reference serves interface 0 directly)
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
            0xD3, 0x01, // wDescriptorLength: 467 (the v1 value; the v2 persona declares 0x01FB)
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            0x84, // bEndpointAddress: IN EP4
            0x03, // bmAttributes: interrupt
            0x40, 0x00, // wMaxPacketSize: 64
            0x05, // bInterval: 5 (the v1 array's own byte)
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            0x03, // bEndpointAddress: OUT EP3
            0x03, // bmAttributes: interrupt
            0x40, 0x00, // wMaxPacketSize: 64
            0x05 // bInterval: 5 (the v1 array's own byte)
        }));
    }

    [Test]
    public void Create_V1DefaultIdentity_ExposesTheV1HidReportDescriptor()
    {
        UsbDescriptorSet descriptors = DualShock4DescriptorSet.Create(new DualShock4Identity(), DualShock4Variant.V1);

        Assert.That(descriptors.HidReportDescriptor, Is.Not.Null);
        ReadOnlyMemory<byte> report = descriptors.HidReportDescriptor.Value;
        Assert.That(report.Length, Is.EqualTo(V1HidReportDescriptorLength)); // not the 507-byte v2 report descriptor
        Assert.That(report.Span[0], Is.EqualTo(0x05)); // Usage Page item
        Assert.That(report.Span[1], Is.EqualTo(0x01)); // Usage Page (Generic Desktop)
        Assert.That(report.Span[6], Is.EqualTo(0x85)); // Report ID item
        Assert.That(report.Span[7], Is.EqualTo(0x01)); // Report ID (1): the input report both variants serve
        Assert.That(report.Span[^1], Is.EqualTo(0xC0)); // End Collection

        // The HID class descriptor (after the 9-byte configuration header and the 9-byte interface
        // descriptor) must declare exactly the served report descriptor length.
        ReadOnlySpan<byte> config = descriptors.ConfigDescriptor.Span;
        Assert.That(config[18], Is.EqualTo((byte)0x09));
        Assert.That(config[19], Is.EqualTo((byte)0x21));
        Assert.That(config[25] | (config[26] << 8), Is.EqualTo(V1HidReportDescriptorLength));
    }

    [Test]
    public void Create_V1ReportDescriptor_IsTheV2PrefixWithoutTheV2OnlyTrailingReportBlocks()
    {
        byte[] v1 = DualShock4DescriptorSet.Create(new DualShock4Identity(), DualShock4Variant.V1).HidReportDescriptor!.Value
            .ToArray();
        byte[] v2 = DualShock4DescriptorSet.Create(new DualShock4Identity()).HidReportDescriptor!.Value.ToArray();

        // The v1 array is byte-identical to the v2 array up to the v2-only trailing
        // feature report blocks: the v2 descriptor appends reports 0xB1, 0xB2, 0xE0, 0xB3 and 0xB4
        // (40 bytes) before its End Collection, while the v1 descriptor ends there.
        Assert.That(v2, Has.Length.EqualTo(HidReportDescriptorLength));
        Assert.That(v1, Has.Length.EqualTo(V1HidReportDescriptorLength));
        Assert.That(v1[..466], Is.EqualTo(v2[..466]));
        Assert.That(v1[466], Is.EqualTo(0xC0)); // Report descriptor byte 466: the v1 End Collection
        Assert.That(v2[466..], Is.EqualTo(new byte[]
        {
            0x85, 0xB1, // Report ID (177) -- v2 only
            0x09, 0x55, // Usage (0x55)
            0x95, 0x02, // Report Count (2)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xB2, // Report ID (178) -- v2 only
            0x09, 0x56, // Usage (0x56)
            0x95, 0x02, // Report Count (2)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xE0, // Report ID (224) -- v2 only
            0x09, 0x57, // Usage (0x57)
            0x95, 0x02, // Report Count (2)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xB3, // Report ID (179) -- v2 only
            0x09, 0x55, // Usage (0x55)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0x85, 0xB4, // Report ID (180) -- v2 only
            0x09, 0x55, // Usage (0x55)
            0x95, 0x3F, // Report Count (63)
            0xB1, 0x02, // Feature (Data,Var)
            0xC0 // End Collection
        }));
    }

    [Test]
    public void Create_V1DefaultIdentity_ExposesTheV1ManufacturerString()
    {
        UsbDescriptorSet v1 = DualShock4DescriptorSet.Create(new DualShock4Identity(), DualShock4Variant.V1);
        UsbDescriptorSet v2 = DualShock4DescriptorSet.Create(new DualShock4Identity());

        Assert.That(v1.DeviceDescriptor.Span[14], Is.EqualTo((byte)1)); // iManufacturer
        Assert.That(v1.DeviceDescriptor.Span[15], Is.EqualTo((byte)2)); // iProduct
        Assert.That(v1.DeviceDescriptor.Span[16], Is.Zero); // iSerialNumber: no serial string
        Assert.That(v1.TryGetString(1, out string manufacturer), Is.True);
        Assert.That(manufacturer, Is.EqualTo(V1ManufacturerName)); // the v1 reference's manufacturer string
        Assert.That(v1.TryGetString(2, out string product), Is.True);
        Assert.That(product, Is.EqualTo("Wireless Controller")); // the same product name in both references
        Assert.That(v2.TryGetString(1, out string v2Manufacturer), Is.True);
        Assert.That(v2Manufacturer, Is.EqualTo("Sony Interactive Entertainment")); // the v2 reference value
    }

    [Test]
    public void Create_IdentityInterplay_KeepsTheSharedFactsIdentityDrivenAndPinsTheV1Ones()
    {
        DualShock4Identity identity = new DualShock4Identity
        {
            IdProduct = 0x0999, // a caller-chosen product id: the v2 path serves it as before
            BcdDevice = 0x0111,
            Manufacturer = "Emulation",
            Product = "Emulation V1 Pad",
            SerialNumber = "Emulation-V1-0001"
        };

        UsbDescriptorSet v1 = DualShock4DescriptorSet.Create(identity, DualShock4Variant.V1);
        UsbDescriptorSet v2 = DualShock4DescriptorSet.Create(identity, DualShock4Variant.V2);

        // Facts that do not differ between the two references stay identity-driven: vendor id, release
        // number, product string, serial number (and its string index).
        Assert.That(v1.IdVendor, Is.EqualTo(0x054C));
        Assert.That(v1.BcdDevice, Is.EqualTo(0x0111));
        Assert.That(v1.TryGetString(2, out string product), Is.True);
        Assert.That(product, Is.EqualTo("Emulation V1 Pad"));
        Assert.That(v1.DeviceDescriptor.Span[16], Is.EqualTo((byte)3)); // iSerialNumber
        Assert.That(v1.TryGetString(3, out string serial), Is.True);
        Assert.That(serial, Is.EqualTo("Emulation-V1-0001"));

        // The variant-distinguishing facts are pinned by the variant: the v1 product id and manufacturer
        // string supersede the identity's values (its defaults carry the v2 facts), while the v2 path
        // keeps serving the identity as before.
        Assert.That(v1.IdProduct, Is.EqualTo(V1ProductId));
        Assert.That(v1.TryGetString(1, out string v1Manufacturer), Is.True);
        Assert.That(v1Manufacturer, Is.EqualTo(V1ManufacturerName));
        Assert.That(v2.IdProduct, Is.EqualTo(0x0999)); // byte parity: the v2 path stays identity-driven
        Assert.That(v2.TryGetString(1, out string v2Manufacturer), Is.True);
        Assert.That(v2Manufacturer, Is.EqualTo("Emulation"));
    }

    [Test]
    public void Create_IdentityOverload_IsByteIdenticalToTheExplicitV2Variant()
    {
        DualShock4Identity identity = new DualShock4Identity
        {
            Product = "Emulation Pad",
            SerialNumber = "Emulation-V2-0001"
        };

        UsbDescriptorSet overload = DualShock4DescriptorSet.Create(identity);
        UsbDescriptorSet explicitV2 = DualShock4DescriptorSet.Create(identity, DualShock4Variant.V2);

        Assert.That(overload.DeviceDescriptor.ToArray(), Is.EqualTo(explicitV2.DeviceDescriptor.ToArray()));
        Assert.That(overload.ConfigDescriptor.ToArray(), Is.EqualTo(explicitV2.ConfigDescriptor.ToArray()));
        Assert.That(overload.HidReportDescriptor!.Value.ToArray(), Is.EqualTo(explicitV2.HidReportDescriptor!.Value.ToArray()));
        Assert.That(overload.Interfaces, Has.Count.EqualTo(explicitV2.Interfaces.Count));
    }

    [Test]
    public void NewDevice_DefaultVariant_ServesTheV2DescriptorSet()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());

        Assert.That(device.Descriptors.IdProduct, Is.EqualTo(0x09CC));
        Assert.That(device.Descriptors.HidReportDescriptor!.Value.Length, Is.EqualTo(HidReportDescriptorLength));
    }

    [Test]
    public void NewDevice_V1Variant_ServesTheV1DescriptorSetAndTheSameReportShape()
    {
        DualShock4Device device = new DualShock4Device(new DualShock4Identity(), variant: DualShock4Variant.V1);

        Assert.That(device.Descriptors.IdProduct, Is.EqualTo(V1ProductId));
        Assert.That(device.Descriptors.HidReportDescriptor!.Value.Length, Is.EqualTo(V1HidReportDescriptorLength));

        // The report shape is variant-independent for this milestone: the device serves report id 0x01
        // in the same 64-byte layout for both variants.
        byte[] report = new byte[DualShock4InputReport.Size];
        Assert.That(device.ProvideInputReport(report), Is.EqualTo(DualShock4InputReport.Size));
        Assert.That(report[0], Is.EqualTo(0x01));
    }
}