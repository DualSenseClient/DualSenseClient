namespace DualSenseClient.Emulation.Windows;

/// <summary>
/// Attaches exported Emulation devices to the local Windows host so they enumerate as real USB
/// devices. Implementations never throw for operational failures: they return <see langword="false"/>
/// with an actionable <c>error</c> the caller can show to the user.
/// </summary>
public interface IUsbIpAttachStrategy
{
    /// <summary>
    /// Attaches the device with the given USB/IP bus id to the local host.
    /// </summary>
    /// <param name="host">Host of the USB/IP server (typically loopback).</param>
    /// <param name="port">TCP port of the USB/IP server.</param>
    /// <param name="busId">Bus id of the exported device (e.g. "1-1").</param>
    /// <param name="error">Actionable failure reason; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the device is attached.</returns>
    bool TryAttach(string host, int port, string busId, out string? error);

    /// <summary>
    /// Detaches the device with the given bus id from the local host.
    /// </summary>
    /// <param name="busId">Bus id of the device an earlier <see cref="TryAttach"/> attached.</param>
    /// <param name="error">Actionable failure reason; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the device is detached.</returns>
    bool TryDetach(string busId, out string? error);
}