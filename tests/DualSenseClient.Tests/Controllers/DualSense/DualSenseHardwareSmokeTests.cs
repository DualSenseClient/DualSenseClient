using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense;

/// <summary>
/// Opt-in smoke tests against a physical DualSense or DualSense Edge. They probe the real
/// feature reports (which also validates the Bluetooth feature CRCs), stream input reports,
/// and write one lightbar color through the real HID handle before restoring it. Set
/// <c>DUALSENSE_HARDWARE=1</c> to run them; without the variable the fixture is ignored.
/// </summary>
[TestFixture]
public sealed class DualSenseHardwareSmokeTests
{
    /// <summary>Environment variable that opts into attaching to real controllers.</summary>
    private const string HardwareEnvironmentVariable = "DUALSENSE_HARDWARE";

    /// <summary>Maximum time to wait for one fresh input report.</summary>
    private static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(5);

    [OneTimeSetUp]
    public void RequirePhysicalHardware()
    {
        if (Environment.GetEnvironmentVariable(HardwareEnvironmentVariable) != "1")
        {
            Assert.Ignore($"Set {HardwareEnvironmentVariable}=1 to run physical-controller smoke tests");
        }
    }

    [Test]
    public void PhysicalController_FeatureProbesAndInputStream_AreValid()
    {
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator();
        using ControllerScanner scanner = new ControllerScanner(enumerator);
        List<DualSenseDevice> controllers = ScanPhysicalControllers(scanner);
        if (controllers.Count == 0)
        {
            Assert.Ignore("No physical DualSense or DualSense Edge was found.");
        }

        try
        {
            foreach (DualSenseDevice controller in controllers)
            {
                string label = $"{controller.Info.ProductName} ({controller.ConnectionType}, edge={controller.IsEdge})";
                TestContext.Out.WriteLine($"Probing {label} at {controller.Info.Path}");

                Assert.Multiple(() =>
                {
                    Assert.That(controller.PairingInfo, Is.Not.Null, $"{label}: pairing report 0x09");
                    Assert.That(controller.FirmwareInfo, Is.Not.Null, $"{label}: firmware report 0x20");
                    Assert.That(controller.CalibrationInfo, Is.Not.Null, $"{label}: calibration report 0x05");
                    Assert.That(controller.CalibrationInfo!.Value.IsValid, Is.True, $"{label}: calibration layout");
                    Assert.That(controller.MaxOutputReportLength, Is.EqualTo(ExpectedOutputLength(controller)),
                        $"{label}: descriptor-sized output transfer");
                });

                InputReport report = WaitForFreshInput(controller)
                                     ?? throw new AssertionException($"{label}: no input report arrived within {InputTimeout.TotalSeconds:0} s");

                Assert.Multiple(() =>
                {
                    Assert.That(report.UsbRaw64, Is.Not.Null, $"{label}: verbatim USB report 0x01");
                    Assert.That(report.UsbRaw64!, Has.Length.EqualTo(64), $"{label}: 64-byte USB image");
                    Assert.That(report.UsbRaw64![0], Is.EqualTo(0x01), $"{label}: report id 0x01");
                    Assert.That(report.Battery.PowerState, Is.Not.EqualTo(BatteryPowerState.Unknown),
                        $"{label}: battery state is decodable");
                });

                TestContext.Out.WriteLine(
                    $"{label}: rolling={controller.PollingRateHz} Hz, battery={report.Battery.DisplayPercentage}% ({report.Battery.PowerState}), " +
                    $"firmware={controller.FirmwareInfo!.Value.MainFirmwareVersion}, update={controller.FirmwareInfo.Value.UpdateVersion}");
            }
        }
        finally
        {
            DisposeAll(controllers);
        }
    }

    [Test]
    public void PhysicalController_LightbarOutput_IsAcceptedAndRestored()
    {
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator();
        using ControllerScanner scanner = new ControllerScanner(enumerator);
        List<DualSenseDevice> controllers = ScanPhysicalControllers(scanner);
        if (controllers.Count == 0)
        {
            Assert.Ignore("No physical DualSense or DualSense Edge was found.");
        }

        try
        {
            foreach (DualSenseDevice controller in controllers)
            {
                string label = $"{controller.Info.ProductName} ({controller.ConnectionType}, edge={controller.IsEdge})";
                (byte Red, byte Green, byte Blue) previous = controller.CurrentLightbarColor;
                bool restored = false;
                try
                {
                    controller.SendOutputState(new SetStateData
                    {
                        ValidFlag1 = ValidFlags.AllowLedColor,
                        LedRed = 0x10,
                        LedGreen = 0x20,
                        LedBlue = 0x40
                    });

                    InputReport afterWrite = WaitForFreshInput(controller)
                                             ?? throw new AssertionException($"{label}: link stopped after the lightbar write");

                    Assert.Multiple(() =>
                    {
                        Assert.That(controller.CurrentLightbarColor, Is.EqualTo(((byte)0x10, (byte)0x20, (byte)0x40)),
                            $"{label}: tracked lightbar color");
                        Assert.That(afterWrite.UsbRaw64, Is.Not.Null, $"{label}: input still flowing after output");
                    });

                    TestContext.Out.WriteLine($"{label}: lightbar write accepted ({ExpectedOutputLength(controller)} wire bytes)");
                }
                finally
                {
                    try
                    {
                        controller.SendOutputState(new SetStateData
                        {
                            ValidFlag1 = ValidFlags.AllowLedColor,
                            LedRed = previous.Red,
                            LedGreen = previous.Green,
                            LedBlue = previous.Blue
                        });
                        restored = true;
                    }
                    catch (HidException ex)
                    {
                        TestContext.Out.WriteLine($"{label}: could not restore the lightbar color: {ex.Message}");
                    }

                    if (!restored)
                    {
                        TestContext.Out.WriteLine($"{label}: previous lightbar color was ({previous.Red}, {previous.Green}, {previous.Blue})");
                    }
                }
            }
        }
        finally
        {
            DisposeAll(controllers);
        }
    }

    /// <summary>Scans only real hardware; the scanner already filters app-created virtual controllers.</summary>
    private static List<DualSenseDevice> ScanPhysicalControllers(ControllerScanner scanner)
    {
        return scanner.Scan()
            .OfType<DualSenseDevice>()
            .Where(device => !VirtualDeviceFilter.IsKnownVirtualMac(device.PairingInfo?.ClientMac))
            .ToList();
    }

    /// <summary>Waits for the next report event and returns its parsed snapshot, or null on timeout.</summary>
    private static InputReport? WaitForFreshInput(DualSenseDevice controller)
    {
        TaskCompletionSource<InputReport> received = new TaskCompletionSource<InputReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<InputReport> handler = (_, report) => received.TrySetResult(report);
        controller.InputReportReceived += handler;
        try
        {
            return received.Task.Wait(InputTimeout) ? received.Task.Result : null;
        }
        finally
        {
            controller.InputReportReceived -= handler;
        }
    }

    /// <summary>Expected descriptor-sized output transfer for the controller's transport and model.</summary>
    private static int ExpectedOutputLength(DualSenseDevice controller)
    {
        return controller.ConnectionType switch
        {
            ConnectionType.Bluetooth => 78,
            ConnectionType.Usb => controller.IsEdge ? 64 : 48,
            _ => throw new ArgumentOutOfRangeException(nameof(controller), controller.ConnectionType, "Unknown transport")
        };
    }

    /// <summary>Disposes every opened controller, continuing if one disposal fails.</summary>
    private static void DisposeAll(List<DualSenseDevice> controllers)
    {
        foreach (DualSenseDevice controller in controllers)
        {
            try
            {
                controller.Dispose();
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"Disposing {controller.Info.ProductName} failed: {ex.Message}");
            }
        }
    }
}