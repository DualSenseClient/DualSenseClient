using System.Net;
using DualSenseClient.Hid;
using DualSenseClient.Emulation;
using DualSenseClient.Emulation.Devices.DualSense;
using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Tests.Emulation.Integration;

/// <summary>
/// End-to-end integration test of the multi-device path: one facade serves a DualShock 4 (VID 0x054C
/// / PID 0x09CC), a DualSense (0x054C / 0x0CE6) and a DualSense Edge (0x054C / 0x0DF2) over USB/IP
/// under three bus ids, the production usbip.exe attach strategy attaches all three to this machine,
/// each persona enumerates as its own HID device, two of them serve their own pinned pressed-button
/// bit concurrently on their own handles (independent input paths, no cross-talk), and detaching all
/// three makes every controller disappear again. Gated behind EMULATION_INTEGRATION=1: without the
/// environment variable every test is ignored (CI and normal runs never attach devices to the
/// machine). An attach failure - e.g. no usbip-win2 installation - is a failure signal of this test,
/// not a skip. The shared plumbing lives in <see cref="EmulationIntegrationSupport"/>.
/// </summary>
[TestFixture]
public sealed class MultiDeviceEndToEndTests
{
    /// <summary>Vendor id of the Sony personas (DualShock 4, DualSense and DualSense Edge).</summary>
    private const ushort SonyVendorId = 0x054C;

    /// <summary>Product id of the DualShock 4 v2 persona.</summary>
    private const ushort DualShock4V2ProductId = 0x09CC;

    /// <summary>Product id of the DualSense persona.</summary>
    private const ushort DualSenseProductId = 0x0CE6;

    /// <summary>Product id of the DualSense Edge persona.</summary>
    private const ushort DualSenseEdgeProductId = 0x0DF2;

    /// <summary>Size of both HID personas' input reports including the report id.</summary>
    private const int InputReportSize = 64;

    /// <summary>Report id of both HID personas' main input report.</summary>
    private const byte InputReportId = 0x01;

    /// <summary>Offset of the D-pad/face-button byte within the DualShock 4 input report.</summary>
    private const int DualShock4FaceButtonsOffset = 5;

    /// <summary>Offset of the D-pad/face-button byte within the DualSense input report.</summary>
    private const int DualSenseFaceButtonsOffset = 8;

    /// <summary>Bit of the Cross button within both personas' face-button byte.</summary>
    private const byte CrossBit = 0x20;

    /// <summary>Neutral stick value (center) of the pressed-button input states.</summary>
    private const byte NeutralStick = 128;

    /// <summary>Neutral DualShock 4 battery level (100% while discharging) of the pressed-button input state.</summary>
    private const byte DualShock4NeutralBatteryLevel = 10;

    /// <summary>Neutral DualSense battery level (the identity's 0x0A, 100%) of the pressed-button input state.</summary>
    private const byte DualSenseNeutralBatteryLevel = 0x0A;

    /// <summary>At-rest DualSense trigger stop location of the pressed-button input state (reference neutral value 9).</summary>
    private const byte DualSenseNeutralTriggerStopLocation = 0x09;

    [OneTimeSetUp]
    public void RequireIntegration()
    {
        if (Environment.GetEnvironmentVariable("EMULATION_INTEGRATION") != "1")
        {
            Assert.Ignore("Set EMULATION_INTEGRATION=1 to run USB/IP integration tests");
        }
    }

    [Test]
    public void FullFlow_ThreePersonasAttachedConcurrently_EachEnumeratesServesItsOwnInputAndDisappears()
    {
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator();
        HashSet<string> dualShock4Baseline = EmulationIntegrationSupport.CollectDevicePaths(enumerator, SonyVendorId, DualShock4V2ProductId);
        HashSet<string> dualSenseBaseline = EmulationIntegrationSupport.CollectDevicePaths(enumerator, SonyVendorId, DualSenseProductId);
        HashSet<string> dualSenseEdgeBaseline = EmulationIntegrationSupport.CollectDevicePaths(enumerator, SonyVendorId, DualSenseEdgeProductId);
        TestContext.Out.WriteLine($"HID enumeration before attach: {dualShock4Baseline.Count} DualShock 4, "
                                  + $"{dualSenseBaseline.Count} DualSense and {dualSenseEdgeBaseline.Count} DualSense Edge device(s).");

        using EmulationServer server = new EmulationServer(new EmulationServerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        server.Start();
        TestContext.Out.WriteLine($"USB/IP server listening on {server.BoundEndPoint}.");
        DualShock4Device dualShock4 = server.AddDualShock4();
        string dualShock4BusId = server.BusIds[^1];
        DualSenseDevice dualSense = server.AddDualSense();
        string dualSenseBusId = server.BusIds[^1];
        DualSenseDevice dualSenseEdge = server.AddDualSense(true);
        string dualSenseEdgeBusId = server.BusIds[^1];
        TestContext.Out.WriteLine($"Registered bus ids \"{dualShock4BusId}\" (DualShock 4), \"{dualSenseBusId}\" (DualSense) "
                                  + $"and \"{dualSenseEdgeBusId}\" (DualSense Edge).");

        bool dualShock4Attached = false;
        bool dualSenseAttached = false;
        bool dualSenseEdgeAttached = false;
        bool dualShock4Detached = false;
        bool dualSenseDetached = false;
        bool dualSenseEdgeDetached = false;
        try
        {
            dualShock4Attached = server.TryAttach(dualShock4, out string? dualShock4AttachError);
            dualSenseAttached = server.TryAttach(dualSense, out string? dualSenseAttachError);
            dualSenseEdgeAttached = server.TryAttach(dualSenseEdge, out string? dualSenseEdgeAttachError);
            Assert.That(dualShock4Attached, Is.True, $"The DualShock 4 could not be attached to this machine: {dualShock4AttachError}");
            Assert.That(dualSenseAttached, Is.True, $"The DualSense could not be attached to this machine: {dualSenseAttachError}");
            Assert.That(dualSenseEdgeAttached, Is.True, $"The DualSense Edge could not be attached to this machine: {dualSenseEdgeAttachError}");
            TestContext.Out.WriteLine("Attached all three bus ids through the production usbip.exe strategy.");

            IHidDeviceInfo dualShock4Controller = WaitForPersonaDevice(
                enumerator, dualShock4Baseline, DualShock4V2ProductId, "DualShock 4", dualShock4AttachError);
            IHidDeviceInfo dualSenseController = WaitForPersonaDevice(
                enumerator, dualSenseBaseline, DualSenseProductId, "DualSense", dualSenseAttachError);
            IHidDeviceInfo dualSenseEdgeController = WaitForPersonaDevice(
                enumerator, dualSenseEdgeBaseline, DualSenseEdgeProductId, "DualSense Edge", dualSenseEdgeAttachError);
            TestContext.Out.WriteLine($"Enumerated \"{dualShock4Controller.ProductName}\" at {dualShock4Controller.Path} (DualShock 4), "
                                      + $"\"{dualSenseController.ProductName}\" at {dualSenseController.Path} (DualSense) and "
                                      + $"\"{dualSenseEdgeController.ProductName}\" at {dualSenseEdgeController.Path} (DualSense Edge).");

            using (IHidDevice dualShock4Hid = enumerator.OpenDevice(dualShock4Controller.Path))
            using (IHidDevice dualSenseHid = enumerator.OpenDevice(dualSenseController.Path))
            {
                // Both presses run concurrently on their own handle: each thread stores its own
                // pinned state and reads until it sees its own bit, so a cross-talk between the two
                // sessions cannot satisfy either wait.
                Task<PressObservation> dualShock4Press = PressAndObserveCrossBit(
                    dualShock4Hid,
                    () => dualShock4.SetInput(CreateDualShock4CrossPressedState()),
                    DualShock4FaceButtonsOffset);
                Task<PressObservation> dualSensePress = PressAndObserveCrossBit(
                    dualSenseHid,
                    () => dualSense.SetInput(CreateDualSenseCrossPressedState()),
                    DualSenseFaceButtonsOffset);
                Task.WaitAll(dualShock4Press, dualSensePress);

                Assert.That(dualShock4Press.Result.CrossSeen, Is.True,
                    $"The DualShock 4 Cross bit (0x{CrossBit:X2} at report byte {DualShock4FaceButtonsOffset}) "
                    + $"did not appear within {EmulationIntegrationSupport.InputChangeTimeout.TotalSeconds:0} s after SetInput; "
                    + $"last report: {dualShock4Press.Result.LastReportHex}");
                Assert.That(dualSensePress.Result.CrossSeen, Is.True, $"The DualSense Cross bit (0x{CrossBit:X2} at report byte {DualSenseFaceButtonsOffset}) "
                                                                      + $"did not appear within {EmulationIntegrationSupport.InputChangeTimeout.TotalSeconds:0} s after SetInput; "
                                                                      + $"last report: {dualSensePress.Result.LastReportHex}");
                TestContext.Out.WriteLine("Both pressed-button bits arrived concurrently on their own handles.");
            }

            dualShock4Detached = server.TryDetach(dualShock4, out string? dualShock4DetachError);
            Assert.That(dualShock4Detached, Is.True, $"Detach of the DualShock 4 failed: {dualShock4DetachError}");
            dualSenseDetached = server.TryDetach(dualSense, out string? dualSenseDetachError);
            Assert.That(dualSenseDetached, Is.True, $"Detach of the DualSense failed: {dualSenseDetachError}");
            dualSenseEdgeDetached = server.TryDetach(dualSenseEdge, out string? dualSenseEdgeDetachError);
            Assert.That(dualSenseEdgeDetached, Is.True, $"Detach of the DualSense Edge failed: {dualSenseEdgeDetachError}");
            TestContext.Out.WriteLine("Detached all three bus ids.");

            bool dualShock4Disappeared = EmulationIntegrationSupport.WaitForDeviceDisappearance(
                enumerator, SonyVendorId, DualShock4V2ProductId, dualShock4Controller.Path, EmulationIntegrationSupport.DeviceDisappearanceTimeout);
            Assert.That(dualShock4Disappeared, Is.True, $"The DualShock 4 at {dualShock4Controller.Path} was still enumerated "
                                                        + $"{EmulationIntegrationSupport.DeviceDisappearanceTimeout.TotalSeconds:0} s after detach.");
            bool dualSenseDisappeared = EmulationIntegrationSupport.WaitForDeviceDisappearance(
                enumerator, SonyVendorId, DualSenseProductId, dualSenseController.Path, EmulationIntegrationSupport.DeviceDisappearanceTimeout);
            Assert.That(dualSenseDisappeared, Is.True, $"The DualSense at {dualSenseController.Path} was still enumerated "
                                                       + $"{EmulationIntegrationSupport.DeviceDisappearanceTimeout.TotalSeconds:0} s after detach.");
            bool dualSenseEdgeDisappeared = EmulationIntegrationSupport.WaitForDeviceDisappearance(
                enumerator, SonyVendorId, DualSenseEdgeProductId, dualSenseEdgeController.Path, EmulationIntegrationSupport.DeviceDisappearanceTimeout);
            Assert.That(dualSenseEdgeDisappeared, Is.True, $"The DualSense Edge at {dualSenseEdgeController.Path} was still enumerated "
                                                           + $"{EmulationIntegrationSupport.DeviceDisappearanceTimeout.TotalSeconds:0} s after detach.");
            TestContext.Out.WriteLine("All three controllers disappeared from HID enumeration.");
        }
        catch (Exception failure)
        {
            // A failed test must not keep a vhci port, but the cleanup must not mask the original
            // failure either: retry the detach of every still-attached device and fold the cleanup
            // failures into the reported failure. (An Assert.Warn here would replace the in-flight
            // failure with the warning exception.)
            List<string> cleanupFailures = [];
            if (dualShock4Attached && !dualShock4Detached && !server.TryDetach(dualShock4, out string? dualShock4CleanupError))
            {
                cleanupFailures.Add($"bus id \"{dualShock4BusId}\": {dualShock4CleanupError}");
            }

            if (dualSenseAttached && !dualSenseDetached && !server.TryDetach(dualSense, out string? dualSenseCleanupError))
            {
                cleanupFailures.Add($"bus id \"{dualSenseBusId}\": {dualSenseCleanupError}");
            }

            if (dualSenseEdgeAttached && !dualSenseEdgeDetached && !server.TryDetach(dualSenseEdge, out string? dualSenseEdgeCleanupError))
            {
                cleanupFailures.Add($"bus id \"{dualSenseEdgeBusId}\": {dualSenseEdgeCleanupError}");
            }

            if (cleanupFailures.Count > 0)
            {
                Assert.Fail($"{failure.Message} Additionally, the cleanup detach failed, so a vhci port may still be held by "
                            + string.Join("; ", cleanupFailures));
            }

            throw;
        }

        // Unreachable while the detach assertions above fail first; kept so a later edit cannot end
        // the test silently attached to the host.
        Assert.That(dualShock4Detached && dualSenseDetached && dualSenseEdgeDetached, Is.True,
            $"The test ended without detaching every controller; a vhci port may still be held by bus ids \"{dualShock4BusId}\", "
            + $"\"{dualSenseBusId}\" or \"{dualSenseEdgeBusId}\".");
    }

    /// <summary>
    /// Starts the pressed-button observation of one controller on a worker thread: stores the
    /// caller's pinned state, then reads that controller's own handle until a full input report
    /// carries its Cross bit. Both fixtures run one observation each against their own device and
    /// handle, so the two waits overlap in time.
    /// </summary>
    /// <param name="hid">Open handle of the controller to read.</param>
    /// <param name="storePressedState">Stores the pressed-button input state in the persona.</param>
    /// <param name="faceButtonsOffset">Offset of the D-pad/face-button byte within that persona's input report.</param>
    /// <returns>A task carrying whether the bit was observed and the last report seen for diagnostics.</returns>
    private static Task<PressObservation> PressAndObserveCrossBit(IHidDevice hid, Action storePressedState, int faceButtonsOffset)
    {
        return Task.Run(() =>
        {
            string lastReportHex = "(no report received)";
            byte[] report = new byte[InputReportSize];
            storePressedState();
            bool crossSeen = EmulationIntegrationSupport.WaitForInputReport(
                hid,
                report,
                (data, length) => length == InputReportSize && data[0] == InputReportId && (data[faceButtonsOffset] & CrossBit) != 0,
                ref lastReportHex,
                EmulationIntegrationSupport.InputChangeTimeout);
            return new PressObservation(crossSeen, lastReportHex);
        });
    }

    /// <summary>
    /// Polls the HID enumeration for the new device of one persona and fails fast with the attach
    /// diagnostics when it does not appear within the enumeration timeout.
    /// </summary>
    /// <param name="enumerator">HID enumerator to poll.</param>
    /// <param name="baselinePaths">Paths of the persona enumerated before the attach.</param>
    /// <param name="productId">Product id of the persona.</param>
    /// <param name="personaName">Display name of the persona, for the failure message.</param>
    /// <param name="attachError">Diagnostics of the persona's attach call.</param>
    /// <returns>The newly enumerated controller.</returns>
    /// <exception cref="AssertionException">No new device of the persona appeared within the timeout.</exception>
    private static IHidDeviceInfo WaitForPersonaDevice(
        HidDeviceEnumerator enumerator,
        ISet<string> baselinePaths,
        ushort productId,
        string personaName,
        string? attachError)
    {
        return EmulationIntegrationSupport.WaitForNewDevice(
                   enumerator, baselinePaths, SonyVendorId, productId, EmulationIntegrationSupport.DeviceEnumerationTimeout)
               ?? throw new AssertionException(
                   $"No new {personaName} (VID 0x{SonyVendorId:X4}, PID 0x{productId:X4}) enumerated within "
                   + $"{EmulationIntegrationSupport.DeviceEnumerationTimeout.TotalSeconds:0} s; attach diagnostics: {attachError}");
    }

    /// <summary>
    /// Builds the DualShock 4 input state whose only feature is the pressed Cross button: centered
    /// sticks, a released D-pad and a neutral battery, so the assertion can pin exactly one bit.
    /// </summary>
    /// <returns>The pressed-button input state.</returns>
    private static DualShock4InputState CreateDualShock4CrossPressedState()
    {
        return new DualShock4InputState
        {
            LeftStickX = NeutralStick,
            LeftStickY = NeutralStick,
            RightStickX = NeutralStick,
            RightStickY = NeutralStick,
            Dpad = DualShock4DpadDirection.Released,
            BatteryLevel = DualShock4NeutralBatteryLevel,
            Cross = true
        };
    }

    /// <summary>
    /// Builds the DualSense input state whose only feature is the pressed Cross button: centered
    /// sticks, a released D-pad, at-rest trigger stop locations and the identity's neutral battery
    /// (level 0x0A while Complete), so the assertion can pin exactly one bit.
    /// </summary>
    /// <returns>The pressed-button input state.</returns>
    private static DualSenseInputState CreateDualSenseCrossPressedState()
    {
        return new DualSenseInputState
        {
            LeftStickX = NeutralStick,
            LeftStickY = NeutralStick,
            RightStickX = NeutralStick,
            RightStickY = NeutralStick,
            Dpad = DualSenseDpadDirection.Released,
            TriggerRightStopLocation = DualSenseNeutralTriggerStopLocation,
            TriggerLeftStopLocation = DualSenseNeutralTriggerStopLocation,
            BatteryLevel = DualSenseNeutralBatteryLevel,
            PowerState = DualSenseChargingState.Complete,
            Cross = true
        };
    }

    /// <summary>Result of one concurrent pressed-button observation.</summary>
    /// <param name="CrossSeen">Whether a report carrying the persona's Cross bit was observed.</param>
    /// <param name="LastReportHex">Hex of the last input report seen while waiting, for diagnostics.</param>
    private readonly record struct PressObservation(bool CrossSeen, string LastReportHex);
}