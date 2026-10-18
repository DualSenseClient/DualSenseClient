using DualSenseClient.Emulation.Devices.Xbox360;

namespace DualSenseClient.Tests.Emulation.Devices;

/// <summary>
/// Pins the 20-byte Xbox 360 wired controller input packet. The packet layout: the
/// XUSB_INTERRUPT_IN_PACKET { Id, Size, Report } shape and the stage-4 initialization blob (Id
/// 0x00, Size 0x14, the button word, both triggers, four little-endian stick words and a six-byte
/// zero tail); the behavior oracle documents the same offsets byte-by-byte and pins the button
/// masks (the XInput button word layout). The device owns no report counters: this format carries
/// none, so every frame is byte-identical while the input state does not change.
/// </summary>
[TestFixture]
public sealed class Xbox360InputReportTests
{
    /// <summary>Report offset of the little-endian button word.</summary>
    private const int OffsetButtons = 2;

    /// <summary>Report offset of the left analog trigger.</summary>
    private const int OffsetLeftTrigger = 4;

    /// <summary>Report offset of the right analog trigger.</summary>
    private const int OffsetRightTrigger = 5;

    /// <summary>Report offset of the little-endian left stick X word (Y follows).</summary>
    private const int OffsetLeftStickX = 6;

    /// <summary>Report offset of the little-endian right stick X word (Y follows).</summary>
    private const int OffsetRightStickX = 10;

    /// <summary>Packet id byte of the input packet (XUSB_INTERRUPT_IN_PACKET.Id).</summary>
    private const byte PacketId = 0x00;

    /// <summary>Packet size byte of the input packet (XUSB_INTERRUPT_IN_PACKET.Size, 20).</summary>
    private const byte PacketSize = 0x14;

    /// <summary>Neutral packet: the reference header and zeroed fields (centered sticks are zero).</summary>
    private static readonly byte[] NeutralPacket =
    [
        PacketId, PacketSize,
        0x00, 0x00, // no button held
        0x00, 0x00, // LT, RT released
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // sticks centered at zero
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00 // reserved tail (zero in both references)
    ];

    [Test]
    public void Build_NeutralState_WritesTheReferencePacket()
    {
        byte[] buffer = new byte[Xbox360InputReport.Size];

        int written = Xbox360InputReport.Build(new Xbox360InputState(), buffer);

        Assert.That(written, Is.EqualTo(Xbox360InputReport.Size));
        Assert.That(buffer, Is.EqualTo(NeutralPacket));
        Assert.That((ushort)Xbox360DpadDirection.Released, Is.Zero); // a default state is neutral
    }

    [Test]
    public void Build_PressedButtonsAndAxes_WritesTheReferencePacket()
    {
        Xbox360InputState state = new Xbox360InputState
        {
            Dpad = Xbox360DpadDirection.UpRight,
            A = true,
            LeftTrigger = 0x80,
            RightTrigger = 0x40,
            LeftStickX = -32768,
            LeftStickY = 32767,
            RightStickX = 0x0102,
            RightStickY = -2
        };
        byte[] buffer = new byte[Xbox360InputReport.Size];

        int written = Xbox360InputReport.Build(state, buffer);

        byte[] expected =
        [
            PacketId, PacketSize,
            0x09, 0x10, // D-pad up + right (0x0009) and A (0x1000)
            0x80, 0x40, // LT, RT
            0x00, 0x80, // LX: -32768
            0xFF, 0x7F, // LY: 32767
            0x02, 0x01, // RX: 258
            0xFE, 0xFF, // RY: -2
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        Assert.That(written, Is.EqualTo(Xbox360InputReport.Size));
        Assert.That(buffer, Is.EqualTo(expected));
    }

    [Test]
    public void Build_EachButton_SetsExactlyItsReferenceBit()
    {
        (string Name, ushort Mask, Func<Xbox360InputState> Create)[] buttons =
        [
            ("A", 0x1000, () => new Xbox360InputState
            {
                A = true
            }),
            ("B", 0x2000, () => new Xbox360InputState
            {
                B = true
            }),
            ("X", 0x4000, () => new Xbox360InputState
            {
                X = true
            }),
            ("Y", 0x8000, () => new Xbox360InputState
            {
                Y = true
            }),
            ("LB", 0x0100, () => new Xbox360InputState
            {
                LeftShoulder = true
            }),
            ("RB", 0x0200, () => new Xbox360InputState
            {
                RightShoulder = true
            }),
            ("Back", 0x0020, () => new Xbox360InputState
            {
                Back = true
            }),
            ("Start", 0x0010, () => new Xbox360InputState
            {
                Start = true
            }),
            ("L3", 0x0040, () => new Xbox360InputState
            {
                L3 = true
            }),
            ("R3", 0x0080, () => new Xbox360InputState
            {
                R3 = true
            }),
            ("Guide", 0x0400, () => new Xbox360InputState
            {
                Guide = true
            })
        ];

        foreach ((string name, ushort mask, Func<Xbox360InputState> create) in buttons)
        {
            Xbox360InputState state = create();
            byte[] buffer = new byte[Xbox360InputReport.Size];

            Xbox360InputReport.Build(state, buffer);

            Assert.That(buffer[OffsetButtons], Is.EqualTo((byte)mask), $"{name}: low button byte");
            Assert.That(buffer[OffsetButtons + 1], Is.EqualTo((byte)(mask >> 8)), $"{name}: high button byte");
            for (int i = 0; i < Xbox360InputReport.Size; i++)
            {
                if (i != OffsetButtons && i != OffsetButtons + 1)
                {
                    Assert.That(buffer[i], Is.EqualTo(NeutralPacket[i]), $"{name}: byte {i} of the packet body");
                }
            }
        }
    }

    [TestCase(Xbox360DpadDirection.Up, (ushort)0x0001)]
    [TestCase(Xbox360DpadDirection.UpRight, (ushort)0x0009)]
    [TestCase(Xbox360DpadDirection.Right, (ushort)0x0008)]
    [TestCase(Xbox360DpadDirection.DownRight, (ushort)0x000A)]
    [TestCase(Xbox360DpadDirection.Down, (ushort)0x0002)]
    [TestCase(Xbox360DpadDirection.DownLeft, (ushort)0x0006)]
    [TestCase(Xbox360DpadDirection.Left, (ushort)0x0004)]
    [TestCase(Xbox360DpadDirection.UpLeft, (ushort)0x0005)]
    [TestCase(Xbox360DpadDirection.Released, (ushort)0x0000)]
    public void Build_DpadDirection_EncodesPerTheReferenceMasks(Xbox360DpadDirection direction, ushort expectedWord)
    {
        Xbox360InputState state = new Xbox360InputState
        {
            Dpad = direction
        };
        byte[] buffer = new byte[Xbox360InputReport.Size];

        Xbox360InputReport.Build(state, buffer);

        // The D-pad lives in the low bits of the button word (the behavior oracle's ButtonDPad* masks).
        Assert.That(buffer[OffsetButtons], Is.EqualTo((byte)expectedWord));
        Assert.That(buffer[OffsetButtons + 1], Is.Zero);
    }

    [Test]
    public void Build_AllButtonsAndDpadHeld_SetsEveryButtonBit()
    {
        Xbox360InputState state = new Xbox360InputState
        {
            Dpad = Xbox360DpadDirection.UpLeft,
            A = true,
            B = true,
            X = true,
            Y = true,
            LeftShoulder = true,
            RightShoulder = true,
            Back = true,
            Start = true,
            L3 = true,
            R3 = true,
            Guide = true
        };
        byte[] buffer = new byte[Xbox360InputReport.Size];

        Xbox360InputReport.Build(state, buffer);

        // All buttons plus the up-left D-pad bits: only the unused 0x0800 bit and the down/right
        // D-pad directions (not held by UpLeft) stay clear.
        Assert.That(buffer[OffsetButtons], Is.EqualTo((byte)0xF5));
        Assert.That(buffer[OffsetButtons + 1], Is.EqualTo((byte)0xF7));
    }

    [Test]
    public void Build_Triggers_WriteAtTheirOffsets()
    {
        Xbox360InputState state = new Xbox360InputState
        {
            LeftTrigger = 0xAB,
            RightTrigger = 0x7F
        };
        byte[] buffer = new byte[Xbox360InputReport.Size];

        Xbox360InputReport.Build(state, buffer);

        Assert.That(buffer[OffsetLeftTrigger], Is.EqualTo(0xAB));
        Assert.That(buffer[OffsetRightTrigger], Is.EqualTo(0x7F));
    }

    [Test]
    public void Build_Sticks_WriteLittleEndianWordsAtTheirOffsets()
    {
        Xbox360InputState state = new Xbox360InputState
        {
            LeftStickX = -2,
            LeftStickY = 0x1234,
            RightStickX = -32768,
            RightStickY = 32767
        };
        byte[] buffer = new byte[Xbox360InputReport.Size];

        Xbox360InputReport.Build(state, buffer);

        Assert.That(buffer[OffsetLeftStickX], Is.EqualTo(0xFE)); // LX low byte
        Assert.That(buffer[OffsetLeftStickX + 1], Is.EqualTo(0xFF)); // LX high byte
        Assert.That(buffer[OffsetLeftStickX + 2], Is.EqualTo(0x34)); // LY low byte
        Assert.That(buffer[OffsetLeftStickX + 3], Is.EqualTo(0x12)); // LY high byte
        Assert.That(buffer[OffsetRightStickX], Is.EqualTo(0x00)); // RX low byte
        Assert.That(buffer[OffsetRightStickX + 1], Is.EqualTo(0x80)); // RX high byte
        Assert.That(buffer[OffsetRightStickX + 2], Is.EqualTo(0xFF)); // RY low byte
        Assert.That(buffer[OffsetRightStickX + 3], Is.EqualTo(0x7F)); // RY high byte
    }

    [Test]
    public void Build_LargerDestination_WritesNothingBeyondTheReport()
    {
        byte[] buffer = new byte[Xbox360InputReport.Size + 2];
        Array.Fill(buffer, (byte)0xEE);

        int written = Xbox360InputReport.Build(new Xbox360InputState(), buffer);

        Assert.That(written, Is.EqualTo(Xbox360InputReport.Size));
        Assert.That(buffer[Xbox360InputReport.Size], Is.EqualTo(0xEE)); // sentinel after the packet
        Assert.That(buffer[Xbox360InputReport.Size + 1], Is.EqualTo(0xEE));
    }

    [Test]
    public void Build_DestinationTooShort_Throws()
    {
        byte[] buffer = new byte[Xbox360InputReport.Size - 1];

        Assert.That(() => Xbox360InputReport.Build(new Xbox360InputState(), buffer), Throws.ArgumentException);
    }
}