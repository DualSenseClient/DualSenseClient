using System.Collections.Concurrent;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Hid;

[TestFixture]
public sealed class HidWatcherTests
{
    private static readonly (ushort VendorId, ushort ProductId)[] SonyIds = [(0x054C, 0x0CE6), (0x054C, 0x0DF2)];

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