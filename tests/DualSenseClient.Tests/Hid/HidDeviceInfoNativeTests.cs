using System.Runtime.InteropServices;
using DualSenseClient.Hid.Interop;

namespace DualSenseClient.Tests.Hid;

/// <summary>
/// Guards the managed mirror of the native <c>hid_device_info</c> struct against
/// drifting from the upstream HIDAPI layout. Upstream places <c>next</c> before
/// <c>bus_type</c> and has no interface class fields, unlike SDL3's fork.
/// </summary>
[TestFixture]
public sealed class HidDeviceInfoNativeTests
{
    [Test]
    public void StructLayout_MatchesUpstreamHidApi()
    {
        Assume.That(Environment.Is64BitProcess, "Layout asserts target 64-bit processes.");

        Assert.That(Marshal.SizeOf<HidDeviceInfoNative>(), Is.EqualTo(72));
        Assert.That(OffsetOf(nameof(HidDeviceInfoNative.Path)), Is.EqualTo(0));
        Assert.That(OffsetOf(nameof(HidDeviceInfoNative.VendorId)), Is.EqualTo(8));
        Assert.That(OffsetOf(nameof(HidDeviceInfoNative.ProductId)), Is.EqualTo(10));
        Assert.That(OffsetOf(nameof(HidDeviceInfoNative.SerialNumber)), Is.EqualTo(16));
        Assert.That(OffsetOf(nameof(HidDeviceInfoNative.ReleaseNumber)), Is.EqualTo(24));
        Assert.That(OffsetOf(nameof(HidDeviceInfoNative.ManufacturerString)), Is.EqualTo(32));
        Assert.That(OffsetOf(nameof(HidDeviceInfoNative.ProductString)), Is.EqualTo(40));
        Assert.That(OffsetOf(nameof(HidDeviceInfoNative.UsagePage)), Is.EqualTo(48));
        Assert.That(OffsetOf(nameof(HidDeviceInfoNative.Usage)), Is.EqualTo(50));
        Assert.That(OffsetOf(nameof(HidDeviceInfoNative.InterfaceNumber)), Is.EqualTo(52));
        Assert.That(OffsetOf(nameof(HidDeviceInfoNative.Next)), Is.EqualTo(56));
        Assert.That(OffsetOf(nameof(HidDeviceInfoNative.BusType)), Is.EqualTo(64));
    }

    private static long OffsetOf(string field) => Marshal.OffsetOf<HidDeviceInfoNative>(field).ToInt64();
}