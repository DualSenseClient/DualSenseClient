using DualSenseClient.Emulation.UsbIp;

namespace DualSenseClient.Emulation.Core;

/// <summary>
/// One URB submitted by a USB/IP client, carrying the fields the session dispatches on plus the
/// completion state the request engine fills in: <see cref="Status"/> and
/// <see cref="ActualLength"/> for every transfer, <see cref="Data"/> for the payload of an OUT
/// transfer (set by the session when the URB is read) and for the response of an IN transfer
/// (set by the completer).
/// </summary>
public sealed class UsbTransfer
{
    /// <summary>Sequence number identifying the URB on the connection (usbip_header.seqnum).</summary>
    public uint SeqNum { get; set; }

    /// <summary>Transfer direction of the URB (usbip_header.direction).</summary>
    public UsbIpDirection Direction { get; set; }

    /// <summary>
    /// Endpoint address of the URB: endpoint number in bits 3..0 and direction in bit 7
    /// (bEndpointAddress); 0 selects the control endpoint.
    /// </summary>
    public byte EndpointAddress { get; set; }

    /// <summary>Maximum number of bytes the client may transfer (usbip_header.transfer_buffer_length).</summary>
    public int MaxTransferLength { get; set; }

    /// <summary>
    /// Payload of the transfer: the host-to-device bytes of an OUT transfer, the device-to-host
    /// bytes of a completed IN transfer; null when there is none.
    /// </summary>
    public byte[]? Data { get; set; }

    /// <summary>Completion status of the transfer: zero on success, a negative errno otherwise (RET_SUBMIT.status).</summary>
    public int Status { get; set; }

    /// <summary>Number of bytes actually transferred (RET_SUBMIT.actual_length).</summary>
    public int ActualLength { get; set; }
}