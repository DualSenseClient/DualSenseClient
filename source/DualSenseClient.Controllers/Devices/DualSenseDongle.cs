using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.Hid;

namespace DualSenseClient.Controllers.Devices;

/// <summary>
/// DualSense connected through a DS5Dongle Bluetooth bridge. The host sees a wired USB
/// DualSense; this type owns the bridge status endpoints.
/// </summary>
public class DualSenseDongle : DualSenseDevice
{
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
    public DualSenseDongle(IHidDevice device, IHidDeviceInfo info) : base(device, info, false)
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
}
