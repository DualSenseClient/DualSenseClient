using System.Collections.Concurrent;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Hid;

[TestFixture]
public sealed class HidWatcherTests
{
    /// <summary>
    /// Generous upper bound for something that should happen within a few hundred milliseconds.
    /// </summary>
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    private static readonly (ushort VendorId, ushort ProductId)[] SonyIds = [(0x054C, 0x0CE6), (0x054C, 0x0DF2)];

    [Test]
    public void Polling_DetectsNewDevice()
    {
        FakeHid hid = new FakeHid();
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator(hid.Enumerate);
        using ManualResetEventSlim connected = new ManualResetEventSlim();
        enumerator.DeviceConnected += (_, _) => connected.Set();

        enumerator.StartWatching(SonyIds, 50);
        hid.Add("usb-1", 0x054C, 0x0CE6, ConnectionType.Usb);

        Assert.That(connected.Wait(WaitLimit), Is.True, "polling fallback never saw the device");
    }

    [Test]
    public void Removal_RaisesDisconnectedEvent()
    {
        FakeHid hid = new FakeHid();
        hid.Add("usb-1", 0x054C, 0x0CE6, ConnectionType.Usb);
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator(hid.Enumerate);
        using ManualResetEventSlim disconnected = new ManualResetEventSlim();
        enumerator.DeviceDisconnected += (_, e) =>
        {
            Assert.That(e.Device.Path, Is.EqualTo("usb-1"));
            disconnected.Set();
        };

        enumerator.StartWatching(SonyIds, 50);
        hid.Remove("usb-1");

        Assert.That(disconnected.Wait(WaitLimit), Is.True);
    }

    [Test]
    public void Watcher_NeverEnumeratesEveryDevice()
    {
        FakeHid hid = new FakeHid();
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator(hid.Enumerate);

        enumerator.StartWatching(SonyIds, 50);
        hid.Add("usb-1", 0x054C, 0x0CE6, ConnectionType.Usb);
        Thread.Sleep(700);

        // A null vendor means hid_enumerate(0, 0), which opens every HID device in the system.
        Assert.That(hid.Queries, Is.Not.Empty);
        Assert.That(hid.Queries.Select(q => q.VendorId), Has.All.EqualTo((ushort?)0x054C));
    }

    [Test]
    public void FilteredEnumerate_QueriesOncePerVendor()
    {
        FakeHid hid = new FakeHid();
        hid.Add("usb-1", 0x054C, 0x0CE6, ConnectionType.Usb);
        hid.Add("usb-2", 0x054C, 0x0DF2, ConnectionType.Usb);
        hid.Add("mouse", 0x0DB0, 0x0D2E, ConnectionType.Usb);
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator(hid.Enumerate);

        IReadOnlyList<IHidDeviceInfo> result = enumerator.Enumerate(SonyIds);

        Assert.That(result.Select(d => d.Path), Is.EquivalentTo(new[]
        {
            "usb-1", "usb-2"
        }));
        Assert.That(hid.Queries, Has.Count.EqualTo(1));
        Assert.That(hid.Queries[0], Is.EqualTo(((ushort?)0x054C, (ushort?)null)));
    }

    [Test]
    public void Handler_CanStopWatching_WithoutDeadlock()
    {
        FakeHid hid = new FakeHid();
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator(hid.Enumerate);
        using ManualResetEventSlim stopped = new ManualResetEventSlim();
        enumerator.DeviceConnected += (_, _) =>
        {
            // Raised on the watcher thread itself, which must not wait for itself to finish.
            enumerator.StopWatching();
            stopped.Set();
        };

        enumerator.StartWatching(SonyIds, 50);
        hid.Add("usb-1", 0x054C, 0x0CE6, ConnectionType.Usb);

        Assert.That(stopped.Wait(WaitLimit), Is.True, "StopWatching from a handler hung");
    }

    [Test]
    public void StopWatching_StopsScanning_AndAllowsRestart()
    {
        FakeHid hid = new FakeHid();
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator(hid.Enumerate);

        enumerator.StartWatching(SonyIds, 20);
        Thread.Sleep(150);
        enumerator.StopWatching();
        int callsAtStop = hid.Calls;
        Thread.Sleep(200);

        Assert.That(hid.Calls, Is.EqualTo(callsAtStop), "kept scanning after StopWatching returned");

        enumerator.StartWatching(SonyIds, 20);
        Thread.Sleep(150);
        Assert.That(hid.Calls, Is.GreaterThan(callsAtStop), "did not resume after restart");
    }

    [Test]
    public void ThrowingHandler_DoesNotKillTheWatcher()
    {
        FakeHid hid = new FakeHid();
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator(hid.Enumerate);
        using CountdownEvent connected = new CountdownEvent(2);
        enumerator.DeviceConnected += (_, _) =>
        {
            connected.Signal();
            throw new InvalidOperationException("subscriber bug");
        };

        enumerator.StartWatching(SonyIds, 50);
        hid.Add("usb-1", 0x054C, 0x0CE6, ConnectionType.Usb);
        hid.Add("usb-2", 0x054C, 0x0DF2, ConnectionType.Usb);

        Assert.That(connected.Wait(WaitLimit), Is.True, "second device was lost after the first handler threw");
    }

    /// <summary>
    /// A fake HIDAPI that records how it was queried.
    /// </summary>
    private sealed class FakeHid
    {
        private readonly Lock _gate = new Lock();
        private readonly List<IHidDeviceInfo> _devices = [];
        private readonly ConcurrentQueue<(ushort? VendorId, ushort? ProductId)> _queries = new ConcurrentQueue<(ushort?, ushort?)>();
        private int _calls;

        public int Calls
        {
            get
            {
                return Volatile.Read(ref _calls);
            }
        }

        public IReadOnlyList<(ushort? VendorId, ushort? ProductId)> Queries
        {
            get
            {
                return [.. _queries];
            }
        }

        public void Add(string path, ushort vid, ushort pid, ConnectionType bus)
        {
            lock (_gate)
            {
                _devices.Add(new HidDeviceInfo
                {
                    Path = path,
                    VendorId = vid,
                    ProductId = pid,
                    ProductName = "Fake",
                    Usage = HidUsageId.GamePad,
                    BusType = bus
                });
            }
        }

        public void Remove(string path)
        {
            lock (_gate)
            {
                _devices.RemoveAll(d => d.Path == path);
            }
        }

        public List<IHidDeviceInfo> Enumerate(ushort? vendorId, ushort? productId)
        {
            Interlocked.Increment(ref _calls);
            _queries.Enqueue((vendorId, productId));

            lock (_gate)
            {
                return _devices
                    .Where(d => (vendorId == null || d.VendorId == vendorId) && (productId == null || d.ProductId == productId))
                    .ToList();
            }
        }
    }
}