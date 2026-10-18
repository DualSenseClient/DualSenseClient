using System.Buffers.Binary;
using DualSenseClient.Emulation.UsbIp;

namespace DualSenseClient.Emulation.Core;

/// <summary>
/// Base class of the synthetic USB devices exposed over USB/IP. Implements the standard control
/// requests of USB 2.0 section 9.4 on top of a <see cref="UsbDescriptorSet"/> and routes
/// non-standard requests, the interrupt IN report path and the interrupt OUT path to overridable
/// hooks. Only the default alternate setting of each interface is modelled.
/// </summary>
public abstract class VirtualUsbDevice
{
    /// <summary>Value returned for a stalled (unsupported) control request (STALL).</summary>
    private const int Stall = -1;

    /// <summary>Mask of the request type bits of bmRequestType (bits 6..5).</summary>
    private const byte RequestTypeMask = 0x60;

    /// <summary>Request type value of a standard request (bmRequestType bits 6..5 = 0b00).</summary>
    private const byte RequestTypeStandard = 0x00;

    /// <summary>Direction bit of a device-to-host request (bmRequestType bit 7).</summary>
    private const byte DirectionIn = 0x80;

    /// <summary>Mask of the recipient bits of bmRequestType (bits 4..0).</summary>
    private const byte RecipientMask = 0x1F;

    /// <summary>Recipient value of a request addressed to the device (bmRequestType bits 4..0 = 0b00000).</summary>
    private const byte RecipientDevice = 0x00;

    /// <summary>Recipient value of a request addressed to an interface (bmRequestType bits 4..0 = 0b00001).</summary>
    private const byte RecipientInterface = 0x01;

    /// <summary>Recipient value of a request addressed to an endpoint (bmRequestType bits 4..0 = 0b00010).</summary>
    private const byte RecipientEndpoint = 0x02;

    /// <summary>Standard request code of GET_STATUS (bRequest).</summary>
    private const byte RequestGetStatus = 0x00;

    /// <summary>Standard request code of CLEAR_FEATURE (bRequest).</summary>
    private const byte RequestClearFeature = 0x01;

    /// <summary>Standard request code of SET_FEATURE (bRequest).</summary>
    private const byte RequestSetFeature = 0x03;

    /// <summary>Standard request code of SET_ADDRESS (bRequest).</summary>
    private const byte RequestSetAddress = 0x05;

    /// <summary>Standard request code of GET_DESCRIPTOR (bRequest).</summary>
    private const byte RequestGetDescriptor = 0x06;

    /// <summary>Standard request code of GET_CONFIGURATION (bRequest).</summary>
    private const byte RequestGetConfiguration = 0x08;

    /// <summary>Standard request code of SET_CONFIGURATION (bRequest).</summary>
    private const byte RequestSetConfiguration = 0x09;

    /// <summary>Standard request code of GET_INTERFACE (bRequest).</summary>
    private const byte RequestGetInterface = 0x0A;

    /// <summary>Standard request code of SET_INTERFACE (bRequest).</summary>
    private const byte RequestSetInterface = 0x0B;

    /// <summary>Descriptor type of the device descriptor (wValue high byte).</summary>
    private const byte DescriptorTypeDevice = 0x01;

    /// <summary>Descriptor type of the configuration descriptor (wValue high byte).</summary>
    private const byte DescriptorTypeConfiguration = 0x02;

    /// <summary>Descriptor type of a string descriptor (wValue high byte).</summary>
    private const byte DescriptorTypeString = 0x03;

    /// <summary>Descriptor type of the HID report descriptor (wValue high byte).</summary>
    private const byte DescriptorTypeHidReport = 0x22;

    /// <summary>Interface class value of a HID interface (bInterfaceClass; USB HID 1.11).</summary>
    private const byte InterfaceClassHid = 0x03;

    /// <summary>String descriptor index of the language id (LANGID) descriptor, which is not part of the descriptor set.</summary>
    private const byte LanguageIdIndex = 0;

    /// <summary>Language id advertised by the synthesized LANGID descriptor: US English (0x0409).</summary>
    private const ushort EnglishUnitedStatesLanguageId = 0x0409;

    /// <summary>Size in bytes of the bLength/bDescriptorType header every descriptor starts with.</summary>
    private const int DescriptorHeaderSize = 2;

    /// <summary>Maximum number of UTF-16 characters a string descriptor can hold (bLength is one byte).</summary>
    private const int MaxStringCharacters = (byte.MaxValue - DescriptorHeaderSize) / 2;

    /// <summary>Maximum size in bytes of a string descriptor.</summary>
    private const int MaxStringDescriptorSize = DescriptorHeaderSize + MaxStringCharacters * 2;

    /// <summary>Size in bytes of a language id descriptor: the header plus one LANGID.</summary>
    private const int LanguageIdDescriptorSize = DescriptorHeaderSize + 2;

    /// <summary>Size in bytes of a GET_STATUS response.</summary>
    private const int StatusResponseSize = 2;

    private byte _configurationValue;

    /// <summary>
    /// Initializes a device that serves <paramref name="descriptors"/>.
    /// </summary>
    /// <param name="descriptors">Descriptor set of the device.</param>
    /// <exception cref="ArgumentNullException"><paramref name="descriptors"/> is <see langword="null"/>.</exception>
    protected VirtualUsbDevice(UsbDescriptorSet descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        Descriptors = descriptors;
    }

    /// <summary>Descriptor set of the device; immutable.</summary>
    public UsbDescriptorSet Descriptors { get; }

    /// <summary>
    /// Transfer speed the device is exported with in the USB/IP handshake entries; defaults to
    /// full speed, the speed of the M1 personas. Personas override this property to declare a
    /// different speed.
    /// </summary>
    public virtual UsbIpDeviceSpeed Speed
    {
        get
        {
            return UsbIpDeviceSpeed.Full;
        }
    }

    /// <summary>
    /// Handles one control transfer of endpoint 0: standard requests are served from the
    /// descriptor set, non-standard requests are forwarded to <see cref="OnClassRequest"/>, and the
    /// response is truncated to the request length. This is the single entry point used by the server.
    /// </summary>
    /// <param name="setup">Setup packet of the transfer.</param>
    /// <param name="outData">Payload of a host-to-device transfer; ignored by the standard requests.</param>
    /// <param name="response">Buffer the response is written to; at least the request length is recommended.</param>
    /// <returns>The number of bytes written to <paramref name="response"/>, or -1 for STALL.</returns>
    public int HandleControlRequest(in UsbSetupPacket setup, ReadOnlySpan<byte> outData, Span<byte> response)
    {
        if ((setup.RequestType & RequestTypeMask) != RequestTypeStandard)
        {
            return ClampResponse(setup, response, OnClassRequest(setup, outData, response));
        }

        return HandleStandardRequest(setup, response);
    }

    /// <summary>
    /// Handles a non-standard (class or vendor) control request. The base implementation stalls;
    /// subclasses override this to implement their class requests and discriminate on the
    /// bmRequestType bits themselves.
    /// </summary>
    /// <param name="setup">Setup packet of the transfer.</param>
    /// <param name="outData">Payload of a host-to-device transfer; empty for device-to-host requests.</param>
    /// <param name="response">Buffer for the response of a device-to-host transfer.</param>
    /// <returns>The number of bytes written, or -1 for STALL. The base returns -1.</returns>
    protected virtual int OnClassRequest(in UsbSetupPacket setup, ReadOnlySpan<byte> outData, Span<byte> response) => Stall;

    /// <summary>
    /// Called after SET_CONFIGURATION changed the current configuration value; the base does
    /// nothing. Subclasses use this to start or stop their data paths.
    /// </summary>
    /// <param name="configurationValue">The new configuration value (0 selects the unconfigured state).</param>
    protected virtual void OnConfigurationChanged(byte configurationValue)
    {
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with the next interrupt IN report; the base provides none.
    /// </summary>
    /// <param name="destination">Buffer the report is written to.</param>
    /// <returns>The number of bytes written, or 0 when the device has no report to provide.</returns>
    public virtual int ProvideInputReport(Span<byte> destination) => 0;

    /// <summary>
    /// Handles one interrupt OUT payload; the base ignores it.
    /// </summary>
    /// <param name="data">Payload of the host-to-device transfer.</param>
    public virtual void OnOutputReport(ReadOnlySpan<byte> data)
    {
    }

    /// <summary>
    /// Handles a standard request of USB 2.0 section 9.4 for the recipient it is addressed to.
    /// </summary>
    /// <param name="setup">Setup packet of the transfer.</param>
    /// <param name="response">Buffer the response is written to.</param>
    /// <returns>The number of bytes written, or -1 for STALL.</returns>
    private int HandleStandardRequest(in UsbSetupPacket setup, Span<byte> response)
    {
        byte recipient = (byte)(setup.RequestType & RecipientMask);
        bool deviceToHost = (setup.RequestType & DirectionIn) != 0;
        switch (setup.Request)
        {
            case RequestGetDescriptor:
                // The HID report descriptor is the one descriptor a host requests through the
                // interface recipient (HID 1.11 request table: bmRequestType 0x81 with the interface
                // number in wIndex, observed on a real Windows enumeration); every other descriptor
                // keeps the device recipient.
                bool interfaceReportDescriptor = recipient == RecipientInterface
                                                 && (byte)(setup.Value >> 8) == DescriptorTypeHidReport;
                if ((recipient != RecipientDevice && !interfaceReportDescriptor) || !deviceToHost)
                {
                    return Stall;
                }

                return HandleGetDescriptor(setup, response);

            case RequestGetStatus:
                if (recipient > RecipientEndpoint || !deviceToHost)
                {
                    return Stall;
                }

                return WriteZeroStatus(setup, response);

            case RequestSetAddress:
                if (recipient != RecipientDevice || deviceToHost)
                {
                    return Stall;
                }

                return 0; // the assigned address is meaningless for a USB/IP device; no state is kept

            case RequestGetConfiguration:
                if (recipient != RecipientDevice || !deviceToHost)
                {
                    return Stall;
                }

                if (setup.Length == 0 || response.Length == 0)
                {
                    return 0;
                }

                response[0] = _configurationValue;
                return 1;

            case RequestSetConfiguration:
                if (recipient != RecipientDevice || deviceToHost)
                {
                    return Stall;
                }

                _configurationValue = (byte)setup.Value;
                OnConfigurationChanged(_configurationValue);
                return 0;

            case RequestSetFeature:
            case RequestClearFeature:
                // Endpoint halt is accepted and not tracked; device features are accepted as no-ops.
                if (deviceToHost || (recipient != RecipientDevice && recipient != RecipientEndpoint))
                {
                    return Stall;
                }

                return 0;

            case RequestGetInterface:
                if (recipient != RecipientInterface || !deviceToHost || !HasInterface((byte)setup.Index))
                {
                    return Stall;
                }

                if (setup.Length == 0 || response.Length == 0)
                {
                    return 0;
                }

                response[0] = 0; // only the default alternate setting is modelled
                return 1;

            case RequestSetInterface:
                if (recipient != RecipientInterface || deviceToHost || setup.Value != 0 || !HasInterface((byte)setup.Index))
                {
                    return Stall;
                }

                return 0; // only the default alternate setting is modelled

            default:
                return Stall;
        }
    }

    /// <summary>
    /// Serves GET_DESCRIPTOR (USB 2.0 section 9.4.3): descriptor type in the wValue high byte,
    /// descriptor index in the low byte, interface number of a HID report descriptor in wIndex.
    /// </summary>
    /// <param name="setup">Setup packet of the transfer.</param>
    /// <param name="response">Buffer the response is written to.</param>
    /// <returns>The number of bytes written, or -1 for STALL.</returns>
    private int HandleGetDescriptor(in UsbSetupPacket setup, Span<byte> response)
    {
        byte type = (byte)(setup.Value >> 8);
        byte index = (byte)setup.Value;
        switch (type)
        {
            case DescriptorTypeDevice:
                return index == 0 ? CopyPayload(Descriptors.DeviceDescriptor.Span, setup.Length, response) : Stall;

            case DescriptorTypeConfiguration:
                return index == 0 ? CopyPayload(Descriptors.ConfigDescriptor.Span, setup.Length, response) : Stall;

            case DescriptorTypeString:
                return HandleGetStringDescriptor(index, setup.Length, response);

            case DescriptorTypeHidReport:
                return HandleGetReportDescriptor(setup, response);

            default:
                return Stall;
        }
    }

    /// <summary>
    /// Serves GET_DESCRIPTOR(STRING, <paramref name="index"/>): index 0 is the synthesized
    /// language id descriptor and every other index is looked up in the descriptor set.
    /// </summary>
    /// <param name="index">String descriptor index (wValue low byte).</param>
    /// <param name="requestedLength">Requested transfer length (wLength).</param>
    /// <param name="response">Buffer the response is written to.</param>
    /// <returns>The number of bytes written, or -1 for STALL.</returns>
    private int HandleGetStringDescriptor(byte index, ushort requestedLength, Span<byte> response)
    {
        if (index == LanguageIdIndex)
        {
            return WriteLanguageIdDescriptor(requestedLength, response);
        }

        if (!Descriptors.TryGetString(index, out string value))
        {
            return Stall;
        }

        return WriteStringDescriptor(value, requestedLength, response);
    }

    /// <summary>
    /// Serves GET_DESCRIPTOR(REPORT, wIndex) for the HID interface named by
    /// <paramref name="setup"/>; stalls when the device has no HID report descriptor or the
    /// interface is not declared as HID (class 0x03).
    /// </summary>
    /// <param name="setup">Setup packet of the transfer.</param>
    /// <param name="response">Buffer the response is written to.</param>
    /// <returns>The number of bytes written, or -1 for STALL.</returns>
    private int HandleGetReportDescriptor(in UsbSetupPacket setup, Span<byte> response)
    {
        if (Descriptors.HidReportDescriptor is null || !IsHidInterface((byte)setup.Index))
        {
            return Stall;
        }

        return CopyPayload(Descriptors.HidReportDescriptor.Value.Span, setup.Length, response);
    }

    /// <summary>
    /// Writes the synthesized language id (LANGID) descriptor of string index 0: a four-byte
    /// descriptor advertising US English (0x0409), the only language the engine serves.
    /// </summary>
    /// <param name="requestedLength">Requested transfer length (wLength).</param>
    /// <param name="response">Buffer the response is written to.</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteLanguageIdDescriptor(ushort requestedLength, Span<byte> response)
    {
        Span<byte> descriptor = stackalloc byte[LanguageIdDescriptorSize];
        descriptor[0] = LanguageIdDescriptorSize;
        descriptor[1] = DescriptorTypeString;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[DescriptorHeaderSize..], EnglishUnitedStatesLanguageId);
        return CopyPayload(descriptor, requestedLength, response);
    }

    /// <summary>
    /// Writes a string descriptor for <paramref name="value"/>: the two-byte header followed by
    /// the UTF-16LE code units of the string, truncated to the characters one descriptor can hold.
    /// </summary>
    /// <param name="value">String value of the descriptor.</param>
    /// <param name="requestedLength">Requested transfer length (wLength).</param>
    /// <param name="response">Buffer the response is written to.</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteStringDescriptor(string value, ushort requestedLength, Span<byte> response)
    {
        int characterCount = Math.Min(value.Length, MaxStringCharacters);
        int descriptorLength = DescriptorHeaderSize + characterCount * 2;
        Span<byte> descriptor = stackalloc byte[MaxStringDescriptorSize];
        descriptor[0] = (byte)descriptorLength;
        descriptor[1] = DescriptorTypeString;
        for (int i = 0; i < characterCount; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[(DescriptorHeaderSize + i * 2)..], value[i]);
        }

        return CopyPayload(descriptor[..descriptorLength], requestedLength, response);
    }

    /// <summary>
    /// Writes a GET_STATUS payload of <see cref="StatusResponseSize"/> zero bytes: the device is
    /// bus-powered, no endpoint is halted and no interface reports a status change.
    /// </summary>
    /// <param name="setup">Setup packet of the transfer.</param>
    /// <param name="response">Buffer the response is written to.</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteZeroStatus(in UsbSetupPacket setup, Span<byte> response)
    {
        int count = Math.Min(Math.Min(StatusResponseSize, (int)setup.Length), response.Length);
        response[..count].Clear();
        return count;
    }

    /// <summary>
    /// Copies a device-to-host payload into <paramref name="response"/>, truncated to the
    /// requested transfer length: the response carries min(wLength, payload length) bytes.
    /// </summary>
    /// <param name="payload">Payload the request resolves to.</param>
    /// <param name="requestedLength">Requested transfer length (wLength).</param>
    /// <param name="response">Buffer the response is written to.</param>
    /// <returns>The number of bytes written.</returns>
    private static int CopyPayload(ReadOnlySpan<byte> payload, ushort requestedLength, Span<byte> response)
    {
        int count = Math.Min(Math.Min((int)requestedLength, payload.Length), response.Length);
        payload[..count].CopyTo(response);
        return count;
    }

    /// <summary>
    /// Clamps the result of a class request handler: negative results become a stall and positive
    /// results are truncated to the request length.
    /// </summary>
    /// <param name="setup">Setup packet of the transfer.</param>
    /// <param name="response">Response buffer of the transfer.</param>
    /// <param name="result">Result returned by the class request handler.</param>
    /// <returns>The number of bytes written, or -1 for STALL.</returns>
    private static int ClampResponse(in UsbSetupPacket setup, Span<byte> response, int result)
    {
        if (result < 0)
        {
            return Stall;
        }

        return Math.Min(result, Math.Min(setup.Length, response.Length));
    }

    /// <summary>
    /// Returns whether the configuration descriptor declares an interface with
    /// <paramref name="number"/> whose class is HID (0x03); only a HID interface may serve a HID
    /// report descriptor.
    /// </summary>
    /// <param name="number">Interface number (bInterfaceNumber) to look up.</param>
    /// <returns><see langword="true"/> when the interface is declared as HID; otherwise <see langword="false"/>.</returns>
    private bool IsHidInterface(byte number)
    {
        IReadOnlyList<UsbInterfaceInfo> interfaces = Descriptors.Interfaces;
        for (int i = 0; i < interfaces.Count; i++)
        {
            if (interfaces[i].Number == number)
            {
                return interfaces[i].Class == InterfaceClassHid;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether the configuration descriptor declares an interface with
    /// <paramref name="number"/>.
    /// </summary>
    /// <param name="number">Interface number (bInterfaceNumber) to look up.</param>
    /// <returns><see langword="true"/> when the interface is declared; otherwise <see langword="false"/>.</returns>
    private bool HasInterface(byte number)
    {
        IReadOnlyList<UsbInterfaceInfo> interfaces = Descriptors.Interfaces;
        for (int i = 0; i < interfaces.Count; i++)
        {
            if (interfaces[i].Number == number)
            {
                return true;
            }
        }

        return false;
    }
}