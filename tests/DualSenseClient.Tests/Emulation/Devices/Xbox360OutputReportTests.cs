using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Devices.Xbox360;
using DualSenseClient.Emulation.UsbIp;

namespace DualSenseClient.Tests.Emulation.Devices;

/// <summary>
/// Pins the Xbox 360 rumble packet decode and the persona device that ties the descriptor set, the
/// input packet builder and the request engine together. The rumble packet layout: an 8-byte host
/// transfer is the rumble packet, with the large motor at byte 3 and the small motor at byte 4 (the
/// hand-off to the rumble notification: LargeMotor = _Rumble[3], SmallMotor = _Rumble[4]); the
/// behavior oracle documents the header bytes ([0] 0x00 id, [1] 0x08 size) and ignores every other
/// outbound packet (the LED packet carries id 0x01, size 0x03). Device assertions cover the neutral
/// initialization, the counterless per-frame equality (the format carries no counters) and the
/// output-report event path.
/// </summary>
[TestFixture]
public sealed class Xbox360OutputReportTests
{
    /// <summary>Packet id byte of the rumble packet (reference: byte 0 is 0x00).</summary>
    private const byte PacketId = 0x00;

    /// <summary>Declared packet size of the rumble packet (XUSB_RUMBLE_SIZE, 8).</summary>
    private const byte RumblePacketSize = 0x08;

    /// <summary>Length in bytes of the reference rumble packet.</summary>
    private const int RumblePacketLength = 8;

    /// <summary>Packet id byte of the LED packet the device ignores (reference: 0x01).</summary>
    private const byte LedPacketId = 0x01;

    [Test]
    public void TryParse_RumblePacket_ReadsBothMotorBytes()
    {
        byte[] payload =
        [
            PacketId, RumblePacketSize, 0x00, 0x80, 0x40, 0x00, 0x00, 0x00
        ];

        bool parsed = Xbox360OutputReport.TryParse(payload, out Xbox360OutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.LeftMotor, Is.EqualTo(0x80)); // large / low-frequency motor (byte 3)
        Assert.That(report.RightMotor, Is.EqualTo(0x40)); // small / high-frequency motor (byte 4)
    }

    [Test]
    public void TryParse_ZeroMotors_ReadsBothMotorsAsZero()
    {
        byte[] payload =
        [
            PacketId, RumblePacketSize, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        bool parsed = Xbox360OutputReport.TryParse(payload, out Xbox360OutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.LeftMotor, Is.Zero);
        Assert.That(report.RightMotor, Is.Zero);
    }

    [Test]
    public void TryParse_FullMotors_ReadsBothMotorsAsMaximum()
    {
        byte[] payload =
        [
            PacketId, RumblePacketSize, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00
        ];

        bool parsed = Xbox360OutputReport.TryParse(payload, out Xbox360OutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.LeftMotor, Is.EqualTo(0xFF));
        Assert.That(report.RightMotor, Is.EqualTo(0xFF));
    }

    [Test]
    public void TryParse_LongerPayload_ReadsTheLeadingFieldsAndIgnoresTrailingBytes()
    {
        byte[] payload =
        [
            PacketId, RumblePacketSize, 0x00, 0x11, 0x22, 0x00, 0x00, 0x00, 0xEE, 0xEE, 0xEE, 0xEE
        ];

        bool parsed = Xbox360OutputReport.TryParse(payload, out Xbox360OutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.LeftMotor, Is.EqualTo(0x11));
        Assert.That(report.RightMotor, Is.EqualTo(0x22));
    }

    [TestCase((byte)0x01)] // LED packet id
    [TestCase((byte)0x02)]
    [TestCase((byte)0xFF)]
    public void TryParse_UnknownPacketId_ReturnsFalse(byte packetId)
    {
        byte[] payload =
        [
            packetId, RumblePacketSize, 0x00, 0x80, 0x80, 0x00, 0x00, 0x00
        ];

        bool parsed = Xbox360OutputReport.TryParse(payload, out Xbox360OutputReport report);

        Assert.That(parsed, Is.False);
        Assert.That(report, Is.EqualTo(default(Xbox360OutputReport)));
    }

    [TestCase((byte)0x00)]
    [TestCase((byte)0x03)] // LED packet size
    [TestCase((byte)0x14)] // input packet size
    public void TryParse_UnknownPacketSize_ReturnsFalse(byte packetSize)
    {
        byte[] payload =
        [
            PacketId, packetSize, 0x00, 0x80, 0x80, 0x00, 0x00, 0x00
        ];

        bool parsed = Xbox360OutputReport.TryParse(payload, out Xbox360OutputReport report);

        Assert.That(parsed, Is.False);
        Assert.That(report, Is.EqualTo(default(Xbox360OutputReport)));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(4)]
    [TestCase(7)]
    public void TryParse_TruncatedPacket_ReturnsFalse(int length)
    {
        byte[] payload =
        [
            PacketId, RumblePacketSize, 0x00, 0x80, 0x80, 0x00, 0x00, 0x00
        ];

        bool parsed = Xbox360OutputReport.TryParse(payload[..length], out Xbox360OutputReport report);

        Assert.That(parsed, Is.False);
        Assert.That(report, Is.EqualTo(default(Xbox360OutputReport)));
    }

    [Test]
    public void TryParse_LedPacket_ReturnsFalseAndDoesNotThrow()
    {
        // The controller's LED packet (id 0x01, size 0x03, LED number) is not a rumble command.
        byte[] payload =
        [
            LedPacketId, 0x03, 0x02
        ];

        bool parsed = false;
        Xbox360OutputReport report = default;

        Assert.That(() => parsed = Xbox360OutputReport.TryParse(payload, out report), Throws.Nothing);

        Assert.That(parsed, Is.False);
        Assert.That(report, Is.EqualTo(default(Xbox360OutputReport)));
    }

    [Test]
    public void NewDevice_ExportsAtFullSpeed()
    {
        // The USB/IP speed follows the behavior oracle's explicit descriptor field (Speed 2 = full
        // speed), whose export corroborates the reference's 4/8 endpoint intervals as full-speed
        // frames. A kernel-bus reference declares the emulated device high speed unconditionally
        // (IsDeviceHighSpeed returns true) — a kernel-domain enumeration fact, not a wire speed —
        // so the base engine default stands.
        Xbox360Device device = CreateDevice();

        Assert.That(device.Speed, Is.EqualTo(UsbIpDeviceSpeed.Full));
    }

    [Test]
    public void ProvideInputReport_UntouchedDevice_ReportsTheNeutralPacket()
    {
        Xbox360Device device = CreateDevice();
        byte[] packet = new byte[Xbox360InputReport.Size];

        int written = device.ProvideInputReport(packet);

        byte[] expected =
        [
            0x00, 0x14, // packet id, packet size
            0x00, 0x00, // no button held
            0x00, 0x00, // LT, RT released
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // sticks centered at zero
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00 // reserved tail
        ];

        Assert.That(written, Is.EqualTo(Xbox360InputReport.Size));
        Assert.That(packet, Is.EqualTo(expected));
    }

    [Test]
    public void ProvideInputReport_StoredState_IsWhatTheFollowingPacketsCarry()
    {
        Xbox360Device device = CreateDevice();
        Xbox360InputState state = new Xbox360InputState
        {
            Dpad = Xbox360DpadDirection.Down,
            B = true,
            LeftTrigger = 0x10,
            RightStickX = -1
        };
        device.SetInput(state);
        byte[] packet = new byte[Xbox360InputReport.Size];

        int written = device.ProvideInputReport(packet);

        byte[] expected =
        [
            0x00, 0x14,
            0x02, 0x20, // D-pad down (0x0002) and B (0x2000)
            0x10, 0x00, // LT, RT
            0x00, 0x00, 0x00, 0x00, // LX, LY
            0xFF, 0xFF, 0x00, 0x00, // RX: -1, RY
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        Assert.That(written, Is.EqualTo(Xbox360InputReport.Size));
        Assert.That(packet, Is.EqualTo(expected));
    }

    [Test]
    public void ProvideInputReport_ConsecutivePulls_ServeByteIdenticalPackets()
    {
        // The format carries no packet counter: every frame of an unchanged state is byte-identical.
        Xbox360Device device = CreateDevice();
        Xbox360InputState state = new Xbox360InputState
        {
            X = true,
            LeftStickY = 0x4444
        };
        device.SetInput(state);
        byte[] first = new byte[Xbox360InputReport.Size];
        byte[] second = new byte[Xbox360InputReport.Size];
        byte[] third = new byte[Xbox360InputReport.Size];

        device.ProvideInputReport(first);
        device.ProvideInputReport(second);
        device.ProvideInputReport(third);

        Assert.That(first, Is.EqualTo(second));
        Assert.That(second, Is.EqualTo(third));
        Assert.That(first[2], Is.EqualTo(0x00)); // X is 0x4000: low button byte
        Assert.That(first[3], Is.EqualTo(0x40));
        Assert.That(first[8], Is.EqualTo(0x44)); // LY low byte
        Assert.That(first[9], Is.EqualTo(0x44)); // LY high byte
    }

    [Test]
    public void ProvideInputReport_UndersizedDestination_ProvidesNothing()
    {
        Xbox360Device device = CreateDevice();
        byte[] packet = new byte[Xbox360InputReport.Size - 1];

        int written = device.ProvideInputReport(packet);

        Assert.That(written, Is.Zero);
        Assert.That(packet, Is.EqualTo(new byte[Xbox360InputReport.Size - 1])); // nothing written
    }

    [Test]
    public void OnOutputReport_RumblePacket_RaisesTheEventWithTheDecodedMotors()
    {
        Xbox360Device device = CreateDevice();
        List<Xbox360OutputReport> received = [];
        device.OutputReportReceived += report => received.Add(report);
        byte[] payload =
        [
            PacketId, RumblePacketSize, 0x00, 0x64, 0x28, 0x00, 0x00, 0x00
        ];

        device.OnOutputReport(payload);

        Assert.That(received, Has.Count.EqualTo(1));
        Assert.That(received[0].LeftMotor, Is.EqualTo(0x64));
        Assert.That(received[0].RightMotor, Is.EqualTo(0x28));
    }

    [Test]
    public void OnOutputReport_ZeroRumblePacket_RaisesTheEventWithZeroMotors()
    {
        Xbox360Device device = CreateDevice();
        List<Xbox360OutputReport> received = [];
        device.OutputReportReceived += report => received.Add(report);
        byte[] payload =
        [
            PacketId, RumblePacketSize, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        device.OnOutputReport(payload);

        Assert.That(received, Has.Count.EqualTo(1)); // stopping the motors is a report too
        Assert.That(received[0].LeftMotor, Is.Zero);
        Assert.That(received[0].RightMotor, Is.Zero);
    }

    [Test]
    public void OnOutputReport_LedPacket_RaisesNothingAndDoesNotThrow()
    {
        Xbox360Device device = CreateDevice();
        int raised = 0;
        device.OutputReportReceived += _ => raised++;
        byte[] payload =
        [
            LedPacketId, 0x03, 0x02
        ];

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);

        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void OnOutputReport_UnknownPacketId_RaisesNothingAndDoesNotThrow()
    {
        Xbox360Device device = CreateDevice();
        int raised = 0;
        device.OutputReportReceived += _ => raised++;
        byte[] payload =
        [
            0x42, RumblePacketSize, 0x00, 0x80, 0x80, 0x00, 0x00, 0x00
        ];

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);

        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void OnOutputReport_TruncatedPacket_RaisesNothingAndDoesNotThrow()
    {
        Xbox360Device device = CreateDevice();
        int raised = 0;
        device.OutputReportReceived += _ => raised++;
        byte[] payload =
        [
            PacketId, RumblePacketSize, 0x00, 0x80
        ];

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);

        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void OnOutputReport_EmptyPayload_RaisesNothingAndDoesNotThrow()
    {
        Xbox360Device device = CreateDevice();
        int raised = 0;
        device.OutputReportReceived += _ => raised++;

        Assert.That(() => device.OnOutputReport(ReadOnlySpan<byte>.Empty), Throws.Nothing);

        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void OnOutputReport_SubscriberThrows_LogsTheFaultAndKeepsRaisingTheEvent()
    {
        RecordingLogger logger = new RecordingLogger();
        Xbox360Device device = new Xbox360Device(new Xbox360Identity(), logger);
        int raised = 0;
        device.OutputReportReceived += _ => throw new InvalidOperationException("subscriber failure");
        device.OutputReportReceived += _ => raised++;
        byte[] payload =
        [
            PacketId, RumblePacketSize, 0x00, 0x80, 0x80, 0x00, 0x00, 0x00
        ];

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);
        Assert.That(raised, Is.EqualTo(1)); // the later subscriber still received the report

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);
        Assert.That(raised, Is.EqualTo(2)); // a subsequent output still raises the event

        Assert.That(logger.ErrorEntries, Has.Count.EqualTo(2));
        Assert.That(logger.ErrorEntries[0].Message, Does.Contain("subscriber failure"));
    }

    /// <summary>Creates the persona device with the reference identity.</summary>
    private static Xbox360Device CreateDevice() => new Xbox360Device(new Xbox360Identity());
}