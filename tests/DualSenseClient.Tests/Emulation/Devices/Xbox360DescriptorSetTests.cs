using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.Xbox360;

namespace DualSenseClient.Tests.Emulation.Devices;

/// <summary>
/// Pins the descriptor set of the Xbox 360 wired controller persona against the reference bytes.
/// The data: the 18-byte device descriptor and the reference configuration blob (153 bytes, whose
/// interface 0 is the bring-up configuration); the identity defaults are the vendor 0x045E, product
/// 0x028E and bcdDevice 0x0114. The behavior oracle's descriptor set corroborates
/// the interface block, the two endpoints and the string values. The bring-up configuration keeps
/// the reference's gamepad interface only and drops its interfaces 1-3 (headset audio, chatpad and
/// the 0x13 vendor interface), exactly like the DualShock 4 persona drops its audio function; the
/// M3 composite restores them.
/// </summary>
[TestFixture]
public sealed class Xbox360DescriptorSetTests
{
    /// <summary>Vendor class of the gamepad interface (bInterfaceClass).</summary>
    private const byte VendorClass = 0xFF;

    /// <summary>Gamepad subclass of the interface (bInterfaceSubClass, reference 0x5D).</summary>
    private const byte GamepadSubClass = 0x5D;

    /// <summary>Gamepad protocol of the interface (bInterfaceProtocol, reference 0x01).</summary>
    private const byte GamepadProtocol = 0x01;

    /// <summary>Polling interval of the interrupt IN endpoint (bInterval 4, full-speed frames).</summary>
    private const byte InterruptInInterval = 0x04;

    /// <summary>Polling interval of the interrupt OUT endpoint (bInterval 8, full-speed frames).</summary>
    private const byte InterruptOutInterval = 0x08;

    /// <summary>Maximum packet size of both interrupt endpoints (wMaxPacketSize 32, reference value).</summary>
    private const ushort InterruptMaxPacketSize = 32;

    [Test]
    public void NewIdentity_Defaults_MatchTheReferenceDescriptorSet()
    {
        Xbox360Identity identity = new Xbox360Identity();

        Assert.That(identity.IdVendor, Is.EqualTo(0x045E)); // XUSB identity default
        Assert.That(identity.IdProduct, Is.EqualTo(0x028E)); // XUSB identity default
        Assert.That(identity.BcdDevice, Is.EqualTo(0x0114)); // reference device descriptor
        Assert.That(identity.Manufacturer, Is.EqualTo("©Microsoft Corporation")); // behavior oracle's descriptor set
        Assert.That(identity.Product, Is.EqualTo("Controller")); // real-controller value documented by the behavior oracle
        Assert.That(identity.SerialNumber, Is.EqualTo("296013F")); // behavior oracle's descriptor set (iSerialNumber 3)
    }

    [Test]
    public void Create_DefaultIdentity_WritesTheReferenceDeviceDescriptor()
    {
        UsbDescriptorSet descriptors = Xbox360DescriptorSet.Create(new Xbox360Identity());

        byte[] expected =
        [
            0x12, // bLength: 18
            0x01, // bDescriptorType: DEVICE
            0x00, 0x02, // bcdUSB: USB 2.0
            0xFF, 0xFF, 0xFF, // vendor-specific class/subclass/protocol (reference device descriptor)
            0x08, // bMaxPacketSize0: 8 (reference value)
            0x5E, 0x04, // idVendor: 0x045E
            0x8E, 0x02, // idProduct: 0x028E
            0x14, 0x01, // bcdDevice: 0x0114
            0x01, 0x02, 0x03, // iManufacturer, iProduct, iSerialNumber
            0x01 // bNumConfigurations: 1
        ];

        Assert.That(descriptors.DeviceDescriptor.ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public void Create_DefaultIdentity_ParsesDeviceIdentifiers()
    {
        UsbDescriptorSet descriptors = Xbox360DescriptorSet.Create(new Xbox360Identity());

        Assert.That(descriptors.IdVendor, Is.EqualTo(0x045E));
        Assert.That(descriptors.IdProduct, Is.EqualTo(0x028E));
        Assert.That(descriptors.BcdDevice, Is.EqualTo(0x0114));
        Assert.That(descriptors.DeviceClass, Is.EqualTo(VendorClass));
        Assert.That(descriptors.DeviceSubClass, Is.EqualTo(VendorClass));
        Assert.That(descriptors.DeviceProtocol, Is.EqualTo(VendorClass));
        Assert.That(descriptors.NumConfigurations, Is.EqualTo((byte)1));
        Assert.That(descriptors.ConfigurationValue, Is.EqualTo((byte)1));
        Assert.That(descriptors.HidReportDescriptor, Is.Null); // vendor-class device, not HID
    }

    [Test]
    public void Create_DefaultIdentity_WritesTheReferenceBringUpConfiguration()
    {
        UsbDescriptorSet descriptors = Xbox360DescriptorSet.Create(new Xbox360Identity());

        byte[] expected =
        [
            0x09, 0x02, // CONFIGURATION
            0x31, 0x00, // wTotalLength: 49 (header + interface + class block + IN + OUT)
            0x01, // bNumInterfaces: 1 (the bring-up keeps the gamepad interface; the reference serves 4)
            0x01, // bConfigurationValue
            0x00, // iConfiguration: none
            0xA0, // bmAttributes: remote wakeup (reference value)
            0xFA, // bMaxPower: 500 mA
            0x09, 0x04, // INTERFACE
            0x00, // bInterfaceNumber: 0 (the reference serves the gamepad interface as 0)
            0x00, // bAlternateSetting: 0
            0x02, // bNumEndpoints: 2 (IN + OUT)
            0xFF, 0x5D, 0x01, // bInterfaceClass/SubClass/Protocol: vendor, gamepad 0x5D/0x01
            0x00, // iInterface: none
            0x11, 0x21, // class block: bLength 17, bDescriptorType 0x21 (reference block)
            0x00, 0x01, // bcdHID: 1.00
            0x01, // bCountryCode
            0x25, // bNumDescriptors (reference value)
            0x81, 0x14, 0x00, // descriptor type 0x81, wDescriptorLength 20 (the input packet)
            0x00, 0x00, 0x00, // descriptor type 0x00, wDescriptorLength 0
            0x13, 0x01, 0x08, // descriptor type 0x13, wDescriptorLength 2049
            0x00, 0x00, // descriptor type 0x00 (the reference block is truncated here at 17 bytes)
            0x07, 0x05, 0x81, 0x03, 0x20, 0x00, 0x04, // interrupt IN EP1, 32 bytes, interval 4
            0x07, 0x05, 0x01, 0x03, 0x20, 0x00, 0x08 // interrupt OUT EP1, 32 bytes, interval 8
        ];

        Assert.That(descriptors.ConfigDescriptor.ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public void Create_DefaultIdentity_ParsesSingleVendorInterface()
    {
        UsbDescriptorSet descriptors = Xbox360DescriptorSet.Create(new Xbox360Identity());

        Assert.That(descriptors.ConfigDescriptor.Length, Is.EqualTo(49)); // wTotalLength: header + gamepad interface
        Assert.That(descriptors.Interfaces, Has.Count.EqualTo(1));
        UsbInterfaceInfo interfaceInfo = descriptors.Interfaces[0];
        Assert.That(interfaceInfo.Number, Is.Zero);
        Assert.That(interfaceInfo.Class, Is.EqualTo(VendorClass));
        Assert.That(interfaceInfo.SubClass, Is.EqualTo(GamepadSubClass));
        Assert.That(interfaceInfo.Protocol, Is.EqualTo(GamepadProtocol));
        Assert.That(interfaceInfo.Endpoints, Has.Count.EqualTo(2));
    }

    [Test]
    public void Create_DefaultIdentity_ParsesInterruptInEndpoint()
    {
        UsbDescriptorSet descriptors = Xbox360DescriptorSet.Create(new Xbox360Identity());
        UsbEndpointInfo endpoint = descriptors.Interfaces[0].Endpoints[0];

        Assert.That(endpoint.Address, Is.EqualTo((byte)0x81)); // IN, endpoint 1 (reference)
        Assert.That(endpoint.Type, Is.EqualTo(UsbEndpointType.Interrupt));
        Assert.That(endpoint.MaxPacketSize, Is.EqualTo(InterruptMaxPacketSize));
        Assert.That(endpoint.Interval, Is.EqualTo(InterruptInInterval));
    }

    [Test]
    public void Create_DefaultIdentity_ParsesInterruptOutEndpoint()
    {
        UsbDescriptorSet descriptors = Xbox360DescriptorSet.Create(new Xbox360Identity());
        UsbEndpointInfo endpoint = descriptors.Interfaces[0].Endpoints[1];

        Assert.That(endpoint.Address, Is.EqualTo((byte)0x01)); // OUT, endpoint 1 (reference)
        Assert.That(endpoint.Type, Is.EqualTo(UsbEndpointType.Interrupt));
        Assert.That(endpoint.MaxPacketSize, Is.EqualTo(InterruptMaxPacketSize));
        Assert.That(endpoint.Interval, Is.EqualTo(InterruptOutInterval));
    }

    [Test]
    public void Create_DefaultIdentity_ExposesStringsIndexedFromOne()
    {
        UsbDescriptorSet descriptors = Xbox360DescriptorSet.Create(new Xbox360Identity());
        ReadOnlySpan<byte> device = descriptors.DeviceDescriptor.Span;

        Assert.That(device[14], Is.EqualTo((byte)1)); // iManufacturer
        Assert.That(device[15], Is.EqualTo((byte)2)); // iProduct
        Assert.That(device[16], Is.EqualTo((byte)3)); // iSerialNumber (reference device descriptor pins 3)
        Assert.That(descriptors.TryGetString(1, out string manufacturer), Is.True);
        Assert.That(manufacturer, Is.EqualTo("©Microsoft Corporation"));
        Assert.That(descriptors.TryGetString(2, out string product), Is.True);
        Assert.That(product, Is.EqualTo("Controller"));
        Assert.That(descriptors.TryGetString(3, out string serial), Is.True);
        Assert.That(serial, Is.EqualTo("296013F"));
    }

    [Test]
    public void Create_CustomProductName_ReflectsInStringTable()
    {
        Xbox360Identity identity = new Xbox360Identity
        {
            Product = "Emulation Custom Pad"
        };

        UsbDescriptorSet descriptors = Xbox360DescriptorSet.Create(identity);

        Assert.That(descriptors.DeviceDescriptor.Span[15], Is.EqualTo((byte)2)); // iProduct
        Assert.That(descriptors.TryGetString(2, out string product), Is.True);
        Assert.That(product, Is.EqualTo("Emulation Custom Pad"));
        Assert.That(descriptors.TryGetString(1, out string manufacturer), Is.True);
        Assert.That(manufacturer, Is.EqualTo("©Microsoft Corporation"));
    }

    [Test]
    public void Create_EmptySerial_OmitsSerialStringAndDeviceIndex()
    {
        Xbox360Identity identity = new Xbox360Identity
        {
            SerialNumber = string.Empty
        };

        UsbDescriptorSet descriptors = Xbox360DescriptorSet.Create(identity);

        Assert.That(descriptors.DeviceDescriptor.Span[16], Is.Zero); // iSerialNumber: no serial string served
        Assert.That(descriptors.TryGetString(3, out string serial), Is.False);
        Assert.That(serial, Is.Empty);
    }
}