using DualSenseClient.Controllers;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers;

public class ControllerTrackerTests
{
    private sealed class FakeControllerDevice(string path) : IControllerDevice
    {
        public string Path { get; } = path;
        public int DisposeCount { get; private set; }

        public IHidDeviceInfo Info => new FakeHidDeviceInfo(Path);
        public ConnectionType ConnectionType => ConnectionType.Usb;
        public ControllerType ControllerType => ControllerType.DualSense;
        public bool IsConnected => true;
        public int MaxOutputReportLength => 64;
        public int PollingRateHz => 0;
        public int ReadInput(byte[] buffer, int offset, int count, int timeoutMs) => 0;
        public void SendOutput(byte[] buffer, int offset, int count)
        {
        }

        public Task<int> ReadInputAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);
        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) => [];
        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => Path;
        public bool DisconnectController() => false;
        public void Dispose() => DisposeCount++;
    }

    private sealed class FakeHidDeviceInfo(string path) : IHidDeviceInfo
    {
        public string Path => path;
        public ushort VendorId => 0x054C;
        public ushort ProductId => 0x0CE6;
        public string ProductName => path;
        public string Manufacturer => "Sony";
        public int InterfaceNumber => 0;
        public ushort UsagePage => 1;
        public HidUsageId Usage => HidUsageId.GamePad;
        public ConnectionType BusType => ConnectionType.Usb;
    }

    private ControllerTracker _tracker = null!;
    private FakeControllerDevice _first = null!;
    private FakeControllerDevice _second = null!;

    [SetUp]
    public void SetUp()
    {
        _tracker = new ControllerTracker();
        _first = new FakeControllerDevice("first");
        _second = new FakeControllerDevice("second");
    }

    [TearDown]
    public void TearDown()
    {
        _tracker.Dispose();
        _first.Dispose();
        _second.Dispose();
    }

    [Test]
    public void TrackController_AddsToControllers()
    {
        _tracker.TrackController(_first);
        _tracker.TrackController(_second);

        Assert.That(_tracker.Controllers, Has.Count.EqualTo(2));
        Assert.That(_tracker.Controllers, Does.Contain(_first));
        Assert.That(_tracker.Controllers, Does.Contain(_second));
    }

    [Test]
    public void TrackController_Duplicate_IsIgnored()
    {
        _tracker.TrackController(_first);
        _tracker.TrackController(_first);

        Assert.That(_tracker.Controllers, Has.Count.EqualTo(1));
    }

    [Test]
    public void TrackController_RaisesControllersChanged()
    {
        int raised = 0;
        _tracker.ControllersChanged += (_, _) => raised++;

        _tracker.TrackController(_first);
        _tracker.TrackController(_second);

        Assert.That(raised, Is.EqualTo(2));
    }

    [Test]
    public void SelectController_SwitchesWithoutDisposingPrevious()
    {
        _tracker.TrackController(_first);
        _tracker.TrackController(_second);

        _tracker.SelectController(_first);
        _tracker.SelectController(_second);

        Assert.That(_tracker.ActiveController, Is.SameAs(_second));
        Assert.That(_first.DisposeCount, Is.Zero);
        Assert.That(_second.DisposeCount, Is.Zero);
        Assert.That(_tracker.Controllers, Has.Count.EqualTo(2));
    }

    [Test]
    public void SelectController_SameController_DoesNotRaise()
    {
        _tracker.TrackController(_first);
        _tracker.SelectController(_first);
        int raised = 0;
        _tracker.ActiveControllerChanged += (_, _) => raised++;

        _tracker.SelectController(_first);

        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void SelectController_Null_ClearsSelection()
    {
        _tracker.TrackController(_first);
        _tracker.SelectController(_first);
        int raised = 0;
        _tracker.ActiveControllerChanged += (_, _) => raised++;

        _tracker.SelectController(null);

        Assert.That(_tracker.ActiveController, Is.Null);
        Assert.That(raised, Is.EqualTo(1));
        Assert.That(_tracker.Controllers, Has.Count.EqualTo(1));
    }

    [Test]
    public void UntrackController_RemovesAndClearsActiveSelection()
    {
        _tracker.TrackController(_first);
        _tracker.TrackController(_second);
        _tracker.SelectController(_first);
        int activeChanged = 0;
        int controllersChanged = 0;
        _tracker.ActiveControllerChanged += (_, _) => activeChanged++;
        _tracker.ControllersChanged += (_, _) => controllersChanged++;

        _tracker.UntrackController(_first);

        Assert.That(_tracker.ActiveController, Is.Null);
        Assert.That(_tracker.Controllers, Has.Count.EqualTo(1));
        Assert.That(_tracker.Controllers, Does.Not.Contain(_first));
        Assert.That(activeChanged, Is.EqualTo(1));
        Assert.That(controllersChanged, Is.EqualTo(1));
        Assert.That(_first.DisposeCount, Is.Zero);
    }

    [Test]
    public void UntrackController_NonActive_KeepsSelection()
    {
        _tracker.TrackController(_first);
        _tracker.TrackController(_second);
        _tracker.SelectController(_first);
        int activeChanged = 0;
        _tracker.ActiveControllerChanged += (_, _) => activeChanged++;

        _tracker.UntrackController(_second);

        Assert.That(_tracker.ActiveController, Is.SameAs(_first));
        Assert.That(_tracker.Controllers, Has.Count.EqualTo(1));
        Assert.That(activeChanged, Is.Zero);
    }

    [Test]
    public void UntrackController_UnknownDevice_IsIgnored()
    {
        _tracker.TrackController(_first);
        int controllersChanged = 0;
        _tracker.ControllersChanged += (_, _) => controllersChanged++;

        _tracker.UntrackController(_second);

        Assert.That(_tracker.Controllers, Has.Count.EqualTo(1));
        Assert.That(controllersChanged, Is.Zero);
    }

    [Test]
    public void Dispose_DoesNotDisposeTrackedDevices()
    {
        _tracker.TrackController(_first);
        _tracker.TrackController(_second);

        _tracker.Dispose();

        Assert.That(_first.DisposeCount, Is.Zero);
        Assert.That(_second.DisposeCount, Is.Zero);
        Assert.That(_tracker.Controllers, Has.Count.EqualTo(2));
    }
}