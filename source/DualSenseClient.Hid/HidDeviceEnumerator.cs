using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using DualSenseClient.Logging;
using SDL;

namespace DualSenseClient.Hid;

/// <summary>
/// Enumerates HID devices and opens them via SDL3.
/// </summary>
public interface IHidDeviceEnumerator : IDisposable
{
    /// <summary>
    /// Enumerates all HID devices, optionally filtered by VID/PID.
    /// </summary>
    IReadOnlyList<IHidDeviceInfo> Enumerate(ushort? vendorId = null, ushort? productId = null);

    /// <summary>
    /// Enumerates HID devices matching any of the given vendor/product ID pairs.
    /// </summary>
    IReadOnlyList<IHidDeviceInfo> Enumerate(IEnumerable<(ushort VendorId, ushort ProductId)> deviceIds);

    /// <summary>
    /// Enumerates all HID devices matching the given VID/PID, including devices hidden
    /// by <see cref="ExcludeDevice"/>. Used to rediscover paths that were excluded by a
    /// previous virtual controller teardown: USB/IP reattaches at the same bus/port, so
    /// a re-created virtual device can reuse the exact devnode path of its removed
    /// predecessor.
    /// </summary>
    IReadOnlyList<IHidDeviceInfo> EnumerateIncludingExcluded(ushort? vendorId = null, ushort? productId = null);

    /// <summary>
    /// Opens a HID device by its platform path.
    /// </summary>
    IHidDevice OpenDevice(string path);

    /// <summary>
    /// Starts a background watcher that polls <c>SDL_hid_device_change_count</c>
    /// at the given interval and raises <see cref="DeviceConnected"/> /
    /// <see cref="DeviceDisconnected"/> when the device list changes.
    /// </summary>
    void StartWatching(int intervalMs = 1000);

    /// <summary>
    /// Stops the background watcher.
    /// </summary>
    void StopWatching();

    /// <summary>
    /// Excludes the device at the given path from all future enumeration results,
    /// so devices created by this application (e.g. virtual controllers) are not
    /// seen as real hardware.
    /// </summary>
    /// <param name="path">The device path to exclude.</param>
    void ExcludeDevice(string path);

    /// <summary>
    /// Removes a previously excluded device path, making the device appear in
    /// enumeration results again.
    /// </summary>
    /// <param name="path">The device path to unexclude.</param>
    void RemoveExcludedDevice(string path);

    /// <summary>
    /// Raised when a HID device connects.
    /// </summary>
    event EventHandler<DeviceConnectionEventArgs>? DeviceConnected;

    /// <summary>
    /// Raised when a HID device disconnects.
    /// </summary>
    event EventHandler<DeviceConnectionEventArgs>? DeviceDisconnected;
}

/// <summary>
/// SDL3-backed HID device enumerator.
/// </summary>
public class HidDeviceEnumerator : IHidDeviceEnumerator
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("HidDeviceEnumerator");

    /// <summary>
    /// Check to see if SDL Hid is initialized
    /// </summary>
    private int _initialized;

    /// <inheritdoc/>
    public event EventHandler<DeviceConnectionEventArgs>? DeviceConnected;

    /// <inheritdoc/>
    public event EventHandler<DeviceConnectionEventArgs>? DeviceDisconnected;

    /// <summary>
    /// The background polling timer, or <c>null</c> while not watching.
    /// </summary>
    private Timer? _watcher;

    /// <summary>
    /// The last observed set of devices, used to detect connections and disconnections.
    /// </summary>
    private List<IHidDeviceInfo>? _previousSnapshot;

    /// <summary>
    /// Devices seen on a poll but not yet reported as connected, keyed by path.
    /// A device is only reported after it is observed on a second consecutive poll,
    /// so devices the application itself created and then excluded (virtual
    /// controllers) are never surfaced as phantom connections.
    /// </summary>
    private readonly Dictionary<string, IHidDeviceInfo> _pendingConnected = new Dictionary<string, IHidDeviceInfo>(StringComparer.Ordinal);

    /// <summary>
    /// Synchronizes access to the device watcher state.
    /// </summary>
    private readonly Lock _watchLock = new Lock();

    /// <summary>
    /// Synchronizes access to <see cref="_excludedPaths"/>.
    /// </summary>
    private readonly Lock _exclusionLock = new Lock();

    /// <summary>
    /// Device paths hidden from enumeration results.
    /// </summary>
    private readonly HashSet<string> _excludedPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc/>
    public void ExcludeDevice(string path)
    {
        lock (_exclusionLock)
        {
            if (_excludedPaths.Add(path))
            {
                _log.Debug($"Excluding device from enumeration: {path}");
            }
        }
    }

    /// <inheritdoc/>
    public void RemoveExcludedDevice(string path)
    {
        lock (_exclusionLock)
        {
            if (_excludedPaths.Remove(path))
            {
                _log.Debug($"Device no longer excluded from enumeration: {path}");
            }
        }
    }

    /// <summary>
    /// Checks whether the given device path has been excluded from enumeration.
    /// </summary>
    private bool IsExcluded(string path)
    {
        lock (_exclusionLock)
        {
            return _excludedPaths.Contains(path);
        }
    }

    /// <inheritdoc/>
    public void StartWatching(int intervalMs = 1000)
    {
        lock (_watchLock)
        {
            if (_watcher != null)
            {
                return;
            }

            _log.Debug($"Starting device watcher (interval={intervalMs}ms)");
            _previousSnapshot = [.. Enumerate()];
            _watcher = new Timer(WatchTick, null, intervalMs, intervalMs);
        }
    }

    /// <inheritdoc/>
    public void StopWatching()
    {
        lock (_watchLock)
        {
            if (_watcher == null)
            {
                return;
            }

            _log.Debug("Stopping device watcher");
            _watcher.Dispose();
            _watcher = null;
            _previousSnapshot = null;
            _pendingConnected.Clear();
        }
    }

    /// <summary>
    /// Timer callback. Re-enumerates active devices and fires <see cref="DeviceConnected"/>
    /// or <see cref="DeviceDisconnected"/> when the set of reachable devices changes.
    /// USB devices are trusted at face value; Bluetooth devices are probed in parallel
    /// to confirm they are still responding. Newly seen devices are only reported as
    /// connected once they have been observed on two consecutive polls, so devices the
    /// app itself created and excluded (virtual controllers) never fire
    /// <see cref="DeviceConnected"/>.
    /// </summary>
    private void WatchTick(object? state)
    {
        List<IHidDeviceInfo> current = [.. Enumerate()];

        lock (_watchLock)
        {
            if (_previousSnapshot == null)
            {
                _previousSnapshot = current;
                return;
            }

            HashSet<string> prevPaths = new HashSet<string>(_previousSnapshot.Select(d => d.Path));
            HashSet<string> currPaths = new HashSet<string>(current.Select(d => d.Path));

            // Confirm devices pending from the previous poll that are still present;
            // devices that vanished are left for the disconnect pass below.
            foreach (KeyValuePair<string, IHidDeviceInfo> pair in _pendingConnected.ToList())
            {
                if (!currPaths.Contains(pair.Key))
                {
                    continue;
                }

                _pendingConnected.Remove(pair.Key);
                _log.Info($"Device connected: {pair.Value.ProductName} (VID=0x{pair.Value.VendorId:X4}, PID=0x{pair.Value.ProductId:X4})");
                try
                {
                    DeviceConnected?.Invoke(this, new DeviceConnectionEventArgs(DeviceChangeType.Connected, pair.Value));
                }
                catch (Exception ex)
                {
                    _log.Warning($"DeviceConnected handler threw: {ex.Message}");
                }
            }

            // First observation of a device: remember it, but only report it after it
            // is seen again on the next poll, so devices the app itself created and
            // then excluded (virtual controllers) never surface as connections.
            foreach (IHidDeviceInfo device in current.Where(device => !prevPaths.Contains(device.Path)))
            {
                _log.Debug(
                    $"Device connected (awaiting confirmation): {device.ProductName} (VID=0x{device.VendorId:X4}, PID=0x{device.ProductId:X4}, path={device.Path})");
                _pendingConnected[device.Path] = device;
            }

            foreach (IHidDeviceInfo device in _previousSnapshot.Where(device => !currPaths.Contains(device.Path)))
            {
                if (_pendingConnected.Remove(device.Path))
                {
                    // Never confirmed, so no connection event was raised for it.
                    continue;
                }

                _log.Info($"Device disconnected: {device.ProductName} (VID=0x{device.VendorId:X4}, PID=0x{device.ProductId:X4})");
                try
                {
                    DeviceDisconnected?.Invoke(this, new DeviceConnectionEventArgs(DeviceChangeType.Disconnected, device));
                }
                catch (Exception ex)
                {
                    _log.Warning($"DeviceDisconnected handler threw: {ex.Message}");
                }
            }

            _previousSnapshot = current;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<IHidDeviceInfo> Enumerate(ushort? vendorId = null, ushort? productId = null)
    {
        _log.Debug($"Enumerating devices (vendorId=0x{vendorId:X4}, productId=0x{productId:X4})");
        List<IHidDeviceInfo> all = NativeEnumerate(vendorId, productId);
        all.RemoveAll(device => IsExcluded(device.Path));
        return FilterConnected(all);
    }

    /// <inheritdoc/>
    public IReadOnlyList<IHidDeviceInfo> EnumerateIncludingExcluded(ushort? vendorId = null, ushort? productId = null)
    {
        _log.Debug($"Enumerating devices including excluded (vendorId=0x{vendorId:X4}, productId=0x{productId:X4})");
        return FilterConnected(NativeEnumerate(vendorId, productId));
    }

    /// <summary>
    /// Splits USB and Bluetooth devices: USB passes through, Bluetooth devices are
    /// probed in parallel for liveness.
    /// </summary>
    private List<IHidDeviceInfo> FilterConnected(List<IHidDeviceInfo> all)
    {
        // Split USB and BT — USB passes through, BT probes in parallel for liveness.
        List<IHidDeviceInfo> result = new List<IHidDeviceInfo>(all.Count);
        List<IHidDeviceInfo> btDevices = new List<IHidDeviceInfo>(all.Count);

        foreach (IHidDeviceInfo device in all)
        {
            if (device.BusType == ConnectionType.Bluetooth)
            {
                btDevices.Add(device);
            }
            else
            {
                result.Add(device);
            }
        }

        if (btDevices.Count > 0)
        {
            ConcurrentDictionary<string, bool> connected = new ConcurrentDictionary<string, bool>();
            Parallel.ForEach(btDevices, device =>
            {
                if (!IsDeviceConnected(device.Path))
                {
                    _log.Debug($"Filtered out disconnected Bluetooth device: {device.ProductName} (VID=0x{device.VendorId:X4}, PID=0x{device.ProductId:X4})");
                    return;
                }

                connected.TryAdd(device.Path, true);
            });

            /*
            foreach (IHidDeviceInfo device in btDevices)
            {
                if (connected.ContainsKey(device.Path))
                {
                    result.Add(device);
                }
            }
            */
            result.AddRange(btDevices.Where(device => connected.ContainsKey(device.Path)));
        }

        _log.Debug($"Enumerate returned {result.Count} device(s) (filtered from {all.Count})");
        return result;
    }

    /// <inheritdoc/>
    public IReadOnlyList<IHidDeviceInfo> Enumerate(IEnumerable<(ushort VendorId, ushort ProductId)> deviceIds)
    {
        HashSet<(ushort, ushort)> filter = new HashSet<(ushort, ushort)>(deviceIds);
        _log.Debug($"Enumerating with {filter.Count} VID/PID filter(s)");

        if (filter.Count == 0)
        {
            _log.Debug("Filter list is empty, returning empty result");
            return [];
        }

        List<IHidDeviceInfo> result = Enumerate().Where(d => filter.Contains((d.VendorId, d.ProductId))).ToList();
        _log.Debug($"Filtered enumerate returned {result.Count} device(s)");
        return result;
    }

    /// <summary>
    /// Calls into SDL3 to enumerate HID devices matching the given VID/PID filter.
    /// </summary>
    /// <param name="vendorId">Optional USB vendor ID to filter by.</param>
    /// <param name="productId">Optional USB product ID to filter by.</param>
    /// <returns>A list of matching <see cref="IHidDeviceInfo"/> instances.</returns>
    private List<IHidDeviceInfo> NativeEnumerate(ushort? vendorId, ushort? productId)
    {
        EnsureInitialized();

        List<IHidDeviceInfo> result = new List<IHidDeviceInfo>();

        unsafe
        {
            SDL_hid_device_info* devices = SDL3.SDL_hid_enumerate(vendorId ?? 0, productId ?? 0);

            int count = 0;
            for (SDL_hid_device_info* cur = devices; cur != null; cur = cur->next)
            {
                string path = cur->path != null ? Utf8ToString(cur->path) : string.Empty;
                string name = PtrToStringWchar(cur->product_string);

                HidUsageId usage = (HidUsageId)cur->usage;
                if (usage == HidUsageId.Unknown)
                {
                    _log.Trace(
                        $"  Skipped {name} (VID=0x{cur->vendor_id:X4}, PID=0x{cur->product_id:X4}, usage=0x{cur->usage:X4}) — not a gamepad or joystick");
                    continue;
                }

                count++;
                _log.Debug(
                    $"  [{count}] {name} (VID=0x{cur->vendor_id:X4}, PID=0x{cur->product_id:X4}, bus={cur->bus_type}, usagePage=0x{cur->usage_page:X4}, usage=0x{cur->usage:X4}, path={path})");

                result.Add(new HidDeviceInfo
                {
                    Path = path,
                    VendorId = cur->vendor_id,
                    ProductId = cur->product_id,
                    ProductName = name,
                    Manufacturer = PtrToStringWchar(cur->manufacturer_string),
                    InterfaceNumber = cur->interface_number,
                    UsagePage = cur->usage_page,
                    Usage = usage,
                    BusType = cur->bus_type switch
                    {
                        SDL_hid_bus_type.SDL_HID_API_BUS_USB => ConnectionType.Usb,
                        SDL_hid_bus_type.SDL_HID_API_BUS_BLUETOOTH => ConnectionType.Bluetooth,
                        _ => ConnectionType.Unknown
                    }
                });
            }

            _log.Debug($"SDL_hid_enumerate returned {count} device(s)");
            SDL3.SDL_hid_free_enumeration(devices);
        }

        return result;
    }

    /// <summary>
    /// Ensures the SDL HID subsystem has been initialized.
    /// Safe to call multiple times; only the first call performs initialization.
    /// </summary>
    /// <exception cref="HidException">Thrown when SDL_hid_init fails.</exception>
    private void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            _log.Debug("Initializing SDL HID subsystem");
            if (SDL3.SDL_hid_init() != 0)
            {
                _log.Error("SDL_hid_init failed");
                throw new HidException("SDL_hid_init failed");
            }
        }
    }

    /// <summary>
    /// Converts a native <c>wchar_t*</c> (SDL hidapi) to a managed string, handling
    /// platform wchar_t size: 2 bytes UTF-16 on Windows, 4 bytes UTF-32 on Linux/macOS.
    /// </summary>
    private static unsafe string PtrToStringWchar(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return string.Empty;
        }

        if (OperatingSystem.IsWindows())
        {
            return Marshal.PtrToStringUni(ptr) ?? string.Empty;
        }

        // Linux/macOS: wchar_t is 4 bytes UTF-32.
        int* p = (int*)ptr;
        int len = 0;
        while (p[len] != 0)
        {
            len++;
        }

        if (len == 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder(len);
        for (int i = 0; i < len; i++)
        {
            int cp = p[i];
            if (cp < 0 || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
            {
                continue;
            }

            sb.Append(char.ConvertFromUtf32(cp));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts a null-terminated UTF-8 byte pointer into a managed <see cref="string"/>.
    /// </summary>
    /// <param name="ptr">Pointer to a null-terminated UTF-8 string, or <c>null</c>.</param>
    /// <returns>The decoded string, or <see cref="string.Empty"/> if the pointer is <c>null</c>.</returns>
    private static unsafe string Utf8ToString(byte* ptr)
    {
        if (ptr == null)
        {
            return string.Empty;
        }

        int len = 0;
        while (ptr[len] != 0)
        {
            len++;
        }

        return len > 0 ? Encoding.UTF8.GetString(ptr, len) : string.Empty;
    }

    /// <summary>
    /// Checks whether a Bluetooth HID device at the given path is currently connected
    /// by attempting to open it.
    /// </summary>
    /// <param name="devicePath">The platform device path.</param>
    /// <returns><c>true</c> if the device can be opened; otherwise, <c>false</c>.</returns>
    private bool IsDeviceConnected(string devicePath)
    {
        try
        {
            using HidDevice device = new HidDevice(devicePath);
            bool connected = device.IsConnected;
            _log.Trace($"Device '{devicePath}' connected: {connected}");
            return connected;
        }
        catch (Exception ex)
        {
            _log.Trace($"Device '{devicePath}' not reachable: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public IHidDevice OpenDevice(string path)
    {
        EnsureInitialized();
        _log.Debug($"Opening HID device '{path}'");
        return new HidDevice(path);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        StopWatching();

        if (Interlocked.Exchange(ref _initialized, 0) == 1)
        {
            _log.Debug("Shutting down SDL HID subsystem");
            SDL3.SDL_hid_exit();
        }
    }
}