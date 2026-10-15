using DualSenseClient.Hid;

namespace DualSenseClient.Controllers.DualSense.Input;

/// <summary>
/// Builds the exact 64B USB input report 0x01 from a physical HID read,
/// forwarding the physical bytes verbatim (seq, touch
/// timestamps, trigger echo, headset bits, AES tail preserved).
/// USB reads already carry 0x01; Bluetooth 0x31 reads strip to the same shape.
/// </summary>
public static class DualSenseRawReport
{
    /// <summary>
    /// Size of a USB input report 0x01 (report ID + 63B payload).
    /// </summary>
    public const int UsbReportSize = 64;

    /// <summary>
    /// Tries to build the 64B USB raw report from a HID read buffer.
    /// </summary>
    /// <param name="hidBuffer">Raw HID read buffer.</param>
    /// <param name="bytesRead">Valid bytes in <paramref name="hidBuffer"/>.</param>
    /// <param name="connection">Physical transport.</param>
    /// <param name="usbRaw">64B USB report on success.</param>
    /// <returns>True when a verbatim report could be built.</returns>
    public static bool TryBuildUsbRaw64(byte[] hidBuffer, int bytesRead, ConnectionType connection, out byte[]? usbRaw)
    {
        usbRaw = null;
        if (hidBuffer is null || bytesRead <= 0)
        {
            return false;
        }

        if (connection == ConnectionType.Usb)
        {
            if (bytesRead < UsbReportSize || hidBuffer[0] != 0x01)
            {
                return false;
            }

            usbRaw = new byte[UsbReportSize];
            Buffer.BlockCopy(hidBuffer, 0, usbRaw, 0, UsbReportSize);
            return true;
        }

        if (connection == ConnectionType.Bluetooth)
        {
            // hidapi BT: [0x31, flags, 63B payload]; USB: [0x01 + 63B payload].
            const int btMin = 2 + 63;
            if (bytesRead < btMin || hidBuffer[0] != 0x31)
            {
                return false;
            }

            usbRaw = new byte[UsbReportSize];
            usbRaw[0] = 0x01;
            Buffer.BlockCopy(hidBuffer, 2, usbRaw, 1, 63);
            return true;
        }

        return false;
    }
}