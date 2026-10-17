namespace DualSenseClient.Emulation.Core;

/// <summary>
/// Base class of HID devices exposed over USB/IP: implements the HID class control requests of
/// HID 1.11 section 7.2 for interfaces of class HID (0x03) on top of <see cref="VirtualUsbDevice"/>.
/// Input reports are pulled through <see cref="VirtualUsbDevice.ProvideInputReport"/> (no report
/// available stalls), output reports are pushed to <see cref="VirtualUsbDevice.OnOutputReport"/>,
/// and feature reports are served by the overridable feature hooks, which stall by default. The
/// engine keeps no state, logs nothing and only the default alternate setting is modelled.
/// </summary>
public abstract class VirtualHidDevice : VirtualUsbDevice
{
    /// <summary>Value returned for a stalled (unsupported) control request (STALL).</summary>
    private const int Stall = -1;

    /// <summary>Mask of the request type bits of bmRequestType (bits 6..5).</summary>
    private const byte RequestTypeMask = 0x60;

    /// <summary>Request type value of a class request (bmRequestType bits 6..5 = 0b01).</summary>
    private const byte RequestTypeClass = 0x20;

    /// <summary>Direction bit of a device-to-host request (bmRequestType bit 7).</summary>
    private const byte DirectionIn = 0x80;

    /// <summary>Mask of the recipient bits of bmRequestType (bits 4..0).</summary>
    private const byte RecipientMask = 0x1F;

    /// <summary>Recipient value of a request addressed to an interface (bmRequestType bits 4..0 = 0b00001).</summary>
    private const byte RecipientInterface = 0x01;

    /// <summary>Interface class value of a HID interface (bInterfaceClass; USB HID 1.11).</summary>
    private const byte InterfaceClassHid = 0x03;

    /// <summary>HID class request code of GET_REPORT (HID 1.11, section 7.2.1).</summary>
    private const byte RequestGetReport = 0x01;

    /// <summary>HID class request code of GET_IDLE (HID 1.11, section 7.2.4).</summary>
    private const byte RequestGetIdle = 0x02;

    /// <summary>HID class request code of GET_PROTOCOL (HID 1.11, section 7.2.5).</summary>
    private const byte RequestGetProtocol = 0x03;

    /// <summary>HID class request code of SET_REPORT (HID 1.11, section 7.2.2).</summary>
    private const byte RequestSetReport = 0x09;

    /// <summary>HID class request code of SET_IDLE (HID 1.11, section 7.2.3).</summary>
    private const byte RequestSetIdle = 0x0A;

    /// <summary>HID class request code of SET_PROTOCOL (HID 1.11, section 7.2.6).</summary>
    private const byte RequestSetProtocol = 0x0B;

    /// <summary>Report type value of an input report (wValue high byte).</summary>
    private const byte ReportTypeInput = 1;

    /// <summary>Report type value of an output report (wValue high byte).</summary>
    private const byte ReportTypeOutput = 2;

    /// <summary>Report type value of a feature report (wValue high byte).</summary>
    private const byte ReportTypeFeature = 3;

    /// <summary>Idle rate served by GET_IDLE: 0, the idle period is indefinite.</summary>
    private const byte IndefiniteIdleRate = 0;

    /// <summary>Protocol value served by GET_PROTOCOL: 1, report protocol.</summary>
    private const byte ReportProtocol = 1;

    /// <summary>
    /// Initializes a HID device that serves <paramref name="descriptors"/>.
    /// </summary>
    /// <param name="descriptors">Descriptor set of the device.</param>
    protected VirtualHidDevice(UsbDescriptorSet descriptors)
        : base(descriptors)
    {
    }

    /// <summary>
    /// Handles one HID class request addressed to an interface: GET_REPORT, SET_REPORT, GET_IDLE,
    /// SET_IDLE, GET_PROTOCOL and SET_PROTOCOL. Requests of another type, another recipient, a
    /// direction that does not match the request, or an interface that is not declared as HID
    /// stall. A SET_REPORT whose data stage carries fewer bytes than the transfer declares is
    /// treated as truncated and stalls; bytes beyond the declared length are ignored.
    /// </summary>
    /// <param name="setup">Setup packet of the transfer.</param>
    /// <param name="outData">Payload of a host-to-device transfer; empty for device-to-host requests.</param>
    /// <param name="response">Buffer for the response of a device-to-host transfer.</param>
    /// <returns>The number of bytes written, or -1 for STALL.</returns>
    protected override int OnClassRequest(in UsbSetupPacket setup, ReadOnlySpan<byte> outData, Span<byte> response)
    {
        if ((setup.RequestType & RequestTypeMask) != RequestTypeClass
            || (setup.RequestType & RecipientMask) != RecipientInterface
            || !IsHidInterface((byte)setup.Index))
        {
            return Stall;
        }

        bool deviceToHost = (setup.RequestType & DirectionIn) != 0;
        byte reportType = (byte)(setup.Value >> 8);
        byte reportId = (byte)setup.Value;
        switch (setup.Request)
        {
            case RequestGetReport:
                if (!deviceToHost)
                {
                    return Stall;
                }

                return HandleGetReport(reportType, reportId, setup.Length, response);

            case RequestSetReport:
                if (deviceToHost || outData.Length < setup.Length)
                {
                    return Stall;
                }

                return HandleSetReport(reportType, reportId, setup.Length, outData);

            case RequestGetIdle:
                return deviceToHost ? WriteSingleByte(IndefiniteIdleRate, setup.Length, response) : Stall;

            case RequestSetIdle:
                return deviceToHost ? Stall : 0; // the device always reports at its endpoint interval

            case RequestGetProtocol:
                return deviceToHost ? WriteSingleByte(ReportProtocol, setup.Length, response) : Stall;

            case RequestSetProtocol:
                return deviceToHost ? Stall : 0; // report protocol is the only mode; the value is not tracked

            default:
                return Stall;
        }
    }

    /// <summary>
    /// Serves a feature report of <paramref name="reportId"/>; the base stalls every id. Subclasses
    /// override this to answer feature report reads and return -1 for unknown report ids.
    /// </summary>
    /// <param name="reportId">Report id of the requested report (wValue low byte).</param>
    /// <param name="destination">Buffer the feature report is written to.</param>
    /// <returns>The number of bytes written, or -1 for STALL. The base returns -1.</returns>
    protected virtual int ProvideFeatureReport(byte reportId, Span<byte> destination) => Stall;

    /// <summary>
    /// Handles a feature report write of <paramref name="reportId"/>; the base rejects every id.
    /// Subclasses override this to accept feature report writes and return <see langword="false"/>
    /// for unknown report ids.
    /// </summary>
    /// <param name="reportId">Report id of the written report (wValue low byte).</param>
    /// <param name="data">Payload of the report, without the report id.</param>
    /// <returns><see langword="true"/> when the report was handled; <see langword="false"/> for STALL. The base returns <see langword="false"/>.</returns>
    protected virtual bool OnFeatureReport(byte reportId, ReadOnlySpan<byte> data) => false;

    /// <summary>
    /// Serves GET_REPORT: an input report is pulled from <see cref="VirtualUsbDevice.ProvideInputReport"/>
    /// and stalls when the device has none to provide; a feature report is routed to
    /// <see cref="ProvideFeatureReport"/>. Requesting an output report stalls.
    /// </summary>
    /// <param name="reportType">Report type of the request (wValue high byte).</param>
    /// <param name="reportId">Report id of the request (wValue low byte).</param>
    /// <param name="requestedLength">Requested transfer length (wLength).</param>
    /// <param name="response">Buffer the response is written to.</param>
    /// <returns>The number of bytes written, or -1 for STALL.</returns>
    private int HandleGetReport(byte reportType, byte reportId, ushort requestedLength, Span<byte> response)
    {
        if (requestedLength == 0 || response.Length == 0)
        {
            return 0;
        }

        switch (reportType)
        {
            case ReportTypeInput:
                int count = ProvideInputReport(response);
                return count > 0 ? count : Stall;

            case ReportTypeFeature:
                return ProvideFeatureReport(reportId, response);

            default:
                return Stall;
        }
    }

    /// <summary>
    /// Serves SET_REPORT: an output report is pushed to <see cref="VirtualUsbDevice.OnOutputReport"/>
    /// (the host does not receive a report; the request succeeds), a feature report write is routed
    /// to <see cref="OnFeatureReport"/> and stalls when the device does not handle the report id.
    /// </summary>
    /// <param name="reportType">Report type of the request (wValue high byte).</param>
    /// <param name="reportId">Report id of the request (wValue low byte).</param>
    /// <param name="declaredLength">Data stage length declared by the request (wLength).</param>
    /// <param name="outData">Payload of the transfer, at least <paramref name="declaredLength"/> bytes long.</param>
    /// <returns>The number of bytes written, or -1 for STALL.</returns>
    private int HandleSetReport(byte reportType, byte reportId, ushort declaredLength, ReadOnlySpan<byte> outData)
    {
        ReadOnlySpan<byte> data = outData[..declaredLength];
        switch (reportType)
        {
            case ReportTypeOutput:
                OnOutputReport(data);
                return 0;

            case ReportTypeFeature:
                return OnFeatureReport(reportId, data) ? 0 : Stall;

            default:
                return Stall;
        }
    }

    /// <summary>
    /// Writes the one-byte response of GET_IDLE and GET_PROTOCOL; a zero-length transfer writes nothing.
    /// </summary>
    /// <param name="value">Byte value of the response.</param>
    /// <param name="requestedLength">Requested transfer length (wLength).</param>
    /// <param name="response">Buffer the response is written to.</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteSingleByte(byte value, ushort requestedLength, Span<byte> response)
    {
        if (requestedLength == 0 || response.Length == 0)
        {
            return 0;
        }

        response[0] = value;
        return 1;
    }

    /// <summary>
    /// Returns whether the configuration descriptor declares an interface with
    /// <paramref name="number"/> whose class is HID.
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
}