using System.Reflection;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Hid;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.SpecialActions;

public class SpecialActionEngineRegistryTests
{
    private sealed class SilentHidDevice : IHidDevice
    {
        public ushort VendorId => 0x054C;
        public ushort ProductId => 0x0CE6;
        public string DevicePath => "test";
        public bool IsConnected => true;
        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;
        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);
        public int Write(byte[] buffer, int offset, int count) => count;
        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) => [];

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "Test";

        public void Dispose()
        {
        }
    }

    private sealed class StubHidDeviceInfo(string path) : IHidDeviceInfo
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

    private static readonly FieldInfo ActionsField = typeof(SpecialActionEngine)
        .GetField("_actions", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static DualSenseDevice CreateDevice(string path) => new(new SilentHidDevice(), new StubHidDeviceInfo(path));

    private static List<SpecialAction> ReadActions(SpecialActionEngine engine)
        => (List<SpecialAction>)ActionsField.GetValue(engine)!;

    [Test]
    public void GetOrCreate_ReturnsSameEngineForSameDevice()
    {
        using SpecialActionEngineRegistry registry = new();
        DualSenseDevice device = CreateDevice("first");

        SpecialActionEngine first = registry.GetOrCreate(device);
        SpecialActionEngine second = registry.GetOrCreate(device);

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void GetOrCreate_ReturnsDistinctEnginePerDevice()
    {
        using SpecialActionEngineRegistry registry = new();
        DualSenseDevice first = CreateDevice("first");
        DualSenseDevice second = CreateDevice("second");

        SpecialActionEngine firstEngine = registry.GetOrCreate(first);
        SpecialActionEngine secondEngine = registry.GetOrCreate(second);

        Assert.That(secondEngine, Is.Not.SameAs(firstEngine));
    }

    [Test]
    public void GetOrCreate_AppliesProvidersToCreatedEngine()
    {
        using SpecialActionEngineRegistry registry = new();
        Func<DualSenseDevice, Profile?> profileProvider = _ => null;
        Func<DualSenseDevice, ISpecialActionSoundPlayer> soundFactory = _ => throw new InvalidOperationException();
        registry.ProfileProvider = profileProvider;
        registry.SoundPlayerFactory = soundFactory;

        SpecialActionEngine engine = registry.GetOrCreate(CreateDevice("first"));

        Assert.That(engine.ProfileProvider, Is.SameAs(profileProvider));
        Assert.That(engine.SoundPlayerFactory, Is.SameAs(soundFactory));
    }

    [Test]
    public void GetOrCreate_AppliesConfiguredActionsToNewEngine()
    {
        using SpecialActionEngineRegistry registry = new();
        List<SpecialAction> actions = [new SpecialAction(), new SpecialAction()];
        registry.UpdateActions(actions);

        SpecialActionEngine engine = registry.GetOrCreate(CreateDevice("first"));

        Assert.That(ReadActions(engine), Has.Count.EqualTo(2));
    }

    [Test]
    public void UpdateActions_PropagatesToExistingEngines()
    {
        using SpecialActionEngineRegistry registry = new();
        SpecialActionEngine engine = registry.GetOrCreate(CreateDevice("first"));

        registry.UpdateActions([new SpecialAction(), new SpecialAction()]);

        Assert.That(ReadActions(engine), Has.Count.EqualTo(2));
    }

    [Test]
    public void Remove_DisposesAndForgetsEngine()
    {
        using SpecialActionEngineRegistry registry = new();
        DualSenseDevice device = CreateDevice("first");
        SpecialActionEngine engine = registry.GetOrCreate(device);

        registry.Remove(device);

        Assert.That(registry.GetOrCreate(device), Is.Not.SameAs(engine));
    }

    [Test]
    public void Remove_UnknownDevice_IsIgnored()
    {
        using SpecialActionEngineRegistry registry = new();
        DualSenseDevice device = CreateDevice("first");
        SpecialActionEngine engine = registry.GetOrCreate(device);

        registry.Remove(CreateDevice("other"));

        Assert.That(registry.GetOrCreate(device), Is.SameAs(engine));
    }

    [Test]
    public void Dispose_ForgetsAllEngines()
    {
        SpecialActionEngineRegistry registry = new();
        SpecialActionEngine first = registry.GetOrCreate(CreateDevice("first"));
        SpecialActionEngine second = registry.GetOrCreate(CreateDevice("second"));

        registry.Dispose();

        Assert.That(registry.GetOrCreate(CreateDevice("first")), Is.Not.SameAs(first));
        Assert.That(registry.GetOrCreate(CreateDevice("second")), Is.Not.SameAs(second));
    }
}