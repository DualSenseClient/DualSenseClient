using System.Reflection;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.TouchpadMouse;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.TouchpadMouse;

public class TouchpadMouseEngineTests
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

    /// <summary>
    /// An injector whose pointer movement always throws, simulating a broken injection
    /// backend on the controller's read thread.
    /// </summary>
    private sealed class ThrowingMouseInjector : IMouseInjector
    {
        public bool Move(int dx, int dy) => throw new InvalidOperationException("Injection failed");

        public bool LeftDown() => true;
        public bool LeftUp() => true;
        public bool RightDown() => true;
        public bool RightUp() => true;
        public bool Wheel(int delta) => true;
        public bool HorizontalWheel(int delta) => true;
    }

    private static readonly MethodInfo ProcessInputReportMethod = typeof(DualSenseDevice)
        .GetMethod("ProcessInputReport", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly TouchpadMouseOptions DefaultOptions = new TouchpadMouseOptions(true, 1.0, 1.0, false);

    private static DualSenseDevice CreateDevice() => new DualSenseDevice(new RecordingHidDevice(), new StubHidDeviceInfo());

    private static TouchpadMouseEngine CreateEngine(IMouseInjector injector, TouchpadMouseOptions? options = null) =>
        new TouchpadMouseEngine(injector, options ?? DefaultOptions);

    /// <summary>
    /// Creates a report with no buttons pressed. The payload starts at buffer offset 1, so
    /// D-pad neutral is buffer[8].
    /// </summary>
    private static byte[] CreateReport()
    {
        byte[] buffer = new byte[64];
        buffer[0] = 0x01; // USB input report ID
        buffer[8] = 0x08; // D-Pad neutral, no face buttons
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
    /// Creates a report with the physical touchpad button held (payload byte 9 is buffer[10]).
    /// </summary>
    private static byte[] CreatePadPressReport()
    {
        byte[] buffer = CreateNoTouchReport();
        buffer[10] |= 0x02;
        return buffer;
    }

    private static void FeedReport(DualSenseDevice device, byte[] buffer) =>
        ProcessInputReportMethod.Invoke(device, [buffer, buffer.Length]);

    [Test]
    public void Attach_TouchMovement_ReachesInjector()
    {
        using DualSenseDevice device = CreateDevice();
        RecordingMouseInjector injector = new RecordingMouseInjector();
        using TouchpadMouseEngine engine = CreateEngine(injector);
        engine.Attach(device);

        FeedReport(device, CreateNoTouchReport());
        FeedReport(device, CreateTouchReport(100, 100));
        FeedReport(device, CreateTouchReport(110, 100));

        Assert.That(injector.Moves, Is.Not.Empty);
    }

    [Test]
    public void Attach_PadPress_ReachesInjector()
    {
        using DualSenseDevice device = CreateDevice();
        RecordingMouseInjector injector = new RecordingMouseInjector();
        using TouchpadMouseEngine engine = CreateEngine(injector);
        engine.Attach(device);

        FeedReport(device, CreateNoTouchReport());
        FeedReport(device, CreatePadPressReport());

        Assert.That(injector.Calls, Does.Contain("LeftDown"));
    }

    [Test]
    public void Detach_ReleasesHeldButton()
    {
        using DualSenseDevice device = CreateDevice();
        RecordingMouseInjector injector = new RecordingMouseInjector();
        using TouchpadMouseEngine engine = CreateEngine(injector);
        engine.Attach(device);

        FeedReport(device, CreateNoTouchReport());
        FeedReport(device, CreatePadPressReport());

        engine.Detach();

        Assert.That(injector.Calls, Does.Contain("LeftUp"));
    }

    [Test]
    public void Attach_FirstReportWithPadPressed_DoesNotClick()
    {
        using DualSenseDevice device = CreateDevice();
        RecordingMouseInjector injector = new RecordingMouseInjector();
        using TouchpadMouseEngine engine = CreateEngine(injector);
        engine.Attach(device);

        FeedReport(device, CreatePadPressReport());

        Assert.That(injector.Calls, Is.Empty);
    }

    [Test]
    public void Handler_InjectorThrows_DoesNotEscapeIntoDevice()
    {
        using DualSenseDevice device = CreateDevice();
        using TouchpadMouseEngine engine = CreateEngine(new ThrowingMouseInjector());
        engine.Attach(device);

        FeedReport(device, CreateNoTouchReport());
        FeedReport(device, CreateTouchReport(100, 100));
        byte[] movingReport = CreateTouchReport(200, 100);

        Assert.DoesNotThrow(() => FeedReport(device, movingReport));
    }

    [Test]
    public void UpdateOptions_ChangesPointerSpeed()
    {
        using DualSenseDevice device = CreateDevice();
        RecordingMouseInjector injector = new RecordingMouseInjector();
        using TouchpadMouseEngine engine = CreateEngine(injector);
        engine.Attach(device);

        FeedReport(device, CreateNoTouchReport());
        FeedReport(device, CreateTouchReport(100, 100));
        FeedReport(device, CreateTouchReport(120, 100));
        int firstMoveDx = injector.Moves[^1].Dx;

        engine.UpdateOptions(new TouchpadMouseOptions(true, 2.0, 1.0, false));
        FeedReport(device, CreateTouchReport(140, 100));
        int secondMoveDx = injector.Moves[^1].Dx;

        Assert.That(secondMoveDx, Is.GreaterThan(firstMoveDx));
    }
}