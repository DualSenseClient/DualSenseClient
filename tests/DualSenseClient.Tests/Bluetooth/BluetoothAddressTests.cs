using DualSenseClient.Bluetooth;

namespace DualSenseClient.Tests.Bluetooth;

[TestFixture]
public sealed class BluetoothAddressTests
{
    [Test]
    public void TryParse_ColonSeparated_ReturnsNumericAddress() => Assert.That(BluetoothAddress.TryParse("A4:C1:38:12:34:56"), Is.EqualTo(0xA4C138123456));

    [Test]
    public void TryParse_LowercaseHex_ReturnsNumericAddress() => Assert.That(BluetoothAddress.TryParse("a4:c1:38:12:34:56"), Is.EqualTo(0xA4C138123456));

    [Test]
    public void TryParse_WithoutSeparators_ReturnsNumericAddress() => Assert.That(BluetoothAddress.TryParse("A4C138123456"), Is.EqualTo(0xA4C138123456));

    [Test]
    public void TryParse_DashSeparated_ReturnsNumericAddress() => Assert.That(BluetoothAddress.TryParse("A4-C1-38-12-34-56"), Is.EqualTo(0xA4C138123456));

    [Test]
    public void TryParse_LeadingZeros_PreservesValue() => Assert.That(BluetoothAddress.TryParse("00:11:22:33:44:55"), Is.EqualTo(0x001122334455));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("A4:C1:38")]
    [TestCase("A4:C1:38:12:34")]
    [TestCase("A4:C1:38:12:34:5")]
    [TestCase("A4:C1:38:12:34:567")]
    [TestCase("GG:HH:II:JJ:KK:LL")]
    [TestCase("A4C13812345")]
    [TestCase("A4C1381234567")]
    public void TryParse_InvalidInput_ReturnsNull(string? macAddress) => Assert.That(BluetoothAddress.TryParse(macAddress), Is.Null);
}