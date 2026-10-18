using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using DualSenseClient.Emulation;
using DualSenseClient.Emulation.Devices.Xbox360;

namespace DualSenseClient.Tests.Emulation.Integration;

/// <summary>
/// End-to-end integration test of the Xbox 360 persona over the whole Emulation path: the facade
/// serves the vendor-class controller (VID 0x045E / PID 0x028E) over USB/IP, the production
/// usbip.exe attach strategy attaches it to this machine, Windows creates the PnP devnode whose
/// hardware ids carry <c>USB\VID_045E&amp;PID_028E</c> and binds the Xbox 360 controller driver
/// (<c>xusb22</c>) to it, and detach removes the devnode again. The persona is not HID, so the
/// assertions read the PnP device tree through setupapi.dll instead of hidapi: the devnode's
/// hardware id list (SPDRP_HARDWAREID) and its driver service (SPDRP_SERVICE). Gameplay itself
/// (XInput input and rumble) is covered by the manual checklist of the task report, not by this
/// test. Gated behind EMULATION_INTEGRATION=1: without the environment variable every test is
/// ignored (CI and normal runs never attach devices to the machine). An attach failure - e.g. no
/// usbip-win2 installation - is a failure signal of this test, not a skip.
/// </summary>
[TestFixture]
public sealed class Xbox360EndToEndTests
{
    /// <summary>Vendor id of the Xbox 360 persona (Microsoft).</summary>
    private const ushort MicrosoftVendorId = 0x045E;

    /// <summary>Product id of the Xbox 360 wired controller persona (Xbox360Identity.IdProduct default).</summary>
    private const ushort Xbox360WiredProductId = 0x028E;

    /// <summary>Driver service the Xbox 360 controller INF binds to the persona's hardware id.</summary>
    private const string Xusb22ServiceName = "xusb22";

    /// <summary>Delay between the PnP enumeration polls (the walk reads the whole device tree, so slower than a HID poll).</summary>
    private const int PnpPollIntervalMilliseconds = 250;

    /// <summary>
    /// Hardware id prefix every devnode of the persona carries; Windows appends "&amp;REV_xxxx" to
    /// the first id and reports a second, shorter id next to it, so matching is by prefix.
    /// </summary>
    private static readonly string Xbox360HardwareIdPrefix = $@"USB\VID_{MicrosoftVendorId:X4}&PID_{Xbox360WiredProductId:X4}";

    [OneTimeSetUp]
    public void RequireIntegration()
    {
        if (Environment.GetEnvironmentVariable("EMULATION_INTEGRATION") != "1")
        {
            Assert.Ignore("Set EMULATION_INTEGRATION=1 to run USB/IP integration tests");
        }
    }

    [Test]
    public void FullFlow_AttachDriverBindsAndDetachDevnodeDisappears()
    {
        List<PnpDevnode> baselineDevnodes = EnumeratePresentDevnodes();
        HashSet<string> baselineInstanceIds = CollectUsbDevnodeInstanceIds(baselineDevnodes);
        TestContext.Out.WriteLine($"PnP enumeration before attach: {baselineDevnodes.Count} present devnode(s), "
                                  + $"{baselineInstanceIds.Count} of them under the USB bus.");

        using EmulationServer server = new EmulationServer(new EmulationServerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        server.Start();
        TestContext.Out.WriteLine($"USB/IP server listening on {server.BoundEndPoint}.");
        Xbox360Device device = server.AddXbox360();
        string busId = server.BusIds[^1];

        bool attached = server.TryAttach(device, out string? attachError);
        if (!attached)
        {
            Assert.Fail($"The Xbox 360 controller could not be attached to this machine: {attachError}");
        }

        TestContext.Out.WriteLine($"Attached bus id \"{busId}\" through the production usbip.exe strategy.");

        bool detached = false;
        try
        {
            // Fail fast with the attach diagnostics when no new devnode appeared within the timeout;
            // the diagnostic also lists every devnode the attach created, whatever hardware id it
            // carries (an enumeration failure shows up as "USB\UNKNOWN" rather than the persona id).
            PnpDevnode devnode = WaitForNewDevnode(baselineInstanceIds, EmulationIntegrationSupport.DeviceEnumerationTimeout)
                                 ?? throw new AssertionException(
                                     $"No new Xbox 360 controller devnode (hardware id \"{Xbox360HardwareIdPrefix}\") appeared within "
                                     + $"{EmulationIntegrationSupport.DeviceEnumerationTimeout.TotalSeconds:0} s; attach diagnostics: {attachError}; "
                                     + $"devnodes the attach created: {DescribeDevnodesAppearedSince(baselineInstanceIds)}");

            TestContext.Out.WriteLine($"Devnode enumerated: {devnode.InstanceId} (service: {DescribeService(devnode.Service)}).");

            string? boundService = WaitForService(devnode.InstanceId, Xusb22ServiceName, EmulationIntegrationSupport.DeviceEnumerationTimeout);
            Assert.That(boundService, Is.EqualTo(Xusb22ServiceName), $"The devnode {devnode.InstanceId} did not bind the {Xusb22ServiceName} driver within "
                                                                     + $"{EmulationIntegrationSupport.DeviceEnumerationTimeout.TotalSeconds:0} s; observed service: {DescribeService(boundService)}");
            TestContext.Out.WriteLine($"The {Xusb22ServiceName} driver bound the devnode.");

            bool detachSucceeded = server.TryDetach(device, out string? detachError);
            detached = detachSucceeded;
            Assert.That(detachSucceeded, Is.True, $"Detach failed: {detachError}");
            TestContext.Out.WriteLine($"Detached bus id \"{busId}\".");

            bool disappeared = WaitForDevnodeDisappearance(devnode.InstanceId, EmulationIntegrationSupport.DeviceDisappearanceTimeout);
            Assert.That(disappeared, Is.True, $"The devnode {devnode.InstanceId} was still enumerated "
                                              + $"{EmulationIntegrationSupport.DeviceDisappearanceTimeout.TotalSeconds:0} s after detach.");
            TestContext.Out.WriteLine("Controller devnode disappeared from PnP enumeration.");
        }
        catch (Exception failure)
        {
            // A failed test must not keep the vhci port, but the cleanup must not mask the original
            // failure either: retry the detach and fold a cleanup failure into the reported failure.
            // (An Assert.Warn here would replace the in-flight failure with the warning exception.)
            if (!detached && !server.TryDetach(device, out string? cleanupError))
            {
                Assert.Fail($"{failure.Message} Additionally, the cleanup detach failed, so a vhci port may still be held "
                            + $"by bus id \"{busId}\": {cleanupError}");
            }

            throw;
        }

        // Unreachable while the detach assertion above fails first; kept so a later edit cannot end
        // the test silently attached to the host.
        Assert.That(detached, Is.True, $"The test ended without detaching the controller; a vhci port may still be held by bus id \"{busId}\".");
    }

    /// <summary>
    /// Polls the PnP device tree for the first devnode whose hardware ids carry the persona's
    /// prefix and whose instance id was not present in <paramref name="baselineInstanceIds"/>, i.e.
    /// the devnode this test just attached.
    /// </summary>
    /// <param name="baselineInstanceIds">Instance ids enumerated before the attach.</param>
    /// <param name="timeout">How long to poll.</param>
    /// <returns>The newly enumerated devnode, or <see langword="null"/> on timeout.</returns>
    private static PnpDevnode? WaitForNewDevnode(ISet<string> baselineInstanceIds, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            foreach (PnpDevnode devnode in CollectMatchingDevnodes(Xbox360HardwareIdPrefix))
            {
                if (!baselineInstanceIds.Contains(devnode.InstanceId))
                {
                    return devnode;
                }
            }

            Thread.Sleep(PnpPollIntervalMilliseconds);
        } while (stopwatch.Elapsed < timeout);

        return null;
    }

    /// <summary>
    /// Polls the devnode until its driver service is <paramref name="serviceName"/>, so a devnode
    /// whose driver has not bound yet (or bound to another service) is not mistaken for a bound one.
    /// </summary>
    /// <param name="instanceId">Instance id of the devnode to watch.</param>
    /// <param name="serviceName">Driver service the devnode must bind.</param>
    /// <param name="timeout">How long to poll.</param>
    /// <returns>The last observed service name when the expected one binds; otherwise the last observed value (may be <see langword="null"/>).</returns>
    private static string? WaitForService(string instanceId, string serviceName, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string? observedService = null;
        do
        {
            observedService = FindDevnode(instanceId)?.Service;
            if (string.Equals(observedService, serviceName, StringComparison.OrdinalIgnoreCase))
            {
                return observedService;
            }

            Thread.Sleep(PnpPollIntervalMilliseconds);
        } while (stopwatch.Elapsed < timeout);

        return observedService;
    }

    /// <summary>Polls the PnP device tree until the devnode of <paramref name="instanceId"/> is gone.</summary>
    /// <param name="instanceId">Instance id of the devnode to watch.</param>
    /// <param name="timeout">How long to poll.</param>
    /// <returns><see langword="true"/> when the devnode disappeared; <see langword="false"/> on timeout.</returns>
    private static bool WaitForDevnodeDisappearance(string instanceId, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            if (FindDevnode(instanceId) is null)
            {
                return true;
            }

            Thread.Sleep(PnpPollIntervalMilliseconds);
        } while (stopwatch.Elapsed < timeout);

        return false;
    }

    /// <summary>Reads the present devnode of <paramref name="instanceId"/>, or <see langword="null"/> when it is gone.</summary>
    /// <param name="instanceId">Instance id of the devnode to find.</param>
    /// <returns>The devnode, or <see langword="null"/> when no present devnode carries the instance id.</returns>
    private static PnpDevnode? FindDevnode(string instanceId)
    {
        foreach (PnpDevnode devnode in EnumeratePresentDevnodes())
        {
            if (string.Equals(devnode.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
            {
                return devnode;
            }
        }

        return null;
    }

    /// <summary>Reads every present devnode whose hardware ids carry <paramref name="hardwareIdPrefix"/>.</summary>
    /// <param name="hardwareIdPrefix">Hardware id prefix of the persona (compared case-insensitively).</param>
    /// <returns>The matching devnodes with their instance id, hardware ids and driver service.</returns>
    private static List<PnpDevnode> CollectMatchingDevnodes(string hardwareIdPrefix)
    {
        List<PnpDevnode> matches = [];
        foreach (PnpDevnode devnode in EnumeratePresentDevnodes())
        {
            if (devnode.HardwareIds.Any(id => id.StartsWith(hardwareIdPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(devnode);
            }
        }

        return matches;
    }

    /// <summary>Collects the instance ids of every present devnode that Windows enumerates under the USB bus (hardware ids starting with "USB\").</summary>
    /// <param name="devnodes">Present devnodes to filter.</param>
    /// <returns>The instance ids, so a later enumeration can tell the attach's devnodes apart from the existing ones.</returns>
    private static HashSet<string> CollectUsbDevnodeInstanceIds(IEnumerable<PnpDevnode> devnodes)
    {
        HashSet<string> instanceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PnpDevnode devnode in devnodes)
        {
            if (devnode.HardwareIds.Any(id => id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)))
            {
                instanceIds.Add(devnode.InstanceId);
            }
        }

        return instanceIds;
    }

    /// <summary>
    /// Describes every present USB devnode that was not in <paramref name="baselineInstanceIds"/>:
    /// the diagnostic of a failed appearance wait, which also catches a devnode Windows created for
    /// a device that failed to enumerate (hardware id "USB\UNKNOWN" instead of the persona's id).
    /// </summary>
    /// <param name="baselineInstanceIds">USB devnode instance ids enumerated before the attach.</param>
    /// <returns>One entry per created devnode, or "(none)" when the attach created no devnode at all.</returns>
    private static string DescribeDevnodesAppearedSince(ISet<string> baselineInstanceIds)
    {
        List<string> appeared = [];
        foreach (PnpDevnode devnode in EnumeratePresentDevnodes())
        {
            bool isUsbDevnode = devnode.HardwareIds.Any(id => id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase));
            if (isUsbDevnode && !baselineInstanceIds.Contains(devnode.InstanceId))
            {
                appeared.Add($"{devnode.InstanceId} (hardware ids: {string.Join(", ", devnode.HardwareIds)}; service: {DescribeService(devnode.Service)})");
            }
        }

        return appeared.Count > 0 ? string.Join("; ", appeared) : "(none)";
    }

    /// <summary>
    /// Walks the whole present device tree through setupapi.dll and reads each devnode's instance
    /// id, hardware id list and driver service; a devnode without the hardware-id property (or
    /// without a driver) yields an empty list (or no service).
    /// </summary>
    /// <returns>Every present devnode with the properties this test reads.</returns>
    private static List<PnpDevnode> EnumeratePresentDevnodes()
    {
        List<PnpDevnode> devnodes = [];
        IntPtr deviceInfoSet = SetupApi.SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, SetupApi.DigcfAllClasses | SetupApi.DigcfPresent);
        if (deviceInfoSet == SetupApi.InvalidHandleValue)
        {
            throw new AssertionException($"SetupDiGetClassDevsW failed with Win32 error {Marshal.GetLastWin32Error()}; the PnP device tree is unavailable.");
        }

        try
        {
            SetupApi.SpDevInfoData deviceInfoData = SetupApi.CreateDeviceInfoData();
            for (uint index = 0; SetupApi.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfoData); index++)
            {
                string instanceId = SetupApi.GetDeviceInstanceId(deviceInfoSet, ref deviceInfoData)
                                    ?? throw new AssertionException(
                                        $"SetupDiGetDeviceInstanceIdW failed for devnode {index} with Win32 error {Marshal.GetLastWin32Error()}.");
                string[] hardwareIds = SetupApi.GetDeviceRegistryPropertyStrings(deviceInfoSet, ref deviceInfoData, SetupApi.SpdrpHardwareId);
                string? service = SetupApi.GetDeviceRegistryPropertyStrings(deviceInfoSet, ref deviceInfoData, SetupApi.SpdrpService).FirstOrDefault();
                devnodes.Add(new PnpDevnode(instanceId, hardwareIds, service));

                // SetupDiEnumDeviceInfo requires cbSize to be set before every call; the API writes the rest.
                deviceInfoData = SetupApi.CreateDeviceInfoData();
            }
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return devnodes;
    }

    /// <summary>Describes a devnode's driver service for the test output.</summary>
    /// <param name="service">Service name of the devnode, or <see langword="null"/> while none is bound.</param>
    /// <returns>The service name, or a placeholder while the devnode has no driver.</returns>
    private static string DescribeService(string? service) => service ?? "(no driver service)";

    /// <summary>One present PnP devnode as read through SetupAPI: instance id, hardware ids and driver service.</summary>
    /// <param name="InstanceId">Device instance id (e.g. "USB\VID_045E&amp;PID_028E\296013F").</param>
    /// <param name="HardwareIds">Hardware id strings of the devnode; empty when the property is absent.</param>
    /// <param name="Service">Driver service bound to the devnode, or <see langword="null"/> while none is.</param>
    private readonly record struct PnpDevnode(string InstanceId, string[] HardwareIds, string? Service);

    /// <summary>
    /// Minimal test-side P/Invoke surface over setupapi.dll: enumerate the present device tree of
    /// every class and read one devnode's instance id and string registry properties (hardware ids
    /// and driver service). Only the members the Xbox 360 binding assertion needs are declared.
    /// </summary>
    private static class SetupApi
    {
        /// <summary>Returns a list of all device classes (DIGCF_ALLCLASSES).</summary>
        public const uint DigcfAllClasses = 0x04;

        /// <summary>Returns only devices currently present (DIGCF_PRESENT).</summary>
        public const uint DigcfPresent = 0x02;

        /// <summary>Hardware ids property of a devnode (SPDRP_HARDWAREID, a REG_MULTI_SZ; note SPDRP_DEVICEDESC is 0).</summary>
        public const uint SpdrpHardwareId = 0x00000001;

        /// <summary>Driver service property of a devnode (SPDRP_SERVICE, a REG_SZ).</summary>
        public const uint SpdrpService = 0x00000004;

        /// <summary>Handle value SetupDiGetClassDevsW returns on failure (INVALID_HANDLE_VALUE).</summary>
        public static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        /// <summary>Property registry type of a REG_SZ value (SPDRP_SERVICE).</summary>
        private const uint RegSz = 1;

        /// <summary>Property registry type of a REG_MULTI_SZ value (SPDRP_HARDWAREID).</summary>
        private const uint RegMultiSz = 7;

        /// <summary>Win32 error SetupDiGetDeviceRegistryPropertyW reports for a property the devnode does not have.</summary>
        private const uint ErrorInvalidData = 13;

        /// <summary>Win32 error SetupDiGetDeviceRegistryPropertyW reports when the buffer is too small.</summary>
        private const uint ErrorInsufficientBuffer = 122;

        /// <summary>Initial property buffer size; a REG_MULTI_SZ hardware id list fits comfortably.</summary>
        private const int PropertyBufferSize = 2048;

        /// <summary>Initial instance id buffer size in characters (the maximum is 200).</summary>
        private const int InstanceIdCapacity = 512;

        /// <summary>
        /// Creates the device-info structure with the <c>cbSize</c> the API requires before every
        /// call; the API fills the remaining fields.
        /// </summary>
        /// <returns>A device-info structure ready to be passed by reference.</returns>
        public static SpDevInfoData CreateDeviceInfoData() => new SpDevInfoData
        {
            CbSize = (uint)Marshal.SizeOf<SpDevInfoData>()
        };

        /// <summary>Reads the device instance id of one devnode.</summary>
        /// <param name="deviceInfoSet">Device information set being enumerated.</param>
        /// <param name="deviceInfoData">Devnode to read.</param>
        /// <returns>The instance id, or <see langword="null"/> when the API cannot serve it.</returns>
        public static string? GetDeviceInstanceId(IntPtr deviceInfoSet, ref SpDevInfoData deviceInfoData)
        {
            StringBuilder instanceId = new StringBuilder(InstanceIdCapacity);
            if (!SetupDiGetDeviceInstanceIdW(deviceInfoSet, ref deviceInfoData, instanceId, (uint)instanceId.Capacity, out uint requiredSize))
            {
                if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || requiredSize == 0)
                {
                    return null;
                }

                instanceId = new StringBuilder((int)requiredSize);
                if (!SetupDiGetDeviceInstanceIdW(deviceInfoSet, ref deviceInfoData, instanceId, (uint)instanceId.Capacity, out _))
                {
                    return null;
                }
            }

            return instanceId.ToString();
        }

        /// <summary>
        /// Reads one string-valued property of a devnode and splits it into its strings: a REG_SZ
        /// yields one string, a REG_MULTI_SZ every string up to its double-NUL terminator, and any
        /// other registry type (or an absent property) yields none.
        /// </summary>
        /// <param name="deviceInfoSet">Device information set being enumerated.</param>
        /// <param name="deviceInfoData">Devnode to read.</param>
        /// <param name="property">Property to read (SPDRP_*).</param>
        /// <returns>The property's strings; empty when the property is absent or not a string type.</returns>
        public static string[] GetDeviceRegistryPropertyStrings(IntPtr deviceInfoSet, ref SpDevInfoData deviceInfoData, uint property)
        {
            byte[] buffer = new byte[PropertyBufferSize];
            if (!SetupDiGetDeviceRegistryPropertyW(deviceInfoSet, ref deviceInfoData, property, out uint propertyType, buffer, (uint)buffer.Length,
                    out uint requiredSize))
            {
                uint error = (uint)Marshal.GetLastWin32Error();
                if (error == ErrorInvalidData)
                {
                    // The devnode has no such property (e.g. SPDRP_SERVICE while no driver is bound).
                    return [];
                }

                if (error != ErrorInsufficientBuffer || requiredSize == 0)
                {
                    return [];
                }

                buffer = new byte[(int)requiredSize];
                if (!SetupDiGetDeviceRegistryPropertyW(deviceInfoSet, ref deviceInfoData, property, out propertyType, buffer, (uint)buffer.Length, out _))
                {
                    return [];
                }
            }

            return DecodeRegistryStrings(propertyType, buffer);
        }

        /// <summary>
        /// Splits a zero-initialized registry string buffer into its NUL-separated UTF-16 strings:
        /// one string for REG_SZ, every string up to the double-NUL terminator for REG_MULTI_SZ.
        /// </summary>
        /// <param name="propertyType">Registry type the API reported for the property.</param>
        /// <param name="buffer">Buffer the API filled; bytes beyond the value are zero.</param>
        /// <returns>The property's strings; empty when the type is neither string type.</returns>
        private static string[] DecodeRegistryStrings(uint propertyType, byte[] buffer)
        {
            if (propertyType != RegSz && propertyType != RegMultiSz)
            {
                return [];
            }

            List<string> strings = [];
            int offset = 0;
            while (offset + 2 <= buffer.Length)
            {
                int terminator = offset;
                while (terminator + 2 <= buffer.Length && (buffer[terminator] != 0 || buffer[terminator + 1] != 0))
                {
                    terminator += 2;
                }

                if (terminator == offset)
                {
                    // The empty string is REG_SZ's terminator or REG_MULTI_SZ's double terminator.
                    break;
                }

                strings.Add(Encoding.Unicode.GetString(buffer, offset, terminator - offset));
                offset = terminator + 2;
                if (propertyType == RegSz)
                {
                    break;
                }
            }

            return strings.ToArray();
        }

        /// <summary>
        /// Returns a device information set of the present device tree: with DIGCF_ALLCLASSES the
        /// class GUID argument is zero, and DIGCF_PRESENT keeps only devnodes that currently exist.
        /// </summary>
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr parentWindow, uint flags);

        /// <summary>Returns the devnode at <paramref name="memberIndex"/> of the set; fails when the index is past the end.</summary>
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SpDevInfoData deviceInfoData);

        /// <summary>Reads one property of a devnode; the string types are decoded by <see cref="DecodeRegistryStrings"/>.</summary>
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceRegistryPropertyW(
            IntPtr deviceInfoSet,
            ref SpDevInfoData deviceInfoData,
            uint property,
            out uint propertyRegDataType,
            byte[]? propertyBuffer,
            uint propertyBufferSize,
            out uint requiredSize);

        /// <summary>Reads the device instance id of a devnode into a UTF-16 buffer.</summary>
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInstanceIdW(
            IntPtr deviceInfoSet,
            ref SpDevInfoData deviceInfoData,
            [Out] StringBuilder deviceInstanceId,
            uint deviceInstanceIdSize,
            out uint requiredSize);

        /// <summary>Releases a device information set created by <see cref="SetupDiGetClassDevsW"/>.</summary>
        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        /// <summary>
        /// The native SP_DEVINFO_DATA structure: its size, the class GUID, the device instance
        /// handle and a reserved field. <see cref="CbSize"/> must carry the structure size before
        /// every call.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct SpDevInfoData
        {
            /// <summary>Size of this structure in bytes; set before every API call.</summary>
            public uint CbSize;

            /// <summary>Class GUID of the device; filled by the API.</summary>
            public Guid ClassGuid;

            /// <summary>Device instance handle; filled by the API.</summary>
            public uint DevInst;

            /// <summary>Reserved for internal use; filled by the API.</summary>
            public IntPtr Reserved;
        }
    }
}