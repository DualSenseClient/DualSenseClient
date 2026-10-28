using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Lightbar;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Hid;
using DualSenseClient.Settings.Sections;
using DualSenseClient.Tests.Controllers.DualSense.Input;

namespace DualSenseClient.Tests.Controllers.DualSense;

public class DualSenseDeviceLightbarTests
{
    /// <summary>
    /// USB offset of the first valid-flag byte (payload offset 1 plus report ID).
    /// </summary>
    private const int ValidFlag1Offset = 2;

    /// <summary>
    /// USB offset of the second valid-flag byte (payload offset 38 plus report ID).
    /// </summary>
    private const int ValidFlag2Offset = 39;

    private const int LightBrightnessOffset = 43;
    private const int RedOffset = 45;
    private const int GreenOffset = 46;
    private const int BlueOffset = 47;

    private sealed class RecordingHidDevice(byte[]? firmwareReport = null) : IHidDevice
    {
        public ConcurrentQueue<byte[]> Writes { get; } = new ConcurrentQueue<byte[]>();

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
            Writes.Enqueue(buffer[offset..(offset + count)]);
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

        public void Clear() => Writes.Clear();
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

    private static byte[] CloneUsbReport()
    {
        byte[] clone = new byte[InputReportTestData.UsbReport.Length];
        Buffer.BlockCopy(InputReportTestData.UsbReport, 0, clone, 0, clone.Length);
        return clone;
    }

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

    private static DualSenseDevice CreateDevice(RecordingHidDevice hid) =>
        new DualSenseDevice(hid, new StubHidDeviceInfo());

    private static Profile Profile(LightbarSettings lightbar, byte playerLedMask = 0) =>
        new Profile
        {
            Name = "Test",
            Lightbar = lightbar,
            PlayerLeds = new PlayerLedSettings
            {
                Mask = playerLedMask
            }
        };

    private static byte[][] Snapshot(RecordingHidDevice hid) => hid.Writes.ToArray();

    private static byte[]? FindWrite(RecordingHidDevice hid, Func<byte[], bool> predicate)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(2))
        {
            byte[]? match = Snapshot(hid).FirstOrDefault(predicate);
            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(5);
        }

        return null;
    }

    [Test]
    public void ApplyProfile_SingleColor_SendsScaledColorAndHardwareBrightnessHigh()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = CreateDevice(hid);
        LightbarSettings lightbar = new LightbarSettings
        {
            Red = 200,
            Green = 100,
            Blue = 50,
            Brightness = 50
        };

        device.ApplyProfile(Profile(lightbar));

        byte[] write = Snapshot(hid).Single();
        Assert.Multiple(() =>
        {
            Assert.That(write[RedOffset], Is.EqualTo(100));
            Assert.That(write[GreenOffset], Is.EqualTo(50));
            Assert.That(write[BlueOffset], Is.EqualTo(25));
            Assert.That(write[LightBrightnessOffset], Is.EqualTo(0));
            Assert.That(write[ValidFlag2Offset] & (byte)ValidFlags.AllowBrightnessChange, Is.Not.Zero);
            Assert.That(write[ValidFlag1Offset] & (byte)ValidFlags.AllowLedColor, Is.Not.Zero);
        });
    }

    [Test]
    public void ApplyProfile_AnimatedModeOmitsStaticColor()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = CreateDevice(hid);
        device.LightbarPassthroughEnabled = true;
        LightbarSettings lightbar = new LightbarSettings
        {
            Mode = LightbarMode.Breathing,
            Red = 10,
            Green = 20,
            Blue = 30
        };

        device.ApplyProfile(Profile(lightbar));

        byte[] write = Snapshot(hid).Single(w => (w[ValidFlag1Offset] & (byte)ValidFlags.AllowLedColor) == 0);
        Assert.Multiple(() =>
        {
            Assert.That(write[ValidFlag1Offset] & (byte)ValidFlags.AllowLedColor, Is.Zero);
            Assert.That(write[ValidFlag2Offset] & (byte)ValidFlags.AllowColorFadeAnim, Is.Zero);
            Assert.That(write[ValidFlag2Offset] & (byte)ValidFlags.AllowBrightnessChange, Is.Zero);
        });
    }

    [Test]
    public void AnimatedModePassthroughEnabledSendsNoFrames()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = CreateDevice(hid);
        device.LightbarPassthroughEnabled = true;

        device.ApplyProfile(Profile(new LightbarSettings
        {
            Mode = LightbarMode.Breathing
        }));
        hid.Clear();

        Thread.Sleep(150);

        Assert.That(Snapshot(hid), Is.Empty);
    }

    [Test]
    public void AnimatedModeProfileOverrideSendsFramesDespitePassthrough()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = CreateDevice(hid);
        device.LightbarPassthroughEnabled = true;
        Profile profile = Profile(new LightbarSettings
        {
            Mode = LightbarMode.Breathing,
            Red = 10,
            Green = 20,
            Blue = 30,
            BreathMin = 100,
            BreathMax = 100
        });
        profile.OverrideVirtualLights = true;

        device.ApplyProfile(profile);
        hid.Clear();
        Thread.Sleep(150);

        Assert.That(Snapshot(hid), Is.Not.Empty);
    }

    [Test]
    public void DisablingPassthroughResumesEffectImmediately()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = CreateDevice(hid);
        device.LightbarPassthroughEnabled = true;
        device.ApplyProfile(Profile(new LightbarSettings
        {
            Mode = LightbarMode.Breathing,
            Red = 10,
            Green = 20,
            Blue = 30,
            BreathMin = 100,
            BreathMax = 100
        }));
        hid.Clear();

        device.LightbarPassthroughEnabled = false;

        byte[]? write = FindWrite(hid, w => (w[ValidFlag1Offset] & (byte)ValidFlags.AllowLedColor) != 0);
        Assert.That(write, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(write![RedOffset], Is.EqualTo(10));
            Assert.That(write[GreenOffset], Is.EqualTo(20));
            Assert.That(write[BlueOffset], Is.EqualTo(30));
        });
    }

    [Test]
    public void StopLightbarAnimation_StopsTimer()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = CreateDevice(hid);
        device.ApplyProfile(Profile(new LightbarSettings
        {
            Mode = LightbarMode.Breathing
        }));
        Thread.Sleep(100);

        device.StopLightbarAnimation();
        hid.Clear();

        Thread.Sleep(150);

        Assert.That(Snapshot(hid), Is.Empty);
    }

    [Test]
    public void CurrentBatteryState_TracksInputReport()
    {
        RecordingHidDevice hid = new RecordingHidDevice();
        using DualSenseDevice device = CreateDevice(hid);

        Assert.That(device.CurrentBatteryState, Is.Null);

        byte[] buffer = CloneUsbReport();
        buffer[53] = 0x17;
        FeedReport(device, buffer);

        Assert.That(device.CurrentBatteryState, Is.Not.Null);
        Assert.That(device.CurrentBatteryState!.Value.Raw, Is.EqualTo(0x17));
    }

    [Test]
    public void TryGetLightbarEffectFrame_BatteryCharging_ReturnsChaseMask()
    {
        RecordingHidDevice hid = new RecordingHidDevice(FirmwareReportGeneration2());
        using DualSenseDevice device = CreateDevice(hid);
        Assert.That(device.FirmwareInfo?.HasFullPlayerLedSupport, Is.True);

        byte[] buffer = CloneUsbReport();
        buffer[53] = 0x17;
        FeedReport(device, buffer);
        device.ApplyProfile(Profile(new LightbarSettings
        {
            Mode = LightbarMode.Battery,
            BatteryPlayerLeds = true
        }));

        bool active = device.TryGetLightbarEffectFrame(out LightbarEffectFrame frame);
        Assert.Multiple(() =>
        {
            Assert.That(active, Is.True);
            Assert.That(frame.Red, Is.EqualTo(255));
            Assert.That(frame.Green, Is.EqualTo(60));
            Assert.That(frame.Blue, Is.EqualTo(60));
            Assert.That(frame.PlayerLeds, Is.EqualTo(PlayerLedMask.Led1));
        });

        device.PlayerLedPassthroughEnabled = true;

        Assert.That(device.TryGetLightbarEffectFrame(out LightbarEffectFrame passthroughFrame), Is.True);
        Assert.That(passthroughFrame.PlayerLeds, Is.Null);
    }
}