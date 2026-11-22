using System.Runtime.InteropServices;

namespace DualSenseClient.HidHide;

/// <summary>
/// Resolves the Linux controller identity and hiding group from sysfs and the group database.
/// </summary>
internal static class LinuxHidingIdentity
{
    /// <summary>
    /// Prefix of the HID unique ID property in a hidraw uevent file.
    /// </summary>
    private const string HidUniqPrefix = "HID_UNIQ=";

    /// <summary>
    /// Returns the process's effective group ID.
    /// </summary>
    /// <returns>The effective GID.</returns>
    public static uint GetEffectiveGid() => GetEgid();

    /// <summary>
    /// Resolves a group name to its numeric GID from a group database file.
    /// </summary>
    /// <param name="groupFile">Group database path.</param>
    /// <param name="groupName">Group name to find.</param>
    /// <param name="gid">Resolved GID when found.</param>
    /// <returns><c>true</c> when the group exists; otherwise <c>false</c>.</returns>
    public static bool TryGetGroupGid(string groupFile, string groupName, out uint gid)
    {
        gid = 0;

        try
        {
            foreach (string line in File.ReadLines(groupFile))
            {
                string[] parts = line.Split(':');
                if (parts.Length < 3 || !string.Equals(parts[0], groupName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (uint.TryParse(parts[2], out uint parsed))
                {
                    gid = parsed;
                    return true;
                }

                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return false;
    }

    /// <summary>
    /// Reads <c>HID_UNIQ</c> for the hidraw device at <paramref name="devicePath"/>.
    /// </summary>
    /// <param name="sysfsRoot">Root of the sysfs mount.</param>
    /// <param name="devicePath">Platform HID path, normally <c>/dev/hidrawN</c>.</param>
    /// <param name="id">Controller MAC when successful.</param>
    /// <returns><c>true</c> when a valid unique ID was found; otherwise <c>false</c>.</returns>
    public static bool TryGetHidUniq(string sysfsRoot, string devicePath, out string id)
    {
        id = string.Empty;

        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return false;
        }

        string name = Path.GetFileName(devicePath);
        if (!name.StartsWith("hidraw", StringComparison.Ordinal))
        {
            try
            {
                FileSystemInfo? target = File.ResolveLinkTarget(devicePath, true);
                if (target is null)
                {
                    return false;
                }

                name = Path.GetFileName(target.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        if (!name.StartsWith("hidraw", StringComparison.Ordinal))
        {
            return false;
        }

        string ueventPath = Path.Combine(sysfsRoot, "class", "hidraw", name, "device", "uevent");
        try
        {
            foreach (string line in File.ReadLines(ueventPath))
            {
                if (!line.StartsWith(HidUniqPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string value = line[HidUniqPrefix.Length..].Trim();
                if (!HidingProtocol.IsValidId(value))
                {
                    return false;
                }

                id = value;
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return false;
    }

    /// <summary>
    /// Native libc entry point for the effective group ID.
    /// </summary>
    /// <returns>The effective GID.</returns>
    [DllImport("libc", EntryPoint = "getegid", SetLastError = false)]
    private static extern uint GetEgid();
}