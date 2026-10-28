using System.Reflection;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Hid;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.Emulation;

public class DualSenseDeviceOutputsTests
{
    private sealed class RecordingHidDevice(byte[]? firmwareReport = null) : IHidDevice
    {
        private readonly object _writesLock = new object();

        private readonly List<byte[]> _writes = [];

        /// <summary>
        /// Snapshot of every write, safe to read while an effect timer is writing too.
        /// </summary>
        public IReadOnlyList<byte[]> Writes
        {
            get
            {
                lock (_writesLock)
                {
                    return _writes.ToArray();
                }
            }
        }

        /// <summary>
        /// Discards all recorded writes.
        /// </summary>
        public void Clear()
        {
            lock (_writesLock)
            {
                _writes.Clear();
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
            lock (_writesLock)
            {
                _writes.Add(copy);
            }

            return count;
        }

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) =>
            reportId == 0x20 && firmwareReport is not null ? (byte[])firmwareReport.Clone() : [];

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
        ProcessInputReportMethod.Invoke(device, [buffer, buffer.Length]);

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

    /// <summary>
    /// Marker stored in the game payload's rumble byte so the merged write can be told
    /// apart from the effect timer's own frames.
    /// </summary>
    private const byte GameMarker = 0x5A;

    /// <summary>
    /// The game output a virtual controller would forward, tagged with <see cref="GameMarker"/>.
    /// </summary>
    private static SetStateData GameOutputWithMarker(
        byte red,
        byte green,
        byte blue,
        PlayerLedMask leds = PlayerLedMask.None) =>
        new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowLedColor
                         | (leds == PlayerLedMask.None ? ValidFlags.None : ValidFlags.AllowPlayerIndicators),
            RumbleLeft = GameMarker,
            PlayerLeds = leds,
            LedRed = red,
            LedGreen = green,
            LedBlue = blue
        };

    /// <summary>
    /// Finds the one write carrying the game marker, i.e. the merged game output.
    /// </summary>
    private static byte[] FindMergedWrite(RecordingHidDevice hid) =>
        hid.Writes.Single(write => write[4] == GameMarker);

    /// <summary>
    /// A breathing profile that evaluates to its base color (10,20,30) at every phase.
    /// </summary>
    private static Profile AnimatedProfile() =>
        new Profile
        {
            Name = "Test",
            Lightbar = new LightbarSettings
            {
                Mode = LightbarMode.Breathing,
                Red = 10,
                Green = 20,
                Blue = 30,
                Brightness = 100,
                BreathMin = 100,
                BreathMax = 100
            }
        };

    /// <summary>
    /// A battery profile that drives the charging chase on the player LEDs.
    /// </summary>
    private static Profile BatteryChaseProfile() =>
        new Profile
        {
            Name = "Test",
            Lightbar = new LightbarSettings
            {
                Mode = LightbarMode.Battery,
                BatteryPlayerLeds = true
            }
        };

    /// <summary>
    /// Builds a valid 0x20 firmware report whose hardware generation is 2
    /// (full player-LED support).
    /// </summary>
    private static byte[] FirmwareReportGeneration2()
    {
        byte[] raw = new byte[64];
        raw[0] = 0x20;
        raw[25] = 0x02;
        return raw;
    }

    /// <summary>
    /// Builds an input report carrying a charging battery.
    /// </summary>
    private static byte[] CreateChargingReport()
    {
        byte[] buffer = CreateReport();
        buffer[53] = 0x17;
        return buffer;
    }

    [Test]
    public void SendOutputState_EffectOverridesGameColor_WhenPassthroughDisabled()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        using SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);
        device.ApplyProfile(AnimatedProfile());
        hid.Clear();

        outputs.SendOutputState(GameOutputWithMarker(0x01, 0x02, 0x03));

        byte[] write = FindMergedWrite(hid);
        Assert.Multiple(() =>
        {
            Assert.That(write[45], Is.EqualTo(10));
            Assert.That(write[46], Is.EqualTo(20));
            Assert.That(write[47], Is.EqualTo(30));
            Assert.That(write[2] & (byte)ValidFlags.AllowLedColor, Is.Not.Zero);
        });
    }

    [Test]
    public void SendOutputState_PassthroughEnabled_PassesGameColorThrough()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        using SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);
        device.LightbarPassthroughEnabled = true;
        device.ApplyProfile(AnimatedProfile());
        hid.Clear();

        outputs.SendOutputState(GameOutputWithMarker(0x01, 0x02, 0x03));

        byte[] write = FindMergedWrite(hid);
        Assert.Multiple(() =>
        {
            Assert.That(write[45], Is.EqualTo(0x01));
            Assert.That(write[46], Is.EqualTo(0x02));
            Assert.That(write[47], Is.EqualTo(0x03));
        });
    }

    [Test]
    public void SendOutputState_SpecialActionWinsOverEffect()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        using SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        engine.UpdateActions([CreateSustainedLightbarAction(0xAA, 0xBB, 0xCC)]);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);
        device.ApplyProfile(AnimatedProfile());
        HoldCombo(device);
        hid.Clear();

        outputs.SendOutputState(GameOutputWithMarker(0x01, 0x02, 0x03));

        byte[] write = FindMergedWrite(hid);
        Assert.Multiple(() =>
        {
            Assert.That(write[45], Is.EqualTo(0xAA));
            Assert.That(write[46], Is.EqualTo(0xBB));
            Assert.That(write[47], Is.EqualTo(0xCC));
        });
    }

    [Test]
    public void SendOutputState_ChargingChaseOverridesGamePlayerLeds()
    {
        RecordingHidDevice hid = new RecordingHidDevice(FirmwareReportGeneration2());
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        using SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);
        FeedReport(device, CreateChargingReport());
        device.ApplyProfile(BatteryChaseProfile());
        hid.Clear();

        outputs.SendOutputState(GameOutputWithMarker(0x01, 0x02, 0x03, (PlayerLedMask)0x1F));

        byte[] write = FindMergedWrite(hid);
        Assert.Multiple(() =>
        {
            // The chase phase may advance during the test, so any single LED is valid.
            Assert.That(write[44], Is.AnyOf(0x01, 0x02, 0x04, 0x08, 0x10));
            Assert.That(write[2] & (byte)ValidFlags.AllowPlayerIndicators, Is.Not.Zero);
        });
    }

    [Test]
    public void SendOutputState_ChargingChaseSuppressed_WhenPlayerLedsPassthroughEnabled()
    {
        RecordingHidDevice hid = new RecordingHidDevice(FirmwareReportGeneration2());
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        using SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);
        FeedReport(device, CreateChargingReport());
        device.ApplyProfile(BatteryChaseProfile());
        device.PlayerLedPassthroughEnabled = true;
        hid.Clear();

        outputs.SendOutputState(GameOutputWithMarker(0x01, 0x02, 0x03, (PlayerLedMask)0x1F));

        byte[] write = FindMergedWrite(hid);
        Assert.That(write[44], Is.EqualTo(0x1F));
    }

    [Test]
    public void NeedsOutputStateBlock_TracksActiveOverrides()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        using SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        engine.UpdateActions([CreateSustainedLightbarAction(0xAA, 0xBB, 0xCC)]);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);

        Assert.That(outputs.NeedsOutputStateBlock, Is.False);

        device.ApplyProfile(AnimatedProfile());
        Assert.That(outputs.NeedsOutputStateBlock, Is.True);

        device.LightbarPassthroughEnabled = true;
        device.PlayerLedPassthroughEnabled = true;
        Assert.That(outputs.NeedsOutputStateBlock, Is.False);

        HoldCombo(device);
        Assert.That(outputs.NeedsOutputStateBlock, Is.True);
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
        hid.Clear();

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
        hid.Clear();
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
        hid.Clear();

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
        hid.Clear();
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
        hid.Clear();

        // Fire the while-held player-LED action on Cross+Circle.
        FeedReport(device, CreateReport(ButtonType.Cross, ButtonType.Circle));
        hid.Clear();

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
        hid.Clear();

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

    [Test]
    public void SendOutputState_ProfileLightsRestored_WhenPassthroughDisabled()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);
        device.ApplyProfile(new Profile
        {
            Name = "Single",
            Lightbar = new LightbarSettings
            {
                Red = 200,
                Green = 100,
                Blue = 50
            },
            PlayerLeds = new PlayerLedSettings
            {
                Mask = 0x0A
            }
        });
        device.LightbarPassthroughEnabled = false;
        device.PlayerLedPassthroughEnabled = false;
        hid.Clear();

        // The audio lane's state block clears the lightbar to release the game's color.
        outputs.SendOutputState(new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators,
            PlayerLeds = (PlayerLedMask)0x1F,
            LedRed = 0,
            LedGreen = 0,
            LedBlue = 0
        });

        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        byte[] report = hid.Writes[0];
        Assert.Multiple(() =>
        {
            Assert.That(report[44], Is.EqualTo(0x0A));
            Assert.That(report[45], Is.EqualTo(200));
            Assert.That(report[46], Is.EqualTo(100));
            Assert.That(report[47], Is.EqualTo(50));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void SendOutputState_ProfileLightbarOwned_KeepsForwardedPlayerLeds()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);
        device.ApplyProfile(new Profile
        {
            Name = "Single",
            Lightbar = new LightbarSettings
            {
                Red = 1,
                Green = 2,
                Blue = 3
            },
            PlayerLeds = new PlayerLedSettings
            {
                Mask = 0x0A
            }
        });
        device.LightbarPassthroughEnabled = false;
        device.PlayerLedPassthroughEnabled = true;
        hid.Clear();

        outputs.SendOutputState(new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators,
            PlayerLeds = (PlayerLedMask)0x1F,
            LedRed = 0,
            LedGreen = 0,
            LedBlue = 0
        });

        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        byte[] report = hid.Writes[0];
        Assert.Multiple(() =>
        {
            Assert.That(report[44], Is.EqualTo(0x1F), "the game still owns the player LEDs");
            Assert.That(report[45], Is.EqualTo(1));
            Assert.That(report[46], Is.EqualTo(2));
            Assert.That(report[47], Is.EqualTo(3));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void SendOutputState_ProfileFullyForwarded_KeepsGameLights()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);
        device.ApplyProfile(new Profile
        {
            Name = "Single",
            Lightbar = new LightbarSettings
            {
                Red = 200,
                Green = 100,
                Blue = 50
            },
            PlayerLeds = new PlayerLedSettings
            {
                Mask = 0x0A
            }
        });
        device.LightbarPassthroughEnabled = true;
        device.PlayerLedPassthroughEnabled = true;
        hid.Clear();

        outputs.SendOutputState(new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators,
            PlayerLeds = (PlayerLedMask)0x05,
            LedRed = 1,
            LedGreen = 2,
            LedBlue = 3
        });

        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        byte[] report = hid.Writes[0];
        Assert.Multiple(() =>
        {
            Assert.That(report[44], Is.EqualTo(0x05));
            Assert.That(report[45], Is.EqualTo(1));
            Assert.That(report[46], Is.EqualTo(2));
            Assert.That(report[47], Is.EqualTo(3));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void NeedsOutputStateBlock_TracksProfileLightOwnership()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device, engine);

        Assert.That(outputs.NeedsOutputStateBlock, Is.False);

        device.ApplyProfile(new Profile
        {
            Name = "Single",
            Lightbar = new LightbarSettings
            {
                Red = 1,
                Green = 2,
                Blue = 3
            }
        });
        Assert.That(outputs.NeedsOutputStateBlock, Is.True);

        device.LightbarPassthroughEnabled = true;
        device.PlayerLedPassthroughEnabled = true;
        Assert.That(outputs.NeedsOutputStateBlock, Is.False);

        device.PlayerLedPassthroughEnabled = false;
        Assert.That(outputs.NeedsOutputStateBlock, Is.True);

        engine.Dispose();
        device.Dispose();
    }
}