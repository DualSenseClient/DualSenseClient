using System.Buffers.Binary;
using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Emulation.Core;

[TestFixture]
public sealed class UsbAudioClassTests
{
    [Test]
    public void CreateDualSenseAudioBlock_MatchesReferenceBytes()
    {
        byte[] block = UsbAudioClass.CreateDualSenseAudioBlock();

        Assert.That(block, Has.Length.EqualTo(186));
        Assert.That(block, Is.EqualTo(ExpectedDualSenseAudioBlock()));

        // AC interface (0.0): interface 0, alternate 0, no endpoints, Audio/Audio Control.
        Assert.That(block[0], Is.EqualTo((byte)0x09)); // bLength
        Assert.That(block[1], Is.EqualTo((byte)0x04)); // bDescriptorType: INTERFACE
        Assert.That(block[2], Is.EqualTo((byte)0x00)); // bInterfaceNumber: 0
        Assert.That(block[3], Is.EqualTo((byte)0x00)); // bAlternateSetting: 0
        Assert.That(block[4], Is.EqualTo((byte)0x00)); // bNumEndpoints: 0
        Assert.That(block[5], Is.EqualTo((byte)0x01)); // bInterfaceClass: Audio
        Assert.That(block[6], Is.EqualTo((byte)0x01)); // bInterfaceSubClass: Audio Control

        // Class-specific AC interface header at offset 9: wTotalLength 73 (0x0049) covers the
        // header plus the terminal and unit descriptors up to the first streaming interface.
        Assert.That(block[9], Is.EqualTo((byte)0x0A)); // bLength
        Assert.That(block[10], Is.EqualTo((byte)0x24)); // bDescriptorType: CS_INTERFACE
        Assert.That(block[11], Is.EqualTo((byte)0x01)); // bDescriptorSubtype: Header
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(14)), Is.EqualTo((ushort)0x0049));
        Assert.That(block[16], Is.EqualTo((byte)0x02)); // bInCollection: 2 streaming interfaces

        // The reference's 9-byte feature unit quirk is transcribed verbatim: the microphone input
        // terminal (offset 52) declares 2 channels, but feature unit 5 (offset 64) is the 9-byte
        // descriptor that only fits one per-channel control byte plus iFeature.
        Assert.That(block[59], Is.EqualTo((byte)0x02)); // input terminal 4: bNrChannels
        Assert.That(block[64], Is.EqualTo((byte)0x09)); // feature unit 5: bLength

        // AS OUT (interface 1): alt 0 declares no endpoints, alt 1 declares one.
        Assert.That(block[82], Is.EqualTo((byte)0x09)); // alt 0 descriptor
        Assert.That(block[84], Is.EqualTo((byte)0x01)); // bInterfaceNumber: 1
        Assert.That(block[85], Is.EqualTo((byte)0x00)); // bAlternateSetting: 0
        Assert.That(block[91], Is.EqualTo((byte)0x09)); // alt 1 descriptor
        Assert.That(block[93], Is.EqualTo((byte)0x01)); // bInterfaceNumber: 1
        Assert.That(block[94], Is.EqualTo((byte)0x01)); // bAlternateSetting: 1
        Assert.That(block[95], Is.EqualTo((byte)0x01)); // bNumEndpoints: 1
        Assert.That(block[96], Is.EqualTo((byte)0x01)); // bInterfaceClass: Audio
        Assert.That(block[97], Is.EqualTo((byte)0x02)); // bInterfaceSubClass: Audio Streaming

        // Speaker format (offset 107): 4 channels, 16-bit, 48 kHz.
        Assert.That(block[107], Is.EqualTo((byte)0x0B)); // bLength
        Assert.That(block[111], Is.EqualTo((byte)0x04)); // bNrChannels: 4
        Assert.That(block[112], Is.EqualTo((byte)0x02)); // bSubframeSize: 2 bytes per sample
        Assert.That(block[113], Is.EqualTo((byte)0x10)); // bBitResolution: 16 bits
        Assert.That(block[114], Is.EqualTo((byte)0x01)); // bSamFreqType: one discrete frequency
        Assert.That(ReadSampleFrequency(block, 115), Is.EqualTo(48000));

        // Speaker endpoint (offset 118): isochronous OUT EP1, 392 bytes per packet, every frame.
        Assert.That(block[118], Is.EqualTo((byte)0x09)); // bLength
        Assert.That(block[119], Is.EqualTo((byte)0x05)); // bDescriptorType: ENDPOINT
        Assert.That(block[120], Is.EqualTo((byte)0x01)); // bEndpointAddress: OUT EP1
        Assert.That(block[121], Is.EqualTo((byte)0x09)); // bmAttributes: isochronous, adaptive
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(122)), Is.EqualTo((ushort)392));
        Assert.That(block[124], Is.EqualTo((byte)0x01)); // bInterval: 1 frame

        // AS IN (interface 2): alt 0 declares no endpoints, alt 1 declares one.
        Assert.That(block[134], Is.EqualTo((byte)0x09)); // alt 0 descriptor
        Assert.That(block[136], Is.EqualTo((byte)0x02)); // bInterfaceNumber: 2
        Assert.That(block[137], Is.EqualTo((byte)0x00)); // bAlternateSetting: 0
        Assert.That(block[143], Is.EqualTo((byte)0x09)); // alt 1 descriptor
        Assert.That(block[145], Is.EqualTo((byte)0x02)); // bInterfaceNumber: 2
        Assert.That(block[146], Is.EqualTo((byte)0x01)); // bAlternateSetting: 1
        Assert.That(block[147], Is.EqualTo((byte)0x01)); // bNumEndpoints: 1

        // Microphone format (offset 159): 2 channels, 16-bit, 48 kHz.
        Assert.That(block[159], Is.EqualTo((byte)0x0B)); // bLength
        Assert.That(block[163], Is.EqualTo((byte)0x02)); // bNrChannels: 2
        Assert.That(block[164], Is.EqualTo((byte)0x02)); // bSubframeSize: 2 bytes per sample
        Assert.That(block[165], Is.EqualTo((byte)0x10)); // bBitResolution: 16 bits
        Assert.That(block[166], Is.EqualTo((byte)0x01)); // bSamFreqType: one discrete frequency
        Assert.That(ReadSampleFrequency(block, 167), Is.EqualTo(48000));

        // Microphone endpoint (offset 170): isochronous IN EP2, 196 bytes per packet, every frame.
        Assert.That(block[170], Is.EqualTo((byte)0x09)); // bLength
        Assert.That(block[171], Is.EqualTo((byte)0x05)); // bDescriptorType: ENDPOINT
        Assert.That(block[172], Is.EqualTo((byte)0x82)); // bEndpointAddress: IN EP2
        Assert.That(block[173], Is.EqualTo((byte)0x05)); // bmAttributes: isochronous, asynchronous
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(174)), Is.EqualTo((ushort)196));
        Assert.That(block[176], Is.EqualTo((byte)0x01)); // bInterval: 1 frame
    }

    [Test]
    public void CreateDualShock4AudioBlock_MatchesReferenceBytes()
    {
        byte[] block = UsbAudioClass.CreateDualShock4AudioBlock();

        Assert.That(block, Has.Length.EqualTo(184));
        Assert.That(block, Is.EqualTo(ExpectedDualShock4AudioBlock()));

        // AC interface (0.0): interface 0, alternate 0, no endpoints, Audio/Audio Control.
        Assert.That(block[0], Is.EqualTo((byte)0x09)); // bLength
        Assert.That(block[2], Is.EqualTo((byte)0x00)); // bInterfaceNumber: 0
        Assert.That(block[3], Is.EqualTo((byte)0x00)); // bAlternateSetting: 0
        Assert.That(block[5], Is.EqualTo((byte)0x01)); // bInterfaceClass: Audio
        Assert.That(block[6], Is.EqualTo((byte)0x01)); // bInterfaceSubClass: Audio Control

        // Class-specific AC interface header at offset 9: wTotalLength 71 (0x0047).
        Assert.That(block[9], Is.EqualTo((byte)0x0A)); // bLength
        Assert.That(block[10], Is.EqualTo((byte)0x24)); // bDescriptorType: CS_INTERFACE
        Assert.That(block[11], Is.EqualTo((byte)0x01)); // bDescriptorSubtype: Header
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(14)), Is.EqualTo((ushort)0x0047));
        Assert.That(block[16], Is.EqualTo((byte)0x02)); // bInCollection: 2 streaming interfaces

        // AS OUT (interface 1): alt 0 declares no endpoints, alt 1 declares one.
        Assert.That(block[80], Is.EqualTo((byte)0x09)); // alt 0 descriptor
        Assert.That(block[82], Is.EqualTo((byte)0x01)); // bInterfaceNumber: 1
        Assert.That(block[83], Is.EqualTo((byte)0x00)); // bAlternateSetting: 0
        Assert.That(block[89], Is.EqualTo((byte)0x09)); // alt 1 descriptor
        Assert.That(block[91], Is.EqualTo((byte)0x01)); // bInterfaceNumber: 1
        Assert.That(block[92], Is.EqualTo((byte)0x01)); // bAlternateSetting: 1
        Assert.That(block[93], Is.EqualTo((byte)0x01)); // bNumEndpoints: 1
        Assert.That(block[94], Is.EqualTo((byte)0x01)); // bInterfaceClass: Audio
        Assert.That(block[95], Is.EqualTo((byte)0x02)); // bInterfaceSubClass: Audio Streaming

        // Speaker format (offset 105): 2 channels, 16-bit, 32 kHz.
        Assert.That(block[105], Is.EqualTo((byte)0x0B)); // bLength
        Assert.That(block[109], Is.EqualTo((byte)0x02)); // bNrChannels: 2
        Assert.That(block[110], Is.EqualTo((byte)0x02)); // bSubframeSize: 2 bytes per sample
        Assert.That(block[111], Is.EqualTo((byte)0x10)); // bBitResolution: 16 bits
        Assert.That(block[112], Is.EqualTo((byte)0x01)); // bSamFreqType: one discrete frequency
        Assert.That(ReadSampleFrequency(block, 113), Is.EqualTo(32000));

        // Speaker endpoint (offset 116): isochronous OUT EP1, 132 bytes per packet, every frame.
        Assert.That(block[116], Is.EqualTo((byte)0x09)); // bLength
        Assert.That(block[117], Is.EqualTo((byte)0x05)); // bDescriptorType: ENDPOINT
        Assert.That(block[118], Is.EqualTo((byte)0x01)); // bEndpointAddress: OUT EP1
        Assert.That(block[119], Is.EqualTo((byte)0x09)); // bmAttributes: isochronous, adaptive
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(120)), Is.EqualTo((ushort)132));
        Assert.That(block[122], Is.EqualTo((byte)0x01)); // bInterval: 1 frame

        // AS IN (interface 2): alt 0 declares no endpoints, alt 1 declares one.
        Assert.That(block[132], Is.EqualTo((byte)0x09)); // alt 0 descriptor
        Assert.That(block[134], Is.EqualTo((byte)0x02)); // bInterfaceNumber: 2
        Assert.That(block[135], Is.EqualTo((byte)0x00)); // bAlternateSetting: 0
        Assert.That(block[141], Is.EqualTo((byte)0x09)); // alt 1 descriptor
        Assert.That(block[143], Is.EqualTo((byte)0x02)); // bInterfaceNumber: 2
        Assert.That(block[144], Is.EqualTo((byte)0x01)); // bAlternateSetting: 1
        Assert.That(block[145], Is.EqualTo((byte)0x01)); // bNumEndpoints: 1

        // Microphone format (offset 157): 1 channel, 16-bit, 16 kHz.
        Assert.That(block[157], Is.EqualTo((byte)0x0B)); // bLength
        Assert.That(block[161], Is.EqualTo((byte)0x01)); // bNrChannels: 1
        Assert.That(block[162], Is.EqualTo((byte)0x02)); // bSubframeSize: 2 bytes per sample
        Assert.That(block[163], Is.EqualTo((byte)0x10)); // bBitResolution: 16 bits
        Assert.That(block[164], Is.EqualTo((byte)0x01)); // bSamFreqType: one discrete frequency
        Assert.That(ReadSampleFrequency(block, 165), Is.EqualTo(16000));

        // Microphone endpoint (offset 168): isochronous IN EP2, 34 bytes per packet, every frame.
        Assert.That(block[168], Is.EqualTo((byte)0x09)); // bLength
        Assert.That(block[169], Is.EqualTo((byte)0x05)); // bDescriptorType: ENDPOINT
        Assert.That(block[170], Is.EqualTo((byte)0x82)); // bEndpointAddress: IN EP2
        Assert.That(block[171], Is.EqualTo((byte)0x05)); // bmAttributes: isochronous, asynchronous
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(172)), Is.EqualTo((ushort)34));
        Assert.That(block[174], Is.EqualTo((byte)0x01)); // bInterval: 1 frame
    }

    [Test]
    public void CreateDualShock4AudioBlock_MatchesReferenceStructure()
    {
        byte[] block = UsbAudioClass.CreateDualShock4AudioBlock();

        // Both AC paths use the correct descriptor lengths: 10-byte feature unit 2 for the
        // 2-channel speaker, 9-byte feature unit 5 for the 1-channel microphone.
        Assert.That(block[31], Is.EqualTo((byte)0x0A)); // feature unit 2: bLength
        Assert.That(block[26], Is.EqualTo((byte)0x02)); // input terminal 1: bNrChannels
        Assert.That(block[57], Is.EqualTo((byte)0x01)); // input terminal 4: bNrChannels
        Assert.That(block[62], Is.EqualTo((byte)0x09)); // feature unit 5: bLength
    }

    [Test]
    public void CreateCompositeConfiguration_DualSenseTotals227AndFourInterfaces()
    {
        byte[] audio = UsbAudioClass.CreateDualSenseAudioBlock();
        byte[] hid = CreateMinimalHidBlock();

        byte[] composite = UsbAudioClass.CreateCompositeConfiguration(audio, hid);

        Assert.That(composite, Has.Length.EqualTo(227));
        Assert.That(composite[0], Is.EqualTo((byte)0x09)); // bLength: 9
        Assert.That(composite[1], Is.EqualTo((byte)0x02)); // bDescriptorType: CONFIGURATION
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(composite.AsSpan(2)), Is.EqualTo((ushort)227));
        Assert.That(composite[4], Is.EqualTo((byte)0x04)); // bNumInterfaces: 4
        Assert.That(composite[5], Is.EqualTo((byte)0x01)); // bConfigurationValue: 1
        Assert.That(composite[6], Is.EqualTo((byte)0x00)); // iConfiguration: none
        Assert.That(composite[7], Is.EqualTo((byte)0xC0)); // bmAttributes: self-powered
        Assert.That(composite[8], Is.EqualTo((byte)0xFA)); // bMaxPower: 500 mA

        // The two function blocks follow the header verbatim, in audio-then-HID order.
        Assert.That(composite.AsSpan(9, audio.Length).ToArray(), Is.EqualTo(audio));
        Assert.That(composite.AsSpan(9 + audio.Length).ToArray(), Is.EqualTo(hid));

        // The supplied HID block is not renumbered: callers supply interface 3.
        Assert.That(composite[9 + audio.Length + 2], Is.EqualTo((byte)0x03));

        // Every call returns a fresh array.
        Assert.That(UsbAudioClass.CreateCompositeConfiguration(audio, hid), Is.Not.SameAs(composite));
    }

    [Test]
    public void CreateCompositeConfiguration_DualShock4Totals225AndFourInterfaces()
    {
        byte[] audio = UsbAudioClass.CreateDualShock4AudioBlock();
        byte[] hid = CreateMinimalHidBlock();

        byte[] composite = UsbAudioClass.CreateCompositeConfiguration(audio, hid);

        Assert.That(composite, Has.Length.EqualTo(225));
        Assert.That(composite[0], Is.EqualTo((byte)0x09)); // bLength: 9
        Assert.That(composite[1], Is.EqualTo((byte)0x02)); // bDescriptorType: CONFIGURATION
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(composite.AsSpan(2)), Is.EqualTo((ushort)225));
        Assert.That(composite[4], Is.EqualTo((byte)0x04)); // bNumInterfaces: 4
        Assert.That(composite[5], Is.EqualTo((byte)0x01)); // bConfigurationValue: 1
        Assert.That(composite[7], Is.EqualTo((byte)0xC0)); // bmAttributes: self-powered
        Assert.That(composite[8], Is.EqualTo((byte)0xFA)); // bMaxPower: 500 mA

        Assert.That(composite.AsSpan(9, audio.Length).ToArray(), Is.EqualTo(audio));
        Assert.That(composite.AsSpan(9 + audio.Length).ToArray(), Is.EqualTo(hid));
        Assert.That(composite[9 + audio.Length + 2], Is.EqualTo((byte)0x03));
    }

    [Test]
    public void CreateCompositeConfiguration_EmptyHidBlock_Throws()
    {
        byte[] audio = UsbAudioClass.CreateDualSenseAudioBlock();

        Assert.That(
            () => UsbAudioClass.CreateCompositeConfiguration(audio, ReadOnlySpan<byte>.Empty),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void CreateCompositeConfiguration_ParsedByDescriptorSet_ExposesFourInterfacesAndAudioEndpoints()
    {
        byte[] composite = UsbAudioClass.CreateCompositeConfiguration(UsbAudioClass.CreateDualSenseAudioBlock(), CreateMinimalHidBlock());

        // The composite carries no HID report payload, so the parse check passes an empty report
        // descriptor; the parser walks the configuration chain up to its wTotalLength.
        UsbDescriptorSet descriptors = UsbDescriptorSet.Create(
            TestDevices.CreateSimpleDeviceDescriptor(),
            composite,
            ReadOnlyMemory<byte>.Empty,
            []);

        Assert.That(descriptors.InterfaceNumbers, Is.EqualTo(new byte[]
        {
            0, 1, 2, 3
        }));
        Assert.That(descriptors.InterfaceCount, Is.EqualTo(4));
        Assert.That(descriptors.ConfigurationValue, Is.EqualTo((byte)1));

        Assert.That(descriptors.TryGetInterface(0, 0, out UsbInterfaceInfo audioControl), Is.True);
        Assert.That(audioControl.Class, Is.EqualTo((byte)0x01));
        Assert.That(audioControl.SubClass, Is.EqualTo((byte)0x01));
        Assert.That(audioControl.Endpoints, Is.Empty);

        Assert.That(descriptors.TryGetInterface(1, 1, out UsbInterfaceInfo speaker), Is.True);
        Assert.That(speaker.Endpoints, Has.Count.EqualTo(1));
        Assert.That(speaker.Endpoints[0].Address, Is.EqualTo((byte)0x01));
        Assert.That(speaker.Endpoints[0].Type, Is.EqualTo(UsbEndpointType.Isochronous));
        Assert.That(speaker.Endpoints[0].MaxPacketSize, Is.EqualTo((ushort)392));
        Assert.That(speaker.Endpoints[0].Interval, Is.EqualTo((byte)1));

        Assert.That(descriptors.TryGetInterface(2, 1, out UsbInterfaceInfo microphone), Is.True);
        Assert.That(microphone.Endpoints, Has.Count.EqualTo(1));
        Assert.That(microphone.Endpoints[0].Address, Is.EqualTo((byte)0x82));
        Assert.That(microphone.Endpoints[0].Type, Is.EqualTo(UsbEndpointType.Isochronous));
        Assert.That(microphone.Endpoints[0].MaxPacketSize, Is.EqualTo((ushort)196));
        Assert.That(microphone.Endpoints[0].Interval, Is.EqualTo((byte)1));

        Assert.That(descriptors.TryGetInterface(3, 0, out UsbInterfaceInfo hid), Is.True);
        Assert.That(hid.Endpoints, Has.Count.EqualTo(2));
        Assert.That(hid.Endpoints[0].Address, Is.EqualTo((byte)0x84));
        Assert.That(hid.Endpoints[1].Address, Is.EqualTo((byte)0x03));
    }

    /// <summary>
    /// Reads one 3-byte little-endian tSamFreq value (UAC1 format type descriptor).
    /// </summary>
    /// <param name="bytes">Descriptor bytes holding the sample frequency.</param>
    /// <param name="offset">Offset of the first sample frequency byte.</param>
    /// <returns>The sample frequency in Hz.</returns>
    private static int ReadSampleFrequency(byte[] bytes, int offset) =>
        bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);

    /// <summary>
    /// Creates the 32-byte HID function block the personas place behind the audio block: HID
    /// interface 3 with interrupt IN 0x84 and interrupt OUT 0x03, with a HID class descriptor that
    /// declares no report payload and the personas' interval override 5.
    /// </summary>
    /// <returns>The HID block bytes, 32 in total.</returns>
    private static byte[] CreateMinimalHidBlock()
    {
        return
        [
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x03, // bInterfaceNumber: 3
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
            0x00, 0x00, // wDescriptorLength: 0 (this block declares no report payload)
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            0x84, // bEndpointAddress: IN EP4
            0x03, // bmAttributes: interrupt
            0x40, 0x00, // wMaxPacketSize: 64
            0x05, // bInterval: 5
            0x07, // bLength: 7
            0x05, // bDescriptorType: ENDPOINT
            0x03, // bEndpointAddress: OUT EP3
            0x03, // bmAttributes: interrupt
            0x40, 0x00, // wMaxPacketSize: 64
            0x05 // bInterval: 5
        ];
    }

    // Second, independent transcription of the 186-byte reference audio block, used as the golden
    // copy of CreateDualSenseAudioBlock_MatchesReferenceBytes: the audio block of the reference
    // configuration descriptor with the serial interface association disabled (4-channel speaker
    // and 2-channel microphone at 48 kHz). The production builder holds its
    // own copy of these bytes; the test compares the two byte-for-byte and adds structural checks.
    private static byte[] ExpectedDualSenseAudioBlock()
    {
        return
        [
            0x09, 0x04, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, // AC interface (0.0)
            0x0A, 0x24, 0x01, 0x00, 0x01, 0x49, 0x00, 0x02, 0x01, 0x02, // AC header: bcdADC 1.00, wTotalLength 73, interfaces 1 and 2
            0x0C, 0x24, 0x02, 0x01, 0x01, 0x01, 0x06, 0x04, 0x33, 0x00, 0x00, 0x00, // input terminal 1: 4 channels, L/R front + L/R surround
            0x0C, 0x24, 0x06, 0x02, 0x01, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, // feature unit 2: master mute/volume, no per-channel controls
            0x09, 0x24, 0x03, 0x03, 0x01, 0x03, 0x04, 0x02, 0x00, // output terminal 3: speaker
            0x0C, 0x24, 0x02, 0x04, 0x02, 0x04, 0x03, 0x02, 0x03, 0x00, 0x00, 0x00, // input terminal 4: headset mic, 2 channels
            0x09, 0x24, 0x06, 0x05, 0x04, 0x01, 0x03, 0x00, 0x00, // feature unit 5: 9-byte descriptor quirk, verbatim
            0x09, 0x24, 0x03, 0x06, 0x01, 0x01, 0x01, 0x05, 0x00, // output terminal 6: USB streaming
            0x09, 0x04, 0x01, 0x00, 0x00, 0x01, 0x02, 0x00, 0x00, // AS OUT (1.0)
            0x09, 0x04, 0x01, 0x01, 0x01, 0x01, 0x02, 0x00, 0x00, // AS OUT (1.1)
            0x07, 0x24, 0x01, 0x01, 0x01, 0x01, 0x00, // AS general: terminal link 1, PCM
            0x0B, 0x24, 0x02, 0x01, 0x04, 0x02, 0x10, 0x01, 0x80, 0xBB, 0x00, // format: 4 ch, 16-bit, 48 kHz
            0x09, 0x05, 0x01, 0x09, 0x88, 0x01, 0x01, 0x00, 0x00, // EP OUT 1: isochronous adaptive, 392 bytes, interval 1
            0x07, 0x25, 0x01, 0x00, 0x00, 0x00, 0x00, // CS endpoint: no controls
            0x09, 0x04, 0x02, 0x00, 0x00, 0x01, 0x02, 0x00, 0x00, // AS IN (2.0)
            0x09, 0x04, 0x02, 0x01, 0x01, 0x01, 0x02, 0x00, 0x00, // AS IN (2.1)
            0x07, 0x24, 0x01, 0x06, 0x01, 0x01, 0x00, // AS general: terminal link 6, PCM
            0x0B, 0x24, 0x02, 0x01, 0x02, 0x02, 0x10, 0x01, 0x80, 0xBB, 0x00, // format: 2 ch, 16-bit, 48 kHz
            0x09, 0x05, 0x82, 0x05, 0xC4, 0x00, 0x01, 0x00, 0x00, // EP IN 2: isochronous asynchronous, 196 bytes, interval 1
            0x07, 0x25, 0x01, 0x00, 0x00, 0x00, 0x00 // CS endpoint: no controls
        ];
    }

    // Second, independent transcription of the 184-byte reference audio block, used as the golden
    // copy of CreateDualShock4AudioBlock_MatchesReferenceBytes: the audio block of the reference
    // configuration descriptor with the serial interface association disabled (2-channel speaker at
    // 32 kHz and 1-channel microphone at 16 kHz). The production builder holds its own copy of
    // these bytes; the test compares the two byte-for-byte and adds structural checks.
    private static byte[] ExpectedDualShock4AudioBlock()
    {
        return
        [
            0x09, 0x04, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, // AC interface (0.0)
            0x0A, 0x24, 0x01, 0x00, 0x01, 0x47, 0x00, 0x02, 0x01, 0x02, // AC header: bcdADC 1.00, wTotalLength 71, interfaces 1 and 2
            0x0C, 0x24, 0x02, 0x01, 0x01, 0x01, 0x06, 0x02, 0x03, 0x00, 0x00, 0x00, // input terminal 1: 2 channels, L/R front
            0x0A, 0x24, 0x06, 0x02, 0x01, 0x01, 0x03, 0x00, 0x00, 0x00, // feature unit 2: master mute/volume, 10 bytes for 2 channels
            0x09, 0x24, 0x03, 0x03, 0x02, 0x04, 0x04, 0x02, 0x00, // output terminal 3: headset
            0x0C, 0x24, 0x02, 0x04, 0x02, 0x04, 0x03, 0x01, 0x00, 0x00, 0x00, 0x00, // input terminal 4: headset mic, 1 channel
            0x09, 0x24, 0x06, 0x05, 0x04, 0x01, 0x03, 0x00, 0x00, // feature unit 5: 9 bytes for 1 channel
            0x09, 0x24, 0x03, 0x06, 0x01, 0x01, 0x01, 0x05, 0x00, // output terminal 6: USB streaming
            0x09, 0x04, 0x01, 0x00, 0x00, 0x01, 0x02, 0x00, 0x00, // AS OUT (1.0)
            0x09, 0x04, 0x01, 0x01, 0x01, 0x01, 0x02, 0x00, 0x00, // AS OUT (1.1)
            0x07, 0x24, 0x01, 0x01, 0x01, 0x01, 0x00, // AS general: terminal link 1, PCM
            0x0B, 0x24, 0x02, 0x01, 0x02, 0x02, 0x10, 0x01, 0x00, 0x7D, 0x00, // format: 2 ch, 16-bit, 32 kHz
            0x09, 0x05, 0x01, 0x09, 0x84, 0x00, 0x01, 0x00, 0x00, // EP OUT 1: isochronous adaptive, 132 bytes, interval 1
            0x07, 0x25, 0x01, 0x00, 0x00, 0x00, 0x00, // CS endpoint: no controls
            0x09, 0x04, 0x02, 0x00, 0x00, 0x01, 0x02, 0x00, 0x00, // AS IN (2.0)
            0x09, 0x04, 0x02, 0x01, 0x01, 0x01, 0x02, 0x00, 0x00, // AS IN (2.1)
            0x07, 0x24, 0x01, 0x06, 0x01, 0x01, 0x00, // AS general: terminal link 6, PCM
            0x0B, 0x24, 0x02, 0x01, 0x01, 0x02, 0x10, 0x01, 0x80, 0x3E, 0x00, // format: 1 ch, 16-bit, 16 kHz
            0x09, 0x05, 0x82, 0x05, 0x22, 0x00, 0x01, 0x00, 0x00, // EP IN 2: isochronous asynchronous, 34 bytes, interval 1
            0x07, 0x25, 0x01, 0x00, 0x00, 0x00, 0x00 // CS endpoint: no controls
        ];
    }
}