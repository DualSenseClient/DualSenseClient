using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Emulation.TestSupport;

/// <summary>
/// Synthetic USB descriptor sets shared by the Emulation tests. The simple device is a
/// single-configuration HID device with one interface, interrupt IN and interrupt OUT
/// endpoints, and three strings; later tasks extend this fixture instead of replacing it.
/// </summary>
public static class TestDevices
{
    /// <summary>Vendor id of the simple device (idVendor).</summary>
    public const ushort SimpleIdVendor = 0x1234;

    /// <summary>Product id of the simple device (idProduct).</summary>
    public const ushort SimpleIdProduct = 0x5678;

    /// <summary>Device release number of the simple device (bcdDevice).</summary>
    public const ushort SimpleBcdDevice = 0x0100;

    /// <summary>Configuration value of the simple device (bConfigurationValue).</summary>
    public const byte SimpleConfigurationValue = 0x01;

    /// <summary>Interface class of the simple device's interface (bInterfaceClass; HID).</summary>
    public const byte SimpleInterfaceClass = 0x03;

    /// <summary>Endpoint address of the simple device's interrupt IN endpoint (IN, endpoint 1).</summary>
    public const byte SimpleInterruptInAddress = 0x81;

    /// <summary>Endpoint address of the simple device's interrupt OUT endpoint (OUT, endpoint 2).</summary>
    public const byte SimpleInterruptOutAddress = 0x02;

    /// <summary>Maximum packet size of the interrupt IN endpoint (wMaxPacketSize).</summary>
    public const ushort SimpleInterruptInPacketSize = 64;

    /// <summary>Maximum packet size of the interrupt OUT endpoint (wMaxPacketSize).</summary>
    public const ushort SimpleInterruptOutPacketSize = 32;

    /// <summary>Polling interval of the interrupt IN endpoint (bInterval).</summary>
    public const byte SimpleInterruptInInterval = 4;

    /// <summary>Polling interval of the interrupt OUT endpoint (bInterval).</summary>
    public const byte SimpleInterruptOutInterval = 8;

    /// <summary>Manufacturer string of the simple device (string descriptor index 1).</summary>
    public const string SimpleManufacturer = "Emulation Test Manufacturer";

    /// <summary>Product string of the simple device (string descriptor index 2).</summary>
    public const string SimpleProduct = "Emulation Test Product";

    /// <summary>Serial number string of the simple device (string descriptor index 3).</summary>
    public const string SimpleSerialNumber = "Emulation Test Serial";

    /// <summary>Interface class of the bulk device's interface (bInterfaceClass; vendor specific).</summary>
    public const byte BulkInterfaceClass = 0xFF;

    /// <summary>Endpoint address of the bulk device's bulk IN endpoint (IN, endpoint 1).</summary>
    public const byte BulkInAddress = 0x81;

    /// <summary>Endpoint address of the bulk device's bulk OUT endpoint (OUT, endpoint 2).</summary>
    public const byte BulkOutAddress = 0x02;

    /// <summary>Maximum packet size of the bulk device's endpoints (wMaxPacketSize).</summary>
    public const ushort BulkPacketSize = 64;

    /// <summary>
    /// Creates the simple synthetic device: one configuration with HID interface 0 (interrupt IN
    /// endpoint 0x81, interrupt OUT endpoint 0x02) and the manufacturer, product and serial strings.
    /// </summary>
    /// <returns>The descriptor set of the simple device.</returns>
    public static UsbDescriptorSet CreateSimpleDevice()
    {
        return UsbDescriptorSet.Create(
            CreateSimpleDeviceDescriptor(),
            CreateSimpleConfigDescriptor(),
            CreateSimpleHidReportDescriptor(),
            CreateSimpleStrings());
    }

    /// <summary>
    /// Creates the bulk synthetic device: one configuration with a vendor-specific interface 0
    /// declaring a bulk IN endpoint (0x81) and a bulk OUT endpoint (0x02), and no HID report
    /// descriptor. Used to exercise the immediate (unpaced) transfer paths of the server.
    /// </summary>
    /// <returns>The descriptor set of the bulk device.</returns>
    public static UsbDescriptorSet CreateBulkDevice()
    {
        return UsbDescriptorSet.Create(
            CreateSimpleDeviceDescriptor(),
            CreateBulkConfigDescriptor(),
            null,
            []);
    }

    /// <summary>
    /// Creates the 32-byte configuration descriptor blob of the bulk device: the configuration
    /// descriptor, the vendor-specific interface descriptor and the bulk IN and bulk OUT endpoint
    /// descriptors (USB 2.0, sections 9.6.3 to 9.6.6).
    /// </summary>
    /// <returns>The configuration descriptor bytes.</returns>
    public static byte[] CreateBulkConfigDescriptor()
    {
        int totalLength = 9 + 9 + 7 + 7;

        return
        [
            0x09, // bLength: 9
            0x02, // bDescriptorType: CONFIGURATION
            (byte)totalLength, 0x00, // wTotalLength: 32
            0x01, // bNumInterfaces: 1
            SimpleConfigurationValue, // bConfigurationValue: 1
            0x00, // iConfiguration: none
            0x80, // bmAttributes: bus powered
            0x32, // bMaxPower: 100 mA
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x00, // bInterfaceNumber: 0
            0x00, // bAlternateSetting: 0
            0x02, // bNumEndpoints: 2
            BulkInterfaceClass, // bInterfaceClass: vendor specific
            0x00, // bInterfaceSubClass: none
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            BulkInAddress, // bEndpointAddress: bulk IN, endpoint 1
            0x02, // bmAttributes: bulk
            (byte)BulkPacketSize, 0x00, // wMaxPacketSize: 64
            0x00, // bInterval: ignored for bulk
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            BulkOutAddress, // bEndpointAddress: bulk OUT, endpoint 2
            0x02, // bmAttributes: bulk
            (byte)BulkPacketSize, 0x00, // wMaxPacketSize: 64
            0x00 // bInterval: ignored for bulk
        ];
    }

    /// <summary>Creates the 18-byte device descriptor of the simple device (USB 2.0, section 9.6.1).</summary>
    /// <returns>The device descriptor bytes.</returns>
    public static byte[] CreateSimpleDeviceDescriptor()
    {
        return
        [
            0x12, // bLength: 18
            0x01, // bDescriptorType: DEVICE
            0x00, 0x02, // bcdUSB: USB 2.0
            0x00, // bDeviceClass: defined by the interface
            0x00, // bDeviceSubClass
            0x00, // bDeviceProtocol
            0x40, // bMaxPacketSize0: 64
            0x34, 0x12, // idVendor: 0x1234
            0x78, 0x56, // idProduct: 0x5678
            0x00, 0x01, // bcdDevice: 0x0100
            0x01, // iManufacturer: string index 1
            0x02, // iProduct: string index 2
            0x03, // iSerialNumber: string index 3
            0x01 // bNumConfigurations: 1
        ];
    }

    /// <summary>
    /// Creates the 41-byte configuration descriptor blob of the simple device: the configuration
    /// descriptor, the HID interface descriptor, the HID class descriptor, and the interrupt IN
    /// and interrupt OUT endpoint descriptors (USB 2.0, sections 9.6.3 to 9.6.6).
    /// </summary>
    /// <returns>The configuration descriptor bytes.</returns>
    public static byte[] CreateSimpleConfigDescriptor()
    {
        byte[] reportDescriptor = CreateSimpleHidReportDescriptor();
        int totalLength = 9 + 9 + 9 + 7 + 7;

        return
        [
            0x09, // bLength: 9
            0x02, // bDescriptorType: CONFIGURATION
            (byte)totalLength, 0x00, // wTotalLength: 41
            0x01, // bNumInterfaces: 1
            SimpleConfigurationValue, // bConfigurationValue: 1
            0x00, // iConfiguration: none
            0x80, // bmAttributes: bus powered
            0x32, // bMaxPower: 100 mA
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x00, // bInterfaceNumber: 0
            0x00, // bAlternateSetting: 0
            0x02, // bNumEndpoints: 2
            SimpleInterfaceClass, // bInterfaceClass: HID
            0x00, // bInterfaceSubClass: none
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x09, // bLength: 9
            0x21, // bDescriptorType: HID
            0x11, 0x01, // bcdHID: 1.11
            0x00, // bCountryCode: not localized
            0x01, // bNumDescriptors: 1
            0x22, // bDescriptorType: REPORT
            (byte)reportDescriptor.Length, (byte)(reportDescriptor.Length >> 8), // wDescriptorLength: 7
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            SimpleInterruptInAddress, // bEndpointAddress: interrupt IN, endpoint 1
            0x03, // bmAttributes: interrupt
            (byte)SimpleInterruptInPacketSize, 0x00, // wMaxPacketSize: 64
            SimpleInterruptInInterval, // bInterval: 4
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            SimpleInterruptOutAddress, // bEndpointAddress: interrupt OUT, endpoint 2
            0x03, // bmAttributes: interrupt
            (byte)SimpleInterruptOutPacketSize, 0x00, // wMaxPacketSize: 32
            SimpleInterruptOutInterval // bInterval: 8
        ];
    }

    /// <summary>
    /// Creates the HID report descriptor of the simple device: a minimal joystick application
    /// collection (usage page Generic Desktop, usage Joystick, end collection).
    /// </summary>
    /// <returns>The HID report descriptor bytes.</returns>
    public static byte[] CreateSimpleHidReportDescriptor()
    {
        return
        [
            0x05, 0x01, // Usage Page (Generic Desktop)
            0x09, 0x04, // Usage (Joystick)
            0xA1, 0x01, // Collection (Application)
            0xC0 // End Collection
        ];
    }

    /// <summary>
    /// Creates the string descriptor values of the simple device: manufacturer (index 1),
    /// product (index 2) and serial number (index 3).
    /// </summary>
    /// <returns>The string descriptor values in index order.</returns>
    public static IReadOnlyList<string> CreateSimpleStrings() => [SimpleManufacturer, SimpleProduct, SimpleSerialNumber];
}