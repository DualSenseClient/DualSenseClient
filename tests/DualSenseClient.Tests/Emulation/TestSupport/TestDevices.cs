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

    /// <summary>Endpoint address of the alternate-setting device's alternate IN endpoint (IN, endpoint 3).</summary>
    public const byte AlternateInterruptInAddress = 0x83;

    /// <summary>Interface class of the composite device's vendor-specific interface (bInterfaceClass).</summary>
    public const byte CompositeVendorInterfaceClass = 0xFF;

    /// <summary>Interface class of the composite device's audio control interface (bInterfaceClass; audio).</summary>
    public const byte CompositeAudioControlClass = 0x01;

    /// <summary>Interface subclass of the composite device's audio control interface (bInterfaceSubClass; audio control).</summary>
    public const byte CompositeAudioControlSubClass = 0x01;

    /// <summary>Interface class of the composite device's audio streaming interface (bInterfaceClass; audio).</summary>
    public const byte CompositeAudioStreamingClass = 0x01;

    /// <summary>Interface subclass of the composite device's audio streaming interface (bInterfaceSubClass; audio streaming).</summary>
    public const byte CompositeAudioStreamingSubClass = 0x02;

    /// <summary>Interface protocol of the composite device's audio streaming interface (bInterfaceProtocol; IP version 2).</summary>
    public const byte CompositeAudioStreamingProtocol = 0x20;

    /// <summary>Endpoint address of the composite device's bulk IN endpoint (IN, endpoint 1).</summary>
    public const byte CompositeBulkInAddress = 0x81;

    /// <summary>Endpoint address of the composite device's isochronous IN endpoint of the active audio alternate setting (IN, endpoint 3).</summary>
    public const byte CompositeAudioIsoInAddress = 0x83;

    /// <summary>Endpoint address of the composite device's interrupt IN endpoint of the inactive audio alternate setting (IN, endpoint 5).</summary>
    public const byte CompositeInactiveInterruptInAddress = 0x85;

    /// <summary>Endpoint address of the composite device's HID interrupt IN endpoint (IN, endpoint 4).</summary>
    public const byte CompositeHidInterruptInAddress = 0x84;

    /// <summary>Endpoint address of the composite device's HID interrupt OUT endpoint (OUT, endpoint 3).</summary>
    public const byte CompositeHidInterruptOutAddress = 0x03;

    /// <summary>Interface class of the bulk device's interface (bInterfaceClass; vendor specific).</summary>
    public const byte BulkInterfaceClass = 0xFF;

    /// <summary>Endpoint address of the bulk device's bulk IN endpoint (IN, endpoint 1).</summary>
    public const byte BulkInAddress = 0x81;

    /// <summary>Endpoint address of the bulk device's bulk OUT endpoint (OUT, endpoint 2).</summary>
    public const byte BulkOutAddress = 0x02;

    /// <summary>Maximum packet size of the bulk device's endpoints (wMaxPacketSize).</summary>
    public const ushort BulkPacketSize = 64;

    /// <summary>Interface class of the isochronous device's interface (bInterfaceClass; vendor specific).</summary>
    public const byte IsoInterfaceClass = 0xFF;

    /// <summary>Interface number of the isochronous device's interface (bInterfaceNumber).</summary>
    public const byte IsoInterfaceNumber = 0;

    /// <summary>Alternate setting that declares the isochronous device's endpoints (bAlternateSetting).</summary>
    public const byte IsoAlternateSetting = 1;

    /// <summary>Endpoint address of the isochronous device's isochronous OUT endpoint (OUT, endpoint 1).</summary>
    public const byte IsoOutAddress = 0x01;

    /// <summary>Endpoint address of the isochronous device's isochronous IN endpoint (IN, endpoint 2).</summary>
    public const byte IsoInAddress = 0x82;

    /// <summary>Maximum packet size of the isochronous device's OUT endpoint (wMaxPacketSize).</summary>
    public const ushort IsoOutPacketSize = 16;

    /// <summary>Maximum packet size of the isochronous device's IN endpoint (wMaxPacketSize).</summary>
    public const ushort IsoInPacketSize = 8;

    /// <summary>Packet interval of the isochronous device's endpoints (bInterval; one packet per frame).</summary>
    public const byte IsoInterval = 1;

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
    /// Creates the alternate-setting synthetic device: one configuration with interface 0 in
    /// alternate setting 0 (interrupt IN 0x81 and interrupt OUT 0x02) and alternate setting 1
    /// (interrupt IN 0x83), with class-specific descriptors between the alternate endpoint
    /// descriptors that the parser must ignore.
    /// </summary>
    /// <returns>The descriptor set of the alternate-setting device.</returns>
    public static UsbDescriptorSet CreateAlternateSettingDevice()
    {
        return UsbDescriptorSet.Create(
            CreateSimpleDeviceDescriptor(),
            CreateAlternateSettingConfigDescriptor(),
            CreateSimpleHidReportDescriptor(),
            CreateSimpleStrings());
    }

    /// <summary>
    /// Creates the isochronous synthetic device: one configuration with interface 0 declaring no
    /// endpoints in alternate setting 0 and one isochronous OUT endpoint (0x01, 16 bytes) plus one
    /// isochronous IN endpoint (0x82, 8 bytes) in alternate setting 1, without a HID report
    /// descriptor. Used to exercise the server's isochronous dispatch.
    /// </summary>
    /// <returns>The descriptor set of the isochronous device.</returns>
    public static UsbDescriptorSet CreateIsoDevice()
    {
        return UsbDescriptorSet.Create(
            CreateSimpleDeviceDescriptor(),
            CreateIsoConfigDescriptor(),
            null,
            []);
    }

    /// <summary>
    /// Creates the 41-byte configuration descriptor blob of the isochronous device (USB 2.0,
    /// sections 9.6.3 to 9.6.6): interface 0 declares alternate setting 0 with no endpoints and
    /// alternate setting 1 with an isochronous OUT and an isochronous IN endpoint.
    /// </summary>
    /// <returns>The configuration descriptor bytes.</returns>
    public static byte[] CreateIsoConfigDescriptor()
    {
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
            IsoInterfaceNumber, // bInterfaceNumber: 0
            0x00, // bAlternateSetting: 0
            0x00, // bNumEndpoints: 0
            IsoInterfaceClass, // bInterfaceClass: vendor specific
            0x00, // bInterfaceSubClass: none
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            IsoInterfaceNumber, // bInterfaceNumber: 0
            IsoAlternateSetting, // bAlternateSetting: 1
            0x02, // bNumEndpoints: 2
            IsoInterfaceClass, // bInterfaceClass: vendor specific
            0x00, // bInterfaceSubClass: none
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            IsoOutAddress, // bEndpointAddress: isochronous OUT, endpoint 1
            0x01, // bmAttributes: isochronous
            (byte)IsoOutPacketSize, 0x00, // wMaxPacketSize: 16
            IsoInterval, // bInterval: every frame
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            IsoInAddress, // bEndpointAddress: isochronous IN, endpoint 2
            0x01, // bmAttributes: isochronous
            (byte)IsoInPacketSize, 0x00, // wMaxPacketSize: 8
            IsoInterval // bInterval: every frame
        ];
    }

    /// <summary>
    /// Creates the configuration descriptor blob of the alternate-setting device (USB 2.0,
    /// sections 9.6.3 to 9.6.6): interface 0 declares alternate setting 0 with two interrupt
    /// endpoints and alternate setting 1 with one interrupt endpoint, separated by class-specific
    /// descriptors of types 0x24 and 0x25.
    /// </summary>
    /// <returns>The configuration descriptor bytes.</returns>
    public static byte[] CreateAlternateSettingConfigDescriptor()
    {
        byte[] reportDescriptor = CreateSimpleHidReportDescriptor();
        int totalLength = 9 + 9 + 9 + 7 + 7 + 9 + 3 + 3 + 7;

        return
        [
            0x09, // bLength: 9
            0x02, // bDescriptorType: CONFIGURATION
            (byte)totalLength, 0x00, // wTotalLength: 63
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
            SimpleInterruptOutInterval, // bInterval: 8
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x00, // bInterfaceNumber: 0
            0x01, // bAlternateSetting: 1
            0x01, // bNumEndpoints: 1
            SimpleInterfaceClass, // bInterfaceClass: HID
            0x00, // bInterfaceSubClass: none
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x03, // bLength: 3
            0x24, // bDescriptorType: class-specific (audio style)
            0x01, // class-specific payload
            0x03, // bLength: 3
            0x25, // bDescriptorType: class-specific (audio style)
            0x01, // class-specific payload
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            AlternateInterruptInAddress, // bEndpointAddress: interrupt IN, endpoint 3
            0x03, // bmAttributes: interrupt
            (byte)SimpleInterruptInPacketSize, 0x00, // wMaxPacketSize: 64
            SimpleInterruptInInterval // bInterval: 4
        ];
    }

    /// <summary>
    /// Creates the composite synthetic device: four distinct interface numbers, one of them an
    /// audio streaming interface with an inactive alternate setting, plus a HID interface. Used to
    /// pin the handshake interface record count and the alt-0-only pacer start.
    /// </summary>
    /// <returns>The descriptor set of the composite device.</returns>
    public static UsbDescriptorSet CreateCompositeDevice()
    {
        return UsbDescriptorSet.Create(
            CreateSimpleDeviceDescriptor(),
            CreateCompositeConfigDescriptor(),
            CreateSimpleHidReportDescriptor(),
            CreateSimpleStrings());
    }

    /// <summary>
    /// Creates the configuration descriptor blob of the composite device (USB 2.0, sections 9.6.3
    /// to 9.6.6): a vendor-specific interface 0, an audio control interface 1, an audio streaming
    /// interface 2 with alternate setting 0 (no endpoints) and alternate setting 1 (isochronous IN
    /// 0x83 and interrupt IN 0x85), and a HID interface 3 (interrupt IN 0x84 and interrupt OUT
    /// 0x03). bNumInterfaces counts the four distinct interface numbers.
    /// </summary>
    /// <returns>The configuration descriptor bytes.</returns>
    public static byte[] CreateCompositeConfigDescriptor()
    {
        int totalLength = 9 + 9 + 7 + 9 + 9 + 9 + 7 + 7 + 9 + 7 + 7;

        return
        [
            0x09, // bLength: 9
            0x02, // bDescriptorType: CONFIGURATION
            (byte)totalLength, 0x00, // wTotalLength: 89
            0x04, // bNumInterfaces: 4 distinct interface numbers
            SimpleConfigurationValue, // bConfigurationValue: 1
            0x00, // iConfiguration: none
            0x80, // bmAttributes: bus powered
            0x32, // bMaxPower: 100 mA
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x00, // bInterfaceNumber: 0
            0x00, // bAlternateSetting: 0
            0x01, // bNumEndpoints: 1
            CompositeVendorInterfaceClass, // bInterfaceClass: vendor specific
            0x00, // bInterfaceSubClass: none
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            CompositeBulkInAddress, // bEndpointAddress: bulk IN, endpoint 1
            0x02, // bmAttributes: bulk
            (byte)BulkPacketSize, 0x00, // wMaxPacketSize: 64
            0x00, // bInterval: ignored for bulk
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x01, // bInterfaceNumber: 1
            0x00, // bAlternateSetting: 0
            0x00, // bNumEndpoints: 0
            CompositeAudioControlClass, // bInterfaceClass: audio
            CompositeAudioControlSubClass, // bInterfaceSubClass: audio control
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x02, // bInterfaceNumber: 2
            0x00, // bAlternateSetting: 0
            0x00, // bNumEndpoints: 0
            CompositeAudioStreamingClass, // bInterfaceClass: audio
            CompositeAudioStreamingSubClass, // bInterfaceSubClass: audio streaming
            CompositeAudioStreamingProtocol, // bInterfaceProtocol: IP version 2
            0x00, // iInterface: none
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x02, // bInterfaceNumber: 2
            0x01, // bAlternateSetting: 1
            0x02, // bNumEndpoints: 2
            CompositeAudioStreamingClass, // bInterfaceClass: audio
            CompositeAudioStreamingSubClass, // bInterfaceSubClass: audio streaming
            CompositeAudioStreamingProtocol, // bInterfaceProtocol: IP version 2
            0x00, // iInterface: none
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            CompositeAudioIsoInAddress, // bEndpointAddress: isochronous IN, endpoint 3
            0x01, // bmAttributes: isochronous
            0xC0, 0x00, // wMaxPacketSize: 192
            0x01, // bInterval: every frame
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            CompositeInactiveInterruptInAddress, // bEndpointAddress: interrupt IN, endpoint 5
            0x03, // bmAttributes: interrupt
            0x10, 0x00, // wMaxPacketSize: 16
            0x04, // bInterval: 4
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x03, // bInterfaceNumber: 3
            0x00, // bAlternateSetting: 0
            0x02, // bNumEndpoints: 2
            SimpleInterfaceClass, // bInterfaceClass: HID
            0x00, // bInterfaceSubClass: none
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            CompositeHidInterruptInAddress, // bEndpointAddress: interrupt IN, endpoint 4
            0x03, // bmAttributes: interrupt
            (byte)SimpleInterruptInPacketSize, 0x00, // wMaxPacketSize: 64
            SimpleInterruptInInterval, // bInterval: 4
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            CompositeHidInterruptOutAddress, // bEndpointAddress: interrupt OUT, endpoint 3
            0x03, // bmAttributes: interrupt
            (byte)SimpleInterruptOutPacketSize, 0x00, // wMaxPacketSize: 32
            SimpleInterruptOutInterval // bInterval: 8
        ];
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