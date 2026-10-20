using DualSenseClient.Emulation.UsbIp;

namespace DualSenseClient.Emulation.Core;

/// <summary>
/// One URB submitted by a USB/IP client, carrying the fields the session dispatches on plus the
/// completion state the request engine fills in: <see cref="Status"/> and
/// <see cref="ActualLength"/> for every transfer, <see cref="Data"/> for the payload of an OUT
/// transfer (set by the session when the URB is read) and for the response of an IN transfer
/// (set by the completer). Isochronous URBs additionally carry the frame metadata and one
/// <see cref="IsoPacketDescriptor"/> per packet.
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

    /// <summary>
    /// Initial frame of an isochronous transfer (usbip_header.start_frame); zero for non-ISO
    /// transfers. The scheduler does not interpret this field: every value, including
    /// <c>0xFFFFFFFF</c> and a future frame, schedules from the URB's arrival frame (a ruled
    /// deviation pending live observation). The server preserves and echoes the submitted value in
    /// RET_SUBMIT.
    /// </summary>
    public uint StartFrame { get; set; }

    /// <summary>Number of isochronous packets (usbip_header.number_of_packets); zero for non-ISO transfers.</summary>
    public uint NumberOfPackets { get; set; }

    /// <summary>Maximum time for the request on the server-side host controller (usbip_header.interval); zero for non-ISO transfers.</summary>
    public uint Interval { get; set; }

    /// <summary>Number of isochronous packets that completed with an error (RET_SUBMIT.error_count); zero for non-ISO transfers.</summary>
    public uint ErrorCount { get; set; }

    /// <summary>Per-packet isochronous completion descriptors carried after the URB header; null for non-ISO transfers.</summary>
    public IsoPacketDescriptor[]? IsoPackets { get; set; }
}