using System.Collections.Concurrent;
using DualSenseClient.Hid.Interop;
using DualSenseClient.Logging;

namespace DualSenseClient.Hid;

/// <summary>
/// Enumerates HID devices and opens them via HIDAPI.
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
    /// Starts a background watcher that re-enumerates devices at the given interval
    /// and raises <see cref="DeviceConnected"/> / <see cref="DeviceDisconnected"/>
    /// when the device list changes.
    /// </summary>
    void StartWatching(int intervalMs = 1000);

    /// <summary>
    /// Starts a background watcher that only looks at devices matching the given vendor/product
    /// ID pairs. Unrelated HID devices (mice, keyboards) are never opened or queried, which
    /// matters because some firmware resets when its string descriptors are read repeatedly.
    /// </summary>
    /// <remarks>
    /// The default implementation ignores the filter so existing implementations keep compiling.
    /// </remarks>
    void StartWatching(IEnumerable<(ushort VendorId, ushort ProductId)> deviceIds, int intervalMs = 1000) => StartWatching(intervalMs);

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
/// HIDAPI-backed HID device enumerator.
/// </summary>
public class HidDeviceEnumerator : IHidDeviceEnumerator
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("HidDeviceEnumerator");

    /// <summary>
    /// Check to see if HIDAPI is initialized
    /// </summary>
    private int _initialized;

    /// <inheritdoc/>
    public event EventHandler<DeviceConnectionEventArgs>? DeviceConnected;

    /// <inheritdoc/>
    public event EventHandler<DeviceConnectionEventArgs>? DeviceDisconnected;

    /// <summary>
    /// The watcher thread, or <c>null</c> while not watching.
    /// </summary>
    private Thread? _watchThread;

    /// <summary>
    /// Cancels the watcher thread. Disposed by the thread itself when it exits.
    /// </summary>
    private CancellationTokenSource? _watchCts;

    /// <summary>
    /// Queries HIDAPI. A field only so tests can supply devices without native hardware or a native library.
    /// </summary>
    private readonly Func<ushort?, ushort?, List<IHidDeviceInfo>> _enumerateNative;

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
    private readonly Dictionary<string, IHidDeviceInfo> _pendingConnected = new Dictionary<string, IHidDeviceInfo>(StringComparer.OrdinalIgnoreCase);

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
    private readonly HashSet<string> _excludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates an enumerator that queries HIDAPI.
    /// </summary>
    public HidDeviceEnumerator() : this(null)
    {
    }

    /// <summary>
    /// Creates an enumerator with a replacement HIDAPI query, so tests can supply devices.
    /// </summary>
    /// <param name="enumerateNative">Replaces the HIDAPI query, or <c>null</c> to use HIDAPI.</param>
    internal HidDeviceEnumerator(Func<ushort?, ushort?, List<IHidDeviceInfo>>? enumerateNative)
    {
        _enumerateNative = enumerateNative ?? new Func<ushort?, ushort?, List<IHidDeviceInfo>>(NativeEnumerate);
    }

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
    public void StartWatching(int intervalMs = 1000) => StartWatchingCore(null, intervalMs);

    /// <inheritdoc/>
    public void StartWatching(IEnumerable<(ushort VendorId, ushort ProductId)> deviceIds, int intervalMs = 1000)
        => StartWatchingCore(new HashSet<(ushort VendorId, ushort ProductId)>(deviceIds), intervalMs);

    /// <summary>
    /// Starts the watcher thread, optionally limited to the given VID/PID pairs.
    /// </summary>
    /// <param name="filter">Pairs to watch, or <c>null</c> for every device.</param>
    /// <param name="intervalMs">Polling interval.</param>
    private void StartWatchingCore(HashSet<(ushort VendorId, ushort ProductId)>? filter, int intervalMs)
    {
        lock (_watchLock)
        {
            if (_watchThread != null)
            {
                return;
            }

            _log.Debug($"Starting device watcher (interval={intervalMs}ms, filter={(filter == null ? "none" : $"{filter.Count} VID/PID pair(s)")})");
            _previousSnapshot = [.. EnumerateCore(filter)];

            CancellationTokenSource cts = new CancellationTokenSource();
            _watchCts = cts;
            _watchThread = new Thread(() => WatchLoop(cts, filter, intervalMs))
            {
                IsBackground = true,
                Name = "HidDeviceWatcher"
            };
            _watchThread.Start();
        }
    }

    /// <inheritdoc/>
    public void StopWatching()
    {
        CancellationTokenSource? cts;
        Thread? thread;

        lock (_watchLock)
        {
            if (_watchThread == null)
            {
                return;
            }

            _log.Debug("Stopping device watcher");
            cts = _watchCts;
            thread = _watchThread;
            _watchCts = null;
            _watchThread = null;
            _previousSnapshot = null;
            _pendingConnected.Clear();
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The watcher thread already exited and disposed it.
        }

        // Joining keeps Dispose() from shutting HIDAPI down underneath a running scan. It is skipped when a
        // handler raised by the watcher thread itself ends up here, because a thread cannot join itself.
        if (thread != null && thread != Thread.CurrentThread)
        {
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>
    /// Body of the watcher thread: one scan at a time, so scans can never overlap.
    /// </summary>
    private void WatchLoop(CancellationTokenSource cts, HashSet<(ushort VendorId, ushort ProductId)>? filter, int intervalMs)
    {
        CancellationToken token = cts.Token;

        try
        {
            // WaitOne returns true as soon as the token is cancelled, so stopping does not wait out the interval.
            while (!token.WaitHandle.WaitOne(intervalMs))
            {
                // Nothing may escape: if this thread died, device detection would silently stop for good.
                try
                {
                    ScanOnce(filter);
                }
                catch (Exception ex)
                {
                    _log.Warning($"Device scan failed; retrying in {intervalMs} ms", ex);
                }
            }
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <summary>
    /// Re-enumerates the watched devices and raises <see cref="DeviceConnected"/> or
    /// <see cref="DeviceDisconnected"/> when the set of reachable devices changed.
    /// USB devices are trusted at face value; Bluetooth devices are probed in parallel
    /// to confirm they are still responding. Newly seen devices are only reported as
    /// connected once they have been observed on two consecutive scans, so devices the
    /// app itself created and excluded (virtual controllers) never fire
    /// <see cref="DeviceConnected"/>.
    /// </summary>
    private void ScanOnce(HashSet<(ushort VendorId, ushort ProductId)>? filter)
    {
        // Outside the lock: this can open Bluetooth devices and take a while.
        List<IHidDeviceInfo> current = [.. EnumerateCore(filter)];
        List<(bool Connected, IHidDeviceInfo Device)> changes = [];

        lock (_watchLock)
        {
            if (_watchThread == null || _previousSnapshot == null)
            {
                // Stopped while enumerating.
                return;
            }

            HashSet<string> prevPaths = new HashSet<string>(_previousSnapshot.Select(d => d.Path));
            HashSet<string> currPaths = new HashSet<string>(current.Select(d => d.Path));

            // Confirm devices pending from the previous scan that are still present;
            // devices that vanished are left for the disconnect pass below.
            foreach (KeyValuePair<string, IHidDeviceInfo> pair in _pendingConnected.ToList())
            {
                if (!currPaths.Contains(pair.Key))
                {
                    continue;
                }

                _pendingConnected.Remove(pair.Key);
                _log.Debug(
                    $"Device connected: {pair.Value.ProductName} (VID=0x{pair.Value.VendorId:X4}, PID=0x{pair.Value.ProductId:X4}, path={pair.Value.Path})");
                changes.Add((true, pair.Value));
            }

            // First observation of a device: remember it, but only report it after it
            // is seen again on the next scan, so devices the app itself created and
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

                _log.Debug($"Device disconnected: {device.ProductName} (VID=0x{device.VendorId:X4}, PID=0x{device.ProductId:X4}, path={device.Path})");
                changes.Add((false, device));
            }

            _previousSnapshot = current;
        }

        // Raised outside the lock: handlers open devices, which is slow, and may call back into this class.
        foreach ((bool connected, IHidDeviceInfo device) in changes)
        {
            RaiseChange(connected, device);
        }
    }

    /// <summary>
    /// Raises one connection event, keeping a throwing handler from affecting the others.
    /// </summary>
    private void RaiseChange(bool connected, IHidDeviceInfo device)
    {
        try
        {
            if (connected)
            {
                DeviceConnected?.Invoke(this, new DeviceConnectionEventArgs(DeviceChangeType.Connected, device));
            }
            else
            {
                DeviceDisconnected?.Invoke(this, new DeviceConnectionEventArgs(DeviceChangeType.Disconnected, device));
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"{(connected ? "DeviceConnected" : "DeviceDisconnected")} handler threw for {device.ProductName} ({device.Path})", ex);
        }
    }

    /// <summary>
    /// Shared enumeration: everything when <paramref name="filter"/> is <c>null</c>, otherwise only the given pairs.
    /// </summary>
    private IReadOnlyList<IHidDeviceInfo> EnumerateCore(HashSet<(ushort VendorId, ushort ProductId)>? filter)
    {
        if (filter == null)
        {
            List<IHidDeviceInfo> everything = _enumerateNative(null, null);
            everything.RemoveAll(device => IsExcluded(device.Path));
            return FilterConnected(everything);
        }

        if (filter.Count == 0)
        {
            return [];
        }

        // One native call per vendor instead of enumerating everything: with a non-zero VID HIDAPI only reads
        // the attributes of other vendors' devices and skips them, rather than opening each one and requesting
        // its string descriptors. That request stream is what resets some cheap mouse firmware.
        List<IHidDeviceInfo> all = [];
        foreach (ushort vendorId in filter.Select(pair => pair.VendorId).Distinct())
        {
            all.AddRange(_enumerateNative(vendorId, null));
        }

        all.RemoveAll(device => IsExcluded(device.Path) || !filter.Contains((device.VendorId, device.ProductId)));
        return FilterConnected(all);
    }

    /// <summary>
    /// Formats an optional USB identifier for log output, using <c>any</c> when absent.
    /// </summary>
    private static string FormatVendorFilter(ushort? value) => value is { } id ? $"0x{id:X4}" : "any";

    /// <inheritdoc/>
    public IReadOnlyList<IHidDeviceInfo> Enumerate(ushort? vendorId = null, ushort? productId = null)
    {
        _log.Debug($"Enumerating devices (vendorId={FormatVendorFilter(vendorId)}, productId={FormatVendorFilter(productId)})");
        List<IHidDeviceInfo> all = _enumerateNative(vendorId, productId);
        all.RemoveAll(device => IsExcluded(device.Path));
        return FilterConnected(all);
    }

    /// <inheritdoc/>
    public IReadOnlyList<IHidDeviceInfo> EnumerateIncludingExcluded(ushort? vendorId = null, ushort? productId = null)
    {
        _log.Debug($"Enumerating devices including excluded (vendorId={FormatVendorFilter(vendorId)}, productId={FormatVendorFilter(productId)})");
        return FilterConnected(_enumerateNative(vendorId, productId));
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
            ParallelOptions options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
            };
            Parallel.ForEach(btDevices, options, device =>
            {
                if (!IsDeviceConnected(device.Path))
                {
                    _log.Trace(
                        $"Filtered out disconnected Bluetooth device: {device.ProductName} (VID=0x{device.VendorId:X4}, PID=0x{device.ProductId:X4}, path={device.Path})");
                    return;
                }

                connected.TryAdd(device.Path, true);
            });

            result.AddRange(btDevices.Where(device => connected.ContainsKey(device.Path)));
        }

        _log.Debug($"Enumeration kept {result.Count} device(s) (filtered from {all.Count})");
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

        IReadOnlyList<IHidDeviceInfo> result = EnumerateCore(new HashSet<(ushort VendorId, ushort ProductId)>(filter));
        _log.Debug($"Filtered enumeration kept {result.Count} device(s)");
        return result;
    }

    /// <summary>
    /// Returns whether a HID usage identifies a generic-desktop gamepad or joystick.
    /// Keyboard and consumer-control interfaces exposed by the dongle must not be
    /// mistaken for controllers.
    /// </summary>
    /// <param name="usagePage">HID usage page.</param>
    /// <param name="usage">HID usage ID within the page.</param>
    public static bool IsControllerUsage(ushort usagePage, ushort usage)
    {
        const ushort GenericDesktopUsagePage = 0x01;
        return usagePage == GenericDesktopUsagePage
               && (usage == (ushort)HidUsageId.GamePad || usage == (ushort)HidUsageId.Joystick);
    }

    /// <summary>
    /// Calls into HIDAPI to enumerate HID devices matching the given VID/PID filter.
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
            HidDeviceInfoNative* devices = HidApi.Enumerate(vendorId ?? 0, productId ?? 0);

            int count = 0;
            for (HidDeviceInfoNative* cur = devices; cur != null; cur = cur->Next)
            {
                string path = HidStringMarshal.Utf8ToString(cur->Path);
                string name = HidStringMarshal.PtrToString(cur->ProductString);

                HidUsageId usage = (HidUsageId)cur->Usage;
                count++;
                _log.Debug(
                    $"  [{count}] {name} (VID=0x{cur->VendorId:X4}, PID=0x{cur->ProductId:X4}, bus={cur->BusType}, usagePage=0x{cur->UsagePage:X4}, usage=0x{cur->Usage:X4}, path={path})");

                result.Add(new HidDeviceInfo
                {
                    Path = path,
                    VendorId = cur->VendorId,
                    ProductId = cur->ProductId,
                    ProductName = name,
                    Manufacturer = HidStringMarshal.PtrToString(cur->ManufacturerString),
                    InterfaceNumber = cur->InterfaceNumber,
                    UsagePage = cur->UsagePage,
                    Usage = usage,
                    BusType = HidObjectMapper.ToConnectionType(cur->BusType)
                });
            }

            _log.Debug($"Enumeration kept {count} gamepad/joystick device(s)");
            HidApi.FreeEnumeration(devices);
        }

        return result;
    }

    /// <summary>
    /// Ensures the HIDAPI subsystem has been initialized.
    /// Safe to call multiple times; only the first call performs initialization.
    /// </summary>
    /// <exception cref="HidException">Thrown when hid_init fails.</exception>
    private void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            _log.Debug("Initializing HIDAPI subsystem");
            if (HidApi.Init() != 0)
            {
                string error = HidApi.GetError();
                _log.Error($"hid_init failed: {error}");
                throw new HidException($"hid_init failed: {error}");
            }

            _log.Debug($"HIDAPI native library version: {HidApi.NativeLibraryVersion ?? "unknown"}");
        }
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
            _log.Trace($"Device '{devicePath}' not reachable", ex);
            return false;
        }
    }

    /// <inheritdoc/>
    public IHidDevice OpenDevice(string path)
    {
        EnsureInitialized();
        return new HidDevice(path);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        StopWatching();

        if (Interlocked.Exchange(ref _initialized, 0) == 1)
        {
            _log.Debug("Shutting down HIDAPI subsystem");
            HidApi.Exit();
        }
    }
}