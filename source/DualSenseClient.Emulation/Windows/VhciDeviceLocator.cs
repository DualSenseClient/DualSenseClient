using System.Runtime.InteropServices;

namespace DualSenseClient.Emulation.Windows;

/// <summary>
/// Locates the usbip-win2 VHCI control device on this machine by enumerating the device interfaces
/// registered for the vhci device interface class GUID and returning the first usable device path
/// (e.g. <c>\\?\ROOT#USB#0000#{b4030c06-dc5f-4fcc-87eb-e5515a0935c0}</c>). The path is the handle
/// target of the native attach IOCTLs. Enumeration failures (driver missing, SetupAPI error) are
/// reported as "not found", never as an exception; diagnostics are the caller's responsibility.
/// </summary>
public sealed class VhciDeviceLocator
{
    /// <summary>
    /// Device interface class GUID the usbip-win2 driver registers for its VHCI control device
    /// (established empirically on this machine; see the P2 section of the compat notes).
    /// </summary>
    private static readonly Guid VhciInterfaceClassGuid = new Guid("b4030c06-dc5f-4fcc-87eb-e5515a0935c0");

    /// <summary>Only present devices are enumerated (<c>DIGCF_PRESENT</c>).</summary>
    private const uint DigcfPresent = 0x00000002;

    /// <summary>Enumerate the device interfaces of the requested interface class (<c>DIGCF_DEVICEINTERFACE</c>).</summary>
    private const uint DigcfDeviceInterface = 0x00000010;

    /// <summary>Offset of the <c>DevicePath</c> member within <c>SP_DEVICE_INTERFACE_DETAIL_DATA_W</c> (same on both architectures).</summary>
    private const int DevicePathOffset = 4;

    private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

    private readonly Func<string[]>? _candidateProvider;

    /// <summary>
    /// Initializes a locator.
    /// </summary>
    /// <param name="candidateProvider">
    /// Enumeration seam returning candidate device paths, in preference order; <see langword="null"/>
    /// uses the real SetupAPI enumeration for the vhci interface class GUID. Tests inject fakes so
    /// they need no driver.
    /// </param>
    public VhciDeviceLocator(Func<string[]>? candidateProvider = null)
    {
        _candidateProvider = candidateProvider;
    }

    /// <summary>
    /// Finds the first usable VHCI device path.
    /// </summary>
    /// <param name="devicePath">Trimmed device interface path; empty when no device was found.</param>
    /// <returns>
    /// <see langword="true"/> when a candidate path was found. Enumeration failures (including a
    /// throwing provider) return <see langword="false"/> instead of throwing.
    /// </returns>
    public bool TryFind(out string devicePath)
    {
        string[]? candidates = null;
        try
        {
            candidates = _candidateProvider != null ? _candidateProvider() : EnumerateDeviceInterfacePaths();
        }
        catch (Exception)
        {
            // The driver is missing or enumeration failed; callers see "not found" and higher
            // layers own the diagnostics.
        }

        if (candidates != null)
        {
            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    devicePath = candidate.Trim();
                    return true;
                }
            }
        }

        devicePath = string.Empty;
        return false;
    }

    /// <summary>
    /// Enumerates the device interface paths registered for the vhci interface class GUID through
    /// SetupAPI.
    /// </summary>
    /// <returns>The registered device interface paths; empty when no device info set could be created or no interface is registered.</returns>
    private static string[] EnumerateDeviceInterfacePaths()
    {
        Guid interfaceClassGuid = VhciInterfaceClassGuid;
        IntPtr deviceInfoSet = SetupDiGetClassDevsW(ref interfaceClassGuid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (deviceInfoSet == InvalidHandleValue)
        {
            return Array.Empty<string>();
        }

        try
        {
            List<string> paths = new List<string>();
            SpDeviceInterfaceData interfaceData = new SpDeviceInterfaceData
            {
                CbSize = (uint)Marshal.SizeOf<SpDeviceInterfaceData>()
            };
            uint index = 0;
            while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref interfaceClassGuid, index, ref interfaceData))
            {
                if (TryGetDevicePath(deviceInfoSet, ref interfaceData, out string path))
                {
                    paths.Add(path);
                }

                index++;
            }

            return paths.ToArray();
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    /// <summary>
    /// Reads the device interface path of one enumerated interface using the documented two-call
    /// size query and manual marshalling.
    /// </summary>
    /// <param name="deviceInfoSet">Device info set returned by <c>SetupDiGetClassDevsW</c>.</param>
    /// <param name="interfaceData">Enumerated interface to query.</param>
    /// <param name="path">Device interface path; empty when the query failed.</param>
    /// <returns><see langword="true"/> when the path was read.</returns>
    private static bool TryGetDevicePath(IntPtr deviceInfoSet, ref SpDeviceInterfaceData interfaceData, out string path)
    {
        path = string.Empty;
        // The first call only queries the required buffer size and is expected to fail with
        // ERROR_INSUFFICIENT_BUFFER; the reported size is what matters.
        SetupDiGetDeviceInterfaceDetailW(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);
        if (requiredSize == 0)
        {
            return false;
        }

        IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            // cbSize is sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W): 8 on x64 (4-byte members plus
            // trailing pointer alignment), 6 on x86. DevicePath follows at offset 4 on both.
            Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetailW(deviceInfoSet, ref interfaceData, detailBuffer, requiredSize, out uint _, IntPtr.Zero))
            {
                return false;
            }

            string? detailPath = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, DevicePathOffset));
            if (string.IsNullOrEmpty(detailPath))
            {
                return false;
            }

            path = detailPath;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(detailBuffer);
        }
    }

    /// <summary>Native <c>SP_DEVICE_INTERFACE_DATA</c> of the Windows SetupAPI.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        /// <summary>Structure size in bytes; must be set before the first call.</summary>
        public uint CbSize;

        /// <summary>Interface class GUID the interface belongs to.</summary>
        public Guid InterfaceClassGuid;

        /// <summary>Interface flags (reserved).</summary>
        public uint Flags;

        /// <summary>Reserved.</summary>
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}