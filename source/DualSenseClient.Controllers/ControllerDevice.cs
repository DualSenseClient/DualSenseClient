using System.Diagnostics;
using DualSenseClient.Bluetooth;
using DualSenseClient.Hid;
using DualSenseClient.Logging;

namespace DualSenseClient.Controllers;

/// <summary>
/// Provides read/write access to a recognised game controller.
/// </summary>
public interface IControllerDevice : IDisposable
{
    /// <summary>
    /// The HID device info used to discover this controller.
    /// </summary>
    IHidDeviceInfo Info { get; }

    /// <summary>
    /// The physical transport (USB or Bluetooth).
    /// </summary>
    ConnectionType ConnectionType { get; }

    /// <summary>
    /// The concrete controller type (e.g. DualSense).
    /// </summary>
    ControllerType ControllerType { get; }

    /// <summary>
    /// Whether the underlying HID device is still connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// The maximum length of an output report for this controller.
    /// Varies by connection type (USB vs Bluetooth) for some controllers.
    /// </summary>
    int MaxOutputReportLength { get; }

    /// <summary>
    /// The measured input polling rate in whole Hz (reports per second over the last
    /// measurement window), or 0 until the first window has elapsed.
    /// </summary>
    int PollingRateHz { get; }

    /// <summary>
    /// Reads an input report from the controller.
    /// </summary>
    /// <returns>The number of bytes read.</returns>
    int ReadInput(byte[] buffer, int offset, int count, int timeoutMs);

    /// <summary>
    /// Sends an output report (e.g. rumble, LEDs) to the controller.
    /// </summary>
    void SendOutput(byte[] buffer, int offset, int count);

    /// <summary>
    /// Reads an input report asynchronously (infinite timeout).
    /// </summary>
    Task<int> ReadInputAsync(byte[] buffer, int offset, int count, CancellationToken ct);

    /// <summary>
    /// Gets a feature report from the controller.
    /// </summary>
    byte[] GetFeatureReport(byte reportId, int bufferSize = 64);

    /// <summary>
    /// Sends a feature report to the controller.
    /// </summary>
    void SendFeatureReport(byte[] buffer, int offset, int count);

    /// <summary>
    /// Gets the human-readable product name.
    /// </summary>
    string GetProductName();

    /// <summary>
    /// Disconnects the controller from the PC over Bluetooth.
    /// The device stays paired and can be reconnected later.
    /// </summary>
    /// <returns><c>true</c> if the controller was disconnected; otherwise, <c>false</c>.</returns>
    bool DisconnectController();
}

/// <summary>
/// Base class for typed controller implementations.
/// Delegates read/write to the underlying <see cref="IHidDevice"/> and
/// exposes shared properties like <see cref="Info"/> and <see cref="IsConnected"/>.
/// </summary>
/// <param name="device">The opened HID device for this controller.</param>
/// <param name="info">The device info that was used to discover and open the device.</param>
public abstract class ControllerDevice(IHidDevice device, IHidDeviceInfo info) : IControllerDevice
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("ControllerDevice");

    /// <inheritdoc/>
    public IHidDeviceInfo Info => info;

    /// <inheritdoc/>
    public ConnectionType ConnectionType => info.BusType;

    /// <inheritdoc/>
    public abstract ControllerType ControllerType { get; }

    /// <summary>
    /// Length of the polling-rate measurement window.
    /// </summary>
    private const int PollingRateWindowMs = 500;

    /// <summary>
    /// Stopwatch timestamp at the start of the current polling-rate window.
    /// </summary>
    private long _pollingRateWindowStart = Stopwatch.GetTimestamp();

    /// <summary>
    /// Input reports counted in the current polling-rate window.
    /// </summary>
    private int _pollingRateWindowReports;

    /// <summary>
    /// Latest measured polling rate in whole Hz; written by the device's read thread only.
    /// </summary>
    private volatile int _pollingRateHz;

    /// <inheritdoc/>
    public int PollingRateHz => _pollingRateHz;

    /// <summary>
    /// Counts a received input report and recomputes the polling rate once per
    /// measurement window. Derived classes call this from their read loop for each
    /// parsed input report.
    /// </summary>
    protected void TrackPollingRate()
    {
        long now = Stopwatch.GetTimestamp();
        _pollingRateWindowReports++;
        double elapsedMs = (now - _pollingRateWindowStart) * 1000.0 / Stopwatch.Frequency;
        if (elapsedMs < PollingRateWindowMs)
        {
            return;
        }

        _pollingRateHz = (int)Math.Round(_pollingRateWindowReports * 1000.0 / elapsedMs);
        _pollingRateWindowReports = 0;
        _pollingRateWindowStart = now;
    }

    /// <inheritdoc/>
    public bool IsConnected
    {
        get
        {
            try
            {
                return device.IsConnected;
            }
            catch (ObjectDisposedException)
            {
                // A disposed device is not connected.
                return false;
            }
        }
    }

    /// <inheritdoc/>
    public abstract int MaxOutputReportLength { get; }

    /// <inheritdoc/>
    public virtual int ReadInput(byte[] buffer, int offset, int count, int timeoutMs)
    {
        try
        {
            return device.Read(buffer, offset, count, timeoutMs);
        }
        catch (ObjectDisposedException)
        {
            throw new HidException("Cannot read from a disposed device");
        }
    }

    /// <inheritdoc/>
    public virtual void SendOutput(byte[] buffer, int offset, int count)
    {
        try
        {
            device.Write(buffer, offset, count);
        }
        catch (ObjectDisposedException)
        {
            // The device was disposed (e.g. the controller was unplugged). Writing to a
            // disposed device is an expected race with disconnect, so surface it as a
            // regular HID failure for callers to handle instead of crashing.
            throw new HidException("Cannot write to a disposed device");
        }
    }

    /// <inheritdoc/>
    public virtual async Task<int> ReadInputAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        try
        {
            return await device.ReadAsync(buffer, offset, count, ct);
        }
        catch (ObjectDisposedException)
        {
            throw new HidException("Cannot read from a disposed device");
        }
    }

    /// <inheritdoc/>
    public virtual byte[] GetFeatureReport(byte reportId, int bufferSize = 64)
    {
        try
        {
            return device.GetFeatureReport(reportId, bufferSize);
        }
        catch (ObjectDisposedException)
        {
            throw new HidException("Cannot read a feature report from a disposed device");
        }
    }

    /// <inheritdoc/>
    public virtual void SendFeatureReport(byte[] buffer, int offset, int count)
    {
        try
        {
            device.SendFeatureReport(buffer, offset, count);
        }
        catch (ObjectDisposedException)
        {
            throw new HidException("Cannot send a feature report to a disposed device");
        }
    }

    /// <inheritdoc/>
    public virtual string GetProductName()
    {
        try
        {
            return device.GetProductName();
        }
        catch (ObjectDisposedException)
        {
            throw new HidException("Cannot get the product name of a disposed device");
        }
    }

    /// <summary>
    /// The controller's Bluetooth MAC address (XX:XX:XX:XX:XX:XX), or <c>null</c>
    /// when it is unknown. Only meaningful for controllers connected over Bluetooth.
    /// </summary>
    protected virtual string? BluetoothMacAddress => null;

    /// <inheritdoc/>
    public bool DisconnectController()
    {
        if (ConnectionType != ConnectionType.Bluetooth)
        {
            _log.Warning($"{GetProductName()} is not connected via Bluetooth, nothing to disconnect");
            return false;
        }

        string? mac = BluetoothMacAddress;
        if (string.IsNullOrEmpty(mac))
        {
            _log.Warning($"Could not read the Bluetooth MAC address of {GetProductName()}");
            return false;
        }

        _log.Info($"Disconnecting Bluetooth controller {GetProductName()} ({mac})");
        return BluetoothService.Disconnect(mac);
    }

    /// <inheritdoc/>
    public virtual void Dispose()
    {
        device.Dispose();
    }
}