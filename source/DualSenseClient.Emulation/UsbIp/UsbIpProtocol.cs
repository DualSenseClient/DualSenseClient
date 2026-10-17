namespace DualSenseClient.Emulation.UsbIp;

/// <summary>
/// Wire constants of the USB/IP protocol, version 1.1.1, used by the device-side
/// implementation. Values are verified against the kernel protocol documentation
/// (https://www.kernel.org/doc/html/latest/usb/usbip_protocol.html).
/// Protocol fields are serialized in network (big-endian) byte order; the 8-byte
/// USB setup packet is carried verbatim in little-endian USB byte order.
/// </summary>
public static class UsbIpProtocol
{
    /// <summary>Protocol version 1.1.1 shared by every message.</summary>
    public const ushort Version = 0x0111;

    /// <summary>Command code of OP_REQ_DEVLIST: retrieve the list of exported USB devices.</summary>
    public const ushort OpRequestDevList = 0x8005;

    /// <summary>Reply code of OP_REP_DEVLIST: the list of exported USB devices.</summary>
    public const ushort OpReplyDevList = 0x0005;

    /// <summary>Command code of OP_REQ_IMPORT: import a remote USB device.</summary>
    public const ushort OpRequestImport = 0x8003;

    /// <summary>Reply code of OP_REP_IMPORT: reply to an import request.</summary>
    public const ushort OpReplyImport = 0x0003;

    /// <summary>Command code of USBIP_CMD_SUBMIT: submit an URB.</summary>
    public const uint CommandSubmit = 1;

    /// <summary>Command code of USBIP_CMD_UNLINK: unlink a previously submitted URB.</summary>
    public const uint CommandUnlink = 2;

    /// <summary>Command code of USBIP_RET_SUBMIT: reply to a submitted URB.</summary>
    public const uint CommandRetSubmit = 3;

    /// <summary>Command code of USBIP_RET_UNLINK: reply to an URB unlink request.</summary>
    public const uint CommandRetUnlink = 4;

    /// <summary>Size in bytes of the common URB header (usbip_header) shared by the four URB commands.</summary>
    public const int HeaderSize = 48;

    /// <summary>Size in bytes of the op_common header (version, code, status) of the handshake messages.</summary>
    public const int OpCommonSize = 8;
}

/// <summary>Transfer direction carried in the direction field of the common URB header.</summary>
public enum UsbIpDirection
{
    /// <summary>Host to device (USBIP_DIR_OUT).</summary>
    Out = 0,

    /// <summary>Device to host (USBIP_DIR_IN).</summary>
    In = 1
}

/// <summary>URB command codes carried in the command field of the common URB header.</summary>
public enum UsbIpCommand : uint
{
    /// <summary>USBIP_CMD_SUBMIT: submit an URB.</summary>
    Submit = 1,

    /// <summary>USBIP_CMD_UNLINK: unlink a previously submitted URB.</summary>
    Unlink = 2
}

/// <summary>
/// Status values carried in USBIP_RET_SUBMIT and USBIP_RET_UNLINK.
/// The values are the negative errno codes the USB/IP protocol reuses.
/// </summary>
public static class UsbIpStatus
{
    /// <summary>Transfer completed successfully.</summary>
    public const int Success = 0;

    /// <summary>Endpoint stalled (EPIPE).</summary>
    public const int Stall = -32;

    /// <summary>No entry found (ENOENT); used for unknown bus ids and unknown unlink targets.</summary>
    public const int NoEntry = -2;

    /// <summary>Device or session shut down (ESHUTDOWN).</summary>
    public const int Shutdown = -108;
}