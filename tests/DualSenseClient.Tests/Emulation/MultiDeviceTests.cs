using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.DualSense;
using DualSenseClient.Emulation.Devices.DualShock4;
using DualSenseClient.Emulation.Devices.Xbox360;
using DualSenseClient.Emulation.UsbIp;
using DualSenseClient.Emulation.Windows;

namespace DualSenseClient.Tests.Emulation;

/// <summary>
/// Loopback tests of the <see cref="EmulationServer"/> facade serving several personas at once: the
/// OP_REP_DEVLIST handshake lists every registered persona with its own ids, concurrent sessions
/// import different bus ids and each is served its own device's reports, and removing one device
/// leaves the sessions of the others unaffected while freeing its bus id for reuse. The server binds
/// loopback port 0, every session runs through the test's own raw USB/IP client, the pacers are
/// driven by <see cref="ManualTimeProvider"/> and the attach strategy is a no-op, so no traffic
/// leaves the machine and no usbip.exe run happens. The USB/IP test client and harness are
/// deliberately self-contained copies of the pattern in
/// <c>UsbIpServerTests</c> instead of a promotion into TestSupport, because the server tests' nested
/// helpers are tuned to their own fixture and their file must stay untouched.
/// </summary>
[TestFixture]
public sealed class MultiDeviceTests
{
    /// <summary>Tick interval of the injected pacer schedule.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(4);

    /// <summary>Receive timeout of the exact-length frame reads.</summary>
    private const int CommunicationTimeoutMilliseconds = 5000;

    /// <summary>Endpoint number (bare wire ep field) of the HID personas' interrupt IN endpoint (0x84).</summary>
    private const byte HidInterruptInEndpoint = 4;

    /// <summary>Endpoint number (bare wire ep field) of the Xbox 360 persona's interrupt IN endpoint (0x81).</summary>
    private const byte Xbox360InterruptInEndpoint = 1;

    /// <summary>Declared transfer buffer length of the interrupt IN URBs the tests submit.</summary>
    private const int InterruptTransferLength = 64;

    /// <summary>Size of the HID personas' raw input report (64 bytes with the report id first).</summary>
    private const int RawReportSize = 64;

    /// <summary>Report id of the HID personas' main input report.</summary>
    private const byte InputReportId = 0x01;

    [Test]
    public void DevList_AllPersonaVariants_ListsBusIdsVendorsAndProductIds()
    {
        using FacadeHarness harness = new FacadeHarness();
        harness.Server.AddDualShock4();
        harness.Server.AddDualShock4(DualShock4Variant.V1);
        harness.Server.AddDualSense();
        harness.Server.AddDualSense(true);
        harness.Server.AddXbox360();

        using LoopbackClient client = harness.Connect();
        client.Send(CreateDevListRequest());
        int entrySize = UsbIpDeviceInfo.DevListEntrySize(1);
        byte[] reply = client.ReadExactly(UsbIpProtocol.OpCommonSize + 4 + 5 * entrySize);

        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(8)), Is.EqualTo(5u)); // number of exported devices
        AssertDevListEntry(reply, 0, entrySize, "1-1", 0x054C, 0x09CC); // DualShock 4 v2
        AssertDevListEntry(reply, 1, entrySize, "2-1", 0x054C, 0x05C4); // DualShock 4 v1
        AssertDevListEntry(reply, 2, entrySize, "3-1", 0x054C, 0x0CE6); // DualSense
        AssertDevListEntry(reply, 3, entrySize, "4-1", 0x054C, 0x0DF2); // DualSense Edge
        AssertDevListEntry(reply, 4, entrySize, "5-1", 0x045E, 0x028E); // Xbox 360 wired controller
    }

    [Test]
    public void FourConcurrentSessions_EachServesItsOwnDeviceReports()
    {
        using FacadeHarness harness = new FacadeHarness();
        DualShock4Device dualShock4 = harness.Server.AddDualShock4();
        DualSenseDevice dualSense = harness.Server.AddDualSense();
        DualSenseDevice edge = harness.Server.AddDualSense(true);
        Xbox360Device xbox360 = harness.Server.AddXbox360();

        byte[] dualShock4Report = CreateRawReport(0xD4);
        byte[] dualSenseReport = CreateRawReport(0xD5);
        byte[] edgeReport = CreateRawReport(0xDE);
        dualShock4.SetRawInputReport(dualShock4Report);
        dualSense.SetRawInputReport(dualSenseReport);
        edge.SetRawInputReport(edgeReport);
        xbox360.SetInput(new Xbox360InputState
        {
            A = true,
            LeftTrigger = 0x11,
            RightTrigger = 0x22,
            LeftStickX = 0x1234
        });
        byte[] expectedXbox360Packet = new byte[Xbox360InputReport.Size];
        Assert.That(xbox360.ProvideInputReport(expectedXbox360Packet), Is.EqualTo(Xbox360InputReport.Size));

        using LoopbackClient dualShock4Client = harness.Connect();
        using LoopbackClient dualSenseClient = harness.Connect();
        using LoopbackClient edgeClient = harness.Connect();
        using LoopbackClient xbox360Client = harness.Connect();
        dualShock4Client.Import("1-1");
        dualSenseClient.Import("2-1");
        edgeClient.Import("3-1");
        xbox360Client.Import("4-1");

        QueueInterruptIn(dualShock4Client, 10, HidInterruptInEndpoint);
        QueueInterruptIn(dualSenseClient, 20, HidInterruptInEndpoint);
        QueueInterruptIn(edgeClient, 30, HidInterruptInEndpoint);
        QueueInterruptIn(xbox360Client, 40, Xbox360InterruptInEndpoint);

        // The queueing barriers prove every session has entered its URB loop and started its pacer.
        Assert.That(harness.TimeProvider.TimerCount, Is.EqualTo(4)); // one pacer per session

        harness.TimeProvider.Advance(Interval); // one tick completes the queued URB of every session

        Assert.That(ReadInterruptReport(dualShock4Client, 10), Is.EqualTo(dualShock4Report));
        Assert.That(ReadInterruptReport(dualSenseClient, 20), Is.EqualTo(dualSenseReport));
        Assert.That(ReadInterruptReport(edgeClient, 30), Is.EqualTo(edgeReport));
        Assert.That(ReadInterruptReport(xbox360Client, 40), Is.EqualTo(expectedXbox360Packet));

        Assert.That(harness.Logger.ErrorEntries, Is.Empty); // no session failed while serving the four personas
    }

    [Test]
    public void RemoveDevice_OneOfFour_OtherSessionsKeepServingAndRemovedBusIdRejectsImports()
    {
        using FacadeHarness harness = new FacadeHarness();
        DualShock4Device dualShock4 = harness.Server.AddDualShock4();
        DualSenseDevice dualSense = harness.Server.AddDualSense();
        DualSenseDevice edge = harness.Server.AddDualSense(true);
        Xbox360Device xbox360 = harness.Server.AddXbox360();
        byte[] edgeReport = CreateRawReport(0xDE);
        edge.SetRawInputReport(edgeReport);

        using LoopbackClient dualShock4Client = harness.Connect();
        using LoopbackClient dualSenseClient = harness.Connect();
        using LoopbackClient edgeClient = harness.Connect();
        using LoopbackClient xbox360Client = harness.Connect();
        dualShock4Client.Import("1-1");
        dualSenseClient.Import("2-1");
        edgeClient.Import("3-1");
        xbox360Client.Import("4-1");

        Assert.That(harness.Server.RemoveDevice(edge), Is.True);
        Assert.That(harness.Server.BusIds, Is.EqualTo(new[]
        {
            "1-1", "2-1", "4-1"
        }));

        // Fresh reports after the removal: the three remaining sessions are unaffected.
        byte[] dualShock4Report = CreateRawReport(0xE4);
        byte[] dualSenseReport = CreateRawReport(0xE5);
        dualShock4.SetRawInputReport(dualShock4Report);
        dualSense.SetRawInputReport(dualSenseReport);
        xbox360.SetInput(new Xbox360InputState
        {
            Y = true
        });
        byte[] expectedXbox360Packet = new byte[Xbox360InputReport.Size];
        Assert.That(xbox360.ProvideInputReport(expectedXbox360Packet), Is.EqualTo(Xbox360InputReport.Size));

        QueueInterruptIn(dualShock4Client, 10, HidInterruptInEndpoint);
        QueueInterruptIn(dualSenseClient, 20, HidInterruptInEndpoint);
        QueueInterruptIn(xbox360Client, 40, Xbox360InterruptInEndpoint);
        harness.TimeProvider.Advance(Interval);
        Assert.That(ReadInterruptReport(dualShock4Client, 10), Is.EqualTo(dualShock4Report));
        Assert.That(ReadInterruptReport(dualSenseClient, 20), Is.EqualTo(dualSenseReport));
        Assert.That(ReadInterruptReport(xbox360Client, 40), Is.EqualTo(expectedXbox360Packet));

        // The session that already imported the removed device keeps serving it until it ends.
        byte[] edgeResponse = PullInterruptReport(harness, edgeClient, 30, HidInterruptInEndpoint);
        Assert.That(edgeResponse, Is.EqualTo(edgeReport));

        // The removed bus id is no longer importable.
        using LoopbackClient lateClient = harness.Connect();
        lateClient.Send(CreateImportRequest("3-1"));
        byte[] rejection = lateClient.ReadExactly(UsbIpProtocol.OpCommonSize);
        UsbIpHandshake.ReadOpCommon(rejection, out _, out ushort operation, out int status);
        Assert.That(operation, Is.EqualTo(UsbIpProtocol.OpReplyImport));
        Assert.That(status, Is.EqualTo(1)); // kernel doc: 0 = OK, 1 = error
    }

    [Test]
    public void RemoveDevice_FreedBusId_IsReassignedToTheNextAddedPersona()
    {
        using FacadeHarness harness = new FacadeHarness();
        harness.Server.AddDualShock4();
        DualSenseDevice dualSense = harness.Server.AddDualSense();
        harness.Server.AddXbox360();

        Assert.That(harness.Server.RemoveDevice(dualSense), Is.True);
        DualSenseDevice replacement = harness.Server.AddDualSense(true);
        byte[] replacementReport = CreateRawReport(0xEE);
        replacement.SetRawInputReport(replacementReport);

        Assert.That(harness.Server.BusIds, Is.EqualTo(new[]
        {
            "1-1", "3-1", "2-1" // the freed bus id 2 was reassigned to the replacement
        }));

        using LoopbackClient client = harness.Connect();
        client.Import("2-1"); // the freed bus id now names the replacement
        Assert.That(PullInterruptReport(harness, client, 10, HidInterruptInEndpoint), Is.EqualTo(replacementReport));
    }

    /// <summary>
    /// Builds a 64-byte raw input report of a HID persona with a test-specific marker byte after
    /// the report id; the devices serve published raw reports verbatim, so the byte identifies the
    /// device a session's payloads came from.
    /// </summary>
    /// <param name="marker">Marker byte written after the report id.</param>
    /// <returns>The raw report bytes.</returns>
    private static byte[] CreateRawReport(byte marker)
    {
        byte[] report = new byte[RawReportSize];
        report[0] = InputReportId;
        report[1] = marker;
        return report;
    }

    /// <summary>Asserts the bus id and ids of one OP_REP_DEVLIST device entry.</summary>
    /// <param name="reply">Complete device list reply.</param>
    /// <param name="index">Index of the entry in registration order.</param>
    /// <param name="entrySize">Size of one entry (base entry plus its interface records).</param>
    /// <param name="busId">Expected bus id.</param>
    /// <param name="vendorId">Expected idVendor.</param>
    /// <param name="productId">Expected idProduct.</param>
    private static void AssertDevListEntry(byte[] reply, int index, int entrySize, string busId, ushort vendorId, ushort productId)
    {
        int entryOffset = UsbIpProtocol.OpCommonSize + 4 + index * entrySize;
        int busIdOffset = entryOffset + UsbIpHandshake.PathSize; // 0x100: busid
        int vendorIdOffset = busIdOffset + UsbIpHandshake.BusIdSize + 12; // 0x12C: idVendor after busnum, devnum and speed
        int productIdOffset = vendorIdOffset + 2; // 0x12E: idProduct
        Assert.That(Encoding.ASCII.GetString(reply, busIdOffset, busId.Length), Is.EqualTo(busId));
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(vendorIdOffset)), Is.EqualTo(vendorId));
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(productIdOffset)), Is.EqualTo(productId));
    }

    /// <summary>Creates an OP_REQ_DEVLIST request.</summary>
    /// <returns>The 8-byte request frame.</returns>
    private static byte[] CreateDevListRequest()
    {
        byte[] frame = new byte[UsbIpProtocol.OpCommonSize];
        UsbIpHandshake.WriteOpCommon(frame, UsbIpProtocol.OpRequestDevList, 0);
        return frame;
    }

    /// <summary>Creates an OP_REQ_IMPORT request naming <paramref name="busId"/>.</summary>
    /// <param name="busId">Bus id of the device to import.</param>
    /// <returns>The 40-byte request frame.</returns>
    private static byte[] CreateImportRequest(string busId)
    {
        byte[] frame = new byte[UsbIpProtocol.OpCommonSize + UsbIpHandshake.BusIdSize];
        UsbIpHandshake.WriteOpCommon(frame, UsbIpProtocol.OpRequestImport, 0);
        Encoding.ASCII.GetBytes(busId).CopyTo(frame, UsbIpProtocol.OpCommonSize);
        return frame;
    }

    /// <summary>
    /// Creates a non-ISO interrupt IN submit frame. The wire ep field carries the bare endpoint
    /// number and the direction travels in the separate direction field; the devid field is not
    /// consulted by the server (a session serves the device it imported).
    /// </summary>
    /// <param name="seqNum">Sequence number of the URB.</param>
    /// <param name="direction">Transfer direction.</param>
    /// <param name="endpointNumber">Bare endpoint number the wire ep field carries.</param>
    /// <param name="transferBufferLength">Declared transfer buffer length.</param>
    /// <returns>The submit frame.</returns>
    private static byte[] CreateUrbFrame(uint seqNum, UsbIpDirection direction, byte endpointNumber, uint transferBufferLength)
    {
        UsbIpHeader header = UsbIpHeader.CreateSubmit(
            seqNum,
            0x00010001, // busnum 1, devnum 1 of the first registered device; ignored by the server
            direction,
            endpointNumber,
            transferBufferLength,
            startFrame: uint.MaxValue, // unreliable for non-ISO URBs; the server must ignore it
            numberOfPackets: 0, // unreliable for non-ISO URBs; the server must ignore it
            setup: default);
        byte[] frame = new byte[UsbIpProtocol.HeaderSize];
        header.WriteSubmit(frame);
        return frame;
    }

    /// <summary>
    /// Sends a control SET_CONFIGURATION URB that completes immediately and waits for its reply:
    /// because a session handles frames strictly in order, the reply proves that every earlier
    /// frame was processed (used before advancing the manual clock at a queued interrupt URB).
    /// </summary>
    /// <param name="client">Connected test client.</param>
    /// <param name="seqNum">Sequence number of the barrier URB.</param>
    private static void SendBarrier(LoopbackClient client, uint seqNum)
    {
        UsbSetupPacket setup = new UsbSetupPacket
        {
            RequestType = 0x00, // host-to-device, standard, recipient device
            Request = 0x09, // SET_CONFIGURATION
            Value = 1, // the single configuration of every persona
            Index = 0,
            Length = 0
        };
        UsbIpHeader header = UsbIpHeader.CreateSubmit(
            seqNum,
            0x00010001,
            UsbIpDirection.Out,
            0,
            0,
            startFrame: uint.MaxValue,
            numberOfPackets: 0,
            setup: setup);
        byte[] frame = new byte[UsbIpProtocol.HeaderSize];
        header.WriteSubmit(frame);
        client.Send(frame);

        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.SeqNum, Is.EqualTo(seqNum));
        Assert.That(ret.Status, Is.Zero);
    }

    /// <summary>
    /// Queues one interrupt IN URB on <paramref name="client"/> and waits for a following control
    /// barrier, so a later clock advance finds the URB queued.
    /// </summary>
    /// <param name="client">Connected test client.</param>
    /// <param name="seqNum">Sequence number of the interrupt URB; the barrier uses the next one.</param>
    /// <param name="endpointNumber">Bare endpoint number of the device's interrupt IN endpoint.</param>
    private static void QueueInterruptIn(LoopbackClient client, uint seqNum, byte endpointNumber)
    {
        client.Send(CreateUrbFrame(seqNum, UsbIpDirection.In, endpointNumber, InterruptTransferLength));
        SendBarrier(client, seqNum + 1);
    }

    /// <summary>Reads the completion of a queued interrupt IN URB and its payload.</summary>
    /// <param name="client">Connected test client.</param>
    /// <param name="seqNum">Sequence number of the URB.</param>
    /// <returns>The transferred payload.</returns>
    private static byte[] ReadInterruptReport(LoopbackClient client, uint seqNum)
    {
        RetFrame ret = ReadRetFrame(client);
        Assert.That(ret.Command, Is.EqualTo(UsbIpProtocol.CommandRetSubmit));
        Assert.That(ret.SeqNum, Is.EqualTo(seqNum));
        Assert.That(ret.Status, Is.Zero);
        return client.ReadExactly((int)ret.ActualLength);
    }

    /// <summary>Queues an interrupt IN URB, ticks the manual clock once and reads its payload.</summary>
    /// <param name="harness">Harness owning the manual clock.</param>
    /// <param name="client">Connected test client.</param>
    /// <param name="seqNum">Sequence number of the URB; the barrier uses the next one.</param>
    /// <param name="endpointNumber">Bare endpoint number of the device's interrupt IN endpoint.</param>
    /// <returns>The transferred payload.</returns>
    private static byte[] PullInterruptReport(FacadeHarness harness, LoopbackClient client, uint seqNum, byte endpointNumber)
    {
        QueueInterruptIn(client, seqNum, endpointNumber);
        harness.TimeProvider.Advance(Interval);
        return ReadInterruptReport(client, seqNum);
    }

    /// <summary>Reads one 48-byte return header and parses the fields the tests assert on.</summary>
    /// <param name="client">Connected test client.</param>
    /// <returns>The parsed return header.</returns>
    private static RetFrame ReadRetFrame(LoopbackClient client) => new RetFrame(client.ReadExactly(UsbIpProtocol.HeaderSize));

    /// <summary>One USBIP_RET_SUBMIT frame with the fields the tests assert on.</summary>
    private sealed class RetFrame
    {
        /// <summary>Parses a 48-byte return header.</summary>
        /// <param name="bytes">Raw frame bytes.</param>
        public RetFrame(byte[] bytes)
        {
            Command = BinaryPrimitives.ReadUInt32BigEndian(bytes);
            SeqNum = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
            Status = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
            ActualLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(24));
        }

        /// <summary>Command code: USBIP_RET_SUBMIT or USBIP_RET_UNLINK.</summary>
        public uint Command { get; }

        /// <summary>Sequence number of the answered URB.</summary>
        public uint SeqNum { get; }

        /// <summary>Completion status.</summary>
        public int Status { get; }

        /// <summary>Number of bytes actually transferred.</summary>
        public uint ActualLength { get; }
    }

    /// <summary>
    /// Loopback TCP test client speaking raw USB/IP frames: the handshake and URB submits of one
    /// session, with exact-length reads for expected frames.
    /// </summary>
    private sealed class LoopbackClient : IDisposable
    {
        private readonly TcpClient _client;

        private readonly NetworkStream _stream;

        /// <summary>Connects to <paramref name="endPoint"/> and switches off Nagle.</summary>
        /// <param name="endPoint">Server endpoint to connect to.</param>
        public LoopbackClient(IPEndPoint endPoint)
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

            return buffer;
        }

        /// <summary>Sends an OP_REQ_IMPORT naming <paramref name="busId"/> and asserts the successful reply.</summary>
        /// <param name="busId">Bus id of the device to import.</param>
        public void Import(string busId)
        {
            Send(CreateImportRequest(busId));
            byte[] reply = ReadExactly(UsbIpProtocol.OpCommonSize + UsbIpDeviceInfo.EntrySize);
            UsbIpHandshake.ReadOpCommon(reply, out _, out ushort operation, out int status);
            Assert.That(operation, Is.EqualTo(UsbIpProtocol.OpReplyImport));
            Assert.That(status, Is.Zero, $"The import of bus id \"{busId}\" was rejected.");
        }

        /// <summary>Closes the connection.</summary>
        public void Dispose()
        {
            _stream.Dispose();
            _client.Dispose();
        }
    }

    /// <summary>
    /// Started <see cref="EmulationServer"/> on loopback port 0 with a manual clock, a recording
    /// logger and an attach strategy that never touches the host.
    /// </summary>
    private sealed class FacadeHarness : IDisposable
    {
        /// <summary>Starts the facade; personas are added through <see cref="Server"/>.</summary>
        public FacadeHarness()
        {
            TimeProvider = new ManualTimeProvider();
            Logger = new RecordingLogger();
            Server = new EmulationServer(new EmulationServerOptions
            {
                ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
                TimeProvider = TimeProvider,
                Logger = Logger,
                AttachStrategy = new UnusedAttachStrategy(),
                InterruptInterval = Interval
            });
            Server.Start();
        }

        /// <summary>Manual clock injected into the facade.</summary>
        public ManualTimeProvider TimeProvider { get; }

        /// <summary>Logger injected into the facade.</summary>
        public RecordingLogger Logger { get; }

        /// <summary>Started facade under test.</summary>
        public EmulationServer Server { get; }

        /// <summary>Connects a test client to the facade's bound endpoint.</summary>
        /// <returns>The connected client.</returns>
        public LoopbackClient Connect() => new LoopbackClient(Server.BoundEndPoint);

        /// <summary>Disposes the facade.</summary>
        public void Dispose() => Server.Dispose();
    }

    /// <summary>
    /// <see cref="IUsbIpAttachStrategy"/> that never attaches anything: the loopback tests drive
    /// sessions directly, so no usbip.exe run must ever happen.
    /// </summary>
    private sealed class UnusedAttachStrategy : IUsbIpAttachStrategy
    {
        /// <summary>Answers with failure; the loopback tests never attach.</summary>
        /// <param name="host">Host the facade forwarded.</param>
        /// <param name="port">Port the facade forwarded.</param>
        /// <param name="busId">Bus id the facade forwarded.</param>
        /// <param name="error">Failure reason.</param>
        /// <returns>Always <see langword="false"/>.</returns>
        public bool TryAttach(string host, int port, string busId, out string? error)
        {
            error = "The loopback tests never attach devices to the host.";
            return false;
        }

        /// <summary>Answers with failure; the loopback tests never detach.</summary>
        /// <param name="busId">Bus id the facade forwarded.</param>
        /// <param name="error">Failure reason.</param>
        /// <returns>Always <see langword="false"/>.</returns>
        public bool TryDetach(string busId, out string? error)
        {
            error = "The loopback tests never attach devices to the host.";
            return false;
        }
    }
}