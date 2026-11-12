using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.Hid;
using DualSenseClient.Logging;

namespace DualSenseClient.Controllers.Devices;

/// <summary>
/// DualSense connected through a DS5Dongle Bluetooth bridge. The host sees a wired USB
/// DualSense; this type owns the bridge status and firmware configuration endpoints.
/// </summary>
public sealed class DualSenseDongle : DualSenseDevice
{
    /// <summary>
    /// Settle time between a configuration command and the read-back, matching the
    /// firmware tool and web configurator.
    /// </summary>
    private const int ConfigSettleDelayMs = 50;

    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("DualSenseDongle");

    /// <summary>
    /// Serializes configuration reads and writes.
    /// </summary>
    private readonly Lock _configSync = new Lock();

    /// <summary>
    /// Guards the bridge status snapshot read by UI threads.
    /// </summary>
    private readonly Lock _statusSync = new Lock();

    /// <summary>
    /// Latest bridge status snapshot.
    /// </summary>
    private DongleStatus? _dongleStatus;

    /// <summary>
    /// Creates a new DS5Dongle bridge wrapper around an already-opened HID device.
    /// </summary>
    /// <param name="device">The opened HID device for this controller.</param>
    /// <param name="info">The device info that was used to discover and open the device.</param>
    public DualSenseDongle(IHidDevice device, IHidDeviceInfo info)
        : this(device, info, ControllerType.DualSense)
    {
    }

    /// <summary>
    /// Creates a new DS5Dongle bridge wrapper with an explicit controller model.
    /// </summary>
    /// <param name="device">The opened HID device for this controller.</param>
    /// <param name="info">The device info that was used to discover and open the device.</param>
    /// <param name="controllerType">The DualSense model presented by the bridge.</param>
    public DualSenseDongle(IHidDevice device, IHidDeviceInfo info, ControllerType controllerType)
        : base(device, info, controllerType, false)
    {
        SetDongleStatus(DongleStatusReader.Read(this));
        StartReadLoop();
    }

    /// <summary>
    /// Bridge status read from the DS5Dongle feature reports on connect.
    /// Null when neither report answered.
    /// </summary>
    public DongleStatus? DongleStatus
    {
        get
        {
            lock (_statusSync)
            {
                return _dongleStatus;
            }
        }
    }

    /// <summary>
    /// Raised after <see cref="RefreshDongleStatus"/> updates the bridge snapshot.
    /// </summary>
    public event EventHandler? DongleStatusChanged;

    /// <summary>
    /// Re-reads the DS5Dongle bridge status. A failed read keeps the last good snapshot.
    /// </summary>
    public void RefreshDongleStatus()
    {
        DongleStatus? status = DongleStatusReader.Read(this);
        if (status is null)
        {
            return;
        }

        SetDongleStatus(status);
        DongleStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Publishes a bridge status snapshot atomically for UI readers.
    /// </summary>
    /// <param name="status">Snapshot to publish, or <c>null</c> when unavailable.</param>
    private void SetDongleStatus(DongleStatus? status)
    {
        lock (_statusSync)
        {
            _dongleStatus = status;
        }
    }

    /// <summary>
    /// Reads the DS5Dongle configuration snapshot.
    /// </summary>
    /// <returns>The configuration, or <c>null</c> when the report is unreadable.</returns>
    public DongleConfig? ReadDongleConfig()
    {
        lock (_configSync)
        {
            DongleConfig? config = DongleConfigProtocol.Read(this);
            if (config is null)
            {
                _log.Warning(
                    $"DS5Dongle configuration report 0xF7 was unavailable or used an unsupported layout for {Info.ProductName}");
            }

            return config;
        }
    }

    /// <summary>
    /// Applies a DS5Dongle configuration to RAM and returns the firmware-validated snapshot.
    /// </summary>
    /// <param name="config">Configuration to write.</param>
    /// <returns>The read-back configuration, or <c>null</c> when the report is unreadable.</returns>
    public DongleConfig? ApplyDongleConfig(DongleConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_configSync)
        {
            DongleConfigProtocol.SendUpdate(this, config);
            Thread.Sleep(ConfigSettleDelayMs);
            DongleConfig? applied = DongleConfigProtocol.Read(this);
            if (applied is null)
            {
                _log.Warning(
                    $"DS5Dongle configuration read-back 0xF7 was unavailable after an update for {Info.ProductName}");
            }

            return applied;
        }
    }

    /// <summary>
    /// Persists the current DS5Dongle configuration to flash.
    /// </summary>
    public void SaveDongleConfigToFlash()
    {
        lock (_configSync)
        {
            DongleConfigProtocol.SendSave(this);
            Thread.Sleep(ConfigSettleDelayMs);
        }
    }

    /// <summary>
    /// Asks the DS5Dongle to reconnect its USB device.
    /// </summary>
    public void ReconnectDongleUsb()
    {
        lock (_configSync)
        {
            DongleConfigProtocol.SendReconnect(this);
        }
    }
}