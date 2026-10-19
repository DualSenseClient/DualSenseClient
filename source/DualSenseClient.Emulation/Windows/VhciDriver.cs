using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DualSenseClient.Emulation.Windows;

/// <summary>
/// The native attach/detach seam of the usbip-win2 VHCI control device: opens the device interface
/// (<see cref="VhciDeviceLocator"/> finds its path) and drives the driver's plug-in/plug-out IOCTLs.
/// Implementations never throw: failures are signalled through an invalid handle or
/// <see langword="false"/> plus the best available Win32 error code.
/// </summary>
public interface IVhciDriver
{
    /// <summary>
    /// Opens the VHCI control device for synchronous I/O.
    /// </summary>
    /// <param name="devicePath">Device interface path of the VHCI control device.</param>
    /// <returns>
    /// An open handle, or an invalid handle when the device could not be opened (including a
    /// missing/broken driver); the caller checks <see cref="SafeHandle.IsInvalid"/>. Never throws.
    /// </returns>
    SafeFileHandle Open(string devicePath);

    /// <summary>
    /// Attaches the device described by <paramref name="request"/> and reports the vhci hub port
    /// the driver assigned to it.
    /// </summary>
    /// <param name="device">Handle of the VHCI control device from <see cref="Open"/>.</param>
    /// <param name="request">Remote device location to attach.</param>
    /// <param name="port">Driver-assigned hub port (>= 1); 0 on failure.</param>
    /// <param name="win32Error">Win32 error of the failed IOCTL; 0 on success. A managed failure is reported as the exception's HResult.</param>
    /// <returns><see langword="true"/> when the driver attached the device.</returns>
    bool TryConnect(SafeFileHandle device, VhciAttachRequest request, out uint port, out int win32Error);

    /// <summary>
    /// Detaches the device attached on the given vhci hub port.
    /// </summary>
    /// <param name="device">Handle of the VHCI control device from <see cref="Open"/>.</param>
    /// <param name="port">Hub port previously reported by <see cref="TryConnect"/>.</param>
    /// <param name="win32Error">Win32 error of the failed IOCTL; 0 on success. A managed failure is reported as the exception's HResult.</param>
    /// <returns><see langword="true"/> when the driver detached the port.</returns>
    bool TryDisconnect(SafeFileHandle device, uint port, out int win32Error);
}

/// <summary>
/// <see cref="IVhciDriver"/> backed by the real usbip-win2 VHCI control device P/Invoke
/// (<c>CreateFileW</c> + <c>DeviceIoControl</c>). The IOCTL request/response layouts and codes are
/// the recorded interface facts of usbip-win2 0.9.8.1 (see the compat notes): the driver assigns
/// the hub port during <c>PLUGIN_HARDWARE</c> and the caller reads it back from offset 4 of the
/// response image. A handle stays usable after a failed attach/detach; closing it is the caller's
/// decision.
/// </summary>
public sealed class VhciDriver : IVhciDriver
{
    /// <summary><c>GENERIC_READ</c> access for <c>CreateFileW</c> (the driver grants FILE_READ_DATA).</summary>
    private const uint GenericRead = 0x80000000;

    /// <summary><c>GENERIC_WRITE</c> access for <c>CreateFileW</c> (the driver grants FILE_WRITE_DATA).</summary>
    private const uint GenericWrite = 0x40000000;

    /// <summary><c>FILE_SHARE_READ</c>: other clients (e.g. usbip.exe) keep working concurrently.</summary>
    private const uint FileShareRead = 0x00000001;

    /// <summary><c>FILE_SHARE_WRITE</c>: other clients (e.g. usbip.exe) keep working concurrently.</summary>
    private const uint FileShareWrite = 0x00000002;

    /// <summary><c>OPEN_EXISTING</c>: the device must already be present.</summary>
    private const uint OpenExisting = 3;

    /// <summary>Recorded fact: offset 4 of the plugin_hardware response image holds the driver-assigned hub port.</summary>
    private const int AttachPortOffset = 4;

    /// <summary>Recorded fact: <c>sizeof(plugout_hardware)</c> on x64 (self size + int port).</summary>
    private const int DetachRequestSize = 8;

    /// <summary>Recorded fact: offset 4 of the plugout_hardware request image holds the hub port to detach.</summary>
    private const int DetachPortOffset = 4;

    /// <inheritdoc/>
    public SafeFileHandle Open(string devicePath)
    {
        try
        {
            return CreateFileW(devicePath, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        }
        catch (Exception)
        {
            // Never throws: a zero handle is invalid, so the caller sees plain "could not open".
            return new SafeFileHandle(IntPtr.Zero, true);
        }
    }

    /// <inheritdoc/>
    public bool TryConnect(SafeFileHandle device, VhciAttachRequest request, out uint port, out int win32Error)
    {
        port = 0;
        win32Error = 0;
        try
        {
            byte[] buffer = new byte[VhciAttachRequest.SizeInBytes];
            request.Write(buffer);
            if (!DeviceIoControl(device, VhciIoctl.PluginHardware, buffer, (uint)buffer.Length, buffer, (uint)buffer.Length, out uint _, IntPtr.Zero))
            {
                win32Error = Marshal.GetLastWin32Error();
                return false;
            }

            // Vendor contract: the driver assigns the port during the IOCTL and writes it into the
            // response image; zero means an error.
            port = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(AttachPortOffset));
            return port != 0;
        }
        catch (Exception exception)
        {
            port = 0;
            win32Error = exception.HResult;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryDisconnect(SafeFileHandle device, uint port, out int win32Error)
    {
        win32Error = 0;
        try
        {
            byte[] buffer = new byte[DetachRequestSize];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, DetachRequestSize);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(DetachPortOffset), port);
            if (!DeviceIoControl(device, VhciIoctl.PlugoutHardware, buffer, (uint)buffer.Length, buffer, (uint)buffer.Length, out uint _, IntPtr.Zero))
            {
                win32Error = Marshal.GetLastWin32Error();
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            win32Error = exception.HResult;
            return false;
        }
    }

    /// <summary>Opens a device interface path (<c>CreateFileW</c>); a failed open returns an invalid handle.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    /// <summary>
    /// Sends a control code to the device driver (<c>DeviceIoControl</c>); METHOD_BUFFERED requests
    /// pass the same buffer for input and output.
    /// </summary>
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        byte[] inBuffer,
        uint inBufferSize,
        byte[] outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);
}