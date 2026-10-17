using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Tests.Emulation.Devices;

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
}