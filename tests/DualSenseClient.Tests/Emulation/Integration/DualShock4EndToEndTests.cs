using System.Net;
using DualSenseClient.Hid;
using DualSenseClient.Emulation;
using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Tests.Emulation.Integration;

/// <summary>
/// End-to-end integration test of the whole Emulation M1 path: the facade serves a DualShock 4
/// persona over USB/IP, the production usbip.exe attach strategy attaches it to this machine, the
/// controller enumerates as a real HID device (VID 0x054C / PID 0x09CC), input reports flow from the
/// persona to the host, an interrupt OUT output report flows back, and detach makes the device
/// disappear again. Gated behind EMULATION_INTEGRATION=1: without the environment variable every test
/// is ignored (CI and normal runs never attach devices to the machine). An attach failure - e.g. no
/// usbip-win2 installation - is a failure signal of this test, not a skip. The shared plumbing
/// (enumeration polls and report waits) lives in <see cref="EmulationIntegrationSupport"/>; this
/// fixture pins only the DS4 facts and payloads.
/// </summary>
[TestFixture]
public sealed class DualShock4EndToEndTests
{
    /// <summary>Vendor id of the DualShock 4 persona (Sony).</summary>
    private const ushort SonyVendorId = 0x054C;

    /// <summary>Product id of the DualShock 4 v2 persona (CUH-ZCT2).</summary>
    private const ushort DualShock4V2ProductId = 0x09CC;

    /// <summary>Size of the DualShock 4 input report including its report id.</summary>
    private const int InputReportSize = 64;

    /// <summary>Report id of the main DualShock 4 input report.</summary>
    private const byte InputReportId = 0x01;

    /// <summary>Offset of the D-pad/face-button byte within the input report.</summary>
    private const int FaceButtonsOffset = 5;

    /// <summary>Bit of the Cross button within the face-button byte.</summary>
    private const byte CrossBit = 0x20;

    /// <summary>Report id of the DualShock 4 output report (rumble, lightbar, flash).</summary>
    private const byte OutputReportId = 0x05;

    /// <summary>Full size of the output report: the report id plus the 31 declared payload bytes.</summary>
    private const int OutputReportSize = 32;

    /// <summary>Enable flags of the written output report: rumble, lightbar and flash updates.</summary>
    private const byte OutputFlags = 0x07;

    /// <summary>Right (fast/weak) rumble motor strength of the written output report.</summary>
    private const byte RightMotorStrength = 0x40;

    /// <summary>Left (strong/slow) rumble motor strength of the written output report.</summary>
    private const byte LeftMotorStrength = 0x80;

    /// <summary>Red component of the written lightbar color.</summary>
    private const byte RedComponent = 0x12;

    /// <summary>Green component of the written lightbar color.</summary>
    private const byte GreenComponent = 0x34;

    /// <summary>Blue component of the written lightbar color.</summary>
    private const byte BlueComponent = 0x56;

    /// <summary>Flash-on duration of the written output report.</summary>
    private const byte FlashOnDuration = 0x1E;

    /// <summary>Flash-off duration of the written output report.</summary>
    private const byte FlashOffDuration = 0x0A;

    /// <summary>Neutral stick value (center) of the pressed-button input state.</summary>
    private const byte NeutralStick = 128;

    /// <summary>Neutral battery level (100% while discharging) of the pressed-button input state.</summary>
    private const byte NeutralBatteryLevel = 10;

    [OneTimeSetUp]
    public void RequireIntegration()
    {
        if (Environment.GetEnvironmentVariable("EMULATION_INTEGRATION") != "1")
        {
            Assert.Ignore("Set EMULATION_INTEGRATION=1 to run USB/IP integration tests");
        }
    }

    [Test]
    public void FullFlow_AttachPressWriteDetach_ControllerEnumeratesReportsAndDisappears()
    {
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator();
        HashSet<string> baselinePaths = EmulationIntegrationSupport.CollectDevicePaths(enumerator, SonyVendorId, DualShock4V2ProductId);
        TestContext.Out.WriteLine($"HID enumeration before attach: {baselinePaths.Count} DualShock 4 device(s).");

        using EmulationServer server = new EmulationServer(new EmulationServerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
        server.Start();
        TestContext.Out.WriteLine($"USB/IP server listening on {server.BoundEndPoint}.");
        DualShock4Device device = server.AddDualShock4();

        bool attached = server.TryAttach(device, out string? attachError);
        if (!attached)
        {
            Assert.Fail($"The DualShock 4 could not be attached to this machine: {attachError}");
        }

        TestContext.Out.WriteLine($"Attached bus id \"1-1\" through the production usbip.exe strategy.");

        bool detached = false;
        try
        {
            // Fail fast with the attach diagnostics when no new controller appeared within the timeout.
            IHidDeviceInfo controller = EmulationIntegrationSupport.WaitForNewDevice(
                                            enumerator, baselinePaths, SonyVendorId, DualShock4V2ProductId,
                                            EmulationIntegrationSupport.DeviceEnumerationTimeout)
                                        ?? throw new AssertionException(
                                            $"No new DualShock 4 (VID 0x{SonyVendorId:X4}, PID 0x{DualShock4V2ProductId:X4}) enumerated within "
                                            + $"{EmulationIntegrationSupport.DeviceEnumerationTimeout.TotalSeconds:0} s; attach diagnostics: {attachError}");

            TestContext.Out.WriteLine($"Controller enumerated at {controller.Path} as \"{controller.ProductName}\".");
            Assert.That(controller.ProductName, Is.EqualTo("Wireless Controller"), "The product string served by the persona did not reach the host.");

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
                enumerator, SonyVendorId, DualShock4V2ProductId, devicePath, EmulationIntegrationSupport.DeviceDisappearanceTimeout);
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
    /// released D-pad and a neutral battery, so the assertion can pin exactly one report bit.
    /// </summary>
    /// <returns>The pressed-button input state.</returns>
    private static DualShock4InputState CreateCrossPressedState()
    {
        return new DualShock4InputState
        {
            LeftStickX = NeutralStick,
            LeftStickY = NeutralStick,
            RightStickX = NeutralStick,
            RightStickY = NeutralStick,
            Dpad = DualShock4DpadDirection.Released,
            BatteryLevel = NeutralBatteryLevel,
            Cross = true
        };
    }

    /// <summary>
    /// Writes one DualShock 4 output report 0x05 (rumble, lightbar and flash updates) through the
    /// HID wrapper and waits until the persona raises <see cref="DualShock4Device.OutputReportReceived"/>
    /// with exactly the written values. The interrupt OUT report is id-prefixed and 32 bytes long
    /// (the report id plus the report descriptor's 31 output bytes).
    /// </summary>
    /// <param name="hid">Open handle of the enumerated controller.</param>
    /// <param name="device">Persona device receiving the decoded report.</param>
    private static void WriteOutputReportAndWait(IHidDevice hid, DualShock4Device device)
    {
        EmulationIntegrationSupport.WriteOutputReportAndWait<DualShock4OutputReport>(
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
    /// Creates the 32-byte output report payload: report id 0x05, the flags byte enabling the
    /// rumble/lightbar/flash updates, the two motor strengths, the RGB lightbar color and the flash
    /// durations at the layout offsets the persona decodes (bytes 4/5, 6-8 and 9/10).
    /// </summary>
    /// <returns>The output report buffer, report id first.</returns>
    private static byte[] CreateOutputReport()
    {
        byte[] report = new byte[OutputReportSize];
        report[0] = OutputReportId;
        report[1] = OutputFlags;
        report[4] = RightMotorStrength;
        report[5] = LeftMotorStrength;
        report[6] = RedComponent;
        report[7] = GreenComponent;
        report[8] = BlueComponent;
        report[9] = FlashOnDuration;
        report[10] = FlashOffDuration;
        return report;
    }

    /// <summary>
    /// Returns whether <paramref name="report"/> carries exactly the values written by
    /// <see cref="CreateOutputReport"/>; used to skip unrelated reports another host process may
    /// send (e.g. a lightbar initialization) while waiting for the written one.
    /// </summary>
    /// <param name="report">Decoded output report received from the persona.</param>
    /// <returns><see langword="true"/> when the report matches the written payload.</returns>
    private static bool MatchesWrittenOutput(DualShock4OutputReport report)
    {
        return report.RumbleEnabled
               && report.LightbarEnabled
               && report.FlashEnabled
               && report.RightMotor == RightMotorStrength
               && report.LeftMotor == LeftMotorStrength
               && report.Red == RedComponent
               && report.Green == GreenComponent
               && report.Blue == BlueComponent
               && report.FlashOn == FlashOnDuration
               && report.FlashOff == FlashOffDuration;
    }

    /// <summary>Describes the decoded fields of an output report for the test output.</summary>
    /// <param name="report">Report to describe.</param>
    /// <returns>One diagnostic line.</returns>
    private static string DescribeOutput(DualShock4OutputReport report)
    {
        return $"flags rumble={report.RumbleEnabled} lightbar={report.LightbarEnabled} flash={report.FlashEnabled}, "
               + $"motors R=0x{report.RightMotor:X2} L=0x{report.LeftMotor:X2}, "
               + $"RGB=0x{report.Red:X2}{report.Green:X2}{report.Blue:X2}, flash={report.FlashOn}/{report.FlashOff}";
    }
}