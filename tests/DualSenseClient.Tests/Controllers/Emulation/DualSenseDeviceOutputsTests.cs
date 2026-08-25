using System.Reflection;
using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Hid;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.Emulation;

public class DualSenseDeviceOutputsTests
{
    private sealed class RecordingHidDevice : IHidDevice
    {
        public List<byte[]> Writes { get; } = new List<byte[]>();

        public ushort VendorId
        {
            get
            {
                return 0x054C;
            }
        }

        public ushort ProductId
        {
            get
            {
                return 0x0CE6;
            }
        }

        public string DevicePath
        {
            get
            {
                return "test";
            }
        }

        public bool IsConnected
        {
            get
            {
                return true;
            }
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;
        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count)
        {
            byte[] copy = new byte[count];
            Buffer.BlockCopy(buffer, offset, copy, 0, count);
            Writes.Add(copy);
            return count;
        }

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) => [];

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "Test";

        public void Dispose()
        {
        }
    }

    private sealed class StubHidDeviceInfo : IHidDeviceInfo
    {
        public string Path
        {
            get
            {
                return "test";
            }
        }

        public ushort VendorId
        {
            get
            {
                return 0x054C;
            }
        }

        public ushort ProductId
        {
            get
            {
                return 0x0CE6;
            }
        }

        public string ProductName
        {
            get
            {
                return "DualSense Test";
            }
        }

        public string Manufacturer
        {
            get
            {
                return "Sony";
            }
        }

        public int InterfaceNumber
        {
            get
            {
                return 0;
            }
        }

        public ushort UsagePage
        {
            get
            {
                return 1;
            }
        }

        public HidUsageId Usage
        {
            get
            {
                return HidUsageId.GamePad;
            }
        }

        public ConnectionType BusType
        {
            get
            {
                return ConnectionType.Usb;
            }
        }
    }

    private static readonly MethodInfo ProcessInputReportMethod = typeof(DualSenseDevice)
        .GetMethod("ProcessInputReport", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static void FeedReport(DualSenseDevice device, byte[] buffer) =>
        ProcessInputReportMethod.Invoke(device, [buffer]);

    private static byte[] CreateReport(params ButtonType[] pressed)
    {
        byte[] buffer = new byte[64];
        buffer[0] = 0x01; // USB input report ID
        buffer[8] = 0x08; // D-Pad neutral, no face buttons
        foreach (ButtonType button in pressed)
        {
            (int index, byte mask) = button switch
            {
                ButtonType.L1 => (9, (byte)0x01),
                ButtonType.R1 => (9, (byte)0x02),
                ButtonType.Cross => (8, (byte)0x20),
                ButtonType.Circle => (8, (byte)0x40),
                _ => throw new ArgumentOutOfRangeException(nameof(button), button, null)
            };
            buffer[index] |= mask;
        }

        return buffer;
    }

    /// <summary>
    /// The game output a virtual controller would forward, with a lightbar color.
    /// </summary>
    private static SetStateData GameOutput(byte red, byte green, byte blue) => new SetStateData
    {
        ValidFlag1 = ValidFlags.AllowLedColor,
        LedRed = red,
        LedGreen = green,
        LedBlue = blue
    };

    /// <summary>
    /// Creates a while-held lightbar action on L1+R1 with the given RGB.
    /// </summary>
    private static SpecialAction CreateSustainedLightbarAction(byte red, byte green, byte blue) =>
        new SpecialAction
        {
            Buttons =
            {
                ButtonType.L1.ToString(),
                ButtonType.R1.ToString()
            },
            Effects =
            {
                new SpecialActionEffect
                {
                    Type = SpecialActionTypes.SetLightbarColor,
                    Lightbar = new LightbarSettings
                    {
                        Red = red,
                        Green = green,
                        Blue = blue
                    }
                }
            },
            ApplyWhileHeld = true,
            EnabledControllers =
            {
                "test"
            }
        };

    /// <summary>
    /// Creates a while-held player-LED action on L1+R1 with the given mask.
    /// </summary>
    private static SpecialAction CreateSustainedPlayerLedsAction(byte mask) =>
        new SpecialAction
        {
            Buttons =
            {
                ButtonType.L1.ToString(),
                ButtonType.R1.ToString()
            },
            Effects =
            {
                new SpecialActionEffect
                {
                    Type = SpecialActionTypes.SetPlayerLeds,
                    PlayerLeds = new PlayerLedSettings
                    {
                        Mask = mask
                    }
                }
            },
            ApplyWhileHeld = true,
            EnabledControllers =
            {
                "test"
            }
        };

    /// <summary>
    /// Fires the L1+R1 action by holding the combination.
    /// </summary>
    private static void HoldCombo(DualSenseDevice device)
    {
        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
    }

    /// <summary>
    /// Releases the L1+R1 combination, ending any while-held action.
    /// </summary>
    private static void ReleaseCombo(DualSenseDevice device)
    {
        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
    }

    [Test]
    public void SendOutputState_ActiveLightAction_OverridesLightbarColor()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        engine.UpdateActions([CreateSustainedLightbarAction(0xAA, 0xBB, 0xCC)]);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);

        // Fire the action by holding the combination.
        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        hid.Writes.Clear();

        // The game's output state must carry the action's color, not the game's.
        outputs.SendOutputState(GameOutput(0x01, 0x02, 0x03));
        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes[0][45], Is.EqualTo(0xAA));
            Assert.That(hid.Writes[0][46], Is.EqualTo(0xBB));
            Assert.That(hid.Writes[0][47], Is.EqualTo(0xCC));
        });

        // Releasing the combination ends the action: the game's color passes through.
        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        hid.Writes.Clear();
        outputs.SendOutputState(GameOutput(0x01, 0x02, 0x03));
        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes[0][45], Is.EqualTo(0x01));
            Assert.That(hid.Writes[0][46], Is.EqualTo(0x02));
            Assert.That(hid.Writes[0][47], Is.EqualTo(0x03));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void SendOutputState_NoActiveLightAction_PassesGameColorThrough()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        engine.UpdateActions([CreateSustainedLightbarAction(0xAA, 0xBB, 0xCC)]);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);

        outputs.SendOutputState(GameOutput(0x01, 0x02, 0x03));
        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes[0][45], Is.EqualTo(0x01));
            Assert.That(hid.Writes[0][46], Is.EqualTo(0x02));
            Assert.That(hid.Writes[0][47], Is.EqualTo(0x03));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void SendOutputState_ActivePlayerLedsAction_OverridesPlayerLedsOnly()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        engine.UpdateActions([CreateSustainedPlayerLedsAction(0x05)]);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);

        HoldCombo(device);
        hid.Writes.Clear();

        // The game's player LEDs must be replaced by the action's mask; the game's
        // lightbar color passes through, since no action holds a color.
        SetStateData game = new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators,
            PlayerLeds = (PlayerLedMask)0x02,
            LedRed = 0x01,
            LedGreen = 0x02,
            LedBlue = 0x03
        };
        outputs.SendOutputState(game);
        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes[0][44], Is.EqualTo(0x05));
            Assert.That(hid.Writes[0][45], Is.EqualTo(0x01));
            Assert.That(hid.Writes[0][46], Is.EqualTo(0x02));
            Assert.That(hid.Writes[0][47], Is.EqualTo(0x03));
        });

        // Releasing the combination ends the action: the game's mask passes through.
        // Releasing the combination ends the action: the game's mask passes through.
        ReleaseCombo(device);
        hid.Writes.Clear();
        outputs.SendOutputState(game);
        Assert.That(hid.Writes[0][44], Is.EqualTo(0x02));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void SendOutputState_TwoActiveActions_ResolvePerField()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        // Both actions are while-held on the same combination: firing the second
        // re-arms the first, so instead use a timed color action (stays active after
        // release) and a while-held player-LED action fired afterwards.
        SpecialAction color = CreateSustainedLightbarAction(0xAA, 0xBB, 0xCC);
        color.ApplyWhileHeld = false;
        color.DurationMs = 300;
        SpecialAction leds = CreateSustainedPlayerLedsAction(0x05);
        leds.Buttons = new List<string>
        {
            ButtonType.Cross.ToString(),
            ButtonType.Circle.ToString()
        };
        engine.UpdateActions([color, leds]);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);

        // Fire the timed color action on L1+R1.
        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        hid.Writes.Clear();

        // Fire the while-held player-LED action on Cross+Circle.
        FeedReport(device, CreateReport(ButtonType.Cross, ButtonType.Circle));
        hid.Writes.Clear();

        // Both actions are active: the newer action's LEDs and the older action's
        // color must both override the game's output.
        SetStateData game = new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators,
            PlayerLeds = (PlayerLedMask)0x02,
            LedRed = 0x01,
            LedGreen = 0x02,
            LedBlue = 0x03
        };
        outputs.SendOutputState(game);
        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes[0][44], Is.EqualTo(0x05));
            Assert.That(hid.Writes[0][45], Is.EqualTo(0xAA));
            Assert.That(hid.Writes[0][46], Is.EqualTo(0xBB));
            Assert.That(hid.Writes[0][47], Is.EqualTo(0xCC));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void SendOutputState_OtherFields_UntouchedByOverride()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        engine.UpdateActions([CreateSustainedLightbarAction(0xAA, 0xBB, 0xCC)]);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        hid.Writes.Clear();

        SetStateData game = new SetStateData
        {
            ValidFlag0 = ValidFlags.AllowLeftTriggerFfb,
            ValidFlag1 = ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators,
            RumbleLeft = 0x11,
            PlayerLeds = (PlayerLedMask)0x05,
            LedRed = 0x01,
            LedGreen = 0x02,
            LedBlue = 0x03
        };
        outputs.SendOutputState(game);

        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        byte[] report = hid.Writes[0];
        Assert.Multiple(() =>
        {
            // Rumble byte (payload byte 3) keeps the game's value.
            Assert.That(report[4], Is.EqualTo(0x11));
            // Player LEDs (payload byte 43) keep the game's mask.
            Assert.That(report[44], Is.EqualTo(0x05));
            // Only the RGB bytes are overridden.
            Assert.That(report[45], Is.EqualTo(0xAA));
            Assert.That(report[46], Is.EqualTo(0xBB));
            Assert.That(report[47], Is.EqualTo(0xCC));
        });

        engine.Dispose();
        device.Dispose();
    }
}