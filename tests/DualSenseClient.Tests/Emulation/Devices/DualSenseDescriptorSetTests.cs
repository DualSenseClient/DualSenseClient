using System.Buffers.Binary;
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

    /// <summary>Length of the composite configuration descriptor: header + 186-byte audio block + 32-byte HID block.</summary>
    private const int ConfigurationDescriptorLength = 227;

    /// <summary>Size in bytes of the configuration descriptor header (USB 2.0 specification, section 9.6.3).</summary>
    private const int ConfigurationHeaderSize = 9;

    /// <summary>Size in bytes of an interface descriptor (USB 2.0 specification, section 9.6.5).</summary>
    private const int InterfaceDescriptorSize = 9;

    /// <summary>Interface number of the persona's HID function in the composite configuration.</summary>
    private const byte HidInterfaceNumber = 3;

    /// <summary>Interface number of the audio control interface (UAC1 AC).</summary>
    private const byte AudioControlInterfaceNumber = 0;

    /// <summary>Interface number of the speaker audio streaming interface (UAC1 AS OUT).</summary>
    private const byte SpeakerInterfaceNumber = 1;

    /// <summary>Interface number of the microphone audio streaming interface (UAC1 AS IN).</summary>
    private const byte MicrophoneInterfaceNumber = 2;

    /// <summary>Endpoint address of the speaker isochronous OUT endpoint (EP1 OUT).</summary>
    private const byte SpeakerEndpointAddress = 0x01;

    /// <summary>Endpoint address of the microphone isochronous IN endpoint (EP2 IN).</summary>
    private const byte MicrophoneEndpointAddress = 0x82;

    /// <summary>wMaxPacketSize of the speaker endpoint: 48 frames of four 16-bit channels plus eight spare bytes.</summary>
    private const int SpeakerMaxPacketSize = 392;

    /// <summary>wMaxPacketSize of the microphone endpoint: 48 frames of two 16-bit channels plus four spare bytes.</summary>
    private const int MicrophoneMaxPacketSize = 196;

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
    public void Create_Configuration_Matches227ByteGolden()
    {
        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(new DualSenseIdentity());

        // The composite configuration is the 9-byte header (four interfaces), the 186-byte UAC1 audio
        // block and the 32-byte HID function block (interface 3 with the reference's 321-byte report
        // descriptor declaration, endpoints 0x84/0x03 and the runtime interval 2). The expected blob is
        // composed independently from the audio builder's block and a transcribed HID block.
        byte[] audio = UsbAudioClass.CreateDualSenseAudioBlock();
        byte[] hid = CreateExpectedHidBlock(HidReportDescriptorLength);
        byte[] expected = new byte[ConfigurationDescriptorLength];
        expected[0] = 0x09; // bLength
        expected[1] = 0x02; // bDescriptorType: CONFIGURATION
        BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(2), ConfigurationDescriptorLength);
        expected[4] = 0x04; // bNumInterfaces: audio control, audio OUT, audio IN, HID
        expected[5] = 0x01; // bConfigurationValue: 1
        expected[6] = 0x00; // iConfiguration: none
        expected[7] = 0xC0; // bmAttributes: self-powered, no remote wakeup
        expected[8] = 0xFA; // bMaxPower: 500 mA (250 * 2 mA)
        audio.CopyTo(expected, ConfigurationHeaderSize);
        hid.CopyTo(expected, ConfigurationHeaderSize + audio.Length);

        Assert.That(expected, Has.Length.EqualTo(ConfigurationDescriptorLength));
        Assert.That(descriptors.ConfigDescriptor.Length, Is.EqualTo(ConfigurationDescriptorLength));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(descriptors.ConfigDescriptor.Span[2..]), Is.EqualTo((ushort)ConfigurationDescriptorLength));
        Assert.That(descriptors.ConfigDescriptor.Span[4], Is.EqualTo((byte)0x04));
        Assert.That(descriptors.ConfigDescriptor.ToArray(), Is.EqualTo(expected));
    }

    [Test]
    public void Create_HidInterfaceIsNumberThree()
    {
        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(new DualSenseIdentity());

        // The audio function occupies interfaces 0-2, so the HID block keeps the reference's interface 3
        // (the composite builder does not renumber it). The parser records one entry per alternate setting.
        Assert.That(descriptors.Interfaces, Has.Count.EqualTo(6)); // AC 0.0, AS OUT 1.0/1.1, AS IN 2.0/2.1, HID 3.0
        Assert.That(descriptors.InterfaceNumbers, Is.EqualTo(new byte[]
        {
            0, 1, 2, 3
        }));
        Assert.That(descriptors.ConfigDescriptor.Span[4], Is.EqualTo((byte)0x04));
        Assert.That(descriptors.HasInterface(HidInterfaceNumber), Is.True);

        UsbInterfaceInfo interfaceInfo = GetHidInterface(descriptors);
        Assert.That(interfaceInfo.Number, Is.EqualTo(HidInterfaceNumber));
        Assert.That(interfaceInfo.AlternateSetting, Is.Zero);
        Assert.That(interfaceInfo.Class, Is.EqualTo((byte)0x03)); // HID
        Assert.That(interfaceInfo.SubClass, Is.Zero);
        Assert.That(interfaceInfo.Protocol, Is.Zero);
        Assert.That(interfaceInfo.Endpoints, Has.Count.EqualTo(2));
        Assert.That(interfaceInfo.Endpoints[0].Address, Is.EqualTo((byte)0x84)); // IN EP4
        Assert.That(interfaceInfo.Endpoints[0].Type, Is.EqualTo(UsbEndpointType.Interrupt));
        Assert.That(interfaceInfo.Endpoints[0].MaxPacketSize, Is.EqualTo(64));
        Assert.That(interfaceInfo.Endpoints[0].Interval, Is.EqualTo(HidInterruptInterval));
        Assert.That(interfaceInfo.Endpoints[1].Address, Is.EqualTo((byte)0x03)); // OUT EP3
        Assert.That(interfaceInfo.Endpoints[1].Type, Is.EqualTo(UsbEndpointType.Interrupt));
        Assert.That(interfaceInfo.Endpoints[1].MaxPacketSize, Is.EqualTo(64));
        Assert.That(interfaceInfo.Endpoints[1].Interval, Is.EqualTo(HidInterruptInterval));
    }

    [Test]
    public void Create_AudioEndpointsAndAltSettingsMatchReference()
    {
        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(new DualSenseIdentity());

        Assert.That(descriptors.TryGetInterface(AudioControlInterfaceNumber, 0, out UsbInterfaceInfo audioControl), Is.True);
        Assert.That(audioControl.Class, Is.EqualTo((byte)0x01)); // Audio
        Assert.That(audioControl.SubClass, Is.EqualTo((byte)0x01)); // Audio Control
        Assert.That(audioControl.Endpoints, Is.Empty);

        Assert.That(descriptors.GetAlternates(SpeakerInterfaceNumber), Has.Count.EqualTo(2));
        Assert.That(descriptors.TryGetInterface(SpeakerInterfaceNumber, 0, out UsbInterfaceInfo speakerIdle), Is.True);
        Assert.That(speakerIdle.Class, Is.EqualTo((byte)0x01));
        Assert.That(speakerIdle.SubClass, Is.EqualTo((byte)0x02)); // Audio Streaming
        Assert.That(speakerIdle.Endpoints, Is.Empty);

        Assert.That(descriptors.TryGetInterface(SpeakerInterfaceNumber, 1, out UsbInterfaceInfo speakerActive), Is.True);
        Assert.That(speakerActive.Class, Is.EqualTo((byte)0x01));
        Assert.That(speakerActive.SubClass, Is.EqualTo((byte)0x02));
        Assert.That(speakerActive.Endpoints, Has.Count.EqualTo(1));
        Assert.That(speakerActive.Endpoints[0].Address, Is.EqualTo(SpeakerEndpointAddress));
        Assert.That(speakerActive.Endpoints[0].Type, Is.EqualTo(UsbEndpointType.Isochronous));
        Assert.That(speakerActive.Endpoints[0].MaxPacketSize, Is.EqualTo(SpeakerMaxPacketSize));
        Assert.That(speakerActive.Endpoints[0].Interval, Is.EqualTo((byte)1));

        Assert.That(descriptors.GetAlternates(MicrophoneInterfaceNumber), Has.Count.EqualTo(2));
        Assert.That(descriptors.TryGetInterface(MicrophoneInterfaceNumber, 0, out UsbInterfaceInfo microphoneIdle), Is.True);
        Assert.That(microphoneIdle.Class, Is.EqualTo((byte)0x01));
        Assert.That(microphoneIdle.SubClass, Is.EqualTo((byte)0x02));
        Assert.That(microphoneIdle.Endpoints, Is.Empty);

        Assert.That(descriptors.TryGetInterface(MicrophoneInterfaceNumber, 1, out UsbInterfaceInfo microphoneActive), Is.True);
        Assert.That(microphoneActive.Class, Is.EqualTo((byte)0x01));
        Assert.That(microphoneActive.SubClass, Is.EqualTo((byte)0x02));
        Assert.That(microphoneActive.Endpoints, Has.Count.EqualTo(1));
        Assert.That(microphoneActive.Endpoints[0].Address, Is.EqualTo(MicrophoneEndpointAddress));
        Assert.That(microphoneActive.Endpoints[0].Type, Is.EqualTo(UsbEndpointType.Isochronous));
        Assert.That(microphoneActive.Endpoints[0].MaxPacketSize, Is.EqualTo(MicrophoneMaxPacketSize));
        Assert.That(microphoneActive.Endpoints[0].Interval, Is.EqualTo((byte)1));
    }

    [Test]
    public void Create_DefaultIdentity_ParsesInterruptInEndpoint()
    {
        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(new DualSenseIdentity());
        UsbEndpointInfo endpoint = GetHidInterface(descriptors).Endpoints[0];

        Assert.That(endpoint.Address, Is.EqualTo((byte)0x84)); // IN, endpoint 4 (reference)
        Assert.That(endpoint.Type, Is.EqualTo(UsbEndpointType.Interrupt));
        Assert.That(endpoint.MaxPacketSize, Is.EqualTo(64));
        Assert.That(endpoint.Interval, Is.EqualTo(HidInterruptInterval));
    }

    [Test]
    public void Create_DefaultIdentity_ParsesInterruptOutEndpoint()
    {
        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(new DualSenseIdentity());
        UsbEndpointInfo endpoint = GetHidInterface(descriptors).Endpoints[1];

        Assert.That(endpoint.Address, Is.EqualTo((byte)0x03)); // OUT, endpoint 3 (reference)
        Assert.That(endpoint.Type, Is.EqualTo(UsbEndpointType.Interrupt));
        Assert.That(endpoint.MaxPacketSize, Is.EqualTo(64));
        Assert.That(endpoint.Interval, Is.EqualTo(HidInterruptInterval));
    }

    [Test]
    public void Create_HidReportDescriptorUnchanged()
    {
        UsbDescriptorSet descriptors = DualSenseDescriptorSet.Create(new DualSenseIdentity());

        Assert.That(descriptors.HidReportDescriptor, Is.Not.Null);
        ReadOnlyMemory<byte> report = descriptors.HidReportDescriptor.Value;
        Assert.That(report.Length, Is.EqualTo(HidReportDescriptorLength)); // the P3 bytes, not the 437-byte Edge descriptor
        Assert.That(report.Span[0], Is.EqualTo(0x05)); // Usage Page item
        Assert.That(report.Span[1], Is.EqualTo(0x01)); // Usage Page (Generic Desktop)
        Assert.That(report.Span[6], Is.EqualTo(0x85)); // Report ID item
        Assert.That(report.Span[7], Is.EqualTo(0x01)); // Report ID (1): the input report of every persona
        Assert.That(report.Span[^1], Is.EqualTo(0xC0)); // End Collection

        // The HID class descriptor (at the start of the HID block) declares exactly the served report
        // descriptor length; the HID block starts after the header and the audio block.
        ReadOnlySpan<byte> config = descriptors.ConfigDescriptor.Span;
        int hidClassOffset = ConfigurationHeaderSize + UsbAudioClass.CreateDualSenseAudioBlock().Length + InterfaceDescriptorSize;
        Assert.That(config[hidClassOffset], Is.EqualTo((byte)0x09)); // bLength
        Assert.That(config[hidClassOffset + 1], Is.EqualTo((byte)0x21)); // bDescriptorType: HID
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(config[(hidClassOffset + 7)..]), Is.EqualTo((ushort)HidReportDescriptorLength));
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

    /// <summary>
    /// Creates the transcribed 32-byte HID function block of the composite configuration: HID interface
    /// 3 (as the reference serves it alongside the audio function), its class descriptor with the served
    /// report length, and the interrupt IN 0x84 / OUT 0x03 endpoints with the runtime interval 2.
    /// </summary>
    /// <param name="reportLength">Report descriptor length the HID class descriptor declares.</param>
    /// <returns>The HID block bytes.</returns>
    private static byte[] CreateExpectedHidBlock(ushort reportLength)
    {
        return
        [
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            HidInterfaceNumber, // bInterfaceNumber: 3
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
            (byte)reportLength, (byte)(reportLength >> 8), // wDescriptorLength
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            0x84, // bEndpointAddress: IN EP4
            0x03, // bmAttributes: interrupt
            0x40, 0x00, // wMaxPacketSize: 64
            HidInterruptInterval, // bInterval: 2 -- runtime override for the 500 Hz polling mode
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            0x03, // bEndpointAddress: OUT EP3
            0x03, // bmAttributes: interrupt
            0x40, 0x00, // wMaxPacketSize: 64
            HidInterruptInterval // bInterval: 2 -- runtime override for the 500 Hz polling mode
        ];
    }

    /// <summary>Returns the alternate setting 0 interface entry of the HID function.</summary>
    /// <param name="descriptors">Descriptor set to look up.</param>
    /// <returns>The HID interface entry.</returns>
    private static UsbInterfaceInfo GetHidInterface(UsbDescriptorSet descriptors)
    {
        bool found = descriptors.TryGetInterface(HidInterfaceNumber, 0, out UsbInterfaceInfo interfaceInfo);
        Assert.That(found, Is.True);
        return interfaceInfo;
    }
}