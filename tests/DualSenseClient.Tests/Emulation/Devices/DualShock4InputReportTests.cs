using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Tests.Emulation.Devices;

/// <summary>
/// Pins the 64-byte DualShock 4 USB input report (report id 1): the reference offsets and bit masks
/// of the sticks, face/shoulder/PS buttons, D-pad hat encoding, analog triggers, the little-endian
/// motion words, the battery/ext byte, the touch report samples and the rolling counters. Layout
/// facts: the neutral payload, the button bytes, the battery/ext byte and the touch offsets,
/// cross-checked against the report 0x01 field order of the served HID report descriptor.
/// </summary>
[TestFixture]
public sealed class DualShock4InputReportTests
{
    /// <summary>Neutral stick value (center).</summary>
    private const byte NeutralStick = 128;

    /// <summary>D-pad hat value of the released state (reference: "released hat (0x08)").</summary>
    private const byte ReleasedHat = 0x08;

    /// <summary>Battery/ext byte of a full battery while discharging: level 10, cable bit clear.</summary>
    private const byte FullDischargingBattery = 0x0A;

    /// <summary>Contact byte of an idle touch point: inactive bit set, id zero (reference idle captures).</summary>
    private const byte IdleTouchContact = 0x80;

    /// <summary>Report offset of the first stick axis (LX, then LY, RX and RY).</summary>
    private const int OffsetSticks = 1;

    /// <summary>Report offset of the D-pad nibble and the face buttons.</summary>
    private const int OffsetFaceAndDpad = 5;

    /// <summary>Report offset of the shoulder/directional button byte.</summary>
    private const int OffsetShoulderButtons = 6;

    /// <summary>Report offset of the PS/touchpad bits and the 6-bit packet counter.</summary>
    private const int OffsetPsTouchpadAndCounter = 7;

    /// <summary>Report offset of the L2 analog trigger.</summary>
    private const int OffsetLeftTrigger = 8;

    /// <summary>Report offset of the R2 analog trigger.</summary>
    private const int OffsetRightTrigger = 9;

    /// <summary>Report offset of the first little-endian gyro word (pitch, yaw and roll follow).</summary>
    private const int OffsetGyro = 13;

    /// <summary>Report offset of the first little-endian accelerometer word (x, y and z follow).</summary>
    private const int OffsetAccel = 19;

    /// <summary>Report offset of the battery/ext byte (capacity bits 0-3, cable state bit 4).</summary>
    private const int OffsetBattery = 30;

    /// <summary>Report offset of the number of touch report samples.</summary>
    private const int OffsetTouchReportCount = 33;

    /// <summary>Report offset of the first touch report sample (rolling counter, then two 4-byte points).</summary>
    private const int OffsetTouchReports = 34;

    /// <summary>Report offset of the idle marker of the third sample's second point.</summary>
    private const int OffsetLastIdleTouchContact = 57;

    /// <summary>Report offset of the idle marker of the fourth touch sample, truncated by the USB report size.</summary>
    private const int OffsetTruncatedTouchContact = 62;

    [Test]
    public void Build_NeutralState_WritesTheReferenceReport()
    {
        byte[] buffer = new byte[DualShock4InputReport.Size];

        int written = DualShock4InputReport.Build(CreateNeutralState(), 0x00, 0x00, 0x00, buffer);

        byte[] expected =
        [
            0x01, // 0: report id
            NeutralStick, NeutralStick, NeutralStick, NeutralStick, // 1-4: LX LY RX RY
            ReleasedHat, 0x00, 0x00, // 5-7: hat/face, shoulders, PS/touchpad + packet counter
            0x00, 0x00, // 8-9: L2 R2 (released)
            0x00, 0x00, 0x00, // 10-12: sensor timestamp and temperature (no source in the input state)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 13-18: gyro pitch, yaw, roll
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 19-24: accel x, y, z
            0x00, 0x00, 0x00, 0x00, 0x00, // 25-29: reserved
            FullDischargingBattery, 0x00, 0x00, // 30-32: battery/ext, status, reserved
            0x00, // 33: no touch samples while no point is active
            0x00, IdleTouchContact, 0x00, 0x00, 0x00, IdleTouchContact, 0x00, 0x00, 0x00, // 34-42: sample 1
            0x00, IdleTouchContact, 0x00, 0x00, 0x00, IdleTouchContact, 0x00, 0x00, 0x00, // 43-51: sample 2
            0x00, IdleTouchContact, 0x00, 0x00, 0x00, IdleTouchContact, 0x00, 0x00, 0x00, // 52-60: sample 3
            0x00, IdleTouchContact, 0x00 // 61-63: start of the fourth sample, truncated by the USB report size
        ];

        Assert.That(written, Is.EqualTo(DualShock4InputReport.Size));
        Assert.That(buffer, Is.EqualTo(expected));
        Assert.That((byte)DualShock4DpadDirection.Released, Is.EqualTo(ReleasedHat));
    }

    [Test]
    public void Build_AllButtonsPressed_SetsEveryReferenceBit()
    {
        DualShock4InputState state = CreateNeutralState();
        state.Square = true;
        state.Cross = true;
        state.Circle = true;
        state.Triangle = true;
        state.L1 = true;
        state.R1 = true;
        state.L2 = true;
        state.R2 = true;
        state.Share = true;
        state.Options = true;
        state.L3 = true;
        state.R3 = true;
        state.Ps = true;
        state.TouchpadPressed = true;
        byte[] buffer = new byte[DualShock4InputReport.Size];

        DualShock4InputReport.Build(state, 0x00, 0x00, 0x00, buffer);

        Assert.That(buffer[OffsetFaceAndDpad], Is.EqualTo(ReleasedHat | 0xF0)); // square/cross/circle/triangle
        Assert.That(buffer[OffsetShoulderButtons], Is.EqualTo(0xFF)); // L1 R1 L2 R2 share options L3 R3
        Assert.That(buffer[OffsetPsTouchpadAndCounter], Is.EqualTo(0x03)); // PS + touchpad, counter zero
    }

    [Test]
    public void Build_CrossPressed_SetsExactlyTheCrossBit()
    {
        byte[] neutral = new byte[DualShock4InputReport.Size];
        DualShock4InputReport.Build(CreateNeutralState(), 0x00, 0x00, 0x00, neutral);
        DualShock4InputState crossState = CreateNeutralState();
        crossState.Cross = true;
        byte[] cross = new byte[DualShock4InputReport.Size];

        DualShock4InputReport.Build(crossState, 0x00, 0x00, 0x00, cross);

        Assert.That(cross[OffsetFaceAndDpad], Is.EqualTo(ReleasedHat | 0x20));
        List<int> differing = [];
        for (int i = 0; i < DualShock4InputReport.Size; i++)
        {
            if (cross[i] != neutral[i])
            {
                differing.Add(i);
            }
        }

        Assert.That(differing, Is.EqualTo(new[]
        {
            OffsetFaceAndDpad
        }));
    }

    [TestCase(DualShock4DpadDirection.Up, (byte)0x00)]
    [TestCase(DualShock4DpadDirection.UpRight, (byte)0x01)]
    [TestCase(DualShock4DpadDirection.Right, (byte)0x02)]
    [TestCase(DualShock4DpadDirection.DownRight, (byte)0x03)]
    [TestCase(DualShock4DpadDirection.Down, (byte)0x04)]
    [TestCase(DualShock4DpadDirection.DownLeft, (byte)0x05)]
    [TestCase(DualShock4DpadDirection.Left, (byte)0x06)]
    [TestCase(DualShock4DpadDirection.UpLeft, (byte)0x07)]
    [TestCase(DualShock4DpadDirection.Released, ReleasedHat)]
    public void Build_DpadDirection_EncodesPerTheReferenceTable(DualShock4DpadDirection direction, byte expectedHat)
    {
        DualShock4InputState state = CreateNeutralState();
        state.Dpad = direction;
        byte[] buffer = new byte[DualShock4InputReport.Size];

        DualShock4InputReport.Build(state, 0x00, 0x00, 0x00, buffer);

        // The hat nibble carries the direction; no face button may leak into the high nibble.
        Assert.That(buffer[OffsetFaceAndDpad], Is.EqualTo(expectedHat));
    }

    [Test]
    public void Build_CustomSticksAndTriggers_WriteValuesAtTheirOffsets()
    {
        DualShock4InputState state = CreateNeutralState();
        state.LeftStickX = 0x11;
        state.LeftStickY = 0x22;
        state.RightStickX = 0x33;
        state.RightStickY = 0x44;
        state.LeftTrigger = 0xAB;
        state.RightTrigger = 0x7F;
        byte[] buffer = new byte[DualShock4InputReport.Size];

        DualShock4InputReport.Build(state, 0x00, 0x00, 0x00, buffer);

        Assert.That(buffer[OffsetSticks], Is.EqualTo(0x11));
        Assert.That(buffer[OffsetSticks + 1], Is.EqualTo(0x22));
        Assert.That(buffer[OffsetSticks + 2], Is.EqualTo(0x33));
        Assert.That(buffer[OffsetSticks + 3], Is.EqualTo(0x44));
        Assert.That(buffer[OffsetLeftTrigger], Is.EqualTo(0xAB));
        Assert.That(buffer[OffsetRightTrigger], Is.EqualTo(0x7F));
    }

    [Test]
    public void Build_MotionState_WritesLittleEndianAxisWords()
    {
        DualShock4InputState state = CreateNeutralState();
        state.GyroPitch = -2;
        state.GyroYaw = 0x1234;
        state.GyroRoll = -1;
        state.AccelX = 0x0102;
        state.AccelY = -3;
        state.AccelZ = 0x7FFF;
        byte[] buffer = new byte[DualShock4InputReport.Size];

        DualShock4InputReport.Build(state, 0x00, 0x00, 0x00, buffer);

        Assert.That(buffer[OffsetGyro], Is.EqualTo(0xFE)); // pitch, low byte
        Assert.That(buffer[OffsetGyro + 1], Is.EqualTo(0xFF)); // pitch, high byte
        Assert.That(buffer[OffsetGyro + 2], Is.EqualTo(0x34)); // yaw, low byte
        Assert.That(buffer[OffsetGyro + 3], Is.EqualTo(0x12)); // yaw, high byte
        Assert.That(buffer[OffsetGyro + 4], Is.EqualTo(0xFF)); // roll, low byte
        Assert.That(buffer[OffsetGyro + 5], Is.EqualTo(0xFF)); // roll, high byte
        Assert.That(buffer[OffsetAccel], Is.EqualTo(0x02)); // x, low byte
        Assert.That(buffer[OffsetAccel + 1], Is.EqualTo(0x01)); // x, high byte
        Assert.That(buffer[OffsetAccel + 2], Is.EqualTo(0xFD)); // y, low byte
        Assert.That(buffer[OffsetAccel + 3], Is.EqualTo(0xFF)); // y, high byte
        Assert.That(buffer[OffsetAccel + 4], Is.EqualTo(0xFF)); // z, low byte
        Assert.That(buffer[OffsetAccel + 5], Is.EqualTo(0x7F)); // z, high byte
    }

    [TestCase((byte)10, false, FullDischargingBattery)] // full while discharging
    [TestCase((byte)11, true, (byte)0x1B)] // full while charging ("battery is full" status)
    [TestCase((byte)3, true, (byte)0x13)] // charging, 30-40% band
    [TestCase((byte)0xAB, false, (byte)0x0B)] // level is masked to the low nibble
    public void Build_BatteryLevelAndCharging_EncodeInTheStatusByte(byte level, bool charging, byte expected)
    {
        DualShock4InputState state = CreateNeutralState();
        state.BatteryLevel = level;
        state.Charging = charging;
        byte[] buffer = new byte[DualShock4InputReport.Size];

        DualShock4InputReport.Build(state, 0x00, 0x00, 0x00, buffer);

        Assert.That(buffer[OffsetBattery], Is.EqualTo(expected));
    }

    [TestCase((byte)0x00, (byte)0x00)]
    [TestCase((byte)0x15, (byte)0x54)]
    [TestCase((byte)0x3F, (byte)0xFC)]
    [TestCase((byte)0xFF, (byte)0xFC)] // the packet counter wraps within its six bits
    public void Build_PacketCounter_WritesBelowTheSixCounterBits(byte counter, byte expectedByte)
    {
        byte[] buffer = new byte[DualShock4InputReport.Size];

        DualShock4InputReport.Build(CreateNeutralState(), counter, 0x00, 0x00, buffer);

        Assert.That(buffer[OffsetPsTouchpadAndCounter], Is.EqualTo(expectedByte));
    }

    [Test]
    public void Build_ActiveTouchPoint_WritesItsIdCoordinatesAndRollingCounter()
    {
        DualShock4InputState state = CreateNeutralState();
        state.Touch1Active = true;
        state.Touch1Id = 0x37;
        state.Touch1X = 0x123;
        state.Touch1Y = 0x456;
        byte[] buffer = new byte[DualShock4InputReport.Size];

        DualShock4InputReport.Build(state, 0x00, 0x21, 0x55, buffer);

        Assert.That(buffer[OffsetTouchReportCount], Is.EqualTo(1)); // one active point, one fresh sample
        Assert.That(buffer[OffsetTouchReports], Is.EqualTo(0x21)); // touch 1 rolling counter
        Assert.That(buffer[OffsetTouchReports + 1], Is.EqualTo(0x37)); // contact id, inactive bit clear
        Assert.That(buffer[OffsetTouchReports + 2], Is.EqualTo(0x23)); // x low byte
        Assert.That(buffer[OffsetTouchReports + 3], Is.EqualTo(0x61)); // x high nibble | y low nibble << 4
        Assert.That(buffer[OffsetTouchReports + 4], Is.EqualTo(0x45)); // y high byte
        Assert.That(buffer[OffsetTouchReports + 5], Is.EqualTo(IdleTouchContact)); // touch 2 idle in the sample
        Assert.That(buffer[OffsetTouchReports + 9], Is.EqualTo(0x55)); // touch 2 rolling counter (held while idle)
        Assert.That(buffer[OffsetTouchReports + 10], Is.EqualTo(0x37)); // the second sample repeats the points
        Assert.That(buffer[OffsetTouchReports + 14], Is.EqualTo(IdleTouchContact));
        Assert.That(buffer[OffsetLastIdleTouchContact], Is.EqualTo(IdleTouchContact));
        Assert.That(buffer[OffsetTruncatedTouchContact], Is.EqualTo(IdleTouchContact));
    }

    [Test]
    public void Build_SecondTouchPointOnly_UsesItsCounterAndMarksTheFirstPointIdle()
    {
        DualShock4InputState state = CreateNeutralState();
        state.Touch2Active = true;
        state.Touch2Id = 0x05;
        state.Touch2X = 0x1A2;
        state.Touch2Y = 0x0B7;
        byte[] buffer = new byte[DualShock4InputReport.Size];

        DualShock4InputReport.Build(state, 0x00, 0x21, 0x55, buffer);

        Assert.That(buffer[OffsetTouchReportCount], Is.EqualTo(1));
        Assert.That(buffer[OffsetTouchReports], Is.EqualTo(0x21)); // touch 1 counter is written even while idle
        Assert.That(buffer[OffsetTouchReports + 1], Is.EqualTo(IdleTouchContact));
        Assert.That(buffer[OffsetTouchReports + 5], Is.EqualTo(0x05));
        Assert.That(buffer[OffsetTouchReports + 6], Is.EqualTo(0xA2)); // x low byte
        Assert.That(buffer[OffsetTouchReports + 7], Is.EqualTo(0x71)); // x high nibble | y low nibble << 4
        Assert.That(buffer[OffsetTouchReports + 8], Is.EqualTo(0x0B)); // y high byte
        Assert.That(buffer[OffsetTouchReports + 9], Is.EqualTo(0x55));
        Assert.That(buffer[OffsetTouchReports + 10], Is.EqualTo(IdleTouchContact));
        Assert.That(buffer[OffsetTouchReports + 14], Is.EqualTo(0x05));
    }

    [Test]
    public void Build_ChangedCounters_DifferOnlyAtTheCounterOffsets()
    {
        DualShock4InputState state = CreateNeutralState();
        state.Cross = true;
        state.Touch1Active = true;
        state.Touch1Id = 0x01;
        state.Touch1X = 0x100;
        state.Touch1Y = 0x100;
        state.Touch2Active = true;
        state.Touch2Id = 0x02;
        state.Touch2X = 0x200;
        state.Touch2Y = 0x200;
        byte[] first = new byte[DualShock4InputReport.Size];
        byte[] second = new byte[DualShock4InputReport.Size];

        DualShock4InputReport.Build(state, 0x11, 0x22, 0x33, first);
        DualShock4InputReport.Build(state, 0x44, 0x55, 0x66, second);

        Assert.That(first[OffsetTouchReportCount], Is.EqualTo(2)); // two active points, two fresh samples
        List<int> differing = [];
        for (int i = 0; i < DualShock4InputReport.Size; i++)
        {
            if (first[i] != second[i])
            {
                differing.Add(i);
            }
        }

        Assert.That(differing, Is.EqualTo(new[]
        {
            OffsetPsTouchpadAndCounter, OffsetTouchReports, OffsetTouchReports + 9
        }));
    }

    [Test]
    public void Build_LargerDestination_WritesNothingBeyondTheReport()
    {
        byte[] buffer = new byte[DualShock4InputReport.Size + 2];
        Array.Fill(buffer, (byte)0xEE);

        int written = DualShock4InputReport.Build(CreateNeutralState(), 0x00, 0x00, 0x00, buffer);

        Assert.That(written, Is.EqualTo(DualShock4InputReport.Size));
        Assert.That(buffer[DualShock4InputReport.Size], Is.EqualTo(0xEE)); // sentinel after the report
        Assert.That(buffer[DualShock4InputReport.Size + 1], Is.EqualTo(0xEE));
    }

    [Test]
    public void Build_DestinationTooShort_Throws()
    {
        byte[] buffer = new byte[DualShock4InputReport.Size - 1];

        Assert.That(() => DualShock4InputReport.Build(CreateNeutralState(), 0x00, 0x00, 0x00, buffer), Throws.ArgumentException);
    }

    /// <summary>
    /// Creates the neutral input state: centered sticks, released D-pad, all buttons released and a
    /// full discharging battery.
    /// </summary>
    /// <returns>The neutral state used by the layout assertions.</returns>
    private static DualShock4InputState CreateNeutralState()
    {
        return new DualShock4InputState
        {
            LeftStickX = NeutralStick,
            LeftStickY = NeutralStick,
            RightStickX = NeutralStick,
            RightStickY = NeutralStick,
            Dpad = DualShock4DpadDirection.Released,
            BatteryLevel = 10
        };
    }
}