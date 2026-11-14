using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Triggers;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.GUI.Models.Items;

public class InputMonitorItemTests
{
    [Test]
    public void ResetTestOutputs_WhenNoTestIsActive_DoesNotWriteToController()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));
        using InputMonitorItem item = new InputMonitorItem(new ControllerItem(device, "Test"));
        hid.Writes.Clear();

        item.ResetTestOutputs();

        Assert.That(hid.Writes, Is.Empty);
    }

    [Test]
    public void ResetTestOutputs_WhenTriggerEffectIsActive_SendsResetReport()
    {
        CapturingHidDevice hid = new CapturingHidDevice();
        using DualSenseDevice device = new DualSenseDevice(hid, new StubHidDeviceInfo(ConnectionType.Usb));
        using InputMonitorItem item = new InputMonitorItem(new ControllerItem(device, "Test"));
        item.LeftTriggerModeIndex = 1;
        hid.Writes.Clear();

        item.ResetTestOutputs();

        Assert.Multiple(() =>
        {
            Assert.That(hid.Writes, Is.Not.Empty);
            byte[] payload = hid.Writes[^1];
            Assert.That(payload[1 + 2], Is.EqualTo((byte)0), "left rumble should be cleared");
            Assert.That(payload[1 + 3], Is.EqualTo((byte)0), "right rumble should be cleared");
            Assert.That(payload[1 + 10], Is.EqualTo((byte)TriggerEffectType.Off), "R2 effect should be off");
            Assert.That(payload[1 + 21], Is.EqualTo((byte)TriggerEffectType.Off), "L2 effect should be off");
            Assert.That(item.LeftTriggerModeIndex, Is.Zero);
        });
    }

    private sealed class CapturingHidDevice : IHidDevice
    {
        public List<byte[]> Writes { get; } = [];

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
            Writes.Add((byte[])buffer.Clone());
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

    private sealed class StubHidDeviceInfo(ConnectionType busType) : IHidDeviceInfo
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
                return busType;
            }
        }
    }
}