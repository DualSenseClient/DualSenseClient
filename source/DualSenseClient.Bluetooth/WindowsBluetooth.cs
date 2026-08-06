using System.ComponentModel;
using System.Runtime.InteropServices;
using DualSenseClient.Logging;

namespace DualSenseClient.Bluetooth;

/// <summary>
/// Windows implementation of <see cref="BluetoothService"/>.
/// Opens the local Bluetooth radios (bthprops.cpl) and sends
/// <c>IOCTL_BTH_DISCONNECT_DEVICE</c> (bthioctl.h) to drop the link to a classic
/// Bluetooth device without unpairing it.
/// </summary>
internal static class WindowsBluetooth
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("WindowsBluetooth");

    /// <summary>
    /// IOCTL_BTH_DISCONNECT_DEVICE = CTL_CODE(FILE_DEVICE_BLUETOOTH(0x41), 0x03,
    /// METHOD_BUFFERED, FILE_ANY_ACCESS). Input: the 8-byte BTH_ADDR of the remote device.
    /// </summary>
    private const uint IoctlBthDisconnectDevice = 0x0041000C;

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothFindRadioParams
    {
        public int dwSize;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref BluetoothFindRadioParams pbtfrp, out IntPtr phRadio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothFindNextRadio(IntPtr hFind, out IntPtr phRadio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothFindRadioClose(IntPtr hFind);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        ref ulong lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    /// <summary>
    /// Disconnects the device with the given address, trying every radio until one succeeds.
    /// </summary>
    /// <param name="address">The 48-bit Bluetooth address of the device to disconnect.</param>
    /// <returns><c>true</c> if a radio disconnected the device; otherwise, <c>false</c>.</returns>
    public static bool Disconnect(ulong address)
    {
        BluetoothFindRadioParams findParams = new BluetoothFindRadioParams { dwSize = Marshal.SizeOf<BluetoothFindRadioParams>() };
        IntPtr hFind = BluetoothFindFirstRadio(ref findParams, out IntPtr hRadio);
        if (hFind == IntPtr.Zero)
        {
            _log.Warning($"BluetoothFindFirstRadio failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            return false;
        }

        try
        {
            while (hRadio != IntPtr.Zero)
            {
                try
                {
                    if (DisconnectOnRadio(hRadio, address))
                    {
                        return true;
                    }
                }
                finally
                {
                    CloseHandle(hRadio);
                }

                if (!BluetoothFindNextRadio(hFind, out hRadio))
                {
                    break;
                }
            }
        }
        finally
        {
            BluetoothFindRadioClose(hFind);
        }

        _log.Warning($"Bluetooth disconnect failed for device 0x{address:X12}");
        return false;
    }

    /// <summary>
    /// Sends <c>IOCTL_BTH_DISCONNECT_DEVICE</c> to a single radio.
    /// </summary>
    private static bool DisconnectOnRadio(IntPtr hRadio, ulong address)
    {
        ulong remoteAddress = address;
        if (DeviceIoControl(hRadio, IoctlBthDisconnectDevice, ref remoteAddress, (uint)sizeof(ulong), IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            _log.Info($"Disconnected Bluetooth device 0x{address:X12}");
            return true;
        }

        _log.Debug($"IOCTL_BTH_DISCONNECT_DEVICE failed on radio: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        return false;
    }
}