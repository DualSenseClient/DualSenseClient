using DualSenseClient.Emulation.Devices.DualSense;

namespace DualSenseClient.Tests.Emulation.Devices;

/// <summary>
/// Pins the 64-byte DualSense USB input report (report id 1): the reference offsets and bit masks
/// of the sticks, analog triggers, the sequence counter, the face/shoulder/utility buttons (with
/// the Edge Fn buttons and paddles), the little-endian motion words, the sensor timestamp and the
/// temperature byte, the two touch points with their rolling timestamp, the trigger feedback
/// nibbles, the host and device timestamps, the battery/power byte and the host mute-light echo.
/// Layout facts: the neutral interrupt-in payload, the button bytes and the released hat 0x08, the
/// mic-mute echo bit, the battery byte, the stick/trigger byte ranges and the 63-byte
/// USBGetStateData layout block, cross-checked against the report 0x01 field widths of the served
/// HID report descriptor (6 axes, the vendor byte, the hat plus 15 buttons, the 13-bit vendor field
/// and 52 further vendor bytes).
/// </summary>
[TestFixture]
public sealed class DualSenseInputReportTests
{
    /// <summary>Neutral stick value (center).</summary>
    private const byte NeutralStick = 128;

    /// <summary>D-pad hat value of the released state (reference: released hat 0x08).</summary>
    private const byte ReleasedHat = 0x08;

    /// <summary>At-rest trigger feedback byte: stop located at nine with no active effect (reference main.cpp payload bytes 41 and 42 are 0x09).</summary>
    private const byte NeutralTriggerStatus = 0x09;

    /// <summary>Battery/power byte of a full battery while discharging: percent 10, power state 0.</summary>
    private const byte FullDischargingBattery = 0x0A;

    /// <summary>Contact byte of an idle touch point: not-touching bit set, tracking index zero.</summary>
    private const byte IdleTouchContact = 0x80;

    /// <summary>Report offset of the first stick axis (LX, then LY, RX and RY).</summary>
    private const int OffsetSticks = 1;

    /// <summary>Report offset of the L2 analog trigger.</summary>
    private const int OffsetLeftTrigger = 5;

    /// <summary>Report offset of the R2 analog trigger.</summary>
    private const int OffsetRightTrigger = 6;

    /// <summary>Report offset of the report sequence counter (the reference's SeqNo byte).</summary>
    private const int OffsetSequenceCounter = 7;

    /// <summary>Report offset of the D-pad nibble and the face buttons.</summary>
    private const int OffsetFaceAndDpad = 8;

    /// <summary>Report offset of the shoulder/directional button byte.</summary>
    private const int OffsetShoulderButtons = 9;

    /// <summary>Report offset of the PS/touchpad/mute byte carrying the Edge bits.</summary>
    private const int OffsetUtilityButtons = 10;

    /// <summary>Report offset of the first little-endian gyro word (X, then Z and Y).</summary>
    private const int OffsetGyroX = 16;

    /// <summary>Report offset of the first little-endian accelerometer word (X, then Y and Z).</summary>
    private const int OffsetAccelX = 22;

    /// <summary>Report offset of the little-endian sensor timestamp word.</summary>
    private const int OffsetSensorTimestamp = 28;

    /// <summary>Report offset of the signed temperature byte.</summary>
    private const int OffsetTemperature = 32;

    /// <summary>Report offset of the first touch point (contact byte, then three coordinate bytes).</summary>
    private const int OffsetTouch1 = 33;

    /// <summary>Report offset of the second touch point.</summary>
    private const int OffsetTouch2 = 37;

    /// <summary>Report offset of the rolling touch timestamp of the pair (the reference's TouchData.Timestamp).</summary>
    private const int OffsetTouchTimestamp = 41;

    /// <summary>Report offset of the right trigger stop-location/feedback-status byte.</summary>
    private const int OffsetTriggerRightStatus = 42;

    /// <summary>Report offset of the left trigger stop-location/feedback-status byte.</summary>
    private const int OffsetTriggerLeftStatus = 43;

    /// <summary>Report offset of the little-endian host timestamp echo.</summary>
    private const int OffsetHostTimestamp = 44;

    /// <summary>Report offset of the active trigger effect nibbles.</summary>
    private const int OffsetTriggerEffects = 48;

    /// <summary>Report offset of the little-endian device timestamp word.</summary>
    private const int OffsetDeviceTimestamp = 49;

    /// <summary>Report offset of the battery/power byte.</summary>
    private const int OffsetBattery = 53;

    /// <summary>Report offset of the host status/mute-light echo byte.</summary>
    private const int OffsetHostStatus = 54;

    [Test]
    public void Build_NeutralState_WritesTheReferenceReport()
    {
        byte[] buffer = new byte[DualSenseInputReport.Size];

        int written = DualSenseInputReport.Build(CreateNeutralState(), 0x00, 0x00, 0x00, buffer);

        byte[] expected =
        [
            0x01, // 0: report id
            NeutralStick, NeutralStick, NeutralStick, NeutralStick, // 1-4: LX LY RX RY
            0x00, 0x00, // 5-6: L2 R2 (released)
            0x00, // 7: sequence counter
            ReleasedHat, 0x00, 0x00, // 8-10: hat/face, shoulders, PS/touchpad/mute + Edge bits
            0x00, // 11: reference byte "appears unused"
            0x00, 0x00, 0x00, 0x00, // 12-15: reserved 32-bit counter word (no source in the input state)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 16-21: gyro X, Z, Y
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 22-27: accel X, Y, Z
            0x00, 0x00, 0x00, 0x00, // 28-31: sensor timestamp
            0x00, // 32: temperature
            IdleTouchContact, 0x00, 0x00, 0x00, // 33-36: touch 1 idle
            IdleTouchContact, 0x00, 0x00, 0x00, // 37-40: touch 2 idle
            0x00, // 41: rolling touch timestamp
            NeutralTriggerStatus, NeutralTriggerStatus, // 42-43: trigger stop location nine, status zero
            0x00, 0x00, 0x00, 0x00, // 44-47: host timestamp echo
            0x00, // 48: active trigger effects (none)
            0x00, 0x00, 0x00, 0x00, // 49-52: device timestamp
            FullDischargingBattery, // 53: battery/power
            0x00, // 54: host status echo (microphone not muted)
            0x00, // 55: external-mic / haptics-low-pass status (no source in the input state)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 // 56-63: reference tail (no source in the input state)
        ];

        Assert.That(written, Is.EqualTo(DualSenseInputReport.Size));
        Assert.That(buffer, Is.EqualTo(expected));
        Assert.That((byte)DualSenseDpadDirection.Released, Is.EqualTo(ReleasedHat));
    }

    [Test]
    public void Build_AllButtonsPressed_SetsEveryReferenceBit()
    {
        DualSenseInputState state = CreateNeutralState();
        state.Square = true;
        state.Cross = true;
        state.Circle = true;
        state.Triangle = true;
        state.L1 = true;
        state.R1 = true;
        state.L2 = true;
        state.R2 = true;
        state.Create = true;
        state.Options = true;
        state.L3 = true;
        state.R3 = true;
        state.Ps = true;
        state.TouchpadPressed = true;
        state.Mute = true;
        state.FnLeft = true;
        state.FnRight = true;
        state.PaddleLeft = true;
        state.PaddleRight = true;
        byte[] buffer = new byte[DualSenseInputReport.Size];

        DualSenseInputReport.Build(state, 0x00, 0x00, 0x00, buffer);

        Assert.That(buffer[OffsetFaceAndDpad], Is.EqualTo(ReleasedHat | 0xF0)); // square/cross/circle/triangle
        Assert.That(buffer[OffsetShoulderButtons], Is.EqualTo(0xFF)); // L1 R1 L2 R2 create options L3 R3
        Assert.That(buffer[OffsetUtilityButtons], Is.EqualTo(0xF7)); // PS touchpad mute, Fn left/right, paddles
    }

    [Test]
    public void Build_CrossPressed_SetsExactlyTheCrossBit()
    {
        byte[] neutral = new byte[DualSenseInputReport.Size];
        DualSenseInputReport.Build(CreateNeutralState(), 0x00, 0x00, 0x00, neutral);
        DualSenseInputState crossState = CreateNeutralState();
        crossState.Cross = true;
        byte[] cross = new byte[DualSenseInputReport.Size];

        DualSenseInputReport.Build(crossState, 0x00, 0x00, 0x00, cross);

        Assert.That(cross[OffsetFaceAndDpad], Is.EqualTo(ReleasedHat | 0x20));
        Assert.That(DifferingOffsets(neutral, cross), Is.EqualTo(new[]
        {
            OffsetFaceAndDpad
        }));
    }

    [Test]
    public void Build_EdgePaddlePressed_SetsExactlyThePaddleBit()
    {
        byte[] neutral = new byte[DualSenseInputReport.Size];
        DualSenseInputReport.Build(CreateNeutralState(), 0x00, 0x00, 0x00, neutral);
        DualSenseInputState paddleState = CreateNeutralState();
        paddleState.PaddleRight = true;
        byte[] paddle = new byte[DualSenseInputReport.Size];

        DualSenseInputReport.Build(paddleState, 0x00, 0x00, 0x00, paddle);

        Assert.That(paddle[OffsetUtilityButtons], Is.EqualTo(0x80)); // right paddle, utility byte bit 7
        Assert.That(DifferingOffsets(neutral, paddle), Is.EqualTo(new[]
        {
            OffsetUtilityButtons
        }));
    }

    [TestCase(DualSenseDpadDirection.Up, (byte)0x00)]
    [TestCase(DualSenseDpadDirection.UpRight, (byte)0x01)]
    [TestCase(DualSenseDpadDirection.Right, (byte)0x02)]
    [TestCase(DualSenseDpadDirection.DownRight, (byte)0x03)]
    [TestCase(DualSenseDpadDirection.Down, (byte)0x04)]
    [TestCase(DualSenseDpadDirection.DownLeft, (byte)0x05)]
    [TestCase(DualSenseDpadDirection.Left, (byte)0x06)]
    [TestCase(DualSenseDpadDirection.UpLeft, (byte)0x07)]
    [TestCase(DualSenseDpadDirection.Released, ReleasedHat)]
    public void Build_DpadDirection_EncodesPerTheReferenceTable(DualSenseDpadDirection direction, byte expectedHat)
    {
        DualSenseInputState state = CreateNeutralState();
        state.Dpad = direction;
        byte[] buffer = new byte[DualSenseInputReport.Size];

        DualSenseInputReport.Build(state, 0x00, 0x00, 0x00, buffer);

        // The hat nibble carries the direction; no face button may leak into the high nibble.
        Assert.That(buffer[OffsetFaceAndDpad], Is.EqualTo(expectedHat));
    }

    [Test]
    public void Build_CustomSticksAndTriggers_WriteValuesAtTheirOffsets()
    {
        DualSenseInputState state = CreateNeutralState();
        state.LeftStickX = 0x11;
        state.LeftStickY = 0x22;
        state.RightStickX = 0x33;
        state.RightStickY = 0x44;
        state.LeftTrigger = 0xAB;
        state.RightTrigger = 0x7F;
        byte[] buffer = new byte[DualSenseInputReport.Size];

        DualSenseInputReport.Build(state, 0x00, 0x00, 0x00, buffer);

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
        DualSenseInputState state = CreateNeutralState();
        state.GyroX = -2;
        state.GyroZ = 0x1234;
        state.GyroY = -1;
        state.AccelX = 0x0102;
        state.AccelY = -3;
        state.AccelZ = 0x7FFF;
        byte[] buffer = new byte[DualSenseInputReport.Size];

        DualSenseInputReport.Build(state, 0x00, 0x00, 0x00, buffer);

        Assert.That(buffer[OffsetGyroX], Is.EqualTo(0xFE)); // gyro X, low byte
        Assert.That(buffer[OffsetGyroX + 1], Is.EqualTo(0xFF)); // gyro X, high byte
        Assert.That(buffer[OffsetGyroX + 2], Is.EqualTo(0x34)); // gyro Z, low byte
        Assert.That(buffer[OffsetGyroX + 3], Is.EqualTo(0x12)); // gyro Z, high byte
        Assert.That(buffer[OffsetGyroX + 4], Is.EqualTo(0xFF)); // gyro Y, low byte
        Assert.That(buffer[OffsetGyroX + 5], Is.EqualTo(0xFF)); // gyro Y, high byte
        Assert.That(buffer[OffsetAccelX], Is.EqualTo(0x02)); // accel X, low byte
        Assert.That(buffer[OffsetAccelX + 1], Is.EqualTo(0x01)); // accel X, high byte
        Assert.That(buffer[OffsetAccelX + 2], Is.EqualTo(0xFD)); // accel Y, low byte
        Assert.That(buffer[OffsetAccelX + 3], Is.EqualTo(0xFF)); // accel Y, high byte
        Assert.That(buffer[OffsetAccelX + 4], Is.EqualTo(0xFF)); // accel Z, low byte
        Assert.That(buffer[OffsetAccelX + 5], Is.EqualTo(0x7F)); // accel Z, high byte
    }

    [TestCase((sbyte)25, (byte)0x19)]
    [TestCase((sbyte)-25, (byte)0xE7)]
    [TestCase(sbyte.MinValue, (byte)0x80)]
    public void Build_Temperature_WritesTheSignedByte(sbyte temperature, byte expected)
    {
        DualSenseInputState state = CreateNeutralState();
        state.Temperature = temperature;
        byte[] buffer = new byte[DualSenseInputReport.Size];

        DualSenseInputReport.Build(state, 0x00, 0x00, 0x00, buffer);

        Assert.That(buffer[OffsetTemperature], Is.EqualTo(expected));
    }

    [TestCase((byte)10, DualSenseChargingState.Discharging, FullDischargingBattery)] // full while discharging
    [TestCase((byte)8, DualSenseChargingState.Charging, (byte)0x18)] // charging at the 100% band
    [TestCase((byte)10, DualSenseChargingState.Complete, (byte)0x2A)] // charged: power state 2
    [TestCase((byte)3, DualSenseChargingState.ChargingError, (byte)0xF3)] // hardware error state 0xF
    [TestCase((byte)0xAB, DualSenseChargingState.Discharging, (byte)0x0B)] // percent is masked to the low nibble
    public void Build_BatteryLevelAndPowerState_EncodeInTheStatusByte(byte level, DualSenseChargingState powerState, byte expected)
    {
        DualSenseInputState state = CreateNeutralState();
        state.BatteryLevel = level;
        state.PowerState = powerState;
        byte[] buffer = new byte[DualSenseInputReport.Size];

        DualSenseInputReport.Build(state, 0x00, 0x00, 0x00, buffer);

        Assert.That(buffer[OffsetBattery], Is.EqualTo(expected));
    }

    [Test]
    public void Build_ActiveTouchPoint_WritesItsIdCoordinatesAndCounter()
    {
        DualSenseInputState state = CreateNeutralState();
        state.Touch1Active = true;
        state.Touch1Id = 0x37;
        state.Touch1X = 0x123;
        state.Touch1Y = 0x456;
        byte[] buffer = new byte[DualSenseInputReport.Size];

        DualSenseInputReport.Build(state, 0x00, 0x21, 0x55, buffer);

        Assert.That(buffer[OffsetTouch1], Is.EqualTo(0x37)); // contact id, not-touching bit clear
        Assert.That(buffer[OffsetTouch1 + 1], Is.EqualTo(0x23)); // x low byte
        Assert.That(buffer[OffsetTouch1 + 2], Is.EqualTo(0x61)); // x high nibble | y low nibble << 4
        Assert.That(buffer[OffsetTouch1 + 3], Is.EqualTo(0x45)); // y high byte
        Assert.That(buffer[OffsetTouch2], Is.EqualTo(IdleTouchContact)); // the second point stays idle
        Assert.That(buffer[OffsetTouchTimestamp], Is.EqualTo(0x21)); // first point's rolling counter
    }

    [Test]
    public void Build_SecondTouchPointOnly_UsesItsCounterAndMarksTheFirstPointIdle()
    {
        DualSenseInputState state = CreateNeutralState();
        state.Touch2Active = true;
        state.Touch2Id = 0x05;
        state.Touch2X = 0x1A2;
        state.Touch2Y = 0x0B7;
        byte[] buffer = new byte[DualSenseInputReport.Size];

        DualSenseInputReport.Build(state, 0x00, 0x21, 0x55, buffer);

        Assert.That(buffer[OffsetTouch1], Is.EqualTo(IdleTouchContact));
        Assert.That(buffer[OffsetTouch2], Is.EqualTo(0x05));
        Assert.That(buffer[OffsetTouch2 + 1], Is.EqualTo(0xA2)); // x low byte
        Assert.That(buffer[OffsetTouch2 + 2], Is.EqualTo(0x71)); // x high nibble | y low nibble << 4
        Assert.That(buffer[OffsetTouch2 + 3], Is.EqualTo(0x0B)); // y high byte
        Assert.That(buffer[OffsetTouchTimestamp], Is.EqualTo(0x55)); // second point's rolling counter
    }

    [Test]
    public void Build_ChangedCounters_DifferOnlyAtTheCounterOffsets()
    {
        DualSenseInputState state = CreateNeutralState();
        state.Cross = true;
        state.Touch1Active = true;
        state.Touch1Id = 0x01;
        state.Touch1X = 0x100;
        state.Touch1Y = 0x100;
        state.Touch2Active = true;
        state.Touch2Id = 0x02;
        state.Touch2X = 0x200;
        state.Touch2Y = 0x200;
        byte[] first = new byte[DualSenseInputReport.Size];
        byte[] second = new byte[DualSenseInputReport.Size];

        DualSenseInputReport.Build(state, 0x11, 0x22, 0x33, first);
        DualSenseInputReport.Build(state, 0x44, 0x55, 0x66, second);

        Assert.That(DifferingOffsets(first, second), Is.EqualTo(new[]
        {
            OffsetSequenceCounter, OffsetTouchTimestamp
        }));
    }

    [Test]
    public void Build_TimestampAndEchoFields_WriteAtTheirOffsets()
    {
        DualSenseInputState state = CreateNeutralState();
        state.SensorTimestamp = 0x01020304;
        state.HostTimestamp = 0x0A0B0C0D;
        state.DeviceTimestamp = 0x11223344;
        state.TriggerRightStopLocation = 0x9;
        state.TriggerRightStatus = 0x1;
        state.TriggerLeftStopLocation = 0x2;
        state.TriggerLeftStatus = 0x3;
        state.TriggerRightEffect = 0x1;
        state.TriggerLeftEffect = 0x4;
        state.MicMuted = true;
        byte[] buffer = new byte[DualSenseInputReport.Size];

        DualSenseInputReport.Build(state, 0x00, 0x00, 0x00, buffer);

        Assert.That(buffer[OffsetSensorTimestamp], Is.EqualTo(0x04)); // sensor timestamp, low byte
        Assert.That(buffer[OffsetSensorTimestamp + 3], Is.EqualTo(0x01)); // sensor timestamp, high byte
        Assert.That(buffer[OffsetTriggerRightStatus], Is.EqualTo(0x19)); // right: stop 9 | status 1 << 4
        Assert.That(buffer[OffsetTriggerLeftStatus], Is.EqualTo(0x32)); // left: stop 2 | status 3 << 4
        Assert.That(buffer[OffsetHostTimestamp], Is.EqualTo(0x0D)); // host timestamp echo, low byte
        Assert.That(buffer[OffsetHostTimestamp + 3], Is.EqualTo(0x0A)); // host timestamp echo, high byte
        Assert.That(buffer[OffsetTriggerEffects], Is.EqualTo(0x41)); // right effect 1 | left effect 4 << 4
        Assert.That(buffer[OffsetDeviceTimestamp], Is.EqualTo(0x44)); // device timestamp, low byte
        Assert.That(buffer[OffsetDeviceTimestamp + 3], Is.EqualTo(0x11)); // device timestamp, high byte
        Assert.That(buffer[OffsetHostStatus], Is.EqualTo(0x04)); // host mute-light echo bit
    }

    [Test]
    public void Build_LargerDestination_WritesNothingBeyondTheReport()
    {
        byte[] buffer = new byte[DualSenseInputReport.Size + 2];
        Array.Fill(buffer, (byte)0xEE);

        int written = DualSenseInputReport.Build(CreateNeutralState(), 0x00, 0x00, 0x00, buffer);

        Assert.That(written, Is.EqualTo(DualSenseInputReport.Size));
        Assert.That(buffer[DualSenseInputReport.Size], Is.EqualTo(0xEE)); // sentinel after the report
        Assert.That(buffer[DualSenseInputReport.Size + 1], Is.EqualTo(0xEE));
    }

    [Test]
    public void Build_DestinationTooShort_Throws()
    {
        byte[] buffer = new byte[DualSenseInputReport.Size - 1];

        Assert.That(() => DualSenseInputReport.Build(CreateNeutralState(), 0x00, 0x00, 0x00, buffer), Throws.ArgumentException);
    }

    /// <summary>
    /// Creates the neutral input state: centered sticks, released D-pad, all buttons released, an
    /// at-rest trigger feedback state (stop location nine, no effect), a full discharging battery
    /// and idle touch points.
    /// </summary>
    /// <returns>The neutral state used by the layout assertions.</returns>
    private static DualSenseInputState CreateNeutralState()
    {
        return new DualSenseInputState
        {
            LeftStickX = NeutralStick,
            LeftStickY = NeutralStick,
            RightStickX = NeutralStick,
            RightStickY = NeutralStick,
            Dpad = DualSenseDpadDirection.Released,
            TriggerRightStopLocation = 0x9,
            TriggerLeftStopLocation = 0x9,
            BatteryLevel = 10
        };
    }

    /// <summary>
    /// Collects the report offsets at which two reports differ.
    /// </summary>
    /// <param name="first">First built report.</param>
    /// <param name="second">Second built report.</param>
    /// <returns>The differing offsets in ascending order.</returns>
    private static List<int> DifferingOffsets(byte[] first, byte[] second)
    {
        List<int> differing = [];
        for (int i = 0; i < DualSenseInputReport.Size; i++)
        {
            if (first[i] != second[i])
            {
                differing.Add(i);
            }
        }

        return differing;
    }
}