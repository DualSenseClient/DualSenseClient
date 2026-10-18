using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.DualSense;

namespace DualSenseClient.Tests.Emulation.Devices;

[TestFixture]
public sealed class DualSenseDescriptorSetTests
{
    /// <summary>
    /// Polling interval byte of the HID endpoints: the reference's runtime-effective value 2 for its
    /// default 500 Hz polling mode, which it overrides into its descriptor array (bInterval array bytes are 0x01).
    /// </summary>
    private const byte HidInterruptInterval = 0x02;

    /// <summary>Length of the reference HID report descriptor (wDescriptorLength 321, pinned by the reference's static_assert).</summary>
    private const int HidReportDescriptorLength = 321;

    /// <summary>Length of the bring-up configuration descriptor: header + HID block (the reference's audio function is omitted).</summary>
    private const int ConfigurationDescriptorLength = 41;

    [Test]
    public void NewIdentity_Defaults_MatchTheReferenceDescriptorSet()
    {
        DualSenseIdentity identity = new DualSenseIdentity();

        Assert.That(identity.IdVendor, Is.EqualTo(0x054C));
        Assert.That(identity.IdProduct, Is.EqualTo(0x0CE6));
        Assert.That(identity.BcdDevice, Is.EqualTo(0x0100));
        Assert.That(identity.Manufacturer, Is.EqualTo("Sony Interactive Entertainment"));
        Assert.That(identity.Product, Is.EqualTo("DualSense Wireless Controller"));
        Assert.That(identity.SerialNumber, Is.Empty);
        Assert.That(identity.MacAddress, Has.Length.EqualTo(6));
        Assert.That(identity.MacAddress, Is.All.Zero);
        Assert.That(identity.BatteryLevel, Is.EqualTo((byte)0x0A));
        Assert.That(identity.ChargingState, Is.EqualTo(DualSenseChargingState.Complete));
    }

    [Test]
    public void Create_DefaultIdentity_ParsesDeviceIdentifiers()
    {
        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(new DualSenseIdentity());

        Assert.That(descriptors.IdVendor, Is.EqualTo(0x054C));
        Assert.That(descriptors.IdProduct, Is.EqualTo(0x0CE6));
        Assert.That(descriptors.BcdDevice, Is.EqualTo(0x0100));
        Assert.That(descriptors.DeviceClass, Is.Zero);
        Assert.That(descriptors.NumConfigurations, Is.EqualTo((byte)1));
        Assert.That(descriptors.ConfigurationValue, Is.EqualTo((byte)1));
    }

    [Test]
    public void Create_DefaultIdentity_DeviceDescriptorMatchesReferenceBytes()
    {
        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(new DualSenseIdentity());

        // The reference device descriptor of the no-serial build with wake off (the device descriptor
        // plus the runtime writes): USB 2.0, class defined per interface, the
        // DualSense ids, release 0x0100 and manufacturer/product string indexes.
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
            0xE6, 0x0C, // idProduct: 0x0CE6 (DualSense; the Edge persona serves 0x0DF2)
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
        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(new DualSenseIdentity());

        // The bring-up configuration omits the audio function, so the HID interface is interface 0.
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
        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(new DualSenseIdentity());

        // The reference configuration descriptor with the audio function and the runtime keyboard interface
        // omitted (src/usb_descriptors.cpp descriptor_configuration). Exactly two documented deviations from
        // the reference array: the HID interface is renumbered 3 to 0 for the audio-less bring-up
        // configuration, and both endpoint intervals carry the dongle's runtime override (2) instead of the
        // array bytes (1) -- see the interval ruling in the endpoint tests.
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
            0x41, 0x01, // wDescriptorLength: 321 (the reference's runtime value for the DualSense report descriptor)
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
        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(new DualSenseIdentity());
        UsbEndpointInfo endpoint = descriptors.Interfaces[0].Endpoints[0];

        Assert.That(endpoint.Address, Is.EqualTo((byte)0x84)); // IN, endpoint 4 (reference)
        Assert.That(endpoint.Type, Is.EqualTo(UsbEndpointType.Interrupt));
        Assert.That(endpoint.MaxPacketSize, Is.EqualTo(64));
        Assert.That(endpoint.Interval, Is.EqualTo(HidInterruptInterval));
    }

    [Test]
    public void Create_DefaultIdentity_ParsesInterruptOutEndpoint()
    {
        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(new DualSenseIdentity());
        UsbEndpointInfo endpoint = descriptors.Interfaces[0].Endpoints[1];

        Assert.That(endpoint.Address, Is.EqualTo((byte)0x03)); // OUT, endpoint 3 (reference)
        Assert.That(endpoint.Type, Is.EqualTo(UsbEndpointType.Interrupt));
        Assert.That(endpoint.MaxPacketSize, Is.EqualTo(64));
        Assert.That(endpoint.Interval, Is.EqualTo(HidInterruptInterval));
    }

    [Test]
    public void Create_DefaultIdentity_ExposesHidReportDescriptor()
    {
        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(new DualSenseIdentity());

        Assert.That(descriptors.HidReportDescriptor, Is.Not.Null);
        ReadOnlyMemory<byte> report = descriptors.HidReportDescriptor.Value;
        Assert.That(report.Length, Is.EqualTo(HidReportDescriptorLength)); // not the 437-byte Edge descriptor (0x01B5)
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
    public void Create_DefaultIdentity_ExposesStringsIndexedFromOne()
    {
        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(new DualSenseIdentity());
        ReadOnlySpan<byte> device = descriptors.DeviceDescriptor.Span;

        Assert.That(device[14], Is.EqualTo((byte)1)); // iManufacturer
        Assert.That(device[15], Is.EqualTo((byte)2)); // iProduct
        Assert.That(device[16], Is.Zero); // iSerialNumber: the reference serves no serial string by default
        Assert.That(descriptors.TryGetString(1, out string manufacturer), Is.True);
        Assert.That(manufacturer, Is.EqualTo("Sony Interactive Entertainment"));
        Assert.That(descriptors.TryGetString(2, out string product), Is.True);
        Assert.That(product, Is.EqualTo("DualSense Wireless Controller"));
        Assert.That(descriptors.TryGetString(3, out string serial), Is.False);
        Assert.That(serial, Is.Empty);
    }

    [Test]
    public void Create_CustomProductName_ReflectsInStringTable()
    {
        DualSenseIdentity identity = new DualSenseIdentity
        {
            Product = "Emulation DualSense"
        };

        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(identity);

        Assert.That(descriptors.DeviceDescriptor.Span[15], Is.EqualTo((byte)2)); // iProduct
        Assert.That(descriptors.TryGetString(2, out string product), Is.True);
        Assert.That(product, Is.EqualTo("Emulation DualSense"));
    }

    [Test]
    public void Create_CustomSerial_AddsSerialStringAndDeviceIndex()
    {
        DualSenseIdentity identity = new DualSenseIdentity
        {
            SerialNumber = "Emulation-0001"
        };

        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(identity);

        Assert.That(descriptors.DeviceDescriptor.Span[16], Is.EqualTo((byte)3)); // iSerialNumber
        Assert.That(descriptors.TryGetString(3, out string serial), Is.True);
        Assert.That(serial, Is.EqualTo("Emulation-0001"));
        Assert.That(descriptors.TryGetString(2, out string product), Is.True);
        Assert.That(product, Is.EqualTo("DualSense Wireless Controller"));
    }

    [Test]
    public void Create_CustomMacAddress_LeavesDescriptorSetUnchanged()
    {
        // The reference carries the controller MAC only in its HID feature reports (0x09 pairing info and
        // 0x12) and never in the USB descriptor set, so a custom MAC must not perturb any descriptor byte;
        // the identity retains it for the feature reports that consume it in later milestones.
        DualSenseIdentity identity = new DualSenseIdentity
        {
            MacAddress = [0xA5, 0xFA, 0x9C, 0xCF, 0x92, 0x00]
        };
        DualSenseIdentity defaults = new DualSenseIdentity();

        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(identity);
        UsbDescriptorSet reference = DualSenseDescriptorSet.Create(defaults);

        Assert.That(identity.MacAddress, Is.EqualTo(new byte[]
        {
            0xA5, 0xFA, 0x9C, 0xCF, 0x92, 0x00
        }));
        Assert.That(descriptors.DeviceDescriptor.ToArray(), Is.EqualTo(reference.DeviceDescriptor.ToArray()));
        Assert.That(descriptors.ConfigDescriptor.ToArray(), Is.EqualTo(reference.ConfigDescriptor.ToArray()));
        Assert.That(descriptors.HidReportDescriptor!.Value.ToArray(), Is.EqualTo(reference.HidReportDescriptor!.Value.ToArray()));
    }
}