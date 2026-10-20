using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.UsbIp;

namespace DualSenseClient.Tests.Emulation.UsbIp;

/// <summary>
/// End-to-end tests of the isochronous URB dispatch of <see cref="UsbIpServer"/> over loopback TCP:
/// the ISO framing of OUT submits (payload then packet descriptors) and IN submits (descriptor tail
/// only), the validation rejections that must keep the stream in sync, the per-frame IN completion
/// driven by <see cref="ManualTimeProvider"/>, unlink and the teardown of a session with pending
/// isochronous work.
/// </summary>
[TestFixture]
public sealed class UsbIpServerIsoTests
{
    /// <summary>Duration of one full-speed USB frame, the ISO scheduler cadence.</summary>
    private static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(1);

    /// <summary>Bus id of the registered test device (busnum 1, devnum 1).</summary>
    private const string TestBusId = "1-1";

    /// <summary>Devid a client derives from busid "1-1": (busnum &lt;&lt; 16) | devnum.</summary>
    private const uint TestDeviceId = 0x00010001;

    /// <summary>Timeout of the receive probes that assert nothing arrives.</summary>
    private const int SilenceProbeMilliseconds = 250;

    /// <summary>Timeout of the waits that assert the session closes or the server answers.</summary>
    private const int CommunicationTimeoutMilliseconds = 5000;

    /// <summary>Start frame the ISO submits declare; every ISO reply must echo it.</summary>
    private const uint TestStartFrame = 0x00123456;

    /// <summary>Interval field the ISO submits declare (server-side host controller hint).</summary>
    private const uint TestInterval = 8;

    [Test]
    public void IsoOut_TwoPackets_DeliversExactPayloadsAndRetDescriptors()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        // No inter-packet padding: the 8 payload bytes directly follow the header and the two
        // descriptors (offsets 0 and 4, 4 bytes each) follow the payload.
        byte[] payload = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 4
            },
            new IsoPacketDescriptor
            {
                Offset = 4,
                Length = 4
            }
        ];
        client.Send(CreateIsoSubmitFrame(10, UsbIpDirection.Out, EndpointNumberOf(TestDevices.IsoOutAddress), 8, packets, payload));
        SendBarrier(client, 11); // the OUT URB is queued; the barrier reply proves it was parsed

        Assert.That(device.IsoOutPackets, Is.Empty, "An OUT packet must not be delivered before its USB frame.");
        harness.TimeProvider.Advance(FrameDuration);
        Assert.That(device.IsoOutPackets, Has.Count.EqualTo(1), "The first OUT packet must arrive on the first frame.");
        harness.TimeProvider.Advance(FrameDuration);

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.Command, Is.EqualTo(UsbIpProtocol.CommandRetSubmit));
        Assert.That(ret.SeqNum, Is.EqualTo(10u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.EqualTo(8u)); // the declared transfer length was delivered in full
        Assert.That(ret.NumberOfPackets, Is.EqualTo(2u));
        Assert.That(ret.StartFrame, Is.EqualTo(TestStartFrame));
        Assert.That(ret.ErrorCount, Is.Zero);
        Assert.That(ret.Bytes[8..20], Is.All.Zero); // devid, direction and ep are zeroed
        Assert.That(ret.Bytes[40..48], Is.All.Zero); // padding

        IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, 2);
        Assert.That(descriptors[0].Offset, Is.EqualTo(0u));
        Assert.That(descriptors[0].Length, Is.EqualTo(4u));
        Assert.That(descriptors[0].ActualLength, Is.EqualTo(4u));
        Assert.That(descriptors[0].Status, Is.Zero);
        Assert.That(descriptors[1].Offset, Is.EqualTo(4u));
        Assert.That(descriptors[1].Length, Is.EqualTo(4u));
        Assert.That(descriptors[1].ActualLength, Is.EqualTo(4u));
        Assert.That(descriptors[1].Status, Is.Zero);

        Assert.That(device.IsoOutPackets, Has.Count.EqualTo(2));
        Assert.That(device.IsoOutPackets[0].EndpointAddress, Is.EqualTo(TestDevices.IsoOutAddress));
        Assert.That(device.IsoOutPackets[0].Data, Is.EqualTo(new byte[]
        {
            0x01, 0x02, 0x03, 0x04
        }));
        Assert.That(device.IsoOutPackets[1].EndpointAddress, Is.EqualTo(TestDevices.IsoOutAddress));
        Assert.That(device.IsoOutPackets[1].Data, Is.EqualTo(new byte[]
        {
            0x05, 0x06, 0x07, 0x08
        }));

        SendBarrier(client, 12); // the exact framing consumption keeps the next URB parseable
    }

    [Test]
    public void IsoOut_OffsetBeyondBuffer_ReturnsInvalidArgumentAndKeepsSession()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        byte[] payload = new byte[8];
        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 4,
                Length = 8
            } // 4 + 8 > transfer_buffer_length 8
        ];
        client.Send(CreateIsoSubmitFrame(20, UsbIpDirection.Out, EndpointNumberOf(TestDevices.IsoOutAddress), 8, packets, payload));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(20u));
        Assert.That(ret.Status, Is.EqualTo(-22)); // -EINVAL
        Assert.That(ret.ActualLength, Is.Zero);
        Assert.That(ret.NumberOfPackets, Is.EqualTo(1u));
        Assert.That(ret.StartFrame, Is.EqualTo(TestStartFrame));
        Assert.That(ret.ErrorCount, Is.Zero); // the packet statuses carry the rejection; error_count stays zero

        IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, 1);
        Assert.That(descriptors[0].Offset, Is.EqualTo(4u)); // the declared descriptor is echoed
        Assert.That(descriptors[0].Length, Is.EqualTo(8u));
        Assert.That(descriptors[0].ActualLength, Is.Zero);
        Assert.That(descriptors[0].Status, Is.EqualTo(-22));

        Assert.That(device.IsoOutPackets, Is.Empty); // a rejected URB never reaches the device
        SendBarrier(client, 21);
    }

    [Test]
    public void IsoOut_PacketLengthBeyondEndpointMax_ReturnsInvalidArgument()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        byte[] payload = new byte[20];
        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = TestDevices.IsoOutPacketSize + 4
            } // 20 > wMaxPacketSize 16
        ];
        client.Send(CreateIsoSubmitFrame(30, UsbIpDirection.Out, EndpointNumberOf(TestDevices.IsoOutAddress), 20, packets, payload));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(30u));
        Assert.That(ret.Status, Is.EqualTo(-22));
        Assert.That(ret.ActualLength, Is.Zero);
        Assert.That(ret.NumberOfPackets, Is.EqualTo(1u));
        Assert.That(ret.ErrorCount, Is.Zero);

        IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, 1);
        Assert.That(descriptors[0].ActualLength, Is.Zero);
        Assert.That(descriptors[0].Status, Is.EqualTo(-22));

        Assert.That(device.IsoOutPackets, Is.Empty);
        SendBarrier(client, 31);
    }

    [Test]
    public void IsoIn_TwoPacketUrb_CompletesOnePacketPerFrame()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        device.EnqueueIsoInPacket([0x11, 0x12, 0x13, 0x14]);
        device.EnqueueIsoInPacket([0x21, 0x22, 0x23, 0x24]);
        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 4
            },
            new IsoPacketDescriptor
            {
                Offset = 4,
                Length = 4
            }
        ];
        client.Send(CreateIsoSubmitFrame(40, UsbIpDirection.In, EndpointNumberOf(TestDevices.IsoInAddress), 8, packets));
        SendBarrier(client, 41); // the ISO IN submit is queued; the barrier reply proves it was parsed

        harness.TimeProvider.Advance(FrameDuration);
        Assert.That(client.TryReadExactly(new byte[UsbIpProtocol.HeaderSize], SilenceProbeMilliseconds), Is.False); // one packet per frame

        harness.TimeProvider.Advance(FrameDuration);
        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.Command, Is.EqualTo(UsbIpProtocol.CommandRetSubmit));
        Assert.That(ret.SeqNum, Is.EqualTo(40u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.EqualTo(8u));
        Assert.That(ret.NumberOfPackets, Is.EqualTo(2u));
        Assert.That(ret.StartFrame, Is.EqualTo(TestStartFrame));
        Assert.That(ret.ErrorCount, Is.Zero);
        Assert.That(client.ReadExactly(8), Is.EqualTo(new byte[]
        {
            0x11, 0x12, 0x13, 0x14, 0x21, 0x22, 0x23, 0x24
        })); // the payload follows the header, then the descriptors

        IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, 2);
        Assert.That(descriptors[0].ActualLength, Is.EqualTo(4u));
        Assert.That(descriptors[0].Status, Is.Zero);
        Assert.That(descriptors[1].ActualLength, Is.EqualTo(4u));
        Assert.That(descriptors[1].Status, Is.Zero);

        SendBarrier(client, 42);
    }

    [Test]
    public void IsoIn_ShortReadsAtPackedOffsets_RetPayloadSpansPacketOffsets()
    {
        IsoIn196Device device = new IsoIn196Device();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        // The real DualSense shape: a 196-byte host packet at offset 0 and a second at offset 196.
        // The device returns only 192 bytes per packet (one 1 ms microphone frame), so bytes
        // 192..195 of the transfer buffer are a zero gap and the second frame stays at 196.
        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = IsoIn196Device.PacketSize
            },
            new IsoPacketDescriptor
            {
                Offset = IsoIn196Device.PacketSize,
                Length = IsoIn196Device.PacketSize
            }
        ];
        client.Send(CreateIsoSubmitFrame(
            250,
            UsbIpDirection.In,
            EndpointNumberOf(TestDevices.IsoInAddress),
            2 * IsoIn196Device.PacketSize,
            packets));
        SendBarrier(client, 251);
        harness.TimeProvider.Advance(2 * FrameDuration);

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(250u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.EqualTo(IsoIn196Device.PayloadLength),
            "The RET payload must span the highest packet end, including the zero gap before a packet at its declared offset.");
        Assert.That(ret.NumberOfPackets, Is.EqualTo(2u));
        Assert.That(ret.ErrorCount, Is.Zero);

        byte[] payload = client.ReadExactly(IsoIn196Device.PayloadLength);
        Assert.That(payload[..192], Is.All.EqualTo(IsoIn196Device.FirstPacketByte));
        Assert.That(payload[192..196], Is.All.Zero, "The gap between a 192-byte short packet and the next declared offset must stay zero.");
        Assert.That(payload[196..], Is.All.EqualTo(IsoIn196Device.SecondPacketByte));

        IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, 2);
        Assert.That(descriptors[0].Offset, Is.EqualTo(0u));
        Assert.That(descriptors[0].Length, Is.EqualTo((uint)IsoIn196Device.PacketSize));
        Assert.That(descriptors[0].ActualLength, Is.EqualTo((uint)IsoIn196Device.PacketSize),
            "The short first packet's echo covers its zero-filled unused tail so the descriptor sum matches the RET header.");
        Assert.That(descriptors[0].Status, Is.Zero);
        Assert.That(descriptors[1].Offset, Is.EqualTo((uint)IsoIn196Device.PacketSize));
        Assert.That(descriptors[1].Length, Is.EqualTo((uint)IsoIn196Device.PacketSize));
        Assert.That(descriptors[1].ActualLength, Is.EqualTo(192u),
            "The last producing packet keeps its real length; no data is invented beyond the provider's last byte.");
        Assert.That(descriptors[1].Status, Is.Zero);
        Assert.That((long)descriptors[0].ActualLength + descriptors[1].ActualLength, Is.EqualTo((long)ret.ActualLength));

        SendBarrier(client, 252); // the exact payload length keeps the next URB parseable
    }

    [Test]
    public void IsoIn_ManyQueuedUrbs_CompleteInSubmissionOrderWithoutCap()
    {
        const int UrbCount = 32;
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        for (int i = 0; i < UrbCount; i++)
        {
            device.EnqueueIsoInPacket(new byte[]
            {
                (byte)i, (byte)(i + 1), (byte)(i + 2), (byte)(i + 3)
            });
            IsoPacketDescriptor[] packets =
            [
                new IsoPacketDescriptor
                {
                    Offset = 0,
                    Length = 4
                }
            ];
            client.Send(CreateIsoSubmitFrame(200 + (uint)i, UsbIpDirection.In, EndpointNumberOf(TestDevices.IsoInAddress), 4, packets));
            SendBarrier(client, 400 + (uint)i);
        }

        harness.TimeProvider.Advance(UrbCount * FrameDuration);

        for (int i = 0; i < UrbCount; i++)
        {
            RetFrame ret = ReadRetFrame(client);
            Assert.That(ret.SeqNum, Is.EqualTo(200u + (uint)i));
            Assert.That(ret.Status, Is.Zero);
            Assert.That(ret.ActualLength, Is.EqualTo(4u));
            Assert.That(ret.StartFrame, Is.EqualTo(TestStartFrame));
            Assert.That(ret.NumberOfPackets, Is.EqualTo(1u));
            Assert.That(ret.ErrorCount, Is.Zero);
            Assert.That(client.ReadExactly(4), Is.EqualTo(new byte[]
            {
                (byte)i, (byte)(i + 1), (byte)(i + 2), (byte)(i + 3)
            }));

            IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, 1);
            Assert.That(descriptors[0].ActualLength, Is.EqualTo(4u));
            Assert.That(descriptors[0].Status, Is.Zero);
        }

        Assert.That(harness.TimeProvider.TimerCount, Is.EqualTo(2)); // one ISO scheduler per direction
        harness.TimeProvider.Advance(5 * FrameDuration);
        Assert.That(client.TryReadExactly(new byte[UsbIpProtocol.HeaderSize], SilenceProbeMilliseconds), Is.False);
        SendBarrier(client, 500);
    }

    [Test]
    public void IsoIn_IdleFramesBeforeSubmit_NextUrbStartsOnArrivalFrameWithoutCatchUp()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        harness.TimeProvider.Advance(25 * FrameDuration);
        Assert.That(harness.TimeProvider.TimerCount, Is.EqualTo(2)); // one ISO scheduler per direction
        Assert.That(client.TryReadExactly(new byte[UsbIpProtocol.HeaderSize], SilenceProbeMilliseconds), Is.False); // no URB, no work

        device.EnqueueIsoInPacket([0x31, 0x32, 0x33, 0x34]);
        device.EnqueueIsoInPacket([0x41, 0x42, 0x43, 0x44]);
        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 4
            },
            new IsoPacketDescriptor
            {
                Offset = 4,
                Length = 4
            }
        ];
        client.Send(CreateIsoSubmitFrame(210, UsbIpDirection.In, EndpointNumberOf(TestDevices.IsoInAddress), 8, packets));
        SendBarrier(client, 211);

        harness.TimeProvider.Advance(FrameDuration);
        Assert.That(client.TryReadExactly(new byte[UsbIpProtocol.HeaderSize], SilenceProbeMilliseconds), Is.False); // first packet only

        harness.TimeProvider.Advance(FrameDuration);
        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(210u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.EqualTo(8u));
        Assert.That(ret.ErrorCount, Is.Zero);
        Assert.That(client.ReadExactly(8), Is.EqualTo(new byte[]
        {
            0x31, 0x32, 0x33, 0x34, 0x41, 0x42, 0x43, 0x44
        }));
        ReadIsoDescriptors(client, 2);

        harness.TimeProvider.Advance(10 * FrameDuration);
        Assert.That(client.TryReadExactly(new byte[UsbIpProtocol.HeaderSize], SilenceProbeMilliseconds), Is.False); // no catch-up burst
        Assert.That(harness.TimeProvider.TimerCount, Is.EqualTo(2)); // one ISO scheduler per direction
        SendBarrier(client, 212);
    }

    [Test]
    public void IsoIn_ProviderReturnsZero_CompletesWithActualZero()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        // No queued packet: ProvideIsoPacket reports zero bytes for both packets.
        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 4
            },
            new IsoPacketDescriptor
            {
                Offset = 4,
                Length = 4
            }
        ];
        client.Send(CreateIsoSubmitFrame(50, UsbIpDirection.In, EndpointNumberOf(TestDevices.IsoInAddress), 8, packets));
        SendBarrier(client, 51);

        harness.TimeProvider.Advance(2 * FrameDuration);

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(50u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.Zero);
        Assert.That(ret.NumberOfPackets, Is.EqualTo(2u));
        Assert.That(ret.ErrorCount, Is.Zero);

        IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, 2); // no payload precedes them
        Assert.That(descriptors[0].ActualLength, Is.Zero);
        Assert.That(descriptors[0].Status, Is.Zero);
        Assert.That(descriptors[1].ActualLength, Is.Zero);
        Assert.That(descriptors[1].Status, Is.Zero);
    }

    [TestCase(0u)]
    [TestCase(1u)]
    [TestCase(0x00FFFFFFu)]
    [TestCase(uint.MaxValue)]
    public void IsoIn_StartFrameAnyValue_CompletesOnArrivalFrameAndEchoesSubmittedValue(uint startFrame)
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        device.EnqueueIsoInPacket([0x51, 0x52, 0x53, 0x54]);
        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 4
            }
        ];
        client.Send(CreateIsoSubmitFrame(
            220,
            UsbIpDirection.In,
            EndpointNumberOf(TestDevices.IsoInAddress),
            4,
            packets,
            startFrame: startFrame));
        SendBarrier(client, 221);

        harness.TimeProvider.Advance(FrameDuration);

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(220u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.EqualTo(4u));
        Assert.That(ret.StartFrame, Is.EqualTo(startFrame)); // the RET echoes the submitted header value
        Assert.That(ret.ErrorCount, Is.Zero);
        Assert.That(client.ReadExactly(4), Is.EqualTo(new byte[]
        {
            0x51, 0x52, 0x53, 0x54
        }));
        ReadIsoDescriptors(client, 1);

        harness.TimeProvider.Advance(5 * FrameDuration);
        Assert.That(client.TryReadExactly(new byte[UsbIpProtocol.HeaderSize], SilenceProbeMilliseconds), Is.False);
        SendBarrier(client, 222);
    }

    [Test]
    public void IsoIn_Unlink_ReturnsEconnResetAndNoSubmitReply()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 4
            },
            new IsoPacketDescriptor
            {
                Offset = 4,
                Length = 4
            }
        ];
        client.Send(CreateIsoSubmitFrame(60, UsbIpDirection.In, EndpointNumberOf(TestDevices.IsoInAddress), 8, packets));
        SendBarrier(client, 61);
        client.Send(CreateUnlinkFrame(62, 60));

        RetFrame unlink = ReadRetFrame(client);
        Assert.That(unlink.Command, Is.EqualTo(UsbIpProtocol.CommandRetUnlink));
        Assert.That(unlink.SeqNum, Is.EqualTo(62u));
        Assert.That(unlink.Status, Is.EqualTo(-104)); // -ECONNRESET: the pending ISO transfer was cancelled

        harness.TimeProvider.Advance(3 * FrameDuration);
        Assert.That(client.TryReadExactly(new byte[UsbIpProtocol.HeaderSize], SilenceProbeMilliseconds),
            Is.False); // a cancelled transfer never sends RET_SUBMIT

        SendBarrier(client, 63);
    }

    [Test]
    public void IsoSubmit_AtAltZero_TargetsNoEndpoint()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        SetInterface(client, 70, 1);
        SetInterface(client, 71, 0); // the endpoints live only in alternate setting 1

        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 4
            }
        ];
        client.Send(CreateIsoSubmitFrame(72, UsbIpDirection.In, EndpointNumberOf(TestDevices.IsoInAddress), 4, packets));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(72u));
        Assert.That(ret.Status, Is.EqualTo(-2)); // ENOENT: no active alternate setting declares the endpoint
        Assert.That(ret.ActualLength, Is.Zero);
        Assert.That(ret.NumberOfPackets, Is.EqualTo(1u));
        Assert.That(ret.ErrorCount, Is.Zero);

        IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, 1);
        Assert.That(descriptors[0].ActualLength, Is.Zero);
        Assert.That(descriptors[0].Status, Is.EqualTo(-2));

        SendBarrier(client, 73);
    }

    [Test]
    public void IsoOut_UnknownEndpoint_ConsumesPayloadAndTailAndRepliesNoEntry()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(); // simple device: no OUT endpoint with number 1
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        byte[] payload = [0x10, 0x20, 0x30, 0x40];
        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 4
            }
        ];
        client.Send(CreateIsoSubmitFrame(80, UsbIpDirection.Out, 1, 4, packets, payload));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(80u));
        Assert.That(ret.Status, Is.EqualTo(-2)); // ENOENT
        Assert.That(ret.ActualLength, Is.Zero);
        Assert.That(ret.NumberOfPackets, Is.EqualTo(1u));
        Assert.That(ret.ErrorCount, Is.Zero);

        IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, 1);
        Assert.That(descriptors[0].ActualLength, Is.Zero);
        Assert.That(descriptors[0].Status, Is.EqualTo(-2));

        Assert.That(device.OutputReports, Is.Empty); // the payload and the tail were discarded, never delivered
        SendBarrier(client, 81); // both were consumed exactly: the next URB parses
    }

    [Test]
    public void IsoSubmit_NonIsoEndpoint_ReturnsInvalidArgumentAndConsumesTail()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(); // simple device: 0x81 is an interrupt IN endpoint
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId);

        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 4
            }
        ];
        client.Send(CreateIsoSubmitFrame(90, UsbIpDirection.In, EndpointNumberOf(TestDevices.SimpleInterruptInAddress), 4, packets));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(90u));
        Assert.That(ret.Status, Is.EqualTo(-22)); // -EINVAL: the endpoint is not isochronous
        Assert.That(ret.ActualLength, Is.Zero);
        Assert.That(ret.ErrorCount, Is.Zero);

        IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, 1);
        Assert.That(descriptors[0].ActualLength, Is.Zero);
        Assert.That(descriptors[0].Status, Is.EqualTo(-22));

        harness.TimeProvider.Advance(3 * TimeSpan.FromMilliseconds(4)); // the interrupt pacer must not answer the rejected URB
        Assert.That(client.TryReadExactly(new byte[UsbIpProtocol.HeaderSize], SilenceProbeMilliseconds), Is.False);

        SendBarrier(client, 91);
    }

    [Test]
    public void IsoOut_DeviceThrows_ReturnsInvalidArgumentAndKeepsSession()
    {
        ThrowingIsoDevice device = new ThrowingIsoDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        byte[] payload = [0x01, 0x02, 0x03, 0x04];
        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 4
            }
        ];
        client.Send(CreateIsoSubmitFrame(110, UsbIpDirection.Out, EndpointNumberOf(TestDevices.IsoOutAddress), 4, packets, payload));
        SendBarrier(client, 111); // the OUT URB is queued; the barrier reply proves it was parsed

        harness.TimeProvider.Advance(FrameDuration);

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(110u));
        Assert.That(ret.Status, Is.EqualTo(-22)); // the persona fault is contained as a clean URB rejection
        Assert.That(ret.ActualLength, Is.Zero);
        Assert.That(ret.ErrorCount, Is.Zero); // the descriptor status carries the error

        IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, 1);
        Assert.That(descriptors[0].ActualLength, Is.Zero);
        Assert.That(descriptors[0].Status, Is.EqualTo(-22));
        Assert.That(harness.Logger.ErrorEntries, Has.Count.EqualTo(1));
        Assert.That(harness.Logger.ErrorEntries[0].Message, Does.Contain("persona iso out failure"));

        SendBarrier(client, 112); // the session survives the persona fault
    }

    [Test]
    public void IsoIn_ProviderThrows_CompletesWithStalledPackets()
    {
        ThrowingIsoDevice device = new ThrowingIsoDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 4
            },
            new IsoPacketDescriptor
            {
                Offset = 4,
                Length = 4
            }
        ];
        client.Send(CreateIsoSubmitFrame(120, UsbIpDirection.In, EndpointNumberOf(TestDevices.IsoInAddress), 8, packets));
        SendBarrier(client, 121);

        harness.TimeProvider.Advance(2 * FrameDuration);

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(120u));
        Assert.That(ret.Status, Is.Zero); // the transfer still completes; the packets carry the stalls
        Assert.That(ret.ActualLength, Is.Zero);
        Assert.That(ret.NumberOfPackets, Is.EqualTo(2u));
        Assert.That(ret.ErrorCount, Is.EqualTo(2u));

        IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, 2);
        Assert.That(descriptors[0].ActualLength, Is.Zero);
        Assert.That(descriptors[0].Status, Is.EqualTo(-32)); // STALL per failed packet
        Assert.That(descriptors[1].ActualLength, Is.Zero);
        Assert.That(descriptors[1].Status, Is.EqualTo(-32));
        Assert.That(harness.Logger.ErrorEntries, Has.Count.EqualTo(2));
    }

    [Test]
    public void IsoIn_OnePacketFails_CountsOnlyTheFailedPacket()
    {
        FlakyIsoDevice device = new FlakyIsoDevice();
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 4
            },
            new IsoPacketDescriptor
            {
                Offset = 4,
                Length = 4
            }
        ];
        client.Send(CreateIsoSubmitFrame(230, UsbIpDirection.In, EndpointNumberOf(TestDevices.IsoInAddress), 8, packets));
        SendBarrier(client, 231);

        harness.TimeProvider.Advance(2 * FrameDuration);

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(230u));
        Assert.That(ret.Status, Is.Zero); // packet faults do not fail the URB
        Assert.That(ret.ActualLength, Is.EqualTo(4u));
        Assert.That(ret.ErrorCount, Is.EqualTo(1u)); // only the failed packet is counted
        Assert.That(client.ReadExactly(4), Is.EqualTo(new byte[]
        {
            0x51, 0x51, 0x51, 0x51
        }));

        IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, 2);
        Assert.That(descriptors[0].ActualLength, Is.EqualTo(4u));
        Assert.That(descriptors[0].Status, Is.Zero);
        Assert.That(descriptors[1].ActualLength, Is.Zero);
        Assert.That(descriptors[1].Status, Is.EqualTo(-32)); // STALL on the failed packet

        SendBarrier(client, 232);
    }

    [Test]
    public void IsoSubmit_CountAboveMax_ReturnsInvalidArgumentAndKeepsSession()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        IsoPacketDescriptor[] packets = new IsoPacketDescriptor[UsbIpServer.MaxIsoPacketsPerUrb + 1];
        for (int i = 0; i < packets.Length; i++)
        {
            packets[i] = new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 0
            };
        }

        client.Send(CreateIsoSubmitFrame(100, UsbIpDirection.In, EndpointNumberOf(TestDevices.IsoInAddress), 0, packets));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(100u));
        Assert.That(ret.Status, Is.EqualTo(-22));
        Assert.That(ret.ActualLength, Is.Zero);
        Assert.That(ret.NumberOfPackets, Is.EqualTo((uint)packets.Length)); // every declared descriptor is echoed
        Assert.That(ret.ErrorCount, Is.Zero);

        IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, packets.Length);
        Assert.That(descriptors[0].Status, Is.EqualTo(-22));
        Assert.That(descriptors[^1].Status, Is.EqualTo(-22));

        SendBarrier(client, 101); // the oversized tail was consumed exactly
    }

    [Test]
    public void IsoIn_MaximumPacketUrb_CompletesEveryPacket()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        IsoPacketDescriptor[] packets = new IsoPacketDescriptor[UsbIpServer.MaxIsoPacketsPerUrb];
        for (int i = 0; i < packets.Length; i++)
        {
            packets[i] = new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 0
            };
        }

        client.Send(CreateIsoSubmitFrame(240, UsbIpDirection.In, EndpointNumberOf(TestDevices.IsoInAddress), 0, packets));
        SendBarrier(client, 241);

        harness.TimeProvider.Advance(UsbIpServer.MaxIsoPacketsPerUrb * FrameDuration);

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(240u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.Zero);
        Assert.That(ret.NumberOfPackets, Is.EqualTo((uint)UsbIpServer.MaxIsoPacketsPerUrb));
        Assert.That(ret.ErrorCount, Is.Zero);

        IsoPacketDescriptor[] descriptors = ReadIsoDescriptors(client, packets.Length);
        for (int i = 0; i < descriptors.Length; i++)
        {
            Assert.That(descriptors[i].Status, Is.Zero);
            Assert.That(descriptors[i].ActualLength, Is.Zero);
        }

        SendBarrier(client, 242);
    }

    [Test]
    public void IsoSubmit_NumberPacketsMaxValue_UsesImmediatePath()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        device.InputReport = [0x0A, 0x0B, 0x0C];
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        // 0xFFFFFFFF is the non-ISO sentinel: the URB takes the immediate path (no descriptor tail
        // on the wire) and is answered without time advancing.
        UsbIpHeader submit = UsbIpHeader.CreateSubmit(
            110,
            TestDeviceId,
            UsbIpDirection.In,
            EndpointNumberOf(TestDevices.IsoInAddress),
            8,
            startFrame: TestStartFrame,
            numberOfPackets: uint.MaxValue,
            interval: TestInterval);
        client.Send(CreateUrbFrame(submit));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(110u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.EqualTo(3u));
        Assert.That(ret.NumberOfPackets, Is.EqualTo(uint.MaxValue)); // non-ISO replies stay pinned
        Assert.That(client.ReadExactly(3), Is.EqualTo(new byte[]
        {
            0x0A, 0x0B, 0x0C
        }));

        SendBarrier(client, 111);
    }

    [Test]
    public void IsoRet_NonIsoPathStillWritesMaxValuePackets()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportDevice(client, TestBusId); // alternate setting 0: no endpoints are active

        client.Send(CreateControlSubmitFrame(120, CreateGetDeviceDescriptorSetup(), UsbIpDirection.In, 18));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(120u));
        Assert.That(ret.Status, Is.Zero);
        Assert.That(ret.ActualLength, Is.EqualTo(18u));
        Assert.That(ret.NumberOfPackets, Is.EqualTo(uint.MaxValue)); // regression: non-ISO RET keeps 0xFFFFFFFF
        Assert.That(ret.Bytes[28..32], Is.All.Zero); // start_frame stays zeroed for non-ISO replies
        Assert.That(client.ReadExactly(18), Is.EqualTo(TestDevices.CreateSimpleDeviceDescriptor()));
    }

    [Test]
    public void Teardown_WithPendingIso_StopsTimersAndSendsNothing()
    {
        TestVirtualUsbDevice device = new TestVirtualUsbDevice(TestDevices.CreateIsoDevice());
        using ServerHarness harness = new ServerHarness(device);
        using UsbIpClient client = harness.Connect();
        ImportIsoDevice(client);

        IsoPacketDescriptor[] packets =
        [
            new IsoPacketDescriptor
            {
                Offset = 0,
                Length = 4
            },
            new IsoPacketDescriptor
            {
                Offset = 4,
                Length = 4
            }
        ];
        client.Send(CreateIsoSubmitFrame(130, UsbIpDirection.In, EndpointNumberOf(TestDevices.IsoInAddress), 8, packets));
        SendBarrier(client, 131);
        Assert.That(harness.TimeProvider.TimerCount, Is.EqualTo(2)); // one ISO scheduler for 0x01 and one for 0x82

        harness.Server.Stop(); // tears the session down while the ISO transfer is pending

        Assert.That(client.WaitForRemoteClose(CommunicationTimeoutMilliseconds), Is.True); // no RET was written before the close
        Assert.That(harness.TimeProvider.TimerCount, Is.Zero); // both schedulers stopped without leaking a timer
        Assert.That(harness.Logger.ErrorEntries, Is.Empty); // a completion after teardown is not an error
        Assert.That(() => harness.TimeProvider.Advance(3 * FrameDuration), Throws.Nothing); // and no completion fires afterwards
    }

    /// <summary>Creates a USBIP_CMD_UNLINK frame.</summary>
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

    /// <summary>Sends an OP_REQ_IMPORT naming <paramref name="busId"/>.</summary>
    /// <param name="busId">Bus id of the device to import.</param>
    /// <returns>The 40-byte request frame.</returns>
    private static byte[] CreateImportRequest(string busId)
    {
        byte[] frame = new byte[UsbIpProtocol.OpCommonSize + UsbIpHandshake.BusIdSize];
        UsbIpHandshake.WriteOpCommon(frame, UsbIpProtocol.OpRequestImport, 0);
        Encoding.ASCII.GetBytes(busId).CopyTo(frame, UsbIpProtocol.OpCommonSize);
        return frame;
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
        byte[]? payload = null)
    {
        UsbIpHeader header = UsbIpHeader.CreateSubmit(seqNum, TestDeviceId, direction, 0, transferBufferLength, setup: setup);
        return CreateUrbFrame(header, payload);
    }

    /// <summary>
    /// Creates an isochronous submit frame: the header, then the payload of an OUT transfer, then
    /// the packet descriptors of the ISO framing (no inter-packet padding).
    /// </summary>
    /// <param name="seqNum">Sequence number of the URB.</param>
    /// <param name="direction">Transfer direction.</param>
    /// <param name="endpointNumber">Bare endpoint number the wire ep field carries.</param>
    /// <param name="transferBufferLength">Declared transfer buffer length.</param>
    /// <param name="packets">Packet descriptors of the URB.</param>
    /// <param name="payload">Payload of an OUT transfer; none for IN transfers.</param>
    /// <param name="startFrame">Initial frame field of the submit header.</param>
    /// <returns>The submit frame.</returns>
    private static byte[] CreateIsoSubmitFrame(uint seqNum, UsbIpDirection direction, byte endpointNumber, uint transferBufferLength,
        IsoPacketDescriptor[] packets, byte[]? payload = null, uint startFrame = TestStartFrame)
    {
        UsbIpHeader header = UsbIpHeader.CreateSubmit(
            seqNum,
            TestDeviceId,
            direction,
            endpointNumber,
            transferBufferLength,
            startFrame: startFrame,
            numberOfPackets: (uint)packets.Length,
            interval: TestInterval);
        byte[] frame = new byte[UsbIpProtocol.HeaderSize + (payload?.Length ?? 0) + packets.Length * IsoPacketDescriptor.WireSize];
        header.WriteSubmit(frame);
        int offset = UsbIpProtocol.HeaderSize;
        if (payload is not null)
        {
            payload.CopyTo(frame, offset);
            offset += payload.Length;
        }

        foreach (IsoPacketDescriptor packet in packets)
        {
            packet.Write(frame.AsSpan(offset));
            offset += IsoPacketDescriptor.WireSize;
        }

        return frame;
    }

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

    /// <summary>Imports the isochronous device and settles its alternate setting 1.</summary>
    /// <param name="client">Connected test client.</param>
    private static void ImportIsoDevice(UsbIpClient client)
    {
        ImportDevice(client, TestBusId);
        SetInterface(client, 1, TestDevices.IsoAlternateSetting);
    }

    /// <summary>
    /// Sends a control SET_INTERFACE URB that completes immediately and waits for its reply, so the
    /// requested alternate setting is settled before any ISO URB follows.
    /// </summary>
    /// <param name="client">Connected test client.</param>
    /// <param name="seqNum">Sequence number of the URB.</param>
    /// <param name="alternateSetting">Alternate setting to activate.</param>
    private static void SetInterface(UsbIpClient client, uint seqNum, byte alternateSetting)
    {
        UsbSetupPacket setup = new UsbSetupPacket
        {
            RequestType = 0x01, // host-to-device, standard, recipient interface
            Request = 0x0B, // SET_INTERFACE
            Value = alternateSetting,
            Index = TestDevices.IsoInterfaceNumber,
            Length = 0
        };
        client.Send(CreateControlSubmitFrame(seqNum, setup, UsbIpDirection.Out, 0));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(seqNum));
        Assert.That(ret.Status, Is.Zero);
    }

    /// <summary>
    /// Sends a control GET_CONFIGURATION URB that completes immediately and waits for its reply;
    /// because a session handles frames strictly in order, the reply proves that every earlier
    /// frame was processed (used before advancing the manual clock at a queued ISO URB).
    /// </summary>
    /// <param name="client">Connected test client.</param>
    /// <param name="seqNum">Sequence number of the barrier URB.</param>
    private static void SendBarrier(UsbIpClient client, uint seqNum)
    {
        UsbSetupPacket setup = new UsbSetupPacket
        {
            RequestType = 0x80, // device-to-host, standard, recipient device
            Request = 0x08, // GET_CONFIGURATION
            Value = 0,
            Index = 0,
            Length = 1
        };
        client.Send(CreateControlSubmitFrame(seqNum, setup, UsbIpDirection.In, 1));

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(seqNum));
        Assert.That(ret.Status, Is.Zero);
        client.ReadExactly((int)ret.ActualLength);
    }

    /// <summary>Reads one 48-byte return header and parses its fields.</summary>
    /// <param name="client">Connected test client.</param>
    /// <returns>The parsed return header.</returns>
    private static RetFrame ReadRetFrame(UsbIpClient client) => new RetFrame(client.ReadExactly(UsbIpProtocol.HeaderSize));

    /// <summary>Reads <paramref name="count"/> isochronous packet descriptors that follow a return header.</summary>
    /// <param name="client">Connected test client.</param>
    /// <param name="count">Number of descriptors to read.</param>
    /// <returns>The parsed descriptors in wire order.</returns>
    private static IsoPacketDescriptor[] ReadIsoDescriptors(UsbIpClient client, int count)
    {
        byte[] bytes = client.ReadExactly(count * IsoPacketDescriptor.WireSize);
        IsoPacketDescriptor[] descriptors = new IsoPacketDescriptor[count];
        for (int i = 0; i < count; i++)
        {
            descriptors[i] = IsoPacketDescriptor.Read(bytes.AsSpan(i * IsoPacketDescriptor.WireSize, IsoPacketDescriptor.WireSize));
        }

        return descriptors;
    }

    /// <summary>
    /// Returns the bare endpoint number the wire ep field carries for a descriptor endpoint
    /// address: the protocol carries the number in ep and the direction in the separate direction
    /// field.
    /// </summary>
    /// <param name="endpointAddress">Endpoint address of a descriptor (bEndpointAddress).</param>
    /// <returns>The endpoint number (bits 3..0).</returns>
    private static byte EndpointNumberOf(byte endpointAddress) => (byte)(endpointAddress & 0x0F);

    /// <summary>
    /// Device with the real 196-byte DualSense microphone endpoint shape (ISO IN 0x82, max packet
    /// 196) whose producer returns one 192-byte microphone frame per packet, leaving the last four
    /// bytes of every packet untouched. Pins that the completion payload preserves the declared
    /// packet offsets instead of compacting the short packets together.
    /// </summary>
    private sealed class IsoIn196Device : VirtualUsbDevice
    {
        /// <summary>Declared packet length of the endpoint (196-byte host packets).</summary>
        public const int PacketSize = 196;

        /// <summary>Bytes one 1 ms microphone frame carries (192).</summary>
        public const int FrameSize = 192;

        /// <summary>Payload the completion must carry: the second packet ends at 196 + 192.</summary>
        public const int PayloadLength = PacketSize + FrameSize;

        /// <summary>Fill byte of the first packet's microphone frame.</summary>
        public const byte FirstPacketByte = 0x61;

        /// <summary>Fill byte of the second packet's microphone frame.</summary>
        public const byte SecondPacketByte = 0x62;

        private int _packetIndex;

        /// <summary>Initializes the device with a 196-byte isochronous IN endpoint in alternate setting 1.</summary>
        public IsoIn196Device()
            : base(CreateDescriptorSet())
        {
        }

        /// <inheritdoc/>
        public override int ProvideIsoPacket(byte endpointAddress, Span<byte> destination)
        {
            if (endpointAddress != TestDevices.IsoInAddress)
            {
                return 0;
            }

            byte fill = _packetIndex++ == 0 ? FirstPacketByte : SecondPacketByte;
            destination[..FrameSize].Fill(fill);
            return FrameSize;
        }

        /// <summary>Builds the descriptor set: interface 0 alternate 0 empty, alternate 1 with ISO IN 0x82 max 196.</summary>
        /// <returns>The descriptor set of the device.</returns>
        private static UsbDescriptorSet CreateDescriptorSet()
        {
            const int totalLength = 9 + 9 + 9 + 7;
            byte[] configuration =
            [
                0x09, // bLength: 9
                0x02, // bDescriptorType: CONFIGURATION
                (byte)totalLength, 0x00, // wTotalLength: 34
                0x01, // bNumInterfaces: 1
                TestDevices.SimpleConfigurationValue, // bConfigurationValue: 1
                0x00, // iConfiguration: none
                0x80, // bmAttributes: bus powered
                0x32, // bMaxPower: 100 mA
                0x09, // bLength: 9
                0x04, // bDescriptorType: INTERFACE
                TestDevices.IsoInterfaceNumber, // bInterfaceNumber: 0
                0x00, // bAlternateSetting: 0
                0x00, // bNumEndpoints: 0
                TestDevices.IsoInterfaceClass, // bInterfaceClass: vendor specific
                0x00, // bInterfaceSubClass: none
                0x00, // bInterfaceProtocol: none
                0x00, // iInterface: none
                0x09, // bLength: 9
                0x04, // bDescriptorType: INTERFACE
                TestDevices.IsoInterfaceNumber, // bInterfaceNumber: 0
                TestDevices.IsoAlternateSetting, // bAlternateSetting: 1
                0x01, // bNumEndpoints: 1
                TestDevices.IsoInterfaceClass, // bInterfaceClass: vendor specific
                0x00, // bInterfaceSubClass: none
                0x00, // bInterfaceProtocol: none
                0x00, // iInterface: none
                0x07, // bLength: 7
                0x05, // bDescriptorType: ENDPOINT
                TestDevices.IsoInAddress, // bEndpointAddress: isochronous IN, endpoint 2
                0x05, // bmAttributes: isochronous asynchronous
                (byte)PacketSize, (byte)(PacketSize >> 8), // wMaxPacketSize: 196
                TestDevices.IsoInterval // bInterval: every frame
            ];

            return UsbDescriptorSet.Create(TestDevices.CreateSimpleDeviceDescriptor(), configuration, null, []);
        }
    }

    /// <summary>
    /// Device whose isochronous hooks always throw; drives the server's containment paths, which
    /// answer the OUT URB with -EINVAL and stall the IN packets instead of dropping the transfer.
    /// </summary>
    private sealed class ThrowingIsoDevice : VirtualUsbDevice
    {
        /// <summary>Initializes the device with the isochronous test device's descriptor set.</summary>
        public ThrowingIsoDevice()
            : base(TestDevices.CreateIsoDevice())
        {
        }

        /// <inheritdoc/>
        public override void OnIsoPacketReceived(byte endpointAddress, ReadOnlySpan<byte> packet) =>
            throw new InvalidOperationException("persona iso out failure");

        /// <inheritdoc/>
        public override int ProvideIsoPacket(byte endpointAddress, Span<byte> destination) =>
            throw new InvalidOperationException("persona iso in failure");
    }

    /// <summary>
    /// Device whose second isochronous IN packet throws and whose other packets succeed; pins that
    /// only the failed packet is marked and counted.
    /// </summary>
    private sealed class FlakyIsoDevice : VirtualUsbDevice
    {
        private int _providedPackets;

        /// <summary>Initializes the device with the isochronous test device's descriptor set.</summary>
        public FlakyIsoDevice()
            : base(TestDevices.CreateIsoDevice())
        {
        }

        /// <inheritdoc/>
        public override int ProvideIsoPacket(byte endpointAddress, Span<byte> destination)
        {
            _providedPackets++;
            if (_providedPackets == 2)
            {
                throw new InvalidOperationException("persona iso in failure on the second packet");
            }

            int count = Math.Min(4, destination.Length);
            destination[..count].Fill(0x51);
            return count;
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

        /// <summary>Reads exactly <paramref name="buffer"/>.Length bytes, failing on timeout or close.</summary>
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
                return false; // receive timeout or reset: the session is still open
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
    /// logger, registering one device under <see cref="TestBusId"/>.
    /// </summary>
    private sealed class ServerHarness : IDisposable
    {
        /// <summary>Starts the server and registers <paramref name="device"/>.</summary>
        /// <param name="device">Device to register.</param>
        public ServerHarness(VirtualUsbDevice device)
        {
            TimeProvider = new ManualTimeProvider();
            Logger = new RecordingLogger();
            Server = new UsbIpServer(new UsbIpServerOptions
            {
                ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
                TimeProvider = TimeProvider,
                Logger = Logger,
                InterruptInterval = TimeSpan.FromMilliseconds(4)
            });
            Server.Start();
            Server.AddDevice(TestBusId, device);
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