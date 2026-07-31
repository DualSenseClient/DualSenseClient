using System.Runtime.InteropServices;
using System.Text;
using DualSenseClient.Logging;
using SDL;

namespace DualSenseClient.Hid;

/// <summary>
/// Provides read/write access to a HID device over SDL3.
/// </summary>
public interface IHidDevice : IDisposable
{
    /// <summary>
    /// USB vendor ID.
    /// </summary>
    ushort VendorId { get; }

    /// <summary>
    /// USB product ID.
    /// </summary>
    ushort ProductId { get; }

    /// <summary>
    /// Platform device path used to open this device.
    /// </summary>
    string DevicePath { get; }

    /// <summary>
    /// Reads an input report with a timeout.
    /// </summary>
    int Read(byte[] buffer, int offset, int count, int timeoutMs);

    /// <summary>
    /// Reads an input report asynchronously (infinite timeout).
    /// </summary>
    Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct);

    /// <summary>
    /// Writes an output report.
    /// </summary>
    int Write(byte[] buffer, int offset, int count);

    /// <summary>
    /// Gets a feature report from the device.
    /// </summary>
    byte[] GetFeatureReport(byte reportId, int bufferSize = 64);

    /// <summary>
    /// Sends a feature report to the device.
    /// </summary>
    void SendFeatureReport(byte[] buffer, int offset, int count);

    /// <summary>
    /// Gets the human-readable product name.
    /// </summary>
    string GetProductName();

    /// <summary>
    /// Gets whether the device appears to be actively connected.
    /// Performs a probe read — may be slow.
    /// </summary>
    bool IsConnected { get; }
}

/// <summary>
/// SDL3-backed HID device implementation.
/// </summary>
public sealed class HidDevice : IHidDevice
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("HidDevice");

    /// <summary>
    /// The unmanaged SDL HID device handle, or <c>null</c> when the device is closed.
    /// </summary>
    private unsafe SDL_hid_device* _device;

    /// <summary>
    /// Non-zero once the device has been disposed.
    /// </summary>
    private int _disposed;

    /// <summary>
    /// Opens a HID device by its platform path.
    /// </summary>
    /// <param name="path">The platform device path.</param>
    /// <exception cref="HidException">Thrown when SDL_hid_open_path fails.</exception>
    internal unsafe HidDevice(string path)
    {
        DevicePath = path;

        int byteCount = Encoding.UTF8.GetByteCount(path);
        byte* pathPtr = stackalloc byte[byteCount + 1];
        fixed (char* src = path)
        {
            Encoding.UTF8.GetBytes(src, path.Length, pathPtr, byteCount);
        }
        pathPtr[byteCount] = 0;

        _device = SDL3.SDL_hid_open_path(pathPtr);

        if (_device == null)
        {
            _log.Error($"SDL_hid_open_path failed for '{path}'");
            throw new HidException($"SDL_hid_open_path failed for '{path}'");
        }

        _log.Debug($"Opened HID device '{path}'");
    }

    /// <summary>
    /// Opens a HID device by vendor ID, product ID, and optional serial number.
    /// </summary>
    /// <param name="vendorId">USB vendor ID.</param>
    /// <param name="productId">USB product ID.</param>
    /// <param name="serial">Optional serial number to disambiguate identical devices.</param>
    /// <exception cref="HidException">Thrown when SDL_hid_open fails.</exception>
    internal unsafe HidDevice(ushort vendorId, ushort productId, string? serial = null)
    {
        DevicePath = string.Empty;

        IntPtr serialPtr = IntPtr.Zero;
        try
        {
            if (serial != null)
            {
                serialPtr = Marshal.StringToCoTaskMemUni(serial);
            }

            _device = SDL3.SDL_hid_open(vendorId, productId, serialPtr);
        }
        finally
        {
            if (serialPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(serialPtr);
            }
        }

        if (_device == null)
        {
            _log.Error($"SDL_hid_open({vendorId:X4}, {productId:X4}) failed");
            throw new HidException($"SDL_hid_open({vendorId:X4}, {productId:X4}) failed");
        }

        _log.Debug($"Opened HID device {vendorId:X4}:{productId:X4}");
    }

    // ── Read ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public int Read(byte[] buffer, int offset, int count, int timeoutMs)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        int result;
        unsafe
        {
            fixed (byte* buf = &buffer[offset])
            {
                result = SDL3.SDL_hid_read_timeout(_device, buf, (nuint)count, timeoutMs);
            }
        }

        if (result < 0)
        {
            _log.Error($"SDL_hid_read_timeout failed on '{DevicePath}'");
            throw new HidException("SDL_hid_read_timeout failed");
        }

        _log.Trace($"Read {result} byte(s) from '{DevicePath}'");
        return result;
    }

    /// <inheritdoc/>
    public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<int>(ct);
        }

        return Task.Run(() => Read(buffer, offset, count, -1), ct);
    }

    // ── Write / Output Report ──────────────────────────────────

    /// <inheritdoc/>
    public int Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        int result;
        unsafe
        {
            fixed (byte* buf = &buffer[offset])
            {
                result = SDL3.SDL_hid_write(_device, buf, (nuint)count);
            }
        }

        if (result < 0)
        {
            _log.Error($"SDL_hid_write failed on '{DevicePath}'");
            throw new HidException("SDL_hid_write failed");
        }

        _log.Trace($"Wrote {result} byte(s) to '{DevicePath}'");
        return result;
    }

    // ── Feature Reports ────────────────────────────────────────

    /// <inheritdoc/>
    public byte[] GetFeatureReport(byte reportId, int bufferSize = 64)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        byte[] buffer = new byte[bufferSize];
        buffer[0] = reportId;

        int result;
        unsafe
        {
            fixed (byte* buf = buffer)
            {
                result = SDL3.SDL_hid_get_feature_report(_device, buf, (nuint)bufferSize);
            }
        }

        if (result < 0)
        {
            _log.Error($"SDL_hid_get_feature_report(0x{reportId:X2}) failed on '{DevicePath}'");
            throw new HidException($"SDL_hid_get_feature_report(0x{reportId:X2}) failed");
        }

        _log.Trace($"GetFeatureReport(0x{reportId:X2}) returned {result} byte(s) from '{DevicePath}'");
        return buffer[..result];
    }

    /// <inheritdoc/>
    public void SendFeatureReport(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        int result;
        unsafe
        {
            fixed (byte* buf = &buffer[offset])
            {
                result = SDL3.SDL_hid_send_feature_report(_device, buf, (nuint)count);
            }
        }

        if (result < 0)
        {
            _log.Error($"SDL_hid_send_feature_report failed on '{DevicePath}'");
            throw new HidException("SDL_hid_send_feature_report failed");
        }

        _log.Trace($"SendFeatureReport wrote {count} byte(s) to '{DevicePath}'");
    }

    // ── Info ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public unsafe string GetProductName()
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        char* buf = stackalloc char[256];
        int len = SDL3.SDL_hid_get_product_string(_device, (IntPtr)buf, 256);
        string name = len > 0 ? new string(buf, 0, len) : "Unknown";
        _log.Trace($"GetProductName on '{DevicePath}': \"{name}\"");
        return name;
    }

    /// <inheritdoc/>
    public ushort VendorId { get; init; }

    /// <inheritdoc/>
    public ushort ProductId { get; init; }

    /// <inheritdoc/>
    public string DevicePath { get; init; }

    // ── Connection Check ────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsConnected
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);

            byte[] buffer = new byte[64];
            int read;
            unsafe
            {
                fixed (byte* buf = buffer)
                {
                    read = SDL3.SDL_hid_read_timeout(_device, buf, (nuint)buffer.Length, 200);
                }
            }

            if (read <= 0)
            {
                _log.Trace($"IsConnected on '{DevicePath}': false (read returned {read})");
                return false;
            }

            for (int i = 0; i < read; i++)
            {
                if (buffer[i] != 0)
                {
                    _log.Trace($"IsConnected on '{DevicePath}': true");
                    return true;
                }
            }

            _log.Trace($"IsConnected on '{DevicePath}': false (all zeros)");
            return false;
        }
    }

    // ── Dispose ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _log.Debug($"Closing HID device '{DevicePath}'");

        unsafe
        {
            if (_device != null)
            {
                SDL3.SDL_hid_close(_device);
            }
            _device = null;
        }
    }
}