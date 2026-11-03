using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.DualSense.Triggers;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense;

/// <summary>
/// Opt-in hardware tests against a physical Bluetooth DualSense that validate the output
/// paths used by the combined <c>0x36</c> audio report. Set <c>DUALSENSE_HARDWARE=1</c> to run.
/// </summary>
[TestFixture]
public sealed class DualSenseBluetoothAudioHardwareTests
{
    /// <summary>Environment variable that opts into real-controller tests.</summary>
    private const string HardwareEnvironmentVariable = "DUALSENSE_HARDWARE";

    /// <summary>Maximum time to wait for one input report matching a predicate.</summary>
    private static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(3);

    [OneTimeSetUp]
    public void RequirePhysicalHardware()
    {
        if (Environment.GetEnvironmentVariable(HardwareEnvironmentVariable) != "1")
        {
            Assert.Ignore($"Set {HardwareEnvironmentVariable}=1 to run physical-controller tests");
        }
    }

    [Test]
    public void Bluetooth_StandaloneOutputReport_AppliesAndClearsTriggerEffect()
    {
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator();
        using ControllerScanner scanner = new ControllerScanner(enumerator);
        DualSenseDevice controller = FindBluetoothController(scanner);
        try
        {
            ClearTriggersAndWait(controller);

            controller.SetTriggerEffects(TriggerEffectBuilder.Off(), TriggerEffectBuilder.Feedback(5, 1));
            Assert.That(WaitForRightEffect(controller, 1), Is.True,
                "A standalone 0x31 output report did not set the feedback effect nibble");

            controller.SetTriggerEffects(TriggerEffectBuilder.Off(), TriggerEffectBuilder.Off());
            Assert.That(WaitForRightEffect(controller, 0), Is.True,
                "A standalone 0x31 output report did not clear the feedback effect nibble");

            TestContext.Out.WriteLine("Standalone 0x31 trigger effect readback: OK");
        }
        finally
        {
            ClearTriggersAndWait(controller);
            controller.Dispose();
        }
    }

    [Test]
    public void Bluetooth_CombinedReport_EmbeddedStateAppliesTriggerEffect()
    {
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator();
        using ControllerScanner scanner = new ControllerScanner(enumerator);
        DualSenseDevice controller = FindBluetoothController(scanner);
        try
        {
            ClearTriggersAndWait(controller);
            controller.ResetBluetoothAudioStream();

            // Prime with an Off state so the audio lane opens without touching the triggers.
            controller.SendBluetoothAudioPrime(new SetStateData
            {
                ValidFlag0 = ValidFlags.AllowLeftTriggerFfb
                             | ValidFlags.AllowRightTriggerFfb,
                L2TriggerEffect = TriggerEffectBuilder.Off(),
                R2TriggerEffect = TriggerEffectBuilder.Off()
            });

            // Then apply Feedback only through the 0x36 state block, with silent audio/haptics.
            SetStateData feedbackState = new SetStateData
            {
                ValidFlag0 = ValidFlags.AllowRightTriggerFfb,
                R2TriggerEffect = TriggerEffectBuilder.Feedback(5, 1)
            };
            byte[] silenceOpus = new byte[200];
            byte[] silenceHaptics = new byte[64];
            for (int i = 0; i < 5; i++)
            {
                controller.SendBluetoothAudioAndHaptics(
                    feedbackState,
                    silenceOpus,
                    silenceHaptics,
                    BluetoothAudioRoute.Speaker);
                Thread.Sleep(11);
            }

            bool applied = WaitForRightEffect(controller, 1);
            TestContext.Out.WriteLine($"0x36 embedded state block applied trigger effect: {applied}");

            // Clear through the same lane, then release the trigger explicitly after it idles.
            for (int i = 0; i < 3; i++)
            {
                controller.SendBluetoothAudioAndHaptics(
                    new SetStateData
                    {
                        ValidFlag0 = ValidFlags.AllowRightTriggerFfb,
                        R2TriggerEffect = TriggerEffectBuilder.Off()
                    },
                    silenceOpus,
                    silenceHaptics,
                    BluetoothAudioRoute.Speaker);
                Thread.Sleep(11);
            }

            Assert.That(applied, Is.True,
                "The controller did not apply the trigger effect embedded in the 0x36 state block");
        }
        finally
        {
            Thread.Sleep(150);
            ClearTriggersAndWait(controller);
            controller.Dispose();
        }
    }

    [Test]
    public void Bluetooth_StandaloneOutputWhileAudioLaneActive_IsApplied()
    {
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator();
        using ControllerScanner scanner = new ControllerScanner(enumerator);
        DualSenseDevice controller = FindBluetoothController(scanner);
        try
        {
            ClearTriggersAndWait(controller);
            controller.ResetBluetoothAudioStream();

            byte[] silenceOpus = new byte[200];
            byte[] silenceHaptics = new byte[64];
            SetStateData noTriggerState = new SetStateData
            {
                ValidFlag0 = ValidFlags.None
            };

            controller.SendBluetoothAudioPrime(noTriggerState);
            for (int i = 0; i < 3; i++)
            {
                controller.SendBluetoothAudioAndHaptics(
                    noTriggerState,
                    silenceOpus,
                    silenceHaptics,
                    BluetoothAudioRoute.Speaker);
                Thread.Sleep(11);
            }

            // A standalone 0x31 write while the audio lane is open. Keep the lane alive with
            // 0x36 reports whose state block does not touch the trigger flags.
            controller.SetTriggerEffects(TriggerEffectBuilder.Off(), TriggerEffectBuilder.Feedback(5, 1));
            for (int i = 0; i < 5; i++)
            {
                controller.SendBluetoothAudioAndHaptics(
                    noTriggerState,
                    silenceOpus,
                    silenceHaptics,
                    BluetoothAudioRoute.Speaker);
                Thread.Sleep(11);
            }

            bool applied = WaitForRightEffect(controller, 1);
            TestContext.Out.WriteLine($"Standalone 0x31 applied while audio lane active: {applied}");
            Assert.That(applied, Is.True,
                "A standalone 0x31 report was not applied while the 0x36 audio lane was active");
        }
        finally
        {
            Thread.Sleep(150);
            ClearTriggersAndWait(controller);
            controller.Dispose();
        }
    }

    /// <summary>Finds the first physical Bluetooth DualSense, ignoring the app's virtual devices.</summary>
    private static DualSenseDevice FindBluetoothController(ControllerScanner scanner)
    {
        DualSenseDevice? controller = scanner.Scan()
            .OfType<DualSenseDevice>()
            .FirstOrDefault(device => device.ConnectionType == ConnectionType.Bluetooth
                                      && !VirtualDeviceFilter.IsKnownVirtualMac(device.PairingInfo?.ClientMac));
        if (controller is null)
        {
            Assert.Ignore("No physical Bluetooth DualSense was found.");
        }

        return controller!;
    }

    /// <summary>Clears both trigger effects and waits for the readback to settle.</summary>
    private static void ClearTriggersAndWait(DualSenseDevice controller)
    {
        controller.SetTriggerEffects(TriggerEffectBuilder.Off(), TriggerEffectBuilder.Off());
        WaitForRightEffect(controller, 0);
    }

    /// <summary>Waits for an input report whose low right-trigger effect nibble is <paramref name="expected"/>.</summary>
    private static bool WaitForRightEffect(DualSenseDevice controller, int expected)
    {
        TaskCompletionSource<bool> matched = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<InputReport> handler = (_, report) =>
        {
            int effect = report.AdaptiveTriggers.Status2 & 0x0F;
            if (effect == expected)
            {
                matched.TrySetResult(true);
            }
        };

        controller.InputReportReceived += handler;
        try
        {
            return matched.Task.Wait(InputTimeout);
        }
        finally
        {
            controller.InputReportReceived -= handler;
        }
    }
}