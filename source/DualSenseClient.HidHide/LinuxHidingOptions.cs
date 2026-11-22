namespace DualSenseClient.HidHide;

/// <summary>
/// Filesystem locations used by the Linux hiding backend.
/// </summary>
internal sealed class LinuxHidingOptions
{
    /// <summary>
    /// Root of the sysfs mount used to resolve hidraw devices.
    /// </summary>
    public string SysfsRoot { get; init; } = "/sys";

    /// <summary>
    /// Group database used to resolve the hiding group.
    /// </summary>
    public string GroupFile { get; init; } = "/etc/group";

    /// <summary>
    /// Unix socket exposed by the hiding helper.
    /// </summary>
    public string SocketPath { get; init; } = "/run/dualsenseclient/hiding.sock";

    /// <summary>
    /// Group that owns hidden device nodes and the helper socket.
    /// </summary>
    public string GroupName { get; init; } = "dualsenseclient";
}