using System.Buffers.Binary;

// Audio descriptor data: the reference implementations serve a 186-byte audio block (4-channel
// speaker and 2-channel microphone at 48 kHz) and a 184-byte audio block (2-channel speaker at
// 32 kHz and 1-channel microphone at 16 kHz) of their configuration descriptors with the serial
// interface association disabled. These are static descriptor bytes only.

namespace DualSenseClient.Emulation.Core;

/// <summary>
/// UAC1 descriptor blocks of the Emulation personas and the builder that combines one audio block
/// with a persona HID block into a configuration descriptor. The blocks are byte-for-byte copies
/// of the reference configurations: a 186-byte block (4-channel speaker and 2-channel microphone
/// at 48 kHz) and a 184-byte block (2-channel speaker at 32 kHz and 1-channel microphone at
/// 16 kHz). Both references serve the audio function without an interface association descriptor
/// (serial disabled), so the blocks contain the audio control interface and the two streaming
/// interfaces only.
/// </summary>
public static class UsbAudioClass
{
    /// <summary>Size in bytes of the configuration descriptor header (USB 2.0 specification, section 9.6.3).</summary>
    private const int ConfigurationHeaderSize = 9;

    /// <summary>
    /// Creates the 186-byte UAC1 audio block of the DualSense persona: audio control interface 0
    /// with the 73-byte class-specific set (4-channel speaker path and 2-channel microphone path),
    /// audio streaming interface 1 with alternate settings 0/1 for the speaker (isochronous OUT EP1,
    /// 392 bytes per frame) and audio streaming interface 2 with alternate settings 0/1 for the
    /// microphone (isochronous IN EP2, 196 bytes per frame). Both formats are 16-bit PCM at 48 kHz.
    /// </summary>
    /// <returns>The audio block bytes, exactly as served by the reference.</returns>
    public static byte[] CreateDualSenseAudioBlock()
    {
        return
        [
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x00, // bInterfaceNumber: 0
            0x00, // bAlternateSetting: 0
            0x00, // bNumEndpoints: 0
            0x01, // bInterfaceClass: Audio
            0x01, // bInterfaceSubClass: Audio Control
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x0A, // bLength: 10
            0x24, // bDescriptorType: CS_INTERFACE
            0x01, // bDescriptorSubtype: Header
            0x00, 0x01, // bcdADC: 1.00
            0x49, 0x00, // wTotalLength: 73 (0x0049)
            0x02, // bInCollection: 2 streaming interfaces
            0x01, // baInterfaceNr(1): interface 1
            0x02, // baInterfaceNr(2): interface 2
            0x0C, // bLength: 12
            0x24, // bDescriptorType: CS_INTERFACE
            0x02, // bDescriptorSubtype: Input Terminal
            0x01, // bTerminalID: 1
            0x01, 0x01, // wTerminalType: USB streaming (0x0101)
            0x06, // bAssocTerminal: 6
            0x04, // bNrChannels: 4
            0x33, 0x00, // wChannelConfig: L/R front + L/R surround (0x0033)
            0x00, // iChannelNames: none
            0x00, // iTerminal: none
            0x0C, // bLength: 12
            0x24, // bDescriptorType: CS_INTERFACE
            0x06, // bDescriptorSubtype: Feature Unit
            0x02, // bUnitID: 2
            0x01, // bSourceID: 1
            0x01, // bControlSize: 1 byte per control
            0x03, // bmaControls[0]: master mute/volume
            0x00, 0x00, 0x00, 0x00, // bmaControls[1..4]: no per-channel controls
            0x00, // iFeature: none
            0x09, // bLength: 9
            0x24, // bDescriptorType: CS_INTERFACE
            0x03, // bDescriptorSubtype: Output Terminal
            0x03, // bTerminalID: 3
            0x01, 0x03, // wTerminalType: speaker (0x0301)
            0x04, // bAssocTerminal: 4
            0x02, // bSourceID: 2
            0x00, // iTerminal: none
            0x0C, // bLength: 12
            0x24, // bDescriptorType: CS_INTERFACE
            0x02, // bDescriptorSubtype: Input Terminal
            0x04, // bTerminalID: 4
            0x02, 0x04, // wTerminalType: headset (0x0402)
            0x03, // bAssocTerminal: 3
            0x02, // bNrChannels: 2
            0x03, 0x00, // wChannelConfig: front left + front right (0x0003)
            0x00, // iChannelNames: none
            0x00, // iTerminal: none
            0x09, // bLength: 9
            0x24, // bDescriptorType: CS_INTERFACE
            0x06, // bDescriptorSubtype: Feature Unit
            0x05, // bUnitID: 5
            0x04, // bSourceID: 4
            0x01, // bControlSize: 1 byte per control
            0x03, // bmaControls[0]: master mute/volume
            0x00, // bmaControls[1]: no controls
            0x00, // iFeature: none
            0x09, // bLength: 9
            0x24, // bDescriptorType: CS_INTERFACE
            0x03, // bDescriptorSubtype: Output Terminal
            0x06, // bTerminalID: 6
            0x01, 0x01, // wTerminalType: USB streaming (0x0101)
            0x01, // bAssocTerminal: 1
            0x05, // bSourceID: 5
            0x00, // iTerminal: none
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x01, // bInterfaceNumber: 1
            0x00, // bAlternateSetting: 0
            0x00, // bNumEndpoints: 0
            0x01, // bInterfaceClass: Audio
            0x02, // bInterfaceSubClass: Audio Streaming
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x01, // bInterfaceNumber: 1
            0x01, // bAlternateSetting: 1
            0x01, // bNumEndpoints: 1
            0x01, // bInterfaceClass: Audio
            0x02, // bInterfaceSubClass: Audio Streaming
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x07, // bLength: 7
            0x24, // bDescriptorType: CS_INTERFACE
            0x01, // bDescriptorSubtype: AS_GENERAL
            0x01, // bTerminalLink: terminal 1
            0x01, // bDelay: 1 frame
            0x01, 0x00, // wFormatTag: PCM (0x0001)
            0x0B, // bLength: 11
            0x24, // bDescriptorType: CS_INTERFACE
            0x02, // bDescriptorSubtype: FORMAT_TYPE
            0x01, // bFormatType: TYPE_I
            0x04, // bNrChannels: 4
            0x02, // bSubframeSize: 2 bytes per sample
            0x10, // bBitResolution: 16 bits
            0x01, // bSamFreqType: one discrete frequency
            0x80, 0xBB, 0x00, // tSamFreq: 48000 Hz (0x00BB80)
            0x09, // bLength: 9
            0x05, // bDescriptorType: ENDPOINT
            0x01, // bEndpointAddress: OUT EP1
            0x09, // bmAttributes: isochronous, adaptive
            0x88, 0x01, // wMaxPacketSize: 392 bytes
            0x01, // bInterval: 1 frame
            0x00, // bRefresh: none
            0x00, // bSynchAddress: none
            0x07, // bLength: 7
            0x25, // bDescriptorType: CS_ENDPOINT
            0x01, // bDescriptorSubtype: GENERAL
            0x00, // attributes: no pitch/sampling frequency control
            0x00, // lock delay units: undefined
            0x00, 0x00, // lock delay: 0
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x02, // bInterfaceNumber: 2
            0x00, // bAlternateSetting: 0
            0x00, // bNumEndpoints: 0
            0x01, // bInterfaceClass: Audio
            0x02, // bInterfaceSubClass: Audio Streaming
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x02, // bInterfaceNumber: 2
            0x01, // bAlternateSetting: 1
            0x01, // bNumEndpoints: 1
            0x01, // bInterfaceClass: Audio
            0x02, // bInterfaceSubClass: Audio Streaming
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x07, // bLength: 7
            0x24, // bDescriptorType: CS_INTERFACE
            0x01, // bDescriptorSubtype: AS_GENERAL
            0x06, // bTerminalLink: terminal 6
            0x01, // bDelay: 1 frame
            0x01, 0x00, // wFormatTag: PCM (0x0001)
            0x0B, // bLength: 11
            0x24, // bDescriptorType: CS_INTERFACE
            0x02, // bDescriptorSubtype: FORMAT_TYPE
            0x01, // bFormatType: TYPE_I
            0x02, // bNrChannels: 2
            0x02, // bSubframeSize: 2 bytes per sample
            0x10, // bBitResolution: 16 bits
            0x01, // bSamFreqType: one discrete frequency
            0x80, 0xBB, 0x00, // tSamFreq: 48000 Hz (0x00BB80)
            0x09, // bLength: 9
            0x05, // bDescriptorType: ENDPOINT
            0x82, // bEndpointAddress: IN EP2
            0x05, // bmAttributes: isochronous, asynchronous
            0xC4, 0x00, // wMaxPacketSize: 196 bytes
            0x01, // bInterval: 1 frame
            0x00, // bRefresh: none
            0x00, // bSynchAddress: none
            0x07, // bLength: 7
            0x25, // bDescriptorType: CS_ENDPOINT
            0x01, // bDescriptorSubtype: GENERAL
            0x00, // attributes: no controls
            0x00, // lock delay units: undefined
            0x00, 0x00 // lock delay: 0
        ];
    }

    /// <summary>
    /// Creates the 184-byte UAC1 audio block of the DualShock 4 persona: audio control interface 0
    /// with the 71-byte class-specific set (2-channel speaker path and 1-channel microphone path),
    /// audio streaming interface 1 with alternate settings 0/1 for the speaker (isochronous OUT EP1,
    /// 132 bytes per frame at 32 kHz) and audio streaming interface 2 with alternate settings 0/1
    /// for the microphone (isochronous IN EP2, 34 bytes per frame at 16 kHz).
    /// </summary>
    /// <returns>The audio block bytes, exactly as served by the reference.</returns>
    public static byte[] CreateDualShock4AudioBlock()
    {
        return
        [
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x00, // bInterfaceNumber: 0
            0x00, // bAlternateSetting: 0
            0x00, // bNumEndpoints: 0
            0x01, // bInterfaceClass: Audio
            0x01, // bInterfaceSubClass: Audio Control
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x0A, // bLength: 10
            0x24, // bDescriptorType: CS_INTERFACE
            0x01, // bDescriptorSubtype: Header
            0x00, 0x01, // bcdADC: 1.00
            0x47, 0x00, // wTotalLength: 71 (0x0047)
            0x02, // bInCollection: 2 streaming interfaces
            0x01, // baInterfaceNr(1): interface 1
            0x02, // baInterfaceNr(2): interface 2
            0x0C, // bLength: 12
            0x24, // bDescriptorType: CS_INTERFACE
            0x02, // bDescriptorSubtype: Input Terminal
            0x01, // bTerminalID: 1
            0x01, 0x01, // wTerminalType: USB streaming (0x0101)
            0x06, // bAssocTerminal: 6
            0x02, // bNrChannels: 2
            0x03, 0x00, // wChannelConfig: front left + front right (0x0003)
            0x00, // iChannelNames: none
            0x00, // iTerminal: none
            0x0A, // bLength: 10
            0x24, // bDescriptorType: CS_INTERFACE
            0x06, // bDescriptorSubtype: Feature Unit
            0x02, // bUnitID: 2
            0x01, // bSourceID: 1
            0x01, // bControlSize: 1 byte per control
            0x03, // bmaControls[0]: master mute/volume
            0x00, 0x00, // bmaControls[1..2]: no per-channel controls
            0x00, // iFeature: none
            0x09, // bLength: 9
            0x24, // bDescriptorType: CS_INTERFACE
            0x03, // bDescriptorSubtype: Output Terminal
            0x03, // bTerminalID: 3
            0x02, 0x04, // wTerminalType: headset (0x0402)
            0x04, // bAssocTerminal: 4
            0x02, // bSourceID: 2
            0x00, // iTerminal: none
            0x0C, // bLength: 12
            0x24, // bDescriptorType: CS_INTERFACE
            0x02, // bDescriptorSubtype: Input Terminal
            0x04, // bTerminalID: 4
            0x02, 0x04, // wTerminalType: headset (0x0402)
            0x03, // bAssocTerminal: 3
            0x01, // bNrChannels: 1
            0x00, 0x00, // wChannelConfig: none (master channel only)
            0x00, // iChannelNames: none
            0x00, // iTerminal: none
            0x09, // bLength: 9
            0x24, // bDescriptorType: CS_INTERFACE
            0x06, // bDescriptorSubtype: Feature Unit
            0x05, // bUnitID: 5
            0x04, // bSourceID: 4
            0x01, // bControlSize: 1 byte per control
            0x03, // bmaControls[0]: master mute/volume
            0x00, // bmaControls[1]: no controls
            0x00, // iFeature: none
            0x09, // bLength: 9
            0x24, // bDescriptorType: CS_INTERFACE
            0x03, // bDescriptorSubtype: Output Terminal
            0x06, // bTerminalID: 6
            0x01, 0x01, // wTerminalType: USB streaming (0x0101)
            0x01, // bAssocTerminal: 1
            0x05, // bSourceID: 5
            0x00, // iTerminal: none
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x01, // bInterfaceNumber: 1
            0x00, // bAlternateSetting: 0
            0x00, // bNumEndpoints: 0
            0x01, // bInterfaceClass: Audio
            0x02, // bInterfaceSubClass: Audio Streaming
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x01, // bInterfaceNumber: 1
            0x01, // bAlternateSetting: 1
            0x01, // bNumEndpoints: 1
            0x01, // bInterfaceClass: Audio
            0x02, // bInterfaceSubClass: Audio Streaming
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x07, // bLength: 7
            0x24, // bDescriptorType: CS_INTERFACE
            0x01, // bDescriptorSubtype: AS_GENERAL
            0x01, // bTerminalLink: terminal 1
            0x01, // bDelay: 1 frame
            0x01, 0x00, // wFormatTag: PCM (0x0001)
            0x0B, // bLength: 11
            0x24, // bDescriptorType: CS_INTERFACE
            0x02, // bDescriptorSubtype: FORMAT_TYPE
            0x01, // bFormatType: TYPE_I
            0x02, // bNrChannels: 2
            0x02, // bSubframeSize: 2 bytes per sample
            0x10, // bBitResolution: 16 bits
            0x01, // bSamFreqType: one discrete frequency
            0x00, 0x7D, 0x00, // tSamFreq: 32000 Hz (0x007D00)
            0x09, // bLength: 9
            0x05, // bDescriptorType: ENDPOINT
            0x01, // bEndpointAddress: OUT EP1
            0x09, // bmAttributes: isochronous, adaptive
            0x84, 0x00, // wMaxPacketSize: 132 bytes
            0x01, // bInterval: 1 frame
            0x00, // bRefresh: none
            0x00, // bSynchAddress: none
            0x07, // bLength: 7
            0x25, // bDescriptorType: CS_ENDPOINT
            0x01, // bDescriptorSubtype: GENERAL
            0x00, // attributes: no pitch/sampling frequency control
            0x00, // lock delay units: undefined
            0x00, 0x00, // lock delay: 0
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x02, // bInterfaceNumber: 2
            0x00, // bAlternateSetting: 0
            0x00, // bNumEndpoints: 0
            0x01, // bInterfaceClass: Audio
            0x02, // bInterfaceSubClass: Audio Streaming
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x09, // bLength: 9
            0x04, // bDescriptorType: INTERFACE
            0x02, // bInterfaceNumber: 2
            0x01, // bAlternateSetting: 1
            0x01, // bNumEndpoints: 1
            0x01, // bInterfaceClass: Audio
            0x02, // bInterfaceSubClass: Audio Streaming
            0x00, // bInterfaceProtocol: none
            0x00, // iInterface: none
            0x07, // bLength: 7
            0x24, // bDescriptorType: CS_INTERFACE
            0x01, // bDescriptorSubtype: AS_GENERAL
            0x06, // bTerminalLink: terminal 6
            0x01, // bDelay: 1 frame
            0x01, 0x00, // wFormatTag: PCM (0x0001)
            0x0B, // bLength: 11
            0x24, // bDescriptorType: CS_INTERFACE
            0x02, // bDescriptorSubtype: FORMAT_TYPE
            0x01, // bFormatType: TYPE_I
            0x01, // bNrChannels: 1
            0x02, // bSubframeSize: 2 bytes per sample
            0x10, // bBitResolution: 16 bits
            0x01, // bSamFreqType: one discrete frequency
            0x80, 0x3E, 0x00, // tSamFreq: 16000 Hz (0x003E80)
            0x09, // bLength: 9
            0x05, // bDescriptorType: ENDPOINT
            0x82, // bEndpointAddress: IN EP2
            0x05, // bmAttributes: isochronous, asynchronous
            0x22, 0x00, // wMaxPacketSize: 34 bytes
            0x01, // bInterval: 1 frame
            0x00, // bRefresh: none
            0x00, // bSynchAddress: none
            0x07, // bLength: 7
            0x25, // bDescriptorType: CS_ENDPOINT
            0x01, // bDescriptorSubtype: GENERAL
            0x00, // attributes: no controls
            0x00, // lock delay units: undefined
            0x00, 0x00 // lock delay: 0
        ];
    }

    /// <summary>
    /// Creates the composite configuration blob of one persona: the 9-byte configuration descriptor
    /// header, the audio block and the HID block, concatenated verbatim. The HID block is not
    /// renumbered: callers supply it with interface number 3, and the header declares four
    /// interfaces (audio control, audio OUT, audio IN, HID) in total.
    /// </summary>
    /// <param name="audioBlock">Audio function block (<see cref="CreateDualSenseAudioBlock"/> or <see cref="CreateDualShock4AudioBlock"/>).</param>
    /// <param name="hidBlock">HID function block of the persona: the interface 3 descriptor with its HID class descriptor and endpoints.</param>
    /// <returns>The configuration descriptor blob: 9 header bytes + the audio block + the HID block.</returns>
    /// <exception cref="ArgumentException"><paramref name="hidBlock"/> is empty.</exception>
    public static byte[] CreateCompositeConfiguration(ReadOnlySpan<byte> audioBlock, ReadOnlySpan<byte> hidBlock)
    {
        if (hidBlock.IsEmpty)
        {
            throw new ArgumentException("The HID block must not be empty.", nameof(hidBlock));
        }

        int totalLength = ConfigurationHeaderSize + audioBlock.Length + hidBlock.Length;
        byte[] composite = new byte[totalLength];
        Span<byte> destination = composite;

        destination[0] = 0x09; // bLength: 9
        destination[1] = 0x02; // bDescriptorType: CONFIGURATION
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], (ushort)totalLength); // wTotalLength
        destination[4] = 0x04; // bNumInterfaces: 4 (audio control, audio OUT, audio IN, HID)
        destination[5] = 0x01; // bConfigurationValue: 1
        destination[6] = 0x00; // iConfiguration: none
        destination[7] = 0xC0; // bmAttributes: self-powered, no remote wakeup
        destination[8] = 0xFA; // bMaxPower: 500 mA (250 * 2 mA)

        audioBlock.CopyTo(destination[ConfigurationHeaderSize..]);
        hidBlock.CopyTo(destination[(ConfigurationHeaderSize + audioBlock.Length)..]);

        return composite;
    }
}