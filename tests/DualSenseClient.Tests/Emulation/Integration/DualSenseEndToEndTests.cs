using System.Net;
using DualSenseClient.Hid;
using DualSenseClient.Emulation;
using DualSenseClient.Emulation.Devices.DualSense;

namespace DualSenseClient.Tests.Emulation.Integration;

/// <summary>
/// End-to-end integration test of the DualSense persona over the whole Emulation path: the facade
/// serves a DualSense (VID 0x054C / PID 0x0CE6) over USB/IP, the production usbip.exe attach
/// strategy attaches it to this machine, the controller enumerates as a real HID device, the 64-byte
/// input report 0x01 flows from the persona to the host, a pressed Cross bit set through
/// <see cref="DualSenseDevice.SetInput"/> becomes observable in the stream, an interrupt OUT report
/// 0x02 carrying a lightbar color flows back into <see cref="DualSenseDevice.OutputReportReceived"/>,
/// and detach makes the device disappear again. Gated behind EMULATION_INTEGRATION=1: without the
/// environment variable every test is ignored (CI and normal runs never attach devices to the
/// machine). An attach failure - e.g. no usbip-win2 installation - is a failure signal of this test,
/// not a skip. The shared plumbing lives in <see cref="EmulationIntegrationSupport"/>.
/// </summary>
[TestFixture]
public sealed class DualSenseEndToEndTests
{
    /// <summary>Vendor id of the DualSense persona (Sony).</summary>
    private const ushort SonyVendorId = 0x054C;

    /// <summary>Product id of the DualSense persona (the reference's base mode).</summary>
    private const ushort DualSenseProductId = 0x0CE6;

    /// <summary>Product string the reference identity serves (DualSenseIdentity.Product default).</summary>
    private const string DualSenseProductName = "DualSense Wireless Controller";

    /// <summary>Size of the DualSense input report including its report id.</summary>
    private const int InputReportSize = 64;

    /// <summary>Report id of the main DualSense input report.</summary>
    private const byte InputReportId = 0x01;

    /// <summary>Offset of the D-pad/face-button byte within the input report (the reference's byte 7 plus the report id).</summary>
    private const int FaceButtonsOffset = 8;

    /// <summary>Bit of the Cross button within the face-button byte (the high nibble's second bit).</summary>
    private const byte CrossBit = 0x20;

    /// <summary>Report id of the DualSense output report (rumble, volumes, triggers, lightbar, LEDs).</summary>
    private const byte OutputReportId = 0x02;

    /// <summary>Full size of the output report: the report id plus the 47 declared payload bytes.</summary>
    private const int OutputReportSize = 48;

    /// <summary>Report offset of the second valid-flag byte (payload byte 1, carrying the lightbar-color flag).</summary>
    private const int OutputFlags1Offset = 2;

    /// <summary>Flag bit of the lightbar color allowance (valid-flag byte 1, reference bit 1.2).</summary>
    private const byte LightbarColorAllowedBit = 0x04;

    /// <summary>Report offset of the lightbar red component (payload byte 44).</summary>
    private const int RedOffset = 45;

    /// <summary>Report offset of the lightbar green component (payload byte 45).</summary>
    private const int GreenOffset = 46;

    /// <summary>Report offset of the lightbar blue component (payload byte 46).</summary>
    private const int BlueOffset = 47;

    /// <summary>Red component of the written lightbar color.</summary>
    private const byte RedComponent = 0x12;

    /// <summary>Green component of the written lightbar color.</summary>
    private const byte GreenComponent = 0x34;

    /// <summary>Blue component of the written lightbar color.</summary>
    private const byte BlueComponent = 0x56;

    /// <summary>Neutral stick value (center) of the pressed-button input state.</summary>
    private const byte NeutralStick = 128;

    /// <summary>Neutral battery level of the pressed-button input state (the identity's 0x0A, 100%).</summary>
    private const byte NeutralBatteryLevel = 0x0A;

    /// <summary>At-rest trigger stop location of the pressed-button input state (reference neutral value 9).</summary>
    private const byte NeutralTriggerStopLocation = 0x09;

    [OneTimeSetUp]
    public void RequireIntegration()
    {
        if (Environment.GetEnvironmentVariable("EMULATION_INTEGRATION") != "1")
        {
            Assert.Ignore("Set EMULATION_INTEGRATION=1 to run USB/IP integration tests");
        }
    }

    [Test]
    public void FullFlow_AttachPressWriteLightbarDetach_ControllerEnumeratesReportsAndDisappears()
    {
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator();
        HashSet<string> baselinePaths = EmulationIntegrationSupport.CollectDevicePaths(enumerator, SonyVendorId, DualSenseProductId);
        TestContext.Out.WriteLine($"HID enumeration before attach: {baselinePaths.Count} DualSense device(s).");

        using EmulationServer server = new EmulationServer(new EmulationServerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        server.Start();
        TestContext.Out.WriteLine($"USB/IP server listening on {server.BoundEndPoint}.");
        DualSenseDevice device = server.AddDualSense();

        bool attached = server.TryAttach(device, out string? attachError);
        if (!attached)
        {
            Assert.Fail($"The DualSense could not be attached to this machine: {attachError}");
        }

        TestContext.Out.WriteLine($"Attached bus id \"1-1\" through the production usbip.exe strategy.");

        bool detached = false;
        try
        {
            // Fail fast with the attach diagnostics when no new controller appeared within the timeout.
            IHidDeviceInfo controller = EmulationIntegrationSupport.WaitForNewDevice(
                                            enumerator, baselinePaths, SonyVendorId, DualSenseProductId, EmulationIntegrationSupport.DeviceEnumerationTimeout)
                                        ?? throw new AssertionException(
                                            $"No new DualSense (VID 0x{SonyVendorId:X4}, PID 0x{DualSenseProductId:X4}) enumerated within "
                                            + $"{EmulationIntegrationSupport.DeviceEnumerationTimeout.TotalSeconds:0} s; attach diagnostics: {attachError}");

            TestContext.Out.WriteLine($"Controller enumerated at {controller.Path} as \"{controller.ProductName}\".");
            Assert.That(controller.ProductName, Is.EqualTo(DualSenseProductName), "The product string served by the persona did not reach the host.");

            string devicePath = controller.Path;
            string lastReportHex = "(no report received)";
            using (IHidDevice hid = enumerator.OpenDevice(devicePath))
            {
                byte[] report = new byte[InputReportSize];
                bool firstReportRead = EmulationIntegrationSupport.WaitForInputReport(
                    hid,
                    report,
                    (data, length) => length == InputReportSize && data[0] == InputReportId,
                    ref lastReportHex,
                    EmulationIntegrationSupport.InputReportTimeout);
                Assert.That(firstReportRead, Is.True, "No 64-byte input report 0x01 arrived within "
                                                      + $"{EmulationIntegrationSupport.InputReportTimeout.TotalSeconds:0} s; last report: {lastReportHex}");
                TestContext.Out.WriteLine($"First input report: {lastReportHex}");

                device.SetInput(CreateCrossPressedState());
                bool crossSeen = EmulationIntegrationSupport.WaitForInputReport(
                    hid,
                    report,
                    (data, length) => length == InputReportSize && data[0] == InputReportId && (data[FaceButtonsOffset] & CrossBit) != 0,
                    ref lastReportHex,
                    EmulationIntegrationSupport.InputChangeTimeout);
                Assert.That(crossSeen, Is.True, $"The Cross bit (0x{CrossBit:X2} at report byte {FaceButtonsOffset}) did not appear within "
                                                + $"{EmulationIntegrationSupport.InputChangeTimeout.TotalSeconds:0} s after SetInput; last report: {lastReportHex}");
                TestContext.Out.WriteLine($"Cross-pressed input report: {lastReportHex}");

                WriteOutputReportAndWait(hid, device);
            }

            bool detachSucceeded = server.TryDetach(device, out string? detachError);
            detached = detachSucceeded;
            Assert.That(detachSucceeded, Is.True, $"Detach failed: {detachError}");
            TestContext.Out.WriteLine($"Detached bus id \"1-1\".");

            bool disappeared = EmulationIntegrationSupport.WaitForDeviceDisappearance(
                enumerator, SonyVendorId, DualSenseProductId, devicePath, EmulationIntegrationSupport.DeviceDisappearanceTimeout);
            Assert.That(disappeared, Is.True, $"The controller at {devicePath} was still enumerated "
                                              + $"{EmulationIntegrationSupport.DeviceDisappearanceTimeout.TotalSeconds:0} s after detach.");
            TestContext.Out.WriteLine("Controller disappeared from HID enumeration.");
        }
        catch (Exception failure)
        {
            // A failed test must not keep the vhci port, but the cleanup must not mask the original
            // failure either: retry the detach and fold a cleanup failure into the reported failure.
            // (An Assert.Warn here would replace the in-flight failure with the warning exception.)
            if (!detached && !server.TryDetach(device, out string? cleanupError))
            {
                Assert.Fail($"{failure.Message} Additionally, the cleanup detach failed, so a vhci port may still be held "
                            + $"by bus id \"1-1\": {cleanupError}");
            }

            throw;
        }

        // Unreachable while the detach assertion above fails first; kept so a later edit cannot end
        // the test silently attached to the host.
        Assert.That(detached, Is.True, "The test ended without detaching the controller; a vhci port may still be held by bus id \"1-1\".");
    }

    /// <summary>
    /// Builds the input state whose only feature is the pressed Cross button: centered sticks, a
    /// released D-pad, at-rest trigger stop locations and the identity's neutral battery (level 0x0A
    /// while Complete), so the assertion can pin exactly one report bit.
    /// </summary>
    /// <returns>The pressed-button input state.</returns>
    private static DualSenseInputState CreateCrossPressedState()
    {
        return new DualSenseInputState
        {
            LeftStickX = NeutralStick,
            LeftStickY = NeutralStick,
            RightStickX = NeutralStick,
            RightStickY = NeutralStick,
            Dpad = DualSenseDpadDirection.Released,
            TriggerRightStopLocation = NeutralTriggerStopLocation,
            TriggerLeftStopLocation = NeutralTriggerStopLocation,
            BatteryLevel = NeutralBatteryLevel,
            PowerState = DualSenseChargingState.Complete,
            Cross = true
        };
    }

    /// <summary>
    /// Writes one DualSense output report 0x02 (the lightbar color update) through the HID wrapper
    /// and waits until the persona raises <see cref="DualSenseDevice.OutputReportReceived"/> with
    /// exactly the written values. The interrupt OUT report is id-prefixed and 48 bytes long (the
    /// report id plus the report descriptor's 47 output bytes).
    /// </summary>
    /// <param name="hid">Open handle of the enumerated controller.</param>
    /// <param name="device">Persona device receiving the decoded report.</param>
    private static void WriteOutputReportAndWait(IHidDevice hid, DualSenseDevice device)
    {
        EmulationIntegrationSupport.WriteOutputReportAndWait<DualSenseOutputReport>(
            hid,
            CreateOutputReport(),
            OutputReportId,
            handler => device.OutputReportReceived += handler,
            handler => device.OutputReportReceived -= handler,
            MatchesWrittenOutput,
            DescribeOutput,
            EmulationIntegrationSupport.OutputReportTimeout);
    }

    /// <summary>
    /// Creates the 48-byte output report payload: report id 0x02, the lightbar-color flag in the
    /// second valid-flag byte and the RGB components at the layout offsets the persona decodes
    /// (bytes 45-47). Every other byte stays zero.
    /// </summary>
    /// <returns>The output report buffer, report id first.</returns>
    private static byte[] CreateOutputReport()
    {
        byte[] report = new byte[OutputReportSize];
        report[0] = OutputReportId;
        report[OutputFlags1Offset] = LightbarColorAllowedBit;
        report[RedOffset] = RedComponent;
        report[GreenOffset] = GreenComponent;
        report[BlueOffset] = BlueComponent;
        return report;
    }

    /// <summary>
    /// Returns whether <paramref name="report"/> carries exactly the lightbar color written by
    /// <see cref="CreateOutputReport"/>; used to skip unrelated reports another host process may
    /// send (e.g. a player-LED initialization) while waiting for the written one.
    /// </summary>
    /// <param name="report">Decoded output report received from the persona.</param>
    /// <returns><see langword="true"/> when the report matches the written payload.</returns>
    private static bool MatchesWrittenOutput(DualSenseOutputReport report)
    {
        return report.LightbarColorAllowed
               && report.Red == RedComponent
               && report.Green == GreenComponent
               && report.Blue == BlueComponent;
    }

    /// <summary>Describes the decoded fields of an output report for the test output.</summary>
    /// <param name="report">Report to describe.</param>
    /// <returns>One diagnostic line.</returns>
    private static string DescribeOutput(DualSenseOutputReport report)
    {
        return $"flags lightbar={report.LightbarColorAllowed} rumble={report.RumbleEmulationEnabled}, "
               + $"RGB=0x{report.Red:X2}{report.Green:X2}{report.Blue:X2}";
    }
}