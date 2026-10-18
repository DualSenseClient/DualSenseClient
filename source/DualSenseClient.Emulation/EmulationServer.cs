using System.Globalization;
using System.Net;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.DualSense;
using DualSenseClient.Emulation.Devices.DualShock4;
using DualSenseClient.Emulation.Devices.Xbox360;
using DualSenseClient.Emulation.UsbIp;
using DualSenseClient.Emulation.Windows;

namespace DualSenseClient.Emulation;

/// <summary>
/// Public facade over the Emulation engine: exports persona devices (DualSense, DualShock 4 and
/// Xbox 360) over the <see cref="UsbIpServer"/> and attaches them to the local Windows host through an
/// <see cref="IUsbIpAttachStrategy"/> (the installed usbip.exe by default). Devices are registered
/// under auto-assigned USB/IP bus ids of the form "&lt;busnum&gt;-1" starting at "1-1", and the
/// facade tracks the attach state of every device so a repeated attach or a detach of a device that
/// is not attached is answered without asking the strategy again. The facade is a thin composition:
/// the server owns the wire protocol, the strategy owns the host-side attach.
/// </summary>
public sealed class EmulationServer : IDisposable
{
    /// <summary>The USB/IP server doing the wire work.</summary>
    private readonly UsbIpServer _server;

    /// <summary>Strategy that attaches and detaches exported devices on the local host.</summary>
    private readonly IUsbIpAttachStrategy _attachStrategy;

    /// <summary>Logging seam handed to the persona devices so subscriber faults stay observable.</summary>
    private readonly IEmulationLogger? _logger;

    /// <summary>Guards the registered devices, the bus number bookkeeping and <see cref="_isDisposed"/>.</summary>
    private readonly object _devicesLock = new object();

    /// <summary>Registered persona devices with their bus id and attach state; guarded by <see cref="_devicesLock"/>.</summary>
    private readonly Dictionary<VirtualUsbDevice, DeviceEntry> _devices = new Dictionary<VirtualUsbDevice, DeviceEntry>();

    /// <summary>Bus numbers currently assigned to a registered device; guarded by <see cref="_devicesLock"/>.</summary>
    private readonly HashSet<int> _usedBusNumbers = new HashSet<int>();

    /// <summary>Whether the facade has been disposed; guarded by <see cref="_devicesLock"/>.</summary>
    private bool _isDisposed;

    /// <summary>
    /// Initializes the facade from <paramref name="options"/>: the USB/IP server is created from a
    /// snapshot of the relevant options and the attach strategy defaults to the production
    /// usbip.exe strategy when none is given.
    /// </summary>
    /// <param name="options">Facade configuration; copied at construction.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="EmulationServerOptions.InterruptInterval"/> is not positive.</exception>
    public EmulationServer(EmulationServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        UsbIpServerOptions serverOptions = new UsbIpServerOptions
        {
            ListenEndPoint = options.ListenEndPoint,
            Logger = options.Logger,
            TimeProvider = options.TimeProvider,
            InterruptInterval = options.InterruptInterval
        };
        _server = new UsbIpServer(serverOptions);
        _logger = options.Logger;
        _attachStrategy = options.AttachStrategy ?? new UsbIpExeAttachStrategy(new ProcessRunner(), options.Logger);
    }

    /// <summary>Endpoint the USB/IP listener is actually bound to; read <see cref="IPEndPoint.Port"/> to learn the port of a port-0 bind.</summary>
    /// <exception cref="InvalidOperationException">The server has not been started yet.</exception>
    public IPEndPoint BoundEndPoint
    {
        get
        {
            return _server.BoundEndPoint;
        }
    }

    /// <summary>Bus ids of the registered devices, in registration order.</summary>
    public IReadOnlyList<string> BusIds
    {
        get
        {
            return _server.BusIds;
        }
    }

    /// <summary>
    /// Starts the USB/IP listener; the listener survives every session failure until
    /// <see cref="Stop"/> is called.
    /// </summary>
    /// <exception cref="InvalidOperationException">The server is already started.</exception>
    /// <exception cref="ObjectDisposedException">The facade has been disposed.</exception>
    public void Start() => _server.Start();

    /// <summary>
    /// Stops the USB/IP listener and tears down every live session; registered devices and their
    /// attach bookkeeping stay as they are, so an attached device can still be detached afterwards.
    /// Stopping a server that is not running does nothing.
    /// </summary>
    public void Stop() => _server.Stop();

    /// <summary>
    /// Registers a new DualShock 4 v2 persona device and assigns it the lowest free USB/IP bus id
    /// of the form "&lt;busnum&gt;-1" (the first device gets "1-1"); the bus number of the assigned
    /// id is the device entry's busnum and its devnum is 1. Devices may be added whether or not the
    /// server is running.
    /// </summary>
    /// <param name="identity">Identity of the persona; <see langword="null"/> uses the reference DualShock 4 v2 defaults.</param>
    /// <returns>The registered device.</returns>
    /// <exception cref="ObjectDisposedException">The facade has been disposed.</exception>
    public DualShock4Device AddDualShock4(DualShock4Identity? identity = null) => AddDualShock4(DualShock4Variant.V2, identity);

    /// <summary>
    /// Registers a new DualShock 4 persona device of <paramref name="variant"/> under the lowest
    /// free USB/IP bus id of the form "&lt;busnum&gt;-1". The variant-distinguishing reference
    /// facts (the v1 product id and manufacturer string) are pinned by the device's descriptor
    /// set exactly as the persona's own factory resolves them; the facade pins nothing itself.
    /// </summary>
    /// <param name="variant">Descriptor variant of the persona (v1 or v2).</param>
    /// <param name="identity">Identity of the persona; <see langword="null"/> uses the reference DualShock 4 defaults.</param>
    /// <returns>The registered device.</returns>
    /// <exception cref="ObjectDisposedException">The facade has been disposed.</exception>
    public DualShock4Device AddDualShock4(DualShock4Variant variant, DualShock4Identity? identity = null)
    {
        DualShock4Device device = new DualShock4Device(identity ?? new DualShock4Identity(), _logger, variant);
        RegisterDevice(device);
        return device;
    }

    /// <summary>
    /// Registers a new DualSense persona device under the lowest free USB/IP bus id of the form
    /// "&lt;busnum&gt;-1"; on <paramref name="edge"/> the device serves the Edge persona (its
    /// factory pins the Edge product id and product string). The bus id assignment is the one of
    /// <see cref="AddDualShock4(DualShock4Identity?)"/> and is shared by every persona.
    /// </summary>
    /// <param name="edge"><see langword="true"/> for the DualSense Edge persona; <see langword="false"/> for the base DualSense.</param>
    /// <param name="identity">Identity of the persona; <see langword="null"/> uses the reference DualSense defaults.</param>
    /// <returns>The registered device.</returns>
    /// <exception cref="ObjectDisposedException">The facade has been disposed.</exception>
    public DualSenseDevice AddDualSense(bool edge = false, DualSenseIdentity? identity = null)
    {
        DualSenseDevice device = new DualSenseDevice(identity ?? new DualSenseIdentity(), edge, _logger);
        RegisterDevice(device);
        return device;
    }

    /// <summary>
    /// Registers a new Xbox 360 wired controller persona device under the lowest free USB/IP bus id
    /// of the form "&lt;busnum&gt;-1"; the persona is a vendor-class, non-HID device, which the
    /// facade treats like every other persona.
    /// </summary>
    /// <param name="identity">Identity of the persona; <see langword="null"/> uses the reference Xbox 360 defaults.</param>
    /// <returns>The registered device.</returns>
    /// <exception cref="ObjectDisposedException">The facade has been disposed.</exception>
    public Xbox360Device AddXbox360(Xbox360Identity? identity = null)
    {
        Xbox360Device device = new Xbox360Device(identity ?? new Xbox360Identity(), _logger);
        RegisterDevice(device);
        return device;
    }

    /// <summary>
    /// Unregisters a device: its bus id is freed for reuse by the next persona added to the facade
    /// and the device can no longer be attached through this facade. A device that is currently
    /// attached to the local host is not detached here; detach it first so the host releases the
    /// vhci port. A session that already imported the device keeps serving it until it ends.
    /// </summary>
    /// <param name="device">Device to unregister.</param>
    /// <returns><see langword="true"/> when the device was registered; <see langword="false"/> when it is unknown to the facade.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public bool RemoveDevice(VirtualUsbDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_devicesLock)
        {
            if (!_devices.Remove(device, out DeviceEntry? entry))
            {
                return false;
            }

            _server.RemoveDevice(entry.BusId);
            _usedBusNumbers.Remove(entry.BusNumber);
            return true;
        }
    }

    /// <summary>
    /// Attaches a registered device to the local host by calling the attach strategy with the bus
    /// id of the device and the host and port of the bound endpoint. A device that is already
    /// attached is not attached twice.
    /// </summary>
    /// <param name="device">Registered device to attach.</param>
    /// <param name="error">Actionable failure reason; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the device is attached.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The server has not been started yet, so there is no bound endpoint to attach to.</exception>
    public bool TryAttach(VirtualUsbDevice device, out string? error)
    {
        ArgumentNullException.ThrowIfNull(device);
        DeviceEntry entry;
        lock (_devicesLock)
        {
            if (!_devices.TryGetValue(device, out DeviceEntry? registered))
            {
                error = "The device is not registered with the server; add it with AddDualSense, AddDualShock4 or AddXbox360 first.";
                return false;
            }

            if (registered.Attached)
            {
                error = $"The device on bus id \"{registered.BusId}\" is already attached.";
                return false;
            }

            entry = registered;
        }

        IPEndPoint boundEndPoint = _server.BoundEndPoint;
        bool attached = _attachStrategy.TryAttach(boundEndPoint.Address.ToString(), boundEndPoint.Port, entry.BusId, out error);
        if (attached)
        {
            SetAttached(device, entry, true);
        }

        return attached;
    }

    /// <summary>
    /// Detaches a device that was attached through this facade by calling the attach strategy with
    /// its bus id. A device that is not attached is not detached.
    /// </summary>
    /// <param name="device">Registered device to detach.</param>
    /// <param name="error">Actionable failure reason; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the device is detached.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public bool TryDetach(VirtualUsbDevice device, out string? error)
    {
        ArgumentNullException.ThrowIfNull(device);
        DeviceEntry entry;
        lock (_devicesLock)
        {
            if (!_devices.TryGetValue(device, out DeviceEntry? registered))
            {
                error = "The device is not registered with the server; add it with AddDualSense, AddDualShock4 or AddXbox360 first.";
                return false;
            }

            if (!registered.Attached)
            {
                error = $"The device on bus id \"{registered.BusId}\" is not attached.";
                return false;
            }

            entry = registered;
        }

        bool detached = _attachStrategy.TryDetach(entry.BusId, out error);
        if (detached)
        {
            SetAttached(device, entry, false);
        }

        return detached;
    }

    /// <summary>
    /// Stops the server and releases its listener; disposing an already disposed facade does
    /// nothing. Attached devices are not detached here: call <see cref="TryDetach"/> first so the
    /// host releases the vhci port.
    /// </summary>
    public void Dispose()
    {
        lock (_devicesLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        _server.Dispose();
    }

    /// <summary>
    /// Assigns the already constructed <paramref name="device"/> the lowest free USB/IP bus id of
    /// the form "&lt;busnum&gt;-1" and registers it with the server and the facade's bookkeeping;
    /// the persona's construction stays with the caller.
    /// </summary>
    /// <param name="device">Persona device to register.</param>
    /// <exception cref="ObjectDisposedException">The facade has been disposed.</exception>
    private void RegisterDevice(VirtualUsbDevice device)
    {
        lock (_devicesLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            int busNumber = FindFreeBusNumber();
            string busId = busNumber.ToString(CultureInfo.InvariantCulture) + "-1";
            _server.AddDevice(busId, device);
            _devices.Add(device, new DeviceEntry(busNumber, busId));
            _usedBusNumbers.Add(busNumber);
        }
    }

    /// <summary>
    /// Updates the attach state of <paramref name="device"/> when its registration is still
    /// <paramref name="entry"/> (the device can be removed while the strategy runs).
    /// </summary>
    /// <param name="device">Device whose state changed.</param>
    /// <param name="entry">Registration the state change belongs to.</param>
    /// <param name="attached">New attach state.</param>
    private void SetAttached(VirtualUsbDevice device, DeviceEntry entry, bool attached)
    {
        lock (_devicesLock)
        {
            if (_devices.TryGetValue(device, out DeviceEntry? registered) && ReferenceEquals(registered, entry))
            {
                registered.Attached = attached;
            }
        }
    }

    /// <summary>Returns the lowest bus number not currently assigned to a registered device, starting at 1.</summary>
    /// <returns>The free bus number.</returns>
    private int FindFreeBusNumber()
    {
        int busNumber = 1;
        while (_usedBusNumbers.Contains(busNumber))
        {
            busNumber++;
        }

        return busNumber;
    }

    /// <summary>One registered device with its assigned bus id and attach state.</summary>
    private sealed class DeviceEntry
    {
        /// <summary>
        /// Initializes a registration with the assigned bus number and bus id.
        /// </summary>
        /// <param name="busNumber">Bus number encoded in <paramref name="busId"/>.</param>
        /// <param name="busId">Assigned USB/IP bus id.</param>
        public DeviceEntry(int busNumber, string busId)
        {
            BusNumber = busNumber;
            BusId = busId;
        }

        /// <summary>Bus number encoded in <see cref="BusId"/>.</summary>
        public int BusNumber { get; }

        /// <summary>Assigned USB/IP bus id.</summary>
        public string BusId { get; }

        /// <summary>Whether the device is currently attached to the local host; guarded by the facade's device lock.</summary>
        public bool Attached { get; set; }
    }
}