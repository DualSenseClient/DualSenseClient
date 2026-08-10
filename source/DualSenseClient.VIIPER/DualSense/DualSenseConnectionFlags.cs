namespace DualSenseClient.VIIPER.DualSense;

/// <summary>
/// DualSense input-report connection status byte flags.
/// </summary>
[Flags]
public enum DualSenseConnectionFlags : byte
{
    Headphone = 0x01,
    Mic = 0x02,
    MicMuted = 0x04,
    UsbData = 0x08,
    UsbPower = 0x10
}