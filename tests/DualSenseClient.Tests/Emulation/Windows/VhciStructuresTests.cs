using System.Runtime.InteropServices;
using System.Text;
using DualSenseClient.Emulation.Windows;

namespace DualSenseClient.Tests.Emulation.Windows;

/// <summary>
/// Tests of the VHCI wire structures: <see cref="VhciAttachRequest"/> pins the plugin_hardware
/// request layout recorded in the compat notes (IOCTL interface facts, usbip-win2 v0.9.8.1,
/// x64) — total size, every field offset, the golden request bytes of <c>Write</c> (UTF-8 strings,
/// NUL termination, zero padding) and the capacity error contract.
/// </summary>
[TestFixture]
public sealed class VhciStructuresTests
{
    /// <summary>Recorded fact: sizeof(plugin_hardware) on x64 is 1124 (v0.9.8.1 added location_hash).</summary>
    private const int PluginHardwareSize = 1124;

    /// <summary>Recorded fact: offset of the IN self-size field.</summary>
    private const int SizeOffset = 0;

    /// <summary>Recorded fact: offset of the OUT driver-assigned port field.</summary>
    private const int PortOffset = 4;

    /// <summary>Recorded fact: offset of the OUT location hash field (v0.9.8.1+).</summary>
    private const int LocationHashOffset = 8;

    /// <summary>Recorded fact: offset of the remote bus id string (char[32]).</summary>
    private const int BusIdOffset = 12;

    /// <summary>Recorded fact: offset of the TCP/IP service string (char[32]).</summary>
    private const int ServiceOffset = 44;

    /// <summary>Recorded fact: offset of the hostname/IP string (char[1025]).</summary>
    private const int HostOffset = 76;

    /// <summary>Recorded fact: offset of the optional serial string (char[16]).</summary>
    private const int SerialOffset = 1104;

    /// <summary>Recorded fact: offset of the wsk_events receive-mode flag.</summary>
    private const int WskEventsOffset = 1120;

    [Test]
    public void SizeInBytes_MatchesRecordedPluginHardwareSize()
    {
        Assert.That(VhciAttachRequest.SizeInBytes, Is.EqualTo(PluginHardwareSize));
        Assert.That(Marshal.SizeOf<VhciAttachRequest>(), Is.EqualTo(PluginHardwareSize));
    }

    [Test]
    public void MarshalOffsets_MatchRecordedX64Layout()
    {
        Assert.That(Marshal.OffsetOf<VhciAttachRequest>("_size").ToInt32(), Is.EqualTo(SizeOffset));
        Assert.That(Marshal.OffsetOf<VhciAttachRequest>("_port").ToInt32(), Is.EqualTo(PortOffset));
        Assert.That(Marshal.OffsetOf<VhciAttachRequest>("_locationHash").ToInt32(), Is.EqualTo(LocationHashOffset));
        Assert.That(Marshal.OffsetOf<VhciAttachRequest>("_busId").ToInt32(), Is.EqualTo(BusIdOffset));
        Assert.That(Marshal.OffsetOf<VhciAttachRequest>("_service").ToInt32(), Is.EqualTo(ServiceOffset));
        Assert.That(Marshal.OffsetOf<VhciAttachRequest>("_host").ToInt32(), Is.EqualTo(HostOffset));
        Assert.That(Marshal.OffsetOf<VhciAttachRequest>("_serial").ToInt32(), Is.EqualTo(SerialOffset));
        Assert.That(Marshal.OffsetOf<VhciAttachRequest>("_wskEvents").ToInt32(), Is.EqualTo(WskEventsOffset));
    }

    [Test]
    public void Write_DefaultOptionalFields_EmitsExactRecordedBytes()
    {
        VhciAttachRequest request = new VhciAttachRequest("192.168.1.9", "3240", "1-2");
        byte[] expected = new byte[PluginHardwareSize];
        BitConverter.GetBytes((uint)PluginHardwareSize).CopyTo(expected, SizeOffset);
        Encoding.UTF8.GetBytes("1-2").CopyTo(expected, BusIdOffset);
        Encoding.UTF8.GetBytes("3240").CopyTo(expected, ServiceOffset);
        Encoding.UTF8.GetBytes("192.168.1.9").CopyTo(expected, HostOffset);
        byte[] buffer = new byte[PluginHardwareSize];

        request.Write(buffer);

        Assert.That(buffer, Is.EqualTo(expected));
    }

    [Test]
    public void Write_SerialAndWskEventsSet_EmitsThemAtTheRecordedOffsets()
    {
        VhciAttachRequest request = new VhciAttachRequest("127.0.0.1", "3240", "1-1", "SN0001", true);
        byte[] buffer = new byte[PluginHardwareSize];

        request.Write(buffer);

        Assert.That(buffer.AsSpan(SerialOffset, 6).ToArray(), Is.EqualTo(Encoding.ASCII.GetBytes("SN0001")));
        // NUL terminator of the serial string and the two padding regions stay zeroed.
        Assert.That(buffer[SerialOffset + 6], Is.Zero);
        Assert.That(buffer.AsSpan(1101, 3).ToArray(), Is.EqualTo(new byte[3]));
        Assert.That(buffer[WskEventsOffset], Is.EqualTo(1));
        Assert.That(buffer.AsSpan(1121, 3).ToArray(), Is.EqualTo(new byte[3]));
    }

    [Test]
    public void Write_NonAsciiHost_EncodesUtf8()
    {
        VhciAttachRequest request = new VhciAttachRequest("höst", "3240", "1-1");
        byte[] buffer = new byte[PluginHardwareSize];

        request.Write(buffer);

        // The vendor header pins the encoding: "Strings encoding is UTF8."
        Assert.That(buffer.AsSpan(HostOffset, 6).ToArray(), Is.EqualTo(new byte[]
        {
            0x68, 0xC3, 0xB6, 0x73, 0x74, 0x00
        }));
    }

    [Test]
    public void Write_LargerDestination_LeavesTrailingBytesUntouched()
    {
        VhciAttachRequest request = new VhciAttachRequest("127.0.0.1", "3240", "1-1");
        byte[] buffer = new byte[PluginHardwareSize + 8];
        Array.Fill(buffer, (byte)0xAB);

        request.Write(buffer);

        Assert.That(buffer.AsSpan(PluginHardwareSize).ToArray(), Is.EqualTo(new byte[]
        {
            0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xAB
        }));
    }

    [Test]
    public void Write_TooSmallDestination_ThrowsArgumentException()
    {
        VhciAttachRequest request = new VhciAttachRequest("127.0.0.1", "3240", "1-1");
        byte[] buffer = new byte[PluginHardwareSize - 1];

        Assert.That(() => request.Write(buffer), Throws.ArgumentException);
    }
}