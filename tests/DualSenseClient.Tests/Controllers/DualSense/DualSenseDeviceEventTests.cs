using System.Reflection;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Events;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Hid;
using DualSenseClient.Tests.Controllers.DualSense.Input;

namespace DualSenseClient.Tests.Controllers.DualSense;

public class DualSenseDeviceEventTests
{
    private sealed class StubHidDevice : IHidDevice
    {
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
        public int Write(byte[] buffer, int offset, int count) => 0;
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

    private static byte[] CloneUsbReport()
    {
        byte[] clone = new byte[InputReportTestData.UsbReport.Length];
        Buffer.BlockCopy(InputReportTestData.UsbReport, 0, clone, 0, clone.Length);
        return clone;
    }

    [Test]
    public void FirstReport_FiresNoEvents()
    {
        using DualSenseDevice device = new DualSenseDevice(new StubHidDevice(), new StubHidDeviceInfo());
        bool anyEvent = false;
        SubscribeAll(device, _ => anyEvent = true);

        FeedReport(device, CloneUsbReport());

        Assert.That(anyEvent, Is.False);
    }

    [Test]
    public void SecondReport_NoChanges_FiresNoEvents()
    {
        using DualSenseDevice device = new DualSenseDevice(new StubHidDevice(), new StubHidDeviceInfo());
        bool anyEvent = false;
        SubscribeAll(device, _ => anyEvent = true);

        byte[] buf = CloneUsbReport();
        FeedReport(device, buf);
        anyEvent = false;
        FeedReport(device, buf);

        Assert.That(anyEvent, Is.False);
    }

    [Test]
    public void ButtonPress_FiresButtonPressed()
    {
        using DualSenseDevice device = new DualSenseDevice(new StubHidDevice(), new StubHidDeviceInfo());
        ButtonType? pressed = null as ButtonType?;
        device.ButtonPressed += (_, e) => pressed = e.Button;

        byte[] buf = CloneUsbReport();
        FeedReport(device, buf);
        // UsbReport has all buttons pressed in byte 7. Clear Cross bit (0x20)
        // and then set it back to trigger a press transition.
        buf[8] = 0xD2; // Cross released
        FeedReport(device, buf);
        buf[8] = 0xF2; // Cross pressed again
        FeedReport(device, buf);

        Assert.That(pressed, Is.EqualTo(ButtonType.Cross));
    }

    [Test]
    public void ButtonRelease_FiresButtonReleased()
    {
        using DualSenseDevice device = new DualSenseDevice(new StubHidDevice(), new StubHidDeviceInfo());
        ButtonType? released = null as ButtonType?;
        device.ButtonReleased += (_, e) => released = e.Button;

        byte[] buf = CloneUsbReport();
        FeedReport(device, buf);
        buf[8] = 0xD2; // Clear Cross bit (0x20), leaving other buttons unchanged
        FeedReport(device, buf);

        Assert.That(released, Is.EqualTo(ButtonType.Cross));
    }

    [Test]
    public void StickMove_FiresStickMoved()
    {
        using DualSenseDevice device = new DualSenseDevice(new StubHidDevice(), new StubHidDeviceInfo());
        StickEventArgs? args = null;
        device.StickMoved += (_, e) => args = e;

        byte[] buf = CloneUsbReport();
        FeedReport(device, buf);
        buf[2] = 200; // LeftStickY changed from 255 to 200
        FeedReport(device, buf);

        Assert.That(args, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(args!.Stick, Is.EqualTo(StickType.Left));
            Assert.That(args.Y, Is.EqualTo(200));
            Assert.That(args.PreviousY, Is.EqualTo(255));
        });
    }

    [Test]
    public void TriggerMove_FiresTriggerMoved()
    {
        using DualSenseDevice device = new DualSenseDevice(new StubHidDevice(), new StubHidDeviceInfo());
        TriggerEventArgs? args = null;
        device.TriggerMoved += (_, e) => args = e;

        byte[] buf = CloneUsbReport();
        FeedReport(device, buf);
        buf[5] = 128; // L2 changed from 0 to 128 (payload byte 4 = buffer[1 + 4])
        FeedReport(device, buf);

        Assert.That(args, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(args!.Trigger, Is.EqualTo(TriggerType.L2));
            Assert.That(args.CurrentValue, Is.EqualTo(128));
            Assert.That(args.PreviousValue, Is.EqualTo(0));
        });
    }

    [Test]
    public void BatteryChange_FiresBatteryStateChanged()
    {
        using DualSenseDevice device = new DualSenseDevice(new StubHidDevice(), new StubHidDeviceInfo());
        BatteryStateEventArgs? args = null;
        device.BatteryStateChanged += (_, e) => args = e;

        byte[] buf = CloneUsbReport();
        FeedReport(device, buf);
        buf[53] = 0x0A; // Battery raw changed from 0x18 to 0x0A
        FeedReport(device, buf);

        Assert.That(args, Is.Not.Null);
        Assert.That(args!.PreviousState.Raw, Is.EqualTo(0x18));
        Assert.That(args.CurrentState.Raw, Is.EqualTo(0x0A));
    }

    [Test]
    public void ConnectionChange_FiresConnectionStatusChanged()
    {
        using DualSenseDevice device = new DualSenseDevice(new StubHidDevice(), new StubHidDeviceInfo());
        ConnectionStatusEventArgs? args = null;
        device.ConnectionStatusChanged += (_, e) => args = e;

        byte[] buf = CloneUsbReport();
        FeedReport(device, buf);
        buf[54] = 0x00; // Connection raw changed from 0x0B to 0x00
        FeedReport(device, buf);

        Assert.That(args, Is.Not.Null);
        Assert.That(args!.PreviousStatus.Raw, Is.EqualTo(0x0B));
        Assert.That(args.CurrentStatus.Raw, Is.EqualTo(0x00));
    }

    [Test]
    public void TouchpadChange_FiresTouchpadChanged()
    {
        using DualSenseDevice device = new DualSenseDevice(new StubHidDevice(), new StubHidDeviceInfo());
        TouchpadEventArgs? args = null;
        device.TouchpadChanged += (_, e) => args = e;

        byte[] buf = CloneUsbReport();
        FeedReport(device, buf);
        buf[34] = 0x01; // Touch1 X low byte changed (payload byte 33 = buffer[34])
        FeedReport(device, buf);

        Assert.That(args, Is.Not.Null);
    }

    [Test]
    public void MotionChange_FiresMotionChanged()
    {
        using DualSenseDevice device = new DualSenseDevice(new StubHidDevice(), new StubHidDeviceInfo());
        MotionEventArgs? args = null;
        device.MotionChanged += (_, e) => args = e;

        byte[] buf = CloneUsbReport();
        FeedReport(device, buf);
        buf[17] = 0x00; // GyroX low byte changed (payload byte 15 = buffer[16])
        buf[18] = 0x00; // GyroX high byte changed
        FeedReport(device, buf);

        Assert.That(args, Is.Not.Null);
    }

    [Test]
    public void InputReportReceived_FiresOnFirstReport()
    {
        using DualSenseDevice device = new DualSenseDevice(new StubHidDevice(), new StubHidDeviceInfo());
        InputReport? report = null;
        int count = 0;
        device.InputReportReceived += (_, r) =>
        {
            count++;
            report = r;
        };

        FeedReport(device, CloneUsbReport());

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(1));
            Assert.That(report.HasValue, Is.True);
            Assert.That(report!.Value.Input.L2, Is.EqualTo(InputReportTestData.UsbReport[5]));
        });
    }

    [Test]
    public void InputReportReceived_FiresEvenWhenNoFieldsChanged()
    {
        using DualSenseDevice device = new DualSenseDevice(new StubHidDevice(), new StubHidDeviceInfo());
        int count = 0;
        device.InputReportReceived += (_, _) => count++;

        byte[] buf = CloneUsbReport();
        FeedReport(device, buf);
        FeedReport(device, buf);

        Assert.That(count, Is.EqualTo(2));
    }

    private static void SubscribeAll(DualSenseDevice device, Action<EventArgs> handler)
    {
        device.InputStateChanged += (_, e) => handler(e);
        device.ButtonPressed += (_, e) => handler(e);
        device.ButtonReleased += (_, e) => handler(e);
        device.StickMoved += (_, e) => handler(e);
        device.TriggerMoved += (_, e) => handler(e);
        device.BatteryStateChanged += (_, e) => handler(e);
        device.ConnectionStatusChanged += (_, e) => handler(e);
        device.MotionChanged += (_, e) => handler(e);
        device.TouchpadChanged += (_, e) => handler(e);
    }
}