using System.Globalization;
using DualSenseClient.Emulation.Core;
using Microsoft.Win32.SafeHandles;

namespace DualSenseClient.Emulation.Windows;

/// <summary>
/// <see cref="IUsbIpAttachStrategy"/> that attaches devices through the usbip-win2 VHCI driver
/// directly, without the vendor CLI: locates the driver's control device interface
/// (<see cref="VhciDeviceLocator"/>), opens it (<see cref="IVhciDriver"/>) and sends the native
/// <c>PLUGIN_HARDWARE</c>/<c>PLUGOUT_HARDWARE</c> IOCTLs. The driver assigns the vhci hub port during
/// the attach IOCTL; the strategy remembers it per bus id, so a later detach can release the same
/// port. Each operation locates and opens the device itself and closes its handle on scope exit:
/// the handle is only the IOCTL channel, the attachment itself is owned by the driver once the
/// attach IOCTL succeeded (the vendor CLI behaves the same way: it attaches and exits). Every
/// operational failure (driver missing, open failure, failed IOCTL) is returned as
/// <see langword="false"/> plus an actionable error that carries the install guidance; a defensive
/// catch around each operation maps an unexpected exception (e.g. a misbehaving seam) to the same
/// failure shape, so no exception escapes the public methods.
/// </summary>
public sealed class VhciIoctlAttachStrategy : IUsbIpAttachStrategy
{
    /// <summary>Install guidance every failure message carries, so the caller can show one actionable line.</summary>
    private const string InstallGuidance =
        "USB/IP must be installed for the device to appear (usbip-win2: https://github.com/vadimgrn/usbip-win2).";

    private readonly IVhciDriver _driver;

    private readonly IEmulationLogger? _logger;

    private readonly VhciDeviceLocator _locator;

    private readonly object _portsLock = new object();

    private readonly Dictionary<string, uint> _vhciPortsByBusId = new Dictionary<string, uint>(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a strategy that drives the given native VHCI seam.
    /// </summary>
    /// <param name="driver">Native VHCI driver seam used to open the control device and run the attach/detach IOCTLs.</param>
    /// <param name="logger">Logging seam; <see langword="null"/> disables logging.</param>
    /// <param name="locator">
    /// Locator of the VHCI control device interface; <see langword="null"/> uses the real SetupAPI
    /// enumeration. Tests inject a locator with a fake candidate provider, so they need no driver.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="driver"/> is <see langword="null"/>.</exception>
    public VhciIoctlAttachStrategy(IVhciDriver driver, IEmulationLogger? logger = null, VhciDeviceLocator? locator = null)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _driver = driver;
        _logger = logger;
        _locator = locator ?? new VhciDeviceLocator();
    }

    /// <summary>
    /// Attaches the device by sending the native <c>PLUGIN_HARDWARE</c> IOCTL. On success the
    /// driver-assigned vhci hub port is remembered per bus id for a later <see cref="TryDetach"/>
    /// (a failed attach leaves no remembered port). The device interface is located and opened for
    /// this one operation only.
    /// </summary>
    /// <param name="host">Host of the USB/IP server (typically loopback).</param>
    /// <param name="port">TCP port of the USB/IP server.</param>
    /// <param name="busId">Bus id of the exported device.</param>
    /// <param name="error">Actionable failure reason; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the driver attached the device.</returns>
    public bool TryAttach(string host, int port, string busId, out string? error)
    {
        try
        {
            return Attach(host, port, busId, out error);
        }
        catch (Exception exception)
        {
            error = $"{DescribeAttach(busId)} failed unexpectedly: {exception.Message} {InstallGuidance}";
            LogWarning(error);
            return false;
        }
    }

    /// <summary>
    /// Detaches the device by sending the native <c>PLUGOUT_HARDWARE</c> IOCTL for the vhci hub port
    /// a successful <see cref="TryAttach"/> remembered. Without a remembered port the device cannot
    /// be released: the strategy logs a warning and returns <see langword="false"/>. A failed detach
    /// keeps the port, so the caller can retry.
    /// </summary>
    /// <param name="busId">Bus id of the device an earlier <see cref="TryAttach"/> attached.</param>
    /// <param name="error">Actionable failure reason; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the driver detached the port.</returns>
    public bool TryDetach(string busId, out string? error)
    {
        try
        {
            return Detach(busId, out error);
        }
        catch (Exception exception)
        {
            error = $"{DescribeDetach(busId)} failed unexpectedly: {exception.Message} {InstallGuidance}";
            LogWarning(error);
            return false;
        }
    }

    private bool Attach(string host, int port, string busId, out string? error)
    {
        string operation = DescribeAttach(busId);
        if (!TryLocateControlDevice(operation, out string devicePath, out error))
        {
            return false;
        }

        // One IOCTL per operation: the handle is closed on scope exit (success or failure); the
        // driver owns the attachment once the IOCTL succeeded.
        using SafeFileHandle handle = _driver.Open(devicePath);
        if (handle.IsInvalid)
        {
            error = $"{operation} failed: the VHCI control device could not be opened. {InstallGuidance}";
            LogWarning(error);
            return false;
        }

        // The vendor contract takes the TCP port as the service string of the remote location.
        VhciAttachRequest request = new VhciAttachRequest(host, port.ToString(CultureInfo.InvariantCulture), busId);
        if (!_driver.TryConnect(handle, request, out uint vhciPort, out int win32Error))
        {
            error = $"{operation} failed with Win32 error {win32Error}. {InstallGuidance}";
            LogWarning(error);
            return false;
        }

        lock (_portsLock)
        {
            _vhciPortsByBusId[busId] = vhciPort;
        }

        Log(EmulationLogLevel.Information, $"usbip-win2 attached bus id '{busId}' on vhci port {vhciPort}.");
        error = null;
        return true;
    }

    private bool Detach(string busId, out string? error)
    {
        uint vhciPort;
        bool portKnown;
        lock (_portsLock)
        {
            portKnown = _vhciPortsByBusId.TryGetValue(busId, out vhciPort);
        }

        if (!portKnown)
        {
            error = $"usbip-win2 cannot detach bus id '{busId}': no vhci port was recorded for it (attach it through this strategy first). {InstallGuidance}";
            LogWarning(error);
            return false;
        }

        string operation = DescribeDetach(busId, vhciPort);
        if (!TryLocateControlDevice(operation, out string devicePath, out error))
        {
            return false;
        }

        using SafeFileHandle handle = _driver.Open(devicePath);
        if (handle.IsInvalid)
        {
            error = $"{operation} failed: the VHCI control device could not be opened. {InstallGuidance}";
            LogWarning(error);
            return false;
        }

        if (!_driver.TryDisconnect(handle, vhciPort, out int win32Error))
        {
            // The port stays remembered, so the caller can retry the detach.
            error = $"{operation} failed with Win32 error {win32Error}. {InstallGuidance}";
            LogWarning(error);
            return false;
        }

        lock (_portsLock)
        {
            _vhciPortsByBusId.Remove(busId);
        }

        Log(EmulationLogLevel.Information, $"usbip-win2 detached bus id '{busId}' from vhci port {vhciPort}.");
        error = null;
        return true;
    }

    private bool TryLocateControlDevice(string operation, out string devicePath, out string? error)
    {
        if (_locator.TryFind(out devicePath))
        {
            error = null;
            return true;
        }

        error = $"{operation} failed: the VHCI control device was not found. {InstallGuidance}";
        LogWarning(error);
        return false;
    }

    private static string DescribeAttach(string busId) => $"usbip-win2 attach for bus id '{busId}'";

    private static string DescribeDetach(string busId) => $"usbip-win2 detach for bus id '{busId}'";

    private static string DescribeDetach(string busId, uint vhciPort) => $"{DescribeDetach(busId)} (vhci port {vhciPort})";

    private void LogWarning(string message) => Log(EmulationLogLevel.Warning, message);

    private void Log(EmulationLogLevel level, string message) => _logger?.Log(level, message);
}