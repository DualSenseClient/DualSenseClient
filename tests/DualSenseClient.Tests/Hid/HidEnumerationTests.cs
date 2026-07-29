using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Hid;

[TestFixture]
public sealed class HidEnumerationTests
{
    [Test]
    public void Enumerate_FindsDualSenseOrDualShock4()
    {
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator();
        IReadOnlyList<IHidDeviceInfo> devices = enumerator.Enumerate();

        Assert.That(devices, Is.Not.Null);

        List<IHidDeviceInfo> sonyControllers = devices
            .Where(d => d.VendorId == 0x054C)
            .Where(d => d.ProductId is 0x0CE6 or 0x09CC)
            .ToList();

        if (sonyControllers.Count == 0)
        {
            Assert.Inconclusive("No Sony controller found — connect a DualSense or DS4 to test");
            return;
        }

        Assert.That(sonyControllers, Has.Count.GreaterThan(0));
    }
}