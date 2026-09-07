using System.Runtime.InteropServices;
using System.Text;
using DualSenseClient.Hid.Interop;
using DualSenseClient.Logging;

namespace DualSenseClient.Hid;

/// <summary>
/// Provides read/write access to a HID device over HIDAPI.
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
/// HIDAPI-backed HID device implementation.
/// </summary>
public sealed class HidDevice : IHidDevice
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("HidDevice");

    /// <summary>
    /// The unmanaged native HID device handle, or <c>null</c> when the device is closed.
    /// </summary>
    private unsafe HidDeviceHandle* _device;

    /// <summary>
    /// Non-zero once the device has been disposed.
    /// </summary>
    private int _disposed;

    /// <summary>
    /// Opens a HID device by its platform path.
    /// </summary>
    /// <param name="path">The platform device path.</param>
    /// <exception cref="HidException">Thrown when hid_open_path fails.</exception>
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

        _device = HidApi.OpenPath(pathPtr);

        if (_device == null)
        {
            string error = HidApi.GetError();
            _log.Error($"hid_open_path failed for '{path}': {error}");
            throw new HidException($"hid_open_path failed for '{path}': {error}");
        }

        _log.Debug($"Opened HID device '{path}'");
    }

    // ── Read ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public int Read(byte[] buffer, int offset, int count, int timeoutMs)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        int result;
        string error = string.Empty;
        unsafe
        {
            fixed (byte* buf = &buffer[offset])
            {
                result = HidApi.ReadTimeout(_device, buf, (nuint)count, timeoutMs);
            }

            if (result < 0)
            {
                error = HidApi.GetError(_device);
            }
        }

        if (result < 0)
        {
            // A failed read is the normal symptom of a disconnected controller;
            // the read loop reports it at the appropriate level.
            _log.Debug($"hid_read_timeout failed on '{DevicePath}': {error}");
            throw new HidException("hid_read_timeout failed");
        }

        if (DualSenseClientLogger.MinimumLevel <= LogLevel.Trace)
        {
            _log.Trace($"Read {result} byte(s) from '{DevicePath}'");
        }

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
        string error = string.Empty;
        unsafe
        {
            fixed (byte* buf = &buffer[offset])
            {
                result = HidApi.Write(_device, buf, (nuint)count);
            }

            if (result < 0)
            {
                error = HidApi.GetError(_device);
            }
        }

        if (result < 0)
        {
            _log.Error($"hid_write failed on '{DevicePath}': {error}");
            throw new HidException($"hid_write failed: {error}");
        }

        if (DualSenseClientLogger.MinimumLevel <= LogLevel.Trace)
        {
            _log.Trace($"Wrote {result} byte(s) to '{DevicePath}'");
        }

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
        string error = string.Empty;
        unsafe
        {
            fixed (byte* buf = buffer)
            {
                result = HidApi.GetFeatureReport(_device, buf, (nuint)bufferSize);
            }

            if (result < 0)
            {
                error = HidApi.GetError(_device);
            }
        }

        if (result < 0)
        {
            _log.Error($"hid_get_feature_report(0x{reportId:X2}) failed on '{DevicePath}': {error}");
            throw new HidException($"hid_get_feature_report(0x{reportId:X2}) failed: {error}");
        }

        _log.Trace($"GetFeatureReport(0x{reportId:X2}) returned {result} byte(s) from '{DevicePath}'");
        return buffer[..result];
    }

    /// <inheritdoc/>
    public void SendFeatureReport(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        int result;
        string error = string.Empty;
        unsafe
        {
            fixed (byte* buf = &buffer[offset])
            {
                result = HidApi.SendFeatureReport(_device, buf, (nuint)count);
            }

            if (result < 0)
            {
                error = HidApi.GetError(_device);
            }
        }

        if (result < 0)
        {
            _log.Error($"hid_send_feature_report failed on '{DevicePath}': {error}");
            throw new HidException($"hid_send_feature_report failed: {error}");
        }

        _log.Trace($"SendFeatureReport wrote {count} byte(s) to '{DevicePath}'");
    }

    // ── Info ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public unsafe string GetProductName()
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        string name;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            char* buf = stackalloc char[256];
            int result = HidApi.GetProductString(_device, (IntPtr)buf, 256);
            name = result < 0 ? string.Empty : HidStringMarshal.BufferToString((IntPtr)buf, result);
        }
        else
        {
            // Linux wchar_t is 4 bytes (UTF-32), unlike Windows UTF-16.
            int* buf = stackalloc int[256];
            int result = HidApi.GetProductString(_device, (IntPtr)buf, 256);
            name = result < 0 ? string.Empty : HidStringMarshal.BufferToString((IntPtr)buf, result);
        }

        if (string.IsNullOrEmpty(name))
        {
            name = "Unknown";
        }

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
                    read = HidApi.ReadTimeout(_device, buf, (nuint)buffer.Length, 200);
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
                HidApi.Close(_device);
            }

            _device = null;
        }
    }
}