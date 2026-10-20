using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.UsbIp;

namespace DualSenseClient.Tests.Emulation.UsbIp;

/// <summary>
/// End-to-end tests of <see cref="UsbIpServer"/> over loopback TCP: the handshake replies, the URB
/// session loop, paced interrupt IN completions, unlink semantics and session teardown, driven
/// deterministically by <see cref="ManualTimeProvider"/>.
/// </summary>
[TestFixture]
public sealed class UsbIpServerTests
{
    /// <summary>Tick interval of the injected pacer schedule.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(4);

    /// <summary>Bus id of the registered test device (busnum 1, devnum 1).</summary>
    private const string TestBusId = "1-1";

    /// <summary>Devid a client derives from busid "1-1": (busnum &lt;&lt; 16) | devnum.</summary>
    private const uint TestDeviceId = 0x00010001;

    /// <summary>Size of an OP_REP_DEVLIST reply carrying the one-interface simple device.</summary>
    private static readonly int DevListReplySize = UsbIpProtocol.OpCommonSize + 4 + UsbIpDeviceInfo.DevListEntrySize(1);

    /// <summary>Timeout of the receive probes that assert nothing arrives.</summary>
    private const int SilenceProbeMilliseconds = 250;

    /// <summary>Timeout of the waits that assert the session closes or the server answers.</summary>
    private const int CommunicationTimeoutMilliseconds = 5000;

    [Test]
    public void DevList_NoRegisteredDevice_RepliesEmptyDeviceList()
    {
        using ServerHarness harness = new ServerHarness(null);
        using UsbIpClient client = harness.Connect();

        client.Send(CreateDevListRequest());

        byte[] reply = client.ReadExactly(UsbIpProtocol.OpCommonSize + 4);
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(reply), Is.EqualTo(UsbIpProtocol.Version));
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(2)), Is.EqualTo(UsbIpProtocol.OpReplyDevList));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(reply.AsSpan(4)), Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(8)), Is.Zero); // number of exported devices
    }

    [Test]
    public void DevList_OneRegisteredDevice_RepliesEntryFromDescriptorSet()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();

        client.Send(CreateDevListRequest());

        byte[] reply = client.ReadExactly(DevListReplySize);
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(reply), Is.EqualTo(UsbIpProtocol.Version));
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(2)), Is.EqualTo(UsbIpProtocol.OpReplyDevList));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(reply.AsSpan(4)), Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(8)), Is.EqualTo(1u)); // number of exported devices
        Assert.That(reply[12..268], Is.All.Zero); // path: no host path
        Assert.That(Encoding.ASCII.GetString(reply, 268, 3), Is.EqualTo(TestBusId)); // busid at 12 + 256
        Assert.That(reply[271..300], Is.All.Zero); // busid padding
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(300)), Is.EqualTo(1u)); // busnum
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(304)), Is.EqualTo(1u)); // devnum
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(308)), Is.EqualTo((uint)UsbIpDeviceSpeed.Full)); // speed
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(312)), Is.EqualTo(TestDevices.SimpleIdVendor));
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(314)), Is.EqualTo(TestDevices.SimpleIdProduct));
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(316)), Is.EqualTo(TestDevices.SimpleBcdDevice));
        Assert.That(reply[318..321], Is.All.Zero); // device class, subclass and protocol
        Assert.That(reply[321], Is.EqualTo(TestDevices.SimpleConfigurationValue)); // bConfigurationValue
        Assert.That(reply[322], Is.EqualTo((byte)1)); // bNumConfigurations
        Assert.That(reply[323], Is.EqualTo((byte)1)); // bNumInterfaces
        Assert.That(reply[324..328], Is.EqualTo(new byte[]
        {
            TestDevices.SimpleInterfaceClass, 0, 0, 0
        })); // interface record: class, subclass, protocol, pad
    }

    [Test]
    public void DevList_DefaultSpeed_RepliesFull()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();

        client.Send(CreateDevListRequest());

        byte[] reply = client.ReadExactly(DevListReplySize);
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(308)), Is.EqualTo((uint)UsbIpDeviceSpeed.Full)); // speed
    }

    [Test]
    public void DevList_OverriddenSpeed_RepliesHigh()
    {
        HighSpeedDevice device = new HighSpeedDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();

        client.Send(CreateDevListRequest());

        byte[] reply = client.ReadExactly(DevListReplySize);
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(308)), Is.EqualTo((uint)UsbIpDeviceSpeed.High)); // speed
    }

    [Test]
    public void CreateDeviceInfo_Composite_ReportsDistinctInterfaceCount()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateCompositeDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();

        client.Send(CreateDevListRequest());

        // The descriptor set carries five interface descriptors (interface 2 declares two
        // alternate settings), but the handshake reports one record per distinct interface number.
        Assert.That(device.Descriptors.Interfaces, Has.Count.EqualTo(5));
        Assert.That(device.Descriptors.InterfaceCount, Is.EqualTo(4));
        byte[] reply = client.ReadExactly(UsbIpProtocol.OpCommonSize + 4 + UsbIpDeviceInfo.DevListEntrySize(4));
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(8)), Is.EqualTo(1u)); // number of exported devices
        Assert.That(reply[323], Is.EqualTo((byte)4)); // bNumInterfaces counts distinct interface numbers
        Assert.That(reply[324..340], Is.EqualTo(new byte[]
        {
            TestDevices.CompositeVendorInterfaceClass, 0, 0, 0, // interface 0 alternate setting 0
            TestDevices.CompositeAudioControlClass, TestDevices.CompositeAudioControlSubClass, 0, 0, // interface 1 alternate setting 0
            TestDevices.CompositeAudioStreamingClass, TestDevices.CompositeAudioStreamingSubClass, TestDevices.CompositeAudioStreamingProtocol, 0,
            TestDevices.SimpleInterfaceClass, 0, 0, 0 // interface 3 alternate setting 0
        }));
    }

    [Test]
    public void Import_CompositeDevice_PacesOnlyAltZeroInterruptInEndpoints()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateCompositeDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();

        ImportDevice(client, TestBusId);
        SendBarrier(client, 1);

        // Only the active alternate setting's interrupt IN endpoint (0x84) gets a pacer; the
        // inactive alternate setting's interrupt IN endpoint (0x85) and the bulk IN endpoint do not.
        Assert.That(harness.TimeProvider.TimerCount, Is.EqualTo(1));
    }

    [Test]
    public void Import_KnownBusId_RepliesBaseEntryThenServesUrbs()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();

        client.Send(CreateDevListRequest());
        client.ReadExactly(DevListReplySize);
        client.Send(CreateImportRequest(TestBusId));

        // OP_REP_IMPORT carries the 312-byte base entry only - no interface records.
        byte[] reply = client.ReadExactly(UsbIpProtocol.OpCommonSize + UsbIpDeviceInfo.EntrySize);
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(2)), Is.EqualTo(UsbIpProtocol.OpReplyImport));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(reply.AsSpan(4)), Is.Zero);
        Assert.That(Encoding.ASCII.GetString(reply, 8 + 256, 3), Is.EqualTo(TestBusId)); // busid at 8 + 256
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(8 + 288)), Is.EqualTo(1u)); // busnum
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(8 + 292)), Is.EqualTo(1u)); // devnum
        Assert.That(reply[8 + 311], Is.EqualTo((byte)1)); // bNumInterfaces is set although no records follow

        // The session switched into URB mode: a control submit is answered with a RET_SUBMIT.
        client.Send(CreateControlSubmitFrame(1, CreateGetDeviceDescriptorSetup(), UsbIpDirection.In, 18));
        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.Command, Is.EqualTo(UsbIpProtocol.CommandRetSubmit));
        Assert.That(ret.SeqNum, Is.EqualTo(1u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.EqualTo(18u));
        Assert.That(client.ReadExactly(18), Is.EqualTo(TestDevices.CreateSimpleDeviceDescriptor()));
    }

    [Test]
    public void Import_OverriddenSpeed_RepliesHighInBaseEntry()
    {
        HighSpeedDevice device = new HighSpeedDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();

        client.Send(CreateImportRequest(TestBusId));

        byte[] reply = client.ReadExactly(UsbIpProtocol.OpCommonSize + UsbIpDeviceInfo.EntrySize);
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(8 + 296)), Is.EqualTo((uint)UsbIpDeviceSpeed.High)); // speed
    }

    [Test]
    public void Import_UnknownBusId_RepliesStatusOneAndClosesSession()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();

        client.Send(CreateDevListRequest());
        client.ReadExactly(DevListReplySize);
        client.Send(CreateImportRequest("9-9"));

        byte[] reply = client.ReadExactly(UsbIpProtocol.OpCommonSize);
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(2)), Is.EqualTo(UsbIpProtocol.OpReplyImport));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(reply.AsSpan(4)), Is.EqualTo(1)); // kernel doc: 0 = OK, 1 = error
        Assert.That(client.WaitForRemoteClose(CommunicationTimeoutMilliseconds), Is.True);
        Assert.That(harness.TimeProvider.TimerCount, Is.Zero); // a rejected import creates no pacer
    }

    [Test]
    public void ControlGetDescriptor_NonIsoRequestFieldsIgnored_RepliesDescriptorAndPinnedRetFields()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        // start_frame and number_of_packets are unreliable for non-ISO URBs and must be ignored.
        UsbIpHeader submit = UsbIpHeader.CreateSubmit(
            7,
            TestDeviceId,
            UsbIpDirection.In,
            0,
            18,
            startFrame: uint.MaxValue,
            numberOfPackets: 3,
            setup: CreateGetDeviceDescriptorSetup());
        client.Send(CreateUrbFrame(submit));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.Command, Is.EqualTo(UsbIpProtocol.CommandRetSubmit));
        Assert.That(ret.SeqNum, Is.EqualTo(7u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.EqualTo(18u));
        Assert.That(ret.NumberOfPackets, Is.EqualTo(uint.MaxValue)); // pinned 0xFFFFFFFF for non-ISO rets
        Assert.That(ret.Bytes[8..20], Is.All.Zero); // devid, direction and ep are zeroed
        Assert.That(ret.Bytes[28..32], Is.All.Zero); // start_frame
        Assert.That(ret.Bytes[36..40], Is.All.Zero); // error_count
        Assert.That(ret.Bytes[40..48], Is.All.Zero); // padding
        Assert.That(client.ReadExactly(18), Is.EqualTo(TestDevices.CreateSimpleDeviceDescriptor()));
    }

    [Test]
    public void ControlSetReport_TruncatedPayload_StallsAndFramingStaysInSync()
    {
        TestHidDevice device = new TestHidDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        // SET_REPORT(OUTPUT) declares a 9-byte data stage but only 4 payload bytes follow.
        UsbSetupPacket setup = new UsbSetupPacket
        {
            RequestType = 0x21, // host-to-device, class, recipient interface
            Request = 0x09, // SET_REPORT
            Value = 0x0200, // output report, report id 0
            Index = 0, // HID interface 0
            Length = 9
        };
        client.Send(CreateControlSubmitFrame(100, setup, UsbIpDirection.Out, 4, [0x01, 0x02, 0x03, 0x04]));

        RetFrame stalled = ReadRetFrame(client);
        Assert.That(stalled.SeqNum, Is.EqualTo(100u));
        Assert.That(stalled.Status, Is.EqualTo(-32)); // STALL: the payload was truncated
        Assert.That(stalled.ActualLength, Is.Zero);
        Assert.That(device.OutputReports, Is.Empty);

        // The exact payload consumption keeps the framing in sync: the next request still works.
        client.Send(CreateControlSubmitFrame(101, CreateGetDeviceDescriptorSetup(), UsbIpDirection.In, 18));
        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(101u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.EqualTo(18u));
        Assert.That(client.ReadExactly(18), Is.EqualTo(TestDevices.CreateSimpleDeviceDescriptor()));
    }

    [Test]
    public void ControlSetReport_DeclaredPayload_RoutesToOutputReport()
    {
        TestHidDevice device = new TestHidDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        UsbSetupPacket setup = new UsbSetupPacket
        {
            RequestType = 0x21, // host-to-device, class, recipient interface
            Request = 0x09, // SET_REPORT
            Value = 0x0200, // output report, report id 0
            Index = 0, // HID interface 0
            Length = 5
        };
        byte[] payload = [0x10, 0x20, 0x30, 0x40, 0x50];
        client.Send(CreateControlSubmitFrame(110, setup, UsbIpDirection.Out, 5, payload));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(110u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.EqualTo(5u)); // the accepted payload bytes of the OUT data stage
        Assert.That(device.OutputReports, Has.Count.EqualTo(1));
        Assert.That(device.OutputReports[0], Is.EqualTo(payload));
    }

    [Test]
    public void InterruptIn_AdvanceOneInterval_CompletesUrbWithFreshReport()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        SendInterruptInSubmit(client, 10, 64);
        SendBarrier(client, 11);
        device.InputReport = [0xAA, 0xBB, 0xCC, 0xDD];
        harness.TimeProvider.Advance(Interval);

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.Command, Is.EqualTo(UsbIpProtocol.CommandRetSubmit));
        Assert.That(ret.SeqNum, Is.EqualTo(10u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.EqualTo(4u)); // min(transfer_buffer_length, report length)
        Assert.That(client.ReadExactly(4), Is.EqualTo(new byte[]
        {
            0xAA, 0xBB, 0xCC, 0xDD
        }));

        harness.TimeProvider.Advance(Interval);
        Assert.That(client.TryReadExactly(new byte[UsbIpProtocol.HeaderSize], SilenceProbeMilliseconds), Is.False); // the queue is empty
    }

    [Test]
    public void InterruptIn_ReportLongerThanTransferBuffer_TruncatesToTransferBufferLength()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        device.InputReport = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        SendInterruptInSubmit(client, 20, 4);
        SendBarrier(client, 21);
        harness.TimeProvider.Advance(Interval);

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(20u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.EqualTo(4u));
        Assert.That(client.ReadExactly(4), Is.EqualTo(new byte[]
        {
            0x01, 0x02, 0x03, 0x04
        }));
    }

    [Test]
    public void InterruptIn_DeviceThrows_AnswersUrbWithShutdownAndLogsError()
    {
        ThrowingInputDevice device = new ThrowingInputDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        SendInterruptInSubmit(client, 44, 64);
        SendBarrier(client, 45);
        harness.TimeProvider.Advance(Interval);

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.Command, Is.EqualTo(UsbIpProtocol.CommandRetSubmit));
        Assert.That(ret.SeqNum, Is.EqualTo(44u));
        Assert.That(ret.Status, Is.EqualTo(-108)); // ESHUTDOWN: the persona could not provide the report
        Assert.That(ret.ActualLength, Is.Zero);
        Assert.That(harness.Logger.ErrorEntries, Has.Count.EqualTo(1));
        Assert.That(harness.Logger.ErrorEntries[0].Message, Does.Contain("persona input failure"));

        // The session survives the persona fault: framing is intact and the next request is served.
        client.Send(CreateControlSubmitFrame(46, CreateGetDeviceDescriptorSetup(), UsbIpDirection.In, 18));
        RetFrame next = ReadRetFrame(client);
        Assert.That(next.SeqNum, Is.EqualTo(46u));
        Assert.That(next.Status, Is.Zero);
        Assert.That(client.ReadExactly(18), Is.EqualTo(TestDevices.CreateSimpleDeviceDescriptor()));
    }

    [Test]
    public void InterruptIn_ThreeUrbsTwoIntervals_CompletesInOrderWithDistinctPayloads()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        byte[] first = [0x01, 0x02, 0x03, 0x04];
        byte[] second = [0x05, 0x06, 0x07, 0x08];
        device.EnqueueInputReport(first);
        device.EnqueueInputReport(second);
        SendInterruptInSubmit(client, 30, 64);
        SendInterruptInSubmit(client, 31, 64);
        SendInterruptInSubmit(client, 32, 64);
        SendBarrier(client, 33);

        harness.TimeProvider.Advance(2 * Interval);

        RetFrame firstRet = ReadRetFrame(client);
        Assert.That(firstRet.SeqNum, Is.EqualTo(30u));
        Assert.That(firstRet.Status, Is.Zero);
        Assert.That(firstRet.ActualLength, Is.EqualTo(4u));
        Assert.That(client.ReadExactly(4), Is.EqualTo(first));
        RetFrame secondRet = ReadRetFrame(client);
        Assert.That(secondRet.SeqNum, Is.EqualTo(31u));
        Assert.That(secondRet.Status, Is.Zero);
        Assert.That(secondRet.ActualLength, Is.EqualTo(4u));
        Assert.That(client.ReadExactly(4), Is.EqualTo(second));

        Assert.That(client.TryReadExactly(new byte[UsbIpProtocol.HeaderSize], SilenceProbeMilliseconds), Is.False); // the third URB waits for the next tick

        byte[] third = [0x09, 0x0A];
        device.InputReport = third;
        harness.TimeProvider.Advance(Interval);
        RetFrame thirdRet = ReadRetFrame(client);
        Assert.That(thirdRet.SeqNum, Is.EqualTo(32u));
        Assert.That(thirdRet.Status, Is.Zero);
        Assert.That(thirdRet.ActualLength, Is.EqualTo(2u));
        Assert.That(client.ReadExactly(2), Is.EqualTo(third));
    }

    [Test]
    public void InterruptIn_ZeroLengthReport_KeepsUrbsQueuedInOrder()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        device.InputReport = [];
        SendInterruptInSubmit(client, 40, 64);
        SendInterruptInSubmit(client, 41, 64);
        SendBarrier(client, 42);

        harness.TimeProvider.Advance(Interval); // nothing to report: both URBs stay queued
        Assert.That(client.TryReadExactly(new byte[UsbIpProtocol.HeaderSize], SilenceProbeMilliseconds), Is.False);

        device.InputReport = [0x77, 0x88];
        harness.TimeProvider.Advance(Interval); // the oldest URB completes first
        RetFrame firstRet = ReadRetFrame(client);
        Assert.That(firstRet.SeqNum, Is.EqualTo(40u));
        Assert.That(firstRet.Status, Is.Zero);
        Assert.That(client.ReadExactly(2), Is.EqualTo(new byte[]
        {
            0x77, 0x88
        }));

        harness.TimeProvider.Advance(Interval);
        RetFrame secondRet = ReadRetFrame(client);
        Assert.That(secondRet.SeqNum, Is.EqualTo(41u));
        Assert.That(secondRet.Status, Is.Zero);
        Assert.That(client.ReadExactly(2), Is.EqualTo(new byte[]
        {
            0x77, 0x88
        }));
    }

    [Test]
    public void InterruptOut_SubmitPayload_RaisesOnOutputReportAndRepliesImmediately()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        byte[] payload = [0x10, 0x20, 0x30, 0x40, 0x50];
        client.Send(CreateUrbFrame(50, UsbIpDirection.Out, EndpointNumberOf(TestDevices.SimpleInterruptOutAddress), (uint)payload.Length, payload: payload));

        RetFrame ret = ReadRetFrame(client); // no pacer advance needed
        Assert.That(ret.SeqNum, Is.EqualTo(50u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.EqualTo(5u));
        Assert.That(device.OutputReports, Has.Count.EqualTo(1));
        Assert.That(device.OutputReports[0], Is.EqualTo(payload));
    }

    [Test]
    public void Unlink_PendingUrb_RepliesEconnresetAndSuppressesCompletion()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        SendInterruptInSubmit(client, 60, 64);
        client.Send(CreateUnlinkFrame(61, 60));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.Command, Is.EqualTo(UsbIpProtocol.CommandRetUnlink));
        Assert.That(ret.SeqNum, Is.EqualTo(61u));
        Assert.That(ret.Status, Is.EqualTo(-104)); // -ECONNRESET: the URB was cancelled
        Assert.That(ret.Bytes[8..20], Is.All.Zero); // devid, direction and ep are zeroed
        Assert.That(ret.Bytes[24..48], Is.All.Zero); // padding

        harness.TimeProvider.Advance(3 * Interval);
        Assert.That(client.TryReadExactly(new byte[UsbIpProtocol.HeaderSize], SilenceProbeMilliseconds), Is.False); // no RET_SUBMIT for the unlinked seqnum
    }

    [Test]
    public void Unlink_CompletedAndUnknownSeqNums_ReplySuccess()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        SendInterruptInSubmit(client, 70, 64);
        SendBarrier(client, 71);
        harness.TimeProvider.Advance(Interval);
        RetFrame completed = ReadRetFrame(client);
        Assert.That(completed.SeqNum, Is.EqualTo(70u));
        client.ReadExactly((int)completed.ActualLength);

        client.Send(CreateUnlinkFrame(72, 70)); // already completed
        RetFrame completedUnlink = ReadRetFrame(client);
        Assert.That(completedUnlink.Command, Is.EqualTo(UsbIpProtocol.CommandRetUnlink));
        Assert.That(completedUnlink.SeqNum, Is.EqualTo(72u));
        Assert.That(completedUnlink.Status, Is.Zero);

        client.Send(CreateUnlinkFrame(73, 999)); // never queued
        RetFrame unknownUnlink = ReadRetFrame(client);
        Assert.That(unknownUnlink.Status, Is.Zero);
    }

    [Test]
    public void UnsupportedEndpointDirection_RepliesNoEntryAndKeepsSession()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        // Endpoint number 2 exists only as an OUT endpoint (0x02): the IN pairing matches nothing.
        client.Send(CreateUrbFrame(74, UsbIpDirection.In, EndpointNumberOf(TestDevices.SimpleInterruptOutAddress), 64));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(74u));
        Assert.That(ret.Status, Is.EqualTo(-2)); // ENOENT: no IN endpoint with this number
        SendBarrier(client, 75);
    }

    [Test]
    public void UnknownEndpointOut_RepliesNoEntryConsumesPayloadAndKeepsFraming()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        // No device declares endpoint number 3: the reply is -2 and the 5 payload bytes are discarded.
        byte[] payload = [0x01, 0x02, 0x03, 0x04, 0x05];
        client.Send(CreateUrbFrame(76, UsbIpDirection.Out, 3, (uint)payload.Length, payload: payload));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(76u));
        Assert.That(ret.Status, Is.EqualTo(-2)); // ENOENT
        Assert.That(ret.ActualLength, Is.Zero);
        Assert.That(device.OutputReports, Is.Empty);

        // The payload was consumed exactly, so the next URB on the same connection still parses.
        SendBarrier(client, 77);
    }

    [Test]
    public void BulkTransfers_OnBulkDevice_CompleteImmediatelyWithoutPacers()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateBulkDevice());
        device.InputReport = [0x0A, 0x0B, 0x0C];
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        client.Send(CreateUrbFrame(80, UsbIpDirection.In, EndpointNumberOf(TestDevices.BulkInAddress), 64));
        RetFrame inRet = ReadRetFrame(client); // completes without any time advance
        Assert.That(inRet.SeqNum, Is.EqualTo(80u));
        Assert.That(inRet.Status, Is.Zero);
        Assert.That(inRet.ActualLength, Is.EqualTo(3u));
        Assert.That(client.ReadExactly(3), Is.EqualTo(new byte[]
        {
            0x0A, 0x0B, 0x0C
        }));

        byte[] payload = [0x01, 0x02];
        client.Send(CreateUrbFrame(81, UsbIpDirection.Out, EndpointNumberOf(TestDevices.BulkOutAddress), (uint)payload.Length, payload: payload));
        RetFrame outRet = ReadRetFrame(client);
        Assert.That(outRet.SeqNum, Is.EqualTo(81u));
        Assert.That(outRet.Status, Is.Zero);
        Assert.That(outRet.ActualLength, Is.EqualTo(2u));
        Assert.That(device.OutputReports, Has.Count.EqualTo(1));
        Assert.That(device.OutputReports[0], Is.EqualTo(payload));

        Assert.That(harness.TimeProvider.TimerCount, Is.Zero); // bulk endpoints are never paced
    }

    [Test]
    public void Submit_WithAbsurdTransferBufferLength_ClosesSessionWithoutAllocating()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        // A declared length this large must close the session instead of triggering an allocation.
        client.Send(CreateUrbFrame(500, UsbIpDirection.In, EndpointNumberOf(TestDevices.SimpleInterruptInAddress), uint.MaxValue));

        Assert.That(client.WaitForRemoteClose(CommunicationTimeoutMilliseconds), Is.True);
    }

    [Test]
    public void TwoSessions_OwnQueuesAndPacers_CompleteIndependently()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient first = harness.Connect();
        using UsbIpClient second = harness.Connect();
        ImportDevice(first, TestBusId);
        SendBarrier(first, 200);
        ImportDevice(second, TestBusId);
        SendBarrier(second, 201);

        Assert.That(harness.TimeProvider.TimerCount, Is.EqualTo(2)); // one pacer per session

        SendInterruptInSubmit(first, 210, 64);
        SendBarrier(first, 211);
        SendInterruptInSubmit(second, 220, 64);
        SendBarrier(second, 221);

        harness.TimeProvider.Advance(Interval);

        RetFrame firstRet = ReadRetFrame(first);
        Assert.That(firstRet.SeqNum, Is.EqualTo(210u));
        first.ReadExactly((int)firstRet.ActualLength);
        RetFrame secondRet = ReadRetFrame(second);
        Assert.That(secondRet.SeqNum, Is.EqualTo(220u));
        second.ReadExactly((int)secondRet.ActualLength);

        // An unlink in one session never touches a seqnum pending in the other session's queue.
        SendInterruptInSubmit(second, 230, 64);
        SendBarrier(second, 231);
        first.Send(CreateUnlinkFrame(212, 230));
        RetFrame unknown = ReadRetFrame(first);
        Assert.That(unknown.Status, Is.Zero); // 230 is pending in the second session, not in this one

        harness.TimeProvider.Advance(Interval);
        RetFrame pendingRet = ReadRetFrame(second);
        Assert.That(pendingRet.SeqNum, Is.EqualTo(230u));
        second.ReadExactly((int)pendingRet.ActualLength);
    }

    [Test]
    public void Stop_WhileClientConnected_ClosesSessionAndStopsPacers()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);
        SendInterruptInSubmit(client, 240, 64);
        SendBarrier(client, 241);

        harness.Server.Stop();

        Assert.That(client.WaitForRemoteClose(CommunicationTimeoutMilliseconds), Is.True);
        Assert.That(harness.TimeProvider.TimerCount, Is.Zero); // the live session was torn down
    }

    [Test]
    public void GarbageCommand_ClosesSessionAndServerAcceptsNewConnection()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using (UsbIpClient client = harness.Connect())
        {
            ImportDevice(client, TestBusId);
            byte[] garbage = new byte[UsbIpProtocol.HeaderSize];
            BinaryPrimitives.WriteUInt32BigEndian(garbage, 7); // neither SUBMIT nor UNLINK
            client.Send(garbage);

            Assert.That(client.WaitForRemoteClose(CommunicationTimeoutMilliseconds), Is.True);
        }

        using UsbIpClient secondClient = harness.Connect();
        secondClient.Send(CreateDevListRequest());
        byte[] reply = secondClient.ReadExactly(UsbIpProtocol.OpCommonSize + 4);
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(2)), Is.EqualTo(UsbIpProtocol.OpReplyDevList)); // the listener survived
    }

    [Test]
    public void TruncatedFrameThenDisconnect_ServerSurvivesWithoutLoggedErrors()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using (UsbIpClient handshakeClient = harness.Connect())
        {
            handshakeClient.Send(new byte[4]); // truncated op_common, then disconnect
        }

        using (UsbIpClient urbClient = harness.Connect())
        {
            ImportDevice(urbClient, TestBusId);
            urbClient.Send(new byte[30]); // truncated URB header, then disconnect
        }

        using (UsbIpClient survivor = harness.Connect())
        {
            survivor.Send(CreateDevListRequest());
            survivor.ReadExactly(UsbIpProtocol.OpCommonSize + 4); // the listener survived both disconnects
        }

        harness.Server.Stop(); // wait for every session to tear down before checking the log

        Assert.That(harness.Logger.Entries, Is.Not.Empty);
        Assert.That(harness.Logger.ErrorEntries, Is.Empty); // an unexpected session fault would be logged at Error level
    }

    [Test]
    public void SessionEnd_DisconnectWithPendingUrbs_StopsPacersWithoutLeakedTimers()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice();
        using ServerHarness harness = new ServerHarness(device);
        using (UsbIpClient client = harness.Connect())
        {
            ImportDevice(client, TestBusId);
            SendInterruptInSubmit(client, 90, 64);
            SendInterruptInSubmit(client, 91, 64);
            SendBarrier(client, 92);

            Assert.That(harness.TimeProvider.TimerCount, Is.EqualTo(1)); // one pacer for the interrupt IN endpoint
        }

        harness.Server.Stop(); // waits for the session teardown

        Assert.That(harness.TimeProvider.TimerCount, Is.Zero); // the pacer disposed its timer: no scheduler entry leaked
        Assert.That(() => harness.TimeProvider.Advance(10 * Interval), Throws.Nothing);
    }

    [Test]
    public void AddRemoveDevice_TracksBusIdsAndRejectsMalformedOrDuplicateIds()
    {
        using UsbIpServer server = new UsbIpServer(new UsbIpServerOptions());
        TestVirtualUsbDevice first = new TestVirtualUsbDevice();
        TestVirtualUsbDevice second = new TestVirtualUsbDevice();

        server.AddDevice("1-1", first);
        server.AddDevice("2-1", second);

        Assert.That(server.BusIds, Is.EqualTo(new[]
        {
            "1-1", "2-1"
        }));
        Assert.That(() => server.AddDevice("2-1", first), Throws.ArgumentException); // duplicate bus id
        Assert.That(() => server.AddDevice("nonsense", first), Throws.ArgumentException); // no leading bus number
        Assert.That(server.RemoveDevice("1-1"), Is.True);
        Assert.That(server.BusIds, Is.EqualTo(new[]
        {
            "2-1"
        }));
        Assert.That(server.RemoveDevice("1-1"), Is.False);
    }

    /// <summary>Sends an OP_REQ_DEVLIST request.</summary>
    /// <returns>The 8-byte request frame.</returns>
    private static byte[] CreateDevListRequest()
    {
        byte[] frame = new byte[UsbIpProtocol.OpCommonSize];
        UsbIpHandshake.WriteOpCommon(frame, UsbIpProtocol.OpRequestDevList, 0);
        return frame;
    }

    /// <summary>Sends an OP_REQ_IMPORT request naming <paramref name="busId"/>.</summary>
    /// <param name="busId">Bus id of the device to import.</param>
    /// <returns>The 40-byte request frame.</returns>
    private static byte[] CreateImportRequest(string busId)
    {
        byte[] frame = new byte[UsbIpProtocol.OpCommonSize + UsbIpHandshake.BusIdSize];
        UsbIpHandshake.WriteOpCommon(frame, UsbIpProtocol.OpRequestImport, 0);
        Encoding.ASCII.GetBytes(busId).CopyTo(frame, UsbIpProtocol.OpCommonSize);
        return frame;
    }

    /// <summary>Creates a USBIP_CMD_UNLINK frame; the protocol has no public writer for it yet.</summary>
    /// <param name="seqNum">Sequence number of the unlink request itself.</param>
    /// <param name="unlinkSeqNum">Sequence number of the URB to unlink.</param>
    /// <returns>The 48-byte unlink frame.</returns>
    private static byte[] CreateUnlinkFrame(uint seqNum, uint unlinkSeqNum)
    {
        byte[] frame = new byte[UsbIpProtocol.HeaderSize];
        BinaryPrimitives.WriteUInt32BigEndian(frame, UsbIpProtocol.CommandUnlink);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4), seqNum);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(20), unlinkSeqNum);
        return frame;
    }

    /// <summary>Creates a non-ISO submit frame with the fields the in-the-wild URBs carry.</summary>
    /// <param name="seqNum">Sequence number of the URB.</param>
    /// <param name="direction">Transfer direction.</param>
    /// <param name="endpointNumber">Bare endpoint number the wire ep field carries; the direction travels in the separate direction field.</param>
    /// <param name="transferBufferLength">Declared transfer buffer length.</param>
    /// <param name="setup">Setup packet of a control transfer.</param>
    /// <param name="payload">Payload of an OUT transfer; none for IN transfers.</param>
    /// <returns>The submit frame, payload included.</returns>
    private static byte[] CreateUrbFrame(uint seqNum, UsbIpDirection direction, byte endpointNumber, uint transferBufferLength,
        UsbSetupPacket setup = default, byte[]? payload = null)
    {
        UsbIpHeader header = UsbIpHeader.CreateSubmit(
            seqNum,
            TestDeviceId,
            direction,
            endpointNumber,
            transferBufferLength,
            startFrame: uint.MaxValue, // unreliable for non-ISO URBs; the server must ignore it
            numberOfPackets: 0, // unreliable for non-ISO URBs; the server must ignore it
            setup: setup);
        return CreateUrbFrame(header, payload);
    }

    /// <summary>Serializes a header into a submit frame, appending the payload of an OUT transfer.</summary>
    /// <param name="header">Submit header.</param>
    /// <param name="payload">Payload following the header; none for IN transfers.</param>
    /// <returns>The submit frame.</returns>
    private static byte[] CreateUrbFrame(UsbIpHeader header, byte[]? payload = null)
    {
        byte[] frame = new byte[UsbIpProtocol.HeaderSize + (payload?.Length ?? 0)];
        header.WriteSubmit(frame);
        if (payload is not null)
        {
            payload.CopyTo(frame, UsbIpProtocol.HeaderSize);
        }

        return frame;
    }

    /// <summary>Creates the control submit frame requesting the 18-byte device descriptor.</summary>
    /// <param name="seqNum">Sequence number of the URB.</param>
    /// <param name="setup">Setup packet of the transfer.</param>
    /// <param name="direction">Transfer direction.</param>
    /// <param name="transferBufferLength">Declared transfer buffer length.</param>
    /// <param name="payload">Payload of an OUT transfer; none for IN transfers.</param>
    /// <returns>The submit frame.</returns>
    private static byte[] CreateControlSubmitFrame(uint seqNum, UsbSetupPacket setup, UsbIpDirection direction, uint transferBufferLength,
        byte[]? payload = null) =>
        CreateUrbFrame(seqNum, direction, 0, transferBufferLength, setup, payload);

    /// <summary>Creates the setup packet of GET_DESCRIPTOR(DEVICE, 0) with an 18-byte request length.</summary>
    /// <returns>The setup packet.</returns>
    private static UsbSetupPacket CreateGetDeviceDescriptorSetup()
    {
        return new UsbSetupPacket
        {
            RequestType = 0x80, // device-to-host, standard, recipient device
            Request = 0x06, // GET_DESCRIPTOR
            Value = 0x0100, // DEVICE descriptor, index 0
            Index = 0,
            Length = 18
        };
    }

    /// <summary>
    /// Sends an OP_REQ_IMPORT naming <paramref name="busId"/> and asserts the successful reply.
    /// </summary>
    /// <param name="client">Connected test client.</param>
    /// <param name="busId">Bus id of the device to import.</param>
    private static void ImportDevice(UsbIpClient client, string busId)
    {
        client.Send(CreateImportRequest(busId));
        byte[] reply = client.ReadExactly(UsbIpProtocol.OpCommonSize + UsbIpDeviceInfo.EntrySize);
        UsbIpHandshake.ReadOpCommon(reply, out _, out ushort operation, out int status);
        Assert.That(operation, Is.EqualTo(UsbIpProtocol.OpReplyImport));
        Assert.That(status, Is.Zero);
    }

    /// <summary>Submits an interrupt IN URB to the simple device's endpoint 0x81.</summary>
    /// <param name="client">Connected test client.</param>
    /// <param name="seqNum">Sequence number of the URB.</param>
    /// <param name="transferBufferLength">Declared transfer buffer length.</param>
    private static void SendInterruptInSubmit(UsbIpClient client, uint seqNum, uint transferBufferLength) =>
        client.Send(CreateUrbFrame(seqNum, UsbIpDirection.In, EndpointNumberOf(TestDevices.SimpleInterruptInAddress), transferBufferLength));

    /// <summary>
    /// Returns the bare endpoint number the wire ep field carries for a descriptor endpoint
    /// address: the protocol carries the number in ep and the direction in the separate
    /// direction field (kernel doc CmdIntrIN capture: direction = 1, ep = 1).
    /// </summary>
    /// <param name="endpointAddress">Endpoint address of a descriptor (bEndpointAddress).</param>
    /// <returns>The endpoint number (bits 3..0).</returns>
    private static byte EndpointNumberOf(byte endpointAddress) => (byte)(endpointAddress & 0x0F);

    /// <summary>
    /// Sends a control SET_CONFIGURATION URB that completes immediately and waits for its reply:
    /// because a session handles frames strictly in order, the reply proves that every earlier
    /// frame was processed (used before advancing the manual clock at a queued interrupt URB).
    /// </summary>
    /// <param name="client">Connected test client.</param>
    /// <param name="seqNum">Sequence number of the barrier URB.</param>
    private static void SendBarrier(UsbIpClient client, uint seqNum)
    {
        UsbSetupPacket setup = new UsbSetupPacket
        {
            RequestType = 0x00, // host-to-device, standard, recipient device
            Request = 0x09, // SET_CONFIGURATION
            Value = TestDevices.SimpleConfigurationValue,
            Index = 0,
            Length = 0
        };
        client.Send(CreateControlSubmitFrame(seqNum, setup, UsbIpDirection.Out, 0));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(seqNum));
        Assert.That(ret.Status, Is.Zero);
    }

    /// <summary>Reads one 48-byte return header and parses its fields.</summary>
    /// <param name="client">Connected test client.</param>
    /// <returns>The parsed return header.</returns>
    private static RetFrame ReadRetFrame(UsbIpClient client) => new RetFrame(client.ReadExactly(UsbIpProtocol.HeaderSize));

    /// <summary>
    /// Device whose input report request always throws; drives the server's containment path that
    /// answers the dequeued interrupt IN URB with a shutdown status instead of dropping it.
    /// </summary>
    private sealed class ThrowingInputDevice : VirtualUsbDevice
    {
        /// <summary>Initializes the device with the simple test device's descriptor set.</summary>
        public ThrowingInputDevice()
            : base(TestDevices.CreateSimpleDevice())
        {
        }

        /// <inheritdoc/>
        public override int ProvideInputReport(Span<byte> destination) =>
            throw new InvalidOperationException("persona input failure");
    }

    /// <summary>Device that declares itself as a high-speed USB/IP export over the simple test device's descriptor set.</summary>
    private sealed class HighSpeedDevice : VirtualUsbDevice
    {
        /// <summary>Initializes the device with the simple test device's descriptor set.</summary>
        public HighSpeedDevice()
            : base(TestDevices.CreateSimpleDevice())
        {
        }

        /// <inheritdoc/>
        public override UsbIpDeviceSpeed Speed
        {
            get
            {
                return UsbIpDeviceSpeed.High;
            }
        }
    }

    /// <summary>One USBIP_RET_SUBMIT or USBIP_RET_UNLINK frame with the fields the tests assert on.</summary>
    private sealed class RetFrame
    {
        /// <summary>Parses a 48-byte return header.</summary>
        /// <param name="bytes">Raw frame bytes.</param>
        public RetFrame(byte[] bytes)
        {
            Bytes = bytes;
            Command = BinaryPrimitives.ReadUInt32BigEndian(bytes);
            SeqNum = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
            Status = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
            ActualLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(24));
            StartFrame = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(28));
            NumberOfPackets = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(32));
            ErrorCount = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(36));
        }

        /// <summary>Raw frame bytes.</summary>
        public byte[] Bytes { get; }

        /// <summary>Command code: USBIP_RET_SUBMIT or USBIP_RET_UNLINK.</summary>
        public uint Command { get; }

        /// <summary>Sequence number of the answered URB.</summary>
        public uint SeqNum { get; }

        /// <summary>Completion status.</summary>
        public int Status { get; }

        /// <summary>Number of bytes actually transferred.</summary>
        public uint ActualLength { get; }

        /// <summary>Initial frame field of the reply.</summary>
        public uint StartFrame { get; }

        /// <summary>Number of packets field of the reply.</summary>
        public uint NumberOfPackets { get; }

        /// <summary>Error count field of the reply.</summary>
        public uint ErrorCount { get; }
    }

    /// <summary>
    /// Loopback TCP test client speaking raw USB/IP frames: exact-length reads for expected frames
    /// and timeout probes that assert nothing arrives.
    /// </summary>
    private sealed class UsbIpClient : IDisposable
    {
        private readonly TcpClient _client;

        private readonly NetworkStream _stream;

        /// <summary>Connects to <paramref name="endPoint"/> and switches off Nagle.</summary>
        /// <param name="endPoint">Server endpoint to connect to.</param>
        public UsbIpClient(IPEndPoint endPoint)
        {
            _client = new TcpClient();
            _client.Connect(endPoint);
            _client.NoDelay = true;
            _stream = _client.GetStream();
            _stream.ReadTimeout = CommunicationTimeoutMilliseconds;
        }

        /// <summary>Sends one complete frame.</summary>
        /// <param name="frame">Frame bytes.</param>
        public void Send(byte[] frame) => _stream.Write(frame, 0, frame.Length);

        /// <summary>Reads exactly <paramref name="count"/> bytes, failing on timeout or close.</summary>
        /// <param name="count">Number of bytes to read.</param>
        /// <returns>The bytes read.</returns>
        public byte[] ReadExactly(int count)
        {
            byte[] buffer = new byte[count];
            ReadExactly(buffer);
            return buffer;
        }

        /// <summary>
        /// Reads exactly <paramref name="buffer"/>.Length bytes, failing on timeout or close.
        /// </summary>
        /// <param name="buffer">Buffer to fill.</param>
        public void ReadExactly(byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = _stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException("The USB/IP server closed the connection before the frame was complete.");
                }

                offset += read;
            }
        }

        /// <summary>
        /// Attempts one exact-length read; the tests call it only while expecting nothing to
        /// arrive, so a receive timeout reports "still silent" while a closed connection fails.
        /// </summary>
        /// <param name="buffer">Buffer to fill.</param>
        /// <param name="timeoutMilliseconds">Receive timeout of the attempt.</param>
        /// <returns><see langword="true"/> when the frame arrived; <see langword="false"/> on timeout.</returns>
        public bool TryReadExactly(byte[] buffer, int timeoutMilliseconds)
        {
            int previousTimeout = _stream.ReadTimeout;
            _stream.ReadTimeout = timeoutMilliseconds;
            try
            {
                ReadExactly(buffer);
                return true;
            }
            catch (EndOfStreamException)
            {
                throw; // the connection died: not the "nothing arrived" case
            }
            catch (IOException)
            {
                return false; // receive timeout
            }
            finally
            {
                _stream.ReadTimeout = previousTimeout;
            }
        }

        /// <summary>Waits until the server closes the connection.</summary>
        /// <param name="timeoutMilliseconds">Receive timeout of the wait.</param>
        /// <returns><see langword="true"/> when the connection was closed; <see langword="false"/> on timeout.</returns>
        public bool WaitForRemoteClose(int timeoutMilliseconds)
        {
            int previousTimeout = _stream.ReadTimeout;
            _stream.ReadTimeout = timeoutMilliseconds;
            byte[] probe = new byte[1];
            try
            {
                return _stream.Read(probe, 0, 1) == 0;
            }
            catch (IOException)
            {
                return false; // receive timeout: the session is still open
            }
            finally
            {
                _stream.ReadTimeout = previousTimeout;
            }
        }

        /// <summary>Closes the connection.</summary>
        public void Dispose()
        {
            _stream.Dispose();
            _client.Dispose();
        }
    }

    /// <summary>
    /// Started <see cref="UsbIpServer"/> on loopback port 0 with a manual clock and a recording
    /// logger, optionally registering one device under <see cref="TestBusId"/>.
    /// </summary>
    private sealed class ServerHarness : IDisposable
    {
        /// <summary>Starts the server and registers <paramref name="device"/> when it is not null.</summary>
        /// <param name="device">Device to register; <see langword="null"/> starts an empty server.</param>
        /// <param name="busId">Bus id to register the device under.</param>
        public ServerHarness(VirtualUsbDevice? device, string busId = TestBusId)
        {
            TimeProvider = new ManualTimeProvider();
            Logger = new RecordingLogger();
            Server = new UsbIpServer(new UsbIpServerOptions
            {
                ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
                TimeProvider = TimeProvider,
                Logger = Logger,
                InterruptInterval = Interval
            });
            Server.Start();
            if (device is not null)
            {
                Server.AddDevice(busId, device);
            }
        }

        /// <summary>Manual clock injected into the server.</summary>
        public ManualTimeProvider TimeProvider { get; }

        /// <summary>Logger injected into the server.</summary>
        public RecordingLogger Logger { get; }

        /// <summary>Started server under test.</summary>
        public UsbIpServer Server { get; }

        /// <summary>Connects a test client to the server's bound endpoint.</summary>
        /// <returns>The connected client.</returns>
        public UsbIpClient Connect() => new UsbIpClient(Server.BoundEndPoint);

        /// <summary>Disposes the server.</summary>
        public void Dispose() => Server.Dispose();
    }
}