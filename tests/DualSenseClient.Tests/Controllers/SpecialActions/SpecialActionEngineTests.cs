using System.Diagnostics;
using System.Reflection;
using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Hid;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.SpecialActions;

public class SpecialActionEngineTests
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

    /// <summary>
    /// Maps a button to its (buffer index, bit mask) in a USB input report. The payload
    /// starts at buffer offset 1, so payload byte 7 (face) is buffer[8], byte 8
    /// (shoulders/system) is buffer[9], and byte 9 (PS/touchpad/mute/Edge) is buffer[10].
    /// </summary>
    private static (int Index, byte Mask) Bit(ButtonType button) => button switch
    {
        ButtonType.Square => (8, 0x10),
        ButtonType.Cross => (8, 0x20),
        ButtonType.Circle => (8, 0x40),
        ButtonType.Triangle => (8, 0x80),
        ButtonType.L1 => (9, 0x01),
        ButtonType.R1 => (9, 0x02),
        ButtonType.L2 => (9, 0x04),
        ButtonType.R2 => (9, 0x08),
        ButtonType.Create => (9, 0x10),
        ButtonType.Options => (9, 0x20),
        ButtonType.L3 => (9, 0x40),
        ButtonType.R3 => (9, 0x80),
        ButtonType.PS => (10, 0x01),
        ButtonType.TouchPad => (10, 0x02),
        ButtonType.Mute => (10, 0x04),
        ButtonType.Edge_LeftFunction => (10, 0x10),
        ButtonType.Edge_RightFunction => (10, 0x20),
        ButtonType.Edge_LeftPaddle => (10, 0x40),
        ButtonType.Edge_RightPaddle => (10, 0x80),
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, null)
    };

    private static byte[] CreateReport(params ButtonType[] pressed)
    {
        byte[] buffer = new byte[64];
        buffer[0] = 0x01; // USB input report ID
        buffer[8] = 0x08; // D-Pad neutral, no face buttons
        foreach (ButtonType button in pressed)
        {
            (int index, byte mask) = Bit(button);
            buffer[index] |= mask;
        }

        return buffer;
    }

    /// <summary>
    /// Creates a report with a battery byte set. The payload starts at buffer offset 1, so
    /// payload byte 52 (battery) is buffer[53]. Discharging (high nibble 0) maps a raw
    /// level n to n*10+5 percent.
    /// </summary>
    private static byte[] CreateReportWithBattery(byte battery, params ButtonType[] pressed)
    {
        byte[] buffer = CreateReport(pressed);
        buffer[53] = battery;
        return buffer;
    }

    /// <summary>
    /// Creates a report with no finger on the touchpad: both touch points inactive. The
    /// payload starts at buffer offset 1, so payload bytes 32-39 (touchpad) are
    /// buffer[33..40].
    /// </summary>
    private static byte[] CreateNoTouchReport()
    {
        byte[] buffer = CreateReport();
        buffer[33] = 0x80;
        buffer[34] = 0x00;
        buffer[35] = 0x00;
        buffer[36] = 0x00;
        buffer[37] = 0x80;
        buffer[38] = 0x00;
        buffer[39] = 0x00;
        buffer[40] = 0x00;
        return buffer;
    }

    /// <summary>
    /// Creates a report with touch point 1 active at the given position (touch point 2
    /// inactive). Packed 12-bit coordinates: X = ((b2 & 0x0F) << 8) | b1, Y = (b3 << 4) | (b2 >> 4).
    /// </summary>
    private static byte[] CreateTouchReport(ushort x, ushort y, byte trackingId = 1)
    {
        byte[] buffer = CreateNoTouchReport();
        buffer[33] = (byte)(trackingId & 0x7F);
        buffer[34] = (byte)(x & 0xFF);
        buffer[35] = (byte)(((y & 0x0F) << 4) | ((x >> 8) & 0x0F));
        buffer[36] = (byte)(y >> 4);
        return buffer;
    }

    /// <summary>
    /// Creates a report with both touch points active.
    /// </summary>
    private static byte[] CreateTwoTouchReport(ushort x1, ushort y1, ushort x2, ushort y2)
    {
        byte[] buffer = CreateTouchReport(x1, y1, 1);
        buffer[37] = 0x02;
        buffer[38] = (byte)(x2 & 0xFF);
        buffer[39] = (byte)(((y2 & 0x0F) << 4) | ((x2 >> 8) & 0x0F));
        buffer[40] = (byte)(y2 >> 4);
        return buffer;
    }

    private static void FeedReport(DualSenseDevice device, byte[] buffer) =>
        ProcessInputReportMethod.Invoke(device, [buffer]);

    /// <summary>
    /// Performs a single-finger swipe: finger down at <paramref name="fromX"/>, then moved to
    /// <paramref name="toX"/> (past the engine's swipe threshold). The finger is not lifted,
    /// so a hold can be released or re-armed later.
    /// </summary>
    private static void SwipeRight(DualSenseDevice device, ushort fromX = 100, ushort toX = 600)
    {
        FeedReport(device, CreateNoTouchReport());
        FeedReport(device, CreateTouchReport(fromX, 500));
        FeedReport(device, CreateTouchReport(toX, 520));
    }

    /// <summary>
    /// Lifts the finger from the touchpad, ending the current gesture.
    /// </summary>
    private static void LiftFinger(DualSenseDevice device) => FeedReport(device, CreateNoTouchReport());

    private static SpecialAction CreateAction(params ButtonType[] buttons) => new SpecialAction
    {
        Buttons = buttons.Select(b => b.ToString()).ToList(),
        Effects =
        {
            new SpecialActionEffect
            {
                Type = SpecialActionTypes.Disconnect
            }
        },
        EnabledControllers =
        {
            "test"
        }
    };

    /// <summary>
    /// Creates a wired device + engine: the engine is attached to a real
    /// <see cref="DualSenseDevice"/> over a recording HID stub, and receives the action.
    /// </summary>
    private static (DualSenseDevice Device, RecordingHidDevice Hid, SpecialActionEngine Engine) CreateWired(
        params SpecialAction[] actions)
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        engine.UpdateActions(actions);
        return (device, hid, engine);
    }

    /// <summary>
    /// Creates a lightbar action on L1+R1 with the given RGB and apply-while-held setting.
    /// </summary>
    private static SpecialAction CreateLightbarAction(byte red, byte green, byte blue, bool applyWhileHeld) =>
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
            ApplyWhileHeld = applyWhileHeld,
            EnabledControllers =
            {
                "test"
            }
        };

    /// <summary>
    /// The profile the engine should revert to after while-held actions, RGB (1, 2, 3).
    /// </summary>
    private static Profile CreateRestoreProfile() => new Profile
    {
        Name = "restore",
        Lightbar =
        {
            Red = 1,
            Green = 2,
            Blue = 3
        }
    };

    /// <summary>
    /// Records the calls a real sound player would make.
    /// </summary>
    private sealed class FakeSoundPlayer : ISpecialActionSoundPlayer
    {
        public List<string> PlayedPaths { get; } = new List<string>();
        public SoundOutputTarget LastOutput { get; private set; }
        public byte LastVolume { get; private set; }
        public bool LastHaptics { get; private set; }
        public int LastStrength { get; private set; }
        public int StopCount { get; private set; }
        public bool Disposed { get; private set; }

        public void Play(string path, SoundOutputTarget output, byte speakerVolume, bool hapticFeedback, int hapticStrength)
        {
            PlayedPaths.Add(path);
            LastOutput = output;
            LastVolume = speakerVolume;
            LastHaptics = hapticFeedback;
            LastStrength = hapticStrength;
        }

        public void Stop() => StopCount++;

        public void Dispose() => Disposed = true;
    }

    /// <summary>
    /// Creates a sound action on L1+R1 for the given file and apply-while-held setting.
    /// </summary>
    private static SpecialAction CreateSoundAction(string? path, bool applyWhileHeld) => new SpecialAction
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
                Type = SpecialActionTypes.PlaySound,
                Sound = new SoundSettings
                {
                    Path = path
                }
            }
        },
        ApplyWhileHeld = applyWhileHeld,
        EnabledControllers =
        {
            "test"
        }
    };

    /// <summary>
    /// Creates a battery-level action on L1+R1.
    /// </summary>
    private static SpecialAction CreateBatteryAction(bool applyWhileHeld) => new SpecialAction
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
                Type = SpecialActionTypes.ShowBatteryLevel
            }
        },
        ApplyWhileHeld = applyWhileHeld,
        EnabledControllers =
        {
            "test"
        }
    };

    /// <summary>
    /// Creates a gesture action with a disconnect effect for the given gesture.
    /// </summary>
    private static SpecialAction CreateGestureAction(string gesture) => new SpecialAction
    {
        TouchpadGesture = gesture,
        Effects =
        {
            new SpecialActionEffect
            {
                Type = SpecialActionTypes.Disconnect
            }
        },
        EnabledControllers =
        {
            "test"
        }
    };

    /// <summary>
    /// Creates a lightbar action triggered by the given gesture with the given RGB and
    /// apply-while-held setting.
    /// </summary>
    private static SpecialAction CreateGestureLightbarAction(string gesture, byte red, byte green, byte blue, bool applyWhileHeld) =>
        new SpecialAction
        {
            TouchpadGesture = gesture,
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
            ApplyWhileHeld = applyWhileHeld,
            EnabledControllers =
            {
                "test"
            }
        };

    /// <summary>
    /// Polls until the condition holds or the timeout elapses. Needed because the
    /// hold-duration timer fires on a thread-pool thread.
    /// </summary>
    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    [Test]
    public void ExactCombo_FiresOnceOnFirstHold_AndReArmsOnRelease()
    {
        using DualSenseDevice device = new DualSenseDevice(new RecordingHidDevice(), new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        engine.UpdateActions([CreateAction(ButtonType.L1, ButtonType.R1)]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1));
        Assert.That(executions, Is.EqualTo(0));

        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(executions, Is.EqualTo(1));

        // No state change: still held, must not re-fire.
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(executions, Is.EqualTo(1));

        // Releasing one button breaks the combo and re-arms it.
        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(executions, Is.EqualTo(2));

        engine.Dispose();
    }

    [Test]
    public void ExtraButton_Held_PreventsFire_ButFiresWhenReleasedIntoExactCombo()
    {
        using DualSenseDevice device = new DualSenseDevice(new RecordingHidDevice(), new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        engine.UpdateActions([CreateAction(ButtonType.L1, ButtonType.R1)]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1, ButtonType.Triangle));
        Assert.That(executions, Is.EqualTo(0));

        // Releasing the extra button completes the exact combination.
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(executions, Is.EqualTo(1));

        engine.Dispose();
    }

    [Test]
    public void ExtraButton_DoesNotReArm_AlreadyFiredAction()
    {
        using DualSenseDevice device = new DualSenseDevice(new RecordingHidDevice(), new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        engine.UpdateActions([CreateAction(ButtonType.L1, ButtonType.R1)]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(executions, Is.EqualTo(1));

        // Holding extra buttons then releasing them must not fire again.
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1, ButtonType.Triangle));
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(executions, Is.EqualTo(1));

        // Breaking the combo (releasing a combo button) re-arms it.
        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(executions, Is.EqualTo(2));

        engine.Dispose();
    }

    [Test]
    public void SingleButtonCombo_FiresOnPress()
    {
        using DualSenseDevice device = new DualSenseDevice(new RecordingHidDevice(), new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        engine.UpdateActions([CreateAction(ButtonType.PS)]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.PS));
        Assert.That(executions, Is.EqualTo(1));

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.PS));
        Assert.That(executions, Is.EqualTo(2));

        engine.Dispose();
    }

    [Test]
    public void Action_NotEnabledForController_DoesNotFire()
    {
        using DualSenseDevice device = new DualSenseDevice(new RecordingHidDevice(), new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.EnabledControllers.Clear();
        action.EnabledControllers.Add("AA:BB:CC:DD:EE:FF");
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(executions, Is.EqualTo(0));

        engine.Dispose();
    }

    [Test]
    public void Combo_WithUnknownButtonName_DoesNotFire()
    {
        using DualSenseDevice device = new DualSenseDevice(new RecordingHidDevice(), new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        SpecialAction action = CreateAction(ButtonType.L1);
        action.Buttons.Add("BogusButton");
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1));
        Assert.That(executions, Is.EqualTo(0));

        engine.Dispose();
    }

    [Test]
    public void EmptyCombo_DoesNotFire()
    {
        using DualSenseDevice device = new DualSenseDevice(new RecordingHidDevice(), new StubHidDeviceInfo());
        SpecialActionEngine engine = new SpecialActionEngine();
        engine.Attach(device);
        SpecialAction action = CreateAction();
        action.Buttons.Clear();
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1));
        Assert.That(executions, Is.EqualTo(0));

        engine.Dispose();
    }

    [Test]
    public void SetLightbarColor_WritesColorReport()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.Effects[0].Type = SpecialActionTypes.SetLightbarColor;
        action.Effects[0].Lightbar.Red = 0xAA;
        action.Effects[0].Lightbar.Green = 0xBB;
        action.Effects[0].Lightbar.Blue = 0xCC;
        engine.UpdateActions([action]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));

        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        byte[] report = hid.Writes[^1];
        Assert.Multiple(() =>
        {
            // Report ID 0x02 (USB output), payload at offset 1.
            Assert.That(report[0], Is.EqualTo(0x02));
            // ValidFlag1 = AllowLedColor (payload byte 1), ValidFlag2 = AllowColorFadeAnim
            // (payload byte 38), light fade animation setup byte (payload byte 41).
            Assert.That(report[2], Is.EqualTo((byte)ValidFlags.AllowLedColor));
            Assert.That(report[39], Is.EqualTo((byte)ValidFlags.AllowColorFadeAnim));
            Assert.That(report[42], Is.EqualTo(0x02));
            // Lightbar RGB (payload bytes 44-46).
            Assert.That(report[45], Is.EqualTo(0xAA));
            Assert.That(report[46], Is.EqualTo(0xBB));
            Assert.That(report[47], Is.EqualTo(0xCC));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void AllEffectsDisabled_DoesNotFireOrWrite()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.Effects[0].Type = SpecialActionTypes.SetLightbarColor;
        action.Effects[0].Lightbar.Red = 0xAA;
        action.Effects[0].Lightbar.Green = 0xBB;
        action.Effects[0].Lightbar.Blue = 0xCC;
        action.Effects[0].Enabled = false;
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.EqualTo(0));
            Assert.That(hid.Writes, Is.Empty);
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void DisabledEffect_IsSkipped_ButEnabledEffectsExecute()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.Effects[0].Type = SpecialActionTypes.SetLightbarColor;
        action.Effects[0].Lightbar.Red = 0xAA;
        action.Effects[0].Lightbar.Green = 0xBB;
        action.Effects[0].Lightbar.Blue = 0xCC;
        action.Effects[0].Enabled = false;
        action.Effects.Add(new SpecialActionEffect
        {
            Type = SpecialActionTypes.SetPlayerLeds,
            PlayerLeds = new PlayerLedSettings
            {
                Mask = 0x05
            }
        });
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.EqualTo(1));
            // Only the player LEDs report was written; the disabled lightbar effect was skipped.
            Assert.That(hid.Writes, Has.Count.EqualTo(1));
            Assert.That(hid.Writes[0][2], Is.EqualTo((byte)ValidFlags.AllowPlayerIndicators));
            Assert.That(hid.Writes[0][44], Is.EqualTo(0x05));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void SetPlayerLeds_WritesLedMaskReport()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.Effects[0].Type = SpecialActionTypes.SetPlayerLeds;
        action.Effects[0].PlayerLeds.Mask = 0x05; // LEDs 1 and 3
        engine.UpdateActions([action]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));

        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        byte[] report = hid.Writes[^1];
        Assert.Multiple(() =>
        {
            // ValidFlag1 = AllowPlayerIndicators (payload byte 1), LED mask at payload byte 43.
            Assert.That(report[2], Is.EqualTo((byte)ValidFlags.AllowPlayerIndicators));
            Assert.That(report[44], Is.EqualTo(0x05));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void DisconnectAction_Executes()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired(CreateAction(ButtonType.L1, ButtonType.R1));
        int executions = 0;
        SpecialAction? executed = null;
        engine.ActionExecuted += (_, e) =>
        {
            executions++;
            executed = e.Action;
        };

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.EqualTo(1));
            Assert.That(executed?.Effects.Single(e => e.Type == SpecialActionTypes.Disconnect), Is.Not.Null);
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void UnknownActionType_DoesNotExecuteOrWrite()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.Effects[0].Type = "BogusAction";
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.EqualTo(0));
            Assert.That(hid.Writes, Is.Empty);
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void UpdateActions_ReplacesConfiguration()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        SpecialAction first = CreateAction(ButtonType.L1);
        SpecialAction second = CreateAction(ButtonType.R1);
        engine.UpdateActions([first]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1));
        Assert.That(executions, Is.EqualTo(1));

        engine.UpdateActions([second]);

        // The old combination is no longer configured.
        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1));
        Assert.That(executions, Is.EqualTo(1));

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.R1));
        Assert.That(executions, Is.EqualTo(2));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void Detach_StopsFiring()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired(CreateAction(ButtonType.L1));
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        engine.Detach();
        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1));

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.EqualTo(0));
            Assert.That(hid.Writes, Is.Empty);
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void HoldTime_FiresAfterDeadline()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.HoldTimeMs = 200;
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(executions, Is.EqualTo(0));

        Assert.That(WaitUntil(() => executions == 1), Is.True, "Action did not fire after the hold duration");

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void HoldTime_ReleasedBeforeDeadline_DoesNotFire_AndFiresOnNextHold()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.HoldTimeMs = 200;
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Thread.Sleep(50);
        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Thread.Sleep(400);
        Assert.That(executions, Is.EqualTo(0));

        // A fresh exact hold runs the full duration again.
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(WaitUntil(() => executions == 1), Is.True, "Action did not fire on the next hold");

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void HoldTime_ExtraButton_InterruptsAndRestartsDeadline()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.HoldTimeMs = 200;
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Thread.Sleep(100);
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1, ButtonType.Triangle));
        Thread.Sleep(400);
        Assert.That(executions, Is.EqualTo(0));

        // Releasing the extra button restarts the hold from scratch.
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Thread.Sleep(150);
        Assert.That(executions, Is.EqualTo(0));
        Assert.That(WaitUntil(() => executions == 1), Is.True, "Action did not fire after the restarted hold");

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void SustainedLightbar_AppliesOnHold_AndRestoresProfileOnRelease()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        engine.UpdateActions([CreateLightbarAction(0xAA, 0xBB, 0xCC, true)]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes[0][45], Is.EqualTo(0xAA));
            Assert.That(hid.Writes[0][46], Is.EqualTo(0xBB));
            Assert.That(hid.Writes[0][47], Is.EqualTo(0xCC));
        });

        // Releasing a combination button reverts to the bound profile.
        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.That(hid.Writes, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes[^1][45], Is.EqualTo(0x01));
            Assert.That(hid.Writes[^1][46], Is.EqualTo(0x02));
            Assert.That(hid.Writes[^1][47], Is.EqualTo(0x03));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void Sustained_ExtraButtons_DoNotRestore()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        engine.UpdateActions([CreateLightbarAction(0xAA, 0xBB, 0xCC, true)]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(hid.Writes, Has.Count.EqualTo(1));

        // Extra buttons held (and released again) neither restore nor re-apply.
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1, ButtonType.Triangle));
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(hid.Writes, Has.Count.EqualTo(1));

        // Breaking the combination restores.
        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.That(hid.Writes, Has.Count.EqualTo(2));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void SustainedLightbar_LightbarColorOverride_ActiveWhileHeld_ClearedOnRelease()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        engine.UpdateActions([CreateLightbarAction(0xAA, 0xBB, 0xCC, true)]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(engine.OutputOverride.LightbarColor, Is.EqualTo(((byte)0xAA, (byte)0xBB, (byte)0xCC)));

        // Releasing a combination button ends the action: the override is released.
        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.That(engine.OutputOverride.LightbarColor, Is.Null);

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void OneShotLightbar_DoesNotSetLightbarColorOverride()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.UpdateActions([CreateLightbarAction(0xAA, 0xBB, 0xCC, false)]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        Assert.That(engine.OutputOverride.LightbarColor, Is.Null);

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void TimedLightbar_LightbarColorOverride_ClearedAfterDuration()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        SpecialAction action = CreateLightbarAction(0xAA, 0xBB, 0xCC, false);
        action.DurationMs = 300;
        engine.UpdateActions([action]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(engine.OutputOverride.LightbarColor, Is.EqualTo(((byte)0xAA, (byte)0xBB, (byte)0xCC)));

        Assert.That(WaitUntil(() => hid.Writes.Count == 2), Is.True, "Timed light action did not restore the profile after its duration");
        Assert.That(hid.Writes, Has.Count.EqualTo(2));
        Assert.That(engine.OutputOverride.LightbarColor, Is.Null);

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void ShowBatteryLevel_WhileHeld_SetsLightbarColorOverride_ClearedOnRelease()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        engine.UpdateActions([CreateBatteryAction(true)]);

        // Raw battery 0x04 = 45% -> level 4 -> default color (255, 200, 30).
        FeedReport(device, CreateReportWithBattery(0x04));
        FeedReport(device, CreateReportWithBattery(0x04, ButtonType.L1, ButtonType.R1));
        Assert.That(engine.OutputOverride.LightbarColor, Is.EqualTo(((byte)255, (byte)200, (byte)30)));

        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.That(engine.OutputOverride.LightbarColor, Is.Null);

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void Gesture_WhileHeldLight_LightbarColorOverride_ClearedOnFingerUp()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        engine.UpdateActions([CreateGestureLightbarAction(TouchpadGestures.SwipeRight, 0xAA, 0xBB, 0xCC, true)]);

        SwipeRight(device);
        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        Assert.That(engine.OutputOverride.LightbarColor, Is.EqualTo(((byte)0xAA, (byte)0xBB, (byte)0xCC)));

        LiftFinger(device);
        Assert.That(engine.OutputOverride.LightbarColor, Is.Null);

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void SustainedPlayerLeds_SetsPlayerLedOverride_ClearedOnRelease()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.Effects[0].Type = SpecialActionTypes.SetPlayerLeds;
        action.Effects[0].PlayerLeds.Mask = 0x05;
        action.ApplyWhileHeld = true;
        engine.UpdateActions([action]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes, Has.Count.EqualTo(1));
            Assert.That(engine.OutputOverride.PlayerLeds, Is.EqualTo((byte)0x05));
            Assert.That(engine.OutputOverride.LightbarColor, Is.Null);
        });

        // Releasing a combination button ends the action: the override is released.
        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.That(engine.OutputOverride.PlayerLeds, Is.Null);

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void CombinedLightEffects_SetBothOverrideFields()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.Effects[0].Type = SpecialActionTypes.SetLightbarColor;
        action.Effects[0].Lightbar.Red = 0xAA;
        action.Effects[0].Lightbar.Green = 0xBB;
        action.Effects[0].Lightbar.Blue = 0xCC;
        action.Effects.Add(new SpecialActionEffect
        {
            Type = SpecialActionTypes.SetPlayerLeds,
            PlayerLeds = new PlayerLedSettings
            {
                Mask = 0x05
            }
        });
        action.ApplyWhileHeld = true;
        engine.UpdateActions([action]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.Multiple(() =>
        {
            Assert.That(engine.OutputOverride.LightbarColor, Is.EqualTo(((byte)0xAA, (byte)0xBB, (byte)0xCC)));
            Assert.That(engine.OutputOverride.PlayerLeds, Is.EqualTo((byte)0x05));
        });

        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.Multiple(() =>
        {
            Assert.That(engine.OutputOverride.LightbarColor, Is.Null);
            Assert.That(engine.OutputOverride.PlayerLeds, Is.Null);
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void OneShotLightbar_DoesNotRestoreOnRelease()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        engine.UpdateActions([CreateLightbarAction(0xAA, 0xBB, 0xCC, false)]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(hid.Writes, Has.Count.EqualTo(1));

        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.That(hid.Writes, Has.Count.EqualTo(1));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void SustainedWithHoldTime_AppliesAfterDeadline_AndRestoresOnRelease()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        SpecialAction action = CreateLightbarAction(0xAA, 0xBB, 0xCC, true);
        action.HoldTimeMs = 200;
        engine.UpdateActions([action]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(hid.Writes, Is.Empty);
        Assert.That(WaitUntil(() => hid.Writes.Count == 1), Is.True, "While-held action did not apply after the hold duration");
        Assert.That(hid.Writes[0][45], Is.EqualTo(0xAA));

        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.That(hid.Writes, Has.Count.EqualTo(2));
        Assert.That(hid.Writes[^1][45], Is.EqualTo(0x01));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void ProfileProvider_Null_DoesNotRestoreOnRelease()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.UpdateActions([CreateLightbarAction(0xAA, 0xBB, 0xCC, true)]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(hid.Writes, Has.Count.EqualTo(1));

        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.That(hid.Writes, Has.Count.EqualTo(1));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void UpdateActions_RestoresActiveSustainedState()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        engine.UpdateActions([CreateLightbarAction(0xAA, 0xBB, 0xCC, true)]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(hid.Writes, Has.Count.EqualTo(1));

        // A config change while the action is active reverts it to the bound profile.
        engine.UpdateActions([CreateAction(ButtonType.Triangle)]);
        Assert.That(hid.Writes, Has.Count.EqualTo(2));
        Assert.That(hid.Writes[^1][45], Is.EqualTo(0x01));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void PlaySound_OneShot_PlaysOncePerHold()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        FakeSoundPlayer player = new FakeSoundPlayer();
        engine.SoundPlayerFactory = _ => player;
        engine.UpdateActions([CreateSoundAction(@"C:\sounds\beep.mp3", false)]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(player.PlayedPaths, Is.EqualTo(new[]
        {
            @"C:\sounds\beep.mp3"
        }));

        // A release does not stop a one-shot sound.
        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.That(player.StopCount, Is.EqualTo(0));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void PlaySound_ForwardsVolumeHapticsAndStrength()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        FakeSoundPlayer player = new FakeSoundPlayer();
        engine.SoundPlayerFactory = _ => player;
        SpecialAction action = CreateSoundAction("beep.wav", false);
        action.Effects[0].Sound.Volume = 0x7F;
        action.Effects[0].Haptics.Feedback = true;
        action.Effects[0].Haptics.Strength = 150;
        engine.UpdateActions([action]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));

        Assert.Multiple(() =>
        {
            Assert.That(player.LastOutput, Is.EqualTo(SoundOutputTarget.Speaker));
            Assert.That(player.LastVolume, Is.EqualTo(0x7F));
            Assert.That(player.LastHaptics, Is.True);
            Assert.That(player.LastStrength, Is.EqualTo(150));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void PlaySound_HeadsetOutput_ForwardsHeadset()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        FakeSoundPlayer player = new FakeSoundPlayer();
        engine.SoundPlayerFactory = _ => player;
        SpecialAction action = CreateSoundAction("beep.wav", false);
        action.Effects[0].Sound.Output = SoundOutputDevices.Headset;
        engine.UpdateActions([action]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));

        Assert.That(player.LastOutput, Is.EqualTo(SoundOutputTarget.Headset));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void PlaySound_WhileHeld_StopsOnComboBreak()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        FakeSoundPlayer player = new FakeSoundPlayer();
        engine.SoundPlayerFactory = _ => player;
        engine.UpdateActions([CreateSoundAction("beep.wav", true)]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(player.PlayedPaths, Has.Count.EqualTo(1));

        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.That(player.StopCount, Is.EqualTo(1));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void PlaySound_WhileHeld_ExtraButtonsDoNotStop()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        FakeSoundPlayer player = new FakeSoundPlayer();
        engine.SoundPlayerFactory = _ => player;
        engine.UpdateActions([CreateSoundAction("beep.wav", true)]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(player.PlayedPaths, Has.Count.EqualTo(1));

        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1, ButtonType.Triangle));
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(player.StopCount, Is.EqualTo(0));

        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.That(player.StopCount, Is.EqualTo(1));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void PlaySound_NoPath_DoesNotPlay()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        FakeSoundPlayer player = new FakeSoundPlayer();
        engine.SoundPlayerFactory = _ => player;
        engine.UpdateActions([CreateSoundAction(null, false)]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));

        Assert.That(player.PlayedPaths, Is.Empty);

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void PlaySound_NoFactory_DoesNotThrow()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        engine.UpdateActions([CreateSoundAction("beep.wav", false)]);

        Assert.DoesNotThrow(() =>
        {
            FeedReport(device, CreateReport());
            FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void PlaySound_UpdateActions_StopsSustainedSound()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        FakeSoundPlayer player = new FakeSoundPlayer();
        engine.SoundPlayerFactory = _ => player;
        engine.UpdateActions([CreateSoundAction("beep.wav", true)]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(player.PlayedPaths, Has.Count.EqualTo(1));

        engine.UpdateActions([CreateAction(ButtonType.Triangle)]);
        Assert.That(player.StopCount, Is.EqualTo(1));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void Detach_DisposesSoundPlayer()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        FakeSoundPlayer player = new FakeSoundPlayer();
        engine.SoundPlayerFactory = _ => player;
        engine.UpdateActions([CreateSoundAction("beep.wav", false)]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(player.PlayedPaths, Has.Count.EqualTo(1));

        engine.Detach();
        Assert.That(player.Disposed, Is.True);

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void MultipleEffects_ExecuteTogether()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.Effects[0].Type = SpecialActionTypes.SetLightbarColor;
        action.Effects[0].Lightbar.Red = 0xAA;
        action.Effects[0].Lightbar.Green = 0xBB;
        action.Effects[0].Lightbar.Blue = 0xCC;
        action.Effects.Add(new SpecialActionEffect
        {
            Type = SpecialActionTypes.SetPlayerLeds,
            PlayerLeds = new PlayerLedSettings
            {
                Mask = 0x05
            }
        });
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.EqualTo(1));
            Assert.That(hid.Writes, Has.Count.EqualTo(2));
            Assert.That(hid.Writes[0][45], Is.EqualTo(0xAA));
            Assert.That(hid.Writes[0][47], Is.EqualTo(0xCC));
            Assert.That(hid.Writes[1][2], Is.EqualTo((byte)ValidFlags.AllowPlayerIndicators));
            Assert.That(hid.Writes[1][44], Is.EqualTo(0x05));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void MultipleEffects_DisconnectAndLightbar_ExecuteTogether()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.Effects.Add(new SpecialActionEffect
        {
            Type = SpecialActionTypes.SetLightbarColor,
            Lightbar = new LightbarSettings
            {
                Red = 0x11,
                Green = 0x22,
                Blue = 0x33
            }
        });
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.EqualTo(1));
            Assert.That(hid.Writes, Has.Count.EqualTo(1));
            Assert.That(hid.Writes[0][45], Is.EqualTo(0x11));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void MultipleEffects_UnknownEffect_DoesNotBlockKnownOnes()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.Effects[0].Type = "BogusEffect";
        action.Effects.Add(new SpecialActionEffect
        {
            Type = SpecialActionTypes.SetPlayerLeds,
            PlayerLeds = new PlayerLedSettings
            {
                Mask = 0x03
            }
        });
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.EqualTo(1));
            Assert.That(hid.Writes, Has.Count.EqualTo(1));
            Assert.That(hid.Writes[0][44], Is.EqualTo(0x03));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void MultipleEffects_OnlyUnknownEffects_DoesNotFire()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.Effects[0].Type = "BogusEffect";
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.EqualTo(0));
            Assert.That(hid.Writes, Is.Empty);
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void MultipleEffects_WhileHeld_StopsSoundAndRestoresProfileOnRelease()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        FakeSoundPlayer player = new FakeSoundPlayer();
        engine.SoundPlayerFactory = _ => player;
        SpecialAction action = CreateLightbarAction(0xAA, 0xBB, 0xCC, true);
        action.Effects.Add(new SpecialActionEffect
        {
            Type = SpecialActionTypes.PlaySound,
            Sound = new SoundSettings
            {
                Path = "beep.wav"
            }
        });
        engine.UpdateActions([action]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes, Has.Count.EqualTo(1));
            Assert.That(player.PlayedPaths, Has.Count.EqualTo(1));
        });

        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.Multiple(() =>
        {
            Assert.That(player.StopCount, Is.EqualTo(1));
            Assert.That(hid.Writes, Has.Count.EqualTo(2));
            Assert.That(hid.Writes[^1][45], Is.EqualTo(0x01));
            Assert.That(hid.Writes[^1][46], Is.EqualTo(0x02));
            Assert.That(hid.Writes[^1][47], Is.EqualTo(0x03));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void ShowBatteryLevel_WritesColorForCurrentBattery()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.UpdateActions([CreateBatteryAction(false)]);

        // Raw battery 0x04 = discharging at 45% -> level 4 -> default color (255, 200, 30).
        FeedReport(device, CreateReportWithBattery(0x04));
        FeedReport(device, CreateReportWithBattery(0x04, ButtonType.L1, ButtonType.R1));

        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        byte[] report = hid.Writes[^1];
        Assert.Multiple(() =>
        {
            Assert.That(report[2], Is.EqualTo((byte)ValidFlags.AllowLedColor));
            Assert.That(report[45], Is.EqualTo(255));
            Assert.That(report[46], Is.EqualTo(200));
            Assert.That(report[47], Is.EqualTo(30));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void ShowBatteryLevel_FullBattery_UsesHighestLevel()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.UpdateActions([CreateBatteryAction(false)]);

        // Raw battery 0x0A = discharging at 100% -> level 9 (highest).
        FeedReport(device, CreateReportWithBattery(0x0A));
        FeedReport(device, CreateReportWithBattery(0x0A, ButtonType.L1, ButtonType.R1));

        byte[] report = hid.Writes[^1];
        Assert.Multiple(() =>
        {
            Assert.That(report[45], Is.EqualTo(40));
            Assert.That(report[46], Is.EqualTo(180));
            Assert.That(report[47], Is.EqualTo(110));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void ShowBatteryLevel_LowBattery_UsesLowestLevel()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.UpdateActions([CreateBatteryAction(false)]);

        // Raw battery 0x00 = discharging at 5% -> level 0 (lowest).
        FeedReport(device, CreateReportWithBattery(0x00));
        FeedReport(device, CreateReportWithBattery(0x00, ButtonType.L1, ButtonType.R1));

        byte[] report = hid.Writes[^1];
        Assert.Multiple(() =>
        {
            Assert.That(report[45], Is.EqualTo(255));
            Assert.That(report[46], Is.EqualTo(60));
            Assert.That(report[47], Is.EqualTo(60));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void ShowBatteryLevel_UnknownBattery_Skips()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.UpdateActions([CreateBatteryAction(false)]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        // Raw 0xFF = charging error, percentage unknown -> skipped, lightbar untouched.
        FeedReport(device, CreateReportWithBattery(0xFF));
        FeedReport(device, CreateReportWithBattery(0xFF, ButtonType.L1, ButtonType.R1));

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.EqualTo(1));
            Assert.That(hid.Writes, Is.Empty);
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void ShowBatteryLevel_CustomColors_Used()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateBatteryAction(false);
        action.Effects[0].BatteryColors = Enumerable.Range(0, 10)
            .Select(i => new BatteryLevelColor
            {
                Red = (byte)(i * 10),
                Green = (byte)i,
                Blue = (byte)(255 - i)
            })
            .ToList();
        engine.UpdateActions([action]);

        FeedReport(device, CreateReportWithBattery(0x04));
        FeedReport(device, CreateReportWithBattery(0x04, ButtonType.L1, ButtonType.R1));

        byte[] report = hid.Writes[^1];
        Assert.Multiple(() =>
        {
            Assert.That(report[45], Is.EqualTo(40));
            Assert.That(report[46], Is.EqualTo(4));
            Assert.That(report[47], Is.EqualTo(251));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void ShowBatteryLevel_PartialCustomColors_FallBackToDefaults()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateBatteryAction(false);
        action.Effects[0].BatteryColors =
        [
            new BatteryLevelColor
            {
                Red = 1,
                Green = 2,
                Blue = 3
            }
        ];
        engine.UpdateActions([action]);

        // Level 4 has no custom color -> default (255, 200, 30).
        FeedReport(device, CreateReportWithBattery(0x04));
        FeedReport(device, CreateReportWithBattery(0x04, ButtonType.L1, ButtonType.R1));

        byte[] report = hid.Writes[^1];
        Assert.Multiple(() =>
        {
            Assert.That(report[45], Is.EqualTo(255));
            Assert.That(report[46], Is.EqualTo(200));
            Assert.That(report[47], Is.EqualTo(30));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void ShowBatteryLevel_WhileHeld_RestoresProfileOnRelease()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        engine.UpdateActions([CreateBatteryAction(true)]);

        // Raw battery 0x04 = 45% -> level 4 -> default color (255, 200, 30).
        FeedReport(device, CreateReportWithBattery(0x04));
        FeedReport(device, CreateReportWithBattery(0x04, ButtonType.L1, ButtonType.R1));
        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        Assert.That(hid.Writes[0][45], Is.EqualTo(255));

        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.That(hid.Writes, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes[^1][45], Is.EqualTo(0x01));
            Assert.That(hid.Writes[^1][46], Is.EqualTo(0x02));
            Assert.That(hid.Writes[^1][47], Is.EqualTo(0x03));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void ShowBatteryLevel_WithDisconnect_ExecuteTogether()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.Effects.Add(new SpecialActionEffect
        {
            Type = SpecialActionTypes.ShowBatteryLevel
        });
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReportWithBattery(0x09));
        FeedReport(device, CreateReportWithBattery(0x09, ButtonType.L1, ButtonType.R1));

        Assert.Multiple(() =>
        {
            Assert.That(executions, Is.EqualTo(1));
            Assert.That(hid.Writes, Has.Count.EqualTo(1));
            Assert.That(hid.Writes[0][45], Is.EqualTo(40)); // 95% -> level 9 -> (40, 180, 110)
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void TimedLightbar_RestoresProfileAfterDuration()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        SpecialAction action = CreateLightbarAction(0xAA, 0xBB, 0xCC, false);
        action.DurationMs = 300;
        engine.UpdateActions([action]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        Assert.That(hid.Writes[0][45], Is.EqualTo(0xAA));

        Assert.That(WaitUntil(() => hid.Writes.Count == 2), Is.True, "Timed light action did not restore the profile after its duration");
        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes[^1][45], Is.EqualTo(0x01));
            Assert.That(hid.Writes[^1][46], Is.EqualTo(0x02));
            Assert.That(hid.Writes[^1][47], Is.EqualTo(0x03));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void TimedLightbar_ReleaseDoesNotRestoreBeforeDuration()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        SpecialAction action = CreateLightbarAction(0xAA, 0xBB, 0xCC, false);
        action.DurationMs = 2000;
        engine.UpdateActions([action]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(hid.Writes, Has.Count.EqualTo(1));

        // Releasing the combination re-arms the action but does not restore early; the
        // effect stays until the duration elapsed.
        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.That(hid.Writes, Has.Count.EqualTo(1));

        Assert.That(WaitUntil(() => hid.Writes.Count == 2, 4000), Is.True, "Timed light action did not restore the profile after its duration");
        Assert.That(hid.Writes[^1][45], Is.EqualTo(0x01));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void TimedLightbar_ApplyWhileHeldWins_RestoresOnReleaseOnly()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        SpecialAction action = CreateLightbarAction(0xAA, 0xBB, 0xCC, true);
        action.DurationMs = 300;
        engine.UpdateActions([action]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(hid.Writes, Has.Count.EqualTo(1));

        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());
        Assert.That(hid.Writes, Has.Count.EqualTo(2));

        // The duration is ignored while apply-while-held is set: no restore after it.
        Thread.Sleep(500);
        Assert.That(hid.Writes, Has.Count.EqualTo(2));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void TimedLightbar_UpdateActions_RestoresActiveTimedState()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        SpecialAction action = CreateLightbarAction(0xAA, 0xBB, 0xCC, false);
        action.DurationMs = 5000;
        engine.UpdateActions([action]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(hid.Writes, Has.Count.EqualTo(1));

        // A config change while a timed action is active reverts it to the bound profile.
        engine.UpdateActions([CreateAction(ButtonType.Triangle)]);
        Assert.That(hid.Writes, Has.Count.EqualTo(2));
        Assert.That(hid.Writes[^1][45], Is.EqualTo(0x01));

        // The cleared timed state must not restore again.
        Thread.Sleep(200);
        Assert.That(hid.Writes, Has.Count.EqualTo(2));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void TimedPlayerLeds_RestoresProfileAfterDuration()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        SpecialAction action = CreateAction(ButtonType.L1, ButtonType.R1);
        action.Effects[0].Type = SpecialActionTypes.SetPlayerLeds;
        action.Effects[0].PlayerLeds.Mask = 0x05;
        action.DurationMs = 300;
        engine.UpdateActions([action]);

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1, ButtonType.R1));
        Assert.That(hid.Writes, Has.Count.EqualTo(1));

        Assert.That(WaitUntil(() => hid.Writes.Count == 2), Is.True, "Timed player LED action did not restore the profile after its duration");
        Assert.That(hid.Writes[^1][45], Is.EqualTo(0x01));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void GestureSwipeRight_FiresOnSwipe()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired(CreateGestureAction(TouchpadGestures.SwipeRight));
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        SwipeRight(device);

        Assert.That(executions, Is.EqualTo(1));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void GestureSwipeLeft_FiresOnSwipe()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired(CreateGestureAction(TouchpadGestures.SwipeLeft));
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateNoTouchReport());
        FeedReport(device, CreateTouchReport(1500, 500));
        FeedReport(device, CreateTouchReport(900, 520));

        Assert.That(executions, Is.EqualTo(1));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void GestureSwipeUp_FiresOnSwipe()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired(CreateGestureAction(TouchpadGestures.SwipeUp));
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateNoTouchReport());
        FeedReport(device, CreateTouchReport(500, 900));
        FeedReport(device, CreateTouchReport(520, 300));

        Assert.That(executions, Is.EqualTo(1));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void GestureSwipeDown_FiresOnSwipe()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired(CreateGestureAction(TouchpadGestures.SwipeDown));
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateNoTouchReport());
        FeedReport(device, CreateTouchReport(500, 200));
        FeedReport(device, CreateTouchReport(520, 800));

        Assert.That(executions, Is.EqualTo(1));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void Gesture_SwipeBelowThreshold_DoesNotFire()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired(CreateGestureAction(TouchpadGestures.SwipeRight));
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateNoTouchReport());
        FeedReport(device, CreateTouchReport(100, 500));
        FeedReport(device, CreateTouchReport(200, 520));
        LiftFinger(device);

        Assert.That(executions, Is.EqualTo(0));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void Gesture_WrongDirection_DoesNotFire()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired(CreateGestureAction(TouchpadGestures.SwipeLeft));
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        SwipeRight(device);
        LiftFinger(device);

        Assert.That(executions, Is.EqualTo(0));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void Gesture_WithHoldTime_FiresAfterDeadline()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateGestureAction(TouchpadGestures.SwipeRight);
        action.HoldTimeMs = 200;
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        SwipeRight(device);
        Assert.That(executions, Is.EqualTo(0));

        Assert.That(WaitUntil(() => executions == 1), Is.True, "Gesture action did not fire after the hold duration");

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void Gesture_WithHoldTime_FingerLiftedBeforeDeadline_DoesNotFire()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateGestureAction(TouchpadGestures.SwipeRight);
        action.HoldTimeMs = 200;
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        SwipeRight(device);
        Thread.Sleep(50);
        LiftFinger(device);
        Thread.Sleep(400);

        Assert.That(executions, Is.EqualTo(0));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void Gesture_ReArmsOnNextSwipe()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired(CreateGestureAction(TouchpadGestures.SwipeRight));
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        SwipeRight(device);
        Assert.That(executions, Is.EqualTo(1));

        // Releasing the finger re-arms the action; the next swipe fires it again.
        LiftFinger(device);
        SwipeRight(device);
        Assert.That(executions, Is.EqualTo(2));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void Gesture_WhileHeldLight_AppliesOnSwipeAndRestoresOnFingerUp()
    {
        (DualSenseDevice device, RecordingHidDevice hid, SpecialActionEngine engine) = CreateWired();
        engine.ProfileProvider = _ => CreateRestoreProfile();
        engine.UpdateActions([CreateGestureLightbarAction(TouchpadGestures.SwipeRight, 0xAA, 0xBB, 0xCC, true)]);

        SwipeRight(device);
        Assert.That(hid.Writes, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes[0][45], Is.EqualTo(0xAA));
            Assert.That(hid.Writes[0][46], Is.EqualTo(0xBB));
            Assert.That(hid.Writes[0][47], Is.EqualTo(0xCC));
        });

        // Lifting the finger reverts to the bound profile.
        LiftFinger(device);
        Assert.That(hid.Writes, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes[^1][45], Is.EqualTo(0x01));
            Assert.That(hid.Writes[^1][46], Is.EqualTo(0x02));
            Assert.That(hid.Writes[^1][47], Is.EqualTo(0x03));
        });

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void Gesture_TwoFingers_DoesNotFire()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired(CreateGestureAction(TouchpadGestures.SwipeRight));
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateNoTouchReport());
        FeedReport(device, CreateTwoTouchReport(100, 500, 400, 600));
        FeedReport(device, CreateTouchReport(600, 520));
        FeedReport(device, CreateNoTouchReport());

        Assert.That(executions, Is.EqualTo(0));

        engine.Dispose();
        device.Dispose();
    }

    [Test]
    public void GestureAction_IgnoresButtons()
    {
        (DualSenseDevice device, _, SpecialActionEngine engine) = CreateWired();
        SpecialAction action = CreateGestureAction(TouchpadGestures.SwipeRight);
        action.Buttons.Add(ButtonType.L1.ToString());
        engine.UpdateActions([action]);
        int executions = 0;
        engine.ActionExecuted += (_, _) => executions++;

        FeedReport(device, CreateReport());
        FeedReport(device, CreateReport(ButtonType.L1));
        FeedReport(device, CreateReport());

        Assert.That(executions, Is.EqualTo(0));

        engine.Dispose();
        device.Dispose();
    }
}