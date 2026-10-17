using System.Net;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Windows;

namespace DualSenseClient.Emulation;

/// <summary>
/// Configuration of the <see cref="EmulationServer"/> facade: the endpoint the USB/IP listener
/// binds, the logging seam, the attach strategy and the pacing schedule.
/// </summary>
public sealed class EmulationServerOptions
{
    /// <summary>
    /// Endpoint the USB/IP listener binds to; <see langword="null"/> selects the USB/IP server's
    /// default of loopback 127.0.0.1:3241. Use a port of 0 to let the operating system pick a free
    /// port and read it back from <see cref="EmulationServer.BoundEndPoint"/>.
    /// </summary>
    public IPEndPoint? ListenEndPoint { get; set; }

    /// <summary>Logging seam; <see langword="null"/> disables logging.</summary>
    public IEmulationLogger? Logger { get; set; }

    /// <summary>
    /// Strategy that attaches exported devices to the local Windows host; <see langword="null"/>
    /// selects the production <see cref="UsbIpExeAttachStrategy"/> that drives the installed
    /// usbip.exe.
    /// </summary>
    public IUsbIpAttachStrategy? AttachStrategy { get; set; }

    /// <summary>
    /// Provider that schedules the interrupt IN pacers; <see langword="null"/> selects the system
    /// provider. Tests inject a deterministic manual provider.
    /// </summary>
    public TimeProvider? TimeProvider { get; set; }

    /// <summary>
    /// Interval between the ticks of one interrupt IN pacer; defaults to 1 ms. Must be positive.
    /// </summary>
    public TimeSpan InterruptInterval { get; set; } = TimeSpan.FromMilliseconds(1);
}