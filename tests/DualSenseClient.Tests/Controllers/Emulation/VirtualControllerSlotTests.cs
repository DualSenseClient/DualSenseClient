using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Hid;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.Emulation;

[TestFixture]
public sealed class VirtualControllerSlotTests
{
    [Test]
    public void BeginCreation_MovesToCreatingAndRaisesStatusChanged()
    {
        using DualSenseDevice device = CreateDevice();
        VirtualControllerSlot slot = new VirtualControllerSlot(device);
        int changes = 0;
        slot.StatusChanged += (_, _) => changes++;

        slot.BeginCreation(EmulationMode.DualSense);

        Assert.Multiple(() =>
        {
            Assert.That(slot.Status.State, Is.EqualTo(EmulationState.Creating));
            Assert.That(slot.Status.IsCreating, Is.True);
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void MarkRunning_MovesToRunningWithPathAndVariant()
    {
        using DualSenseDevice device = CreateDevice();
        VirtualControllerSlot slot = new VirtualControllerSlot(device);

        slot.MarkRunning(EmulationMode.DualSense, @"\\?\hid#virtual", DualSenseVariant.Edge, null);

        Assert.Multiple(() =>
        {
            Assert.That(slot.Status.State, Is.EqualTo(EmulationState.Running));
            Assert.That(slot.Status.Running, Is.True);
            Assert.That(slot.Status.VirtualDevicePath, Is.EqualTo(@"\\?\hid#virtual"));
            Assert.That(slot.Status.Variant, Is.EqualTo(DualSenseVariant.Edge));
            Assert.That(slot.IsRunning, Is.True);
        });
    }

    [Test]
    public void MarkFailed_MovesToFailedAndCarriesDetail()
    {
        using DualSenseDevice device = CreateDevice();
        VirtualControllerSlot slot = new VirtualControllerSlot(device);

        slot.MarkFailed(EmulationMode.DualShock4, "could not attach");

        Assert.Multiple(() =>
        {
            Assert.That(slot.Status.State, Is.EqualTo(EmulationState.Failed));
            Assert.That(slot.Status.Running, Is.False);
            Assert.That(slot.Status.Detail, Is.EqualTo("could not attach"));
        });
    }

    [Test]
    public void MarkStopped_MovesToStopped()
    {
        using DualSenseDevice device = CreateDevice();
        VirtualControllerSlot slot = new VirtualControllerSlot(device);

        slot.MarkStopped();

        Assert.That(slot.Status.State, Is.EqualTo(EmulationState.Stopped));
    }

    [Test]
    public void PushInput_ForwardsToVirtualController()
    {
        using DualSenseDevice device = CreateDevice();
        VirtualControllerSlot slot = new VirtualControllerSlot(device);
        FakeVirtualController virtualController = new FakeVirtualController();
        slot.Virtual = virtualController;
        InputReport report = new InputReport(new byte[64], 0);

        slot.PushInput(report);

        Assert.That(virtualController.LastInput, Is.EqualTo(report));
    }

    [Test]
    public void PushBattery_ForwardsLatestInputAndBattery()
    {
        using DualSenseDevice device = CreateDevice();
        VirtualControllerSlot slot = new VirtualControllerSlot(device);
        FakeVirtualController virtualController = new FakeVirtualController();
        slot.Virtual = virtualController;
        InputReport report = new InputReport(new byte[64], 0);
        BatteryState battery = new BatteryState(0x57);

        slot.PushBattery(battery, report);

        Assert.Multiple(() =>
        {
            Assert.That(virtualController.LastInput, Is.EqualTo(report));
            Assert.That(virtualController.LastBattery, Is.EqualTo(battery));
        });
    }

    private static DualSenseDevice CreateDevice() => new DualSenseDevice(new SilentHidDevice(), new StubHidDeviceInfo());

    private sealed class FakeVirtualController : IVirtualController
    {
        public EmulationMode Mode
        {
            get
            {
                return EmulationMode.DualSense;
            }
        }

        public string? VirtualDevicePath { get; set; }
        public ButtonMappingTable? ButtonMappings { get; set; }
        public InputReport? LastInput { get; private set; }
        public BatteryState? LastBattery { get; private set; }

        public void ApplyForwardingSettings(ForwardSettings settings)
        {
        }

        public void PushInput(InputReport report) => LastInput = report;

        public void PushBattery(BatteryState battery) => LastBattery = battery;

        public void PushConnectionStatus(ConnectionStatus status)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class SilentHidDevice : IHidDevice
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
}