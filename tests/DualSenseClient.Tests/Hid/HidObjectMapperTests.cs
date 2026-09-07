using DualSenseClient.Hid;
using DualSenseClient.Hid.Interop;

namespace DualSenseClient.Tests.Hid;

[TestFixture]
public sealed class HidObjectMapperTests
{
    [TestCase(1, ConnectionType.Usb)]
    [TestCase(2, ConnectionType.Bluetooth)]
    [TestCase(0, ConnectionType.Unknown)]
    [TestCase(3, ConnectionType.Unknown)]
    [TestCase(4, ConnectionType.Unknown)]
    public void ToConnectionType_MapsBusType(int busType, ConnectionType expected) =>
        Assert.That(HidObjectMapper.ToConnectionType((HidBusType)busType), Is.EqualTo(expected));

    [Test]
    public void ToConnectionType_UnknownValue_MapsToUnknown() =>
        Assert.That(HidObjectMapper.ToConnectionType((HidBusType)99), Is.EqualTo(ConnectionType.Unknown));
}