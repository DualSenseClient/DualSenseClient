// Field set of the 63-byte USB input payload (USBGetStateData: byte 0 LeftStickX through byte 62
// AesCmac[8]) with the D-pad/face byte, the 24-byte motion block, the sensor timestamp, the
// temperature byte, the 9-byte TouchData block, the trigger feedback nibbles, the host/device
// timestamps, the battery/power byte and the plug/mute echo byte.
// - Each BT 0x31 report payload (data + 3, 63 bytes) is copied verbatim into the interrupt-in
//   buffer and served as USB report 0x01 with 63 payload bytes: BT payload byte N is USB report
//   offset N + 1, the report id sits at offset 0.
// - The wake and Bluetooth paths read the D-pad/face and shoulder bytes and the released hat 0x08,
//   the battery path reads the power byte and the main path the mic-mute echo bit.

namespace DualSenseClient.Emulation.Devices.DualSense;

/// <summary>
/// D-pad direction values of the DualSense input report 0x01 hat field (4-bit, logical range 0-7)
/// plus the null value 8 the controller sends while no direction is held. Encoding per the
/// reference: the 4-bit Direction field is North = 0 running clockwise to NorthWest = 7 and
/// None = 8, the wake path documents the "D-pad direction (0x08 idle)" low nibble, and the
/// Bluetooth path treats the released value 0x08 the same way in its inactivity check.
/// </summary>
public enum DualSenseDpadDirection : byte
{
    /// <summary>Up, hat value 0 (reference Direction.North).</summary>
    Up = 0x00,

    /// <summary>Up and right, hat value 1.</summary>
    UpRight = 0x01,

    /// <summary>Right, hat value 2.</summary>
    Right = 0x02,

    /// <summary>Down and right, hat value 3.</summary>
    DownRight = 0x03,

    /// <summary>Down, hat value 4.</summary>
    Down = 0x04,

    /// <summary>Down and left, hat value 5.</summary>
    DownLeft = 0x05,

    /// <summary>Left, hat value 6.</summary>
    Left = 0x06,

    /// <summary>Up and left, hat value 7.</summary>
    UpLeft = 0x07,

    /// <summary>No direction held; the report carries the hat's null value 8.</summary>
    Released = 0x08
}

/// <summary>
/// Mutable input-state carrier of the DualSense persona: the field set of the 63-byte USB input
/// report 0x01 payload (sticks, analog triggers, the button blocks including the Edge Fn buttons
/// and paddles, motion sensors, battery and power state, both touch points, the temperature byte
/// and the report's echo fields: trigger feedback nibbles, host and device timestamps and the
/// host mute-light echo). <see cref="DualSenseInputReport.Build"/> reads it every frame; the
/// persona device owns the report counters and passes them in. Values are used as-is and each
/// field is masked to its report width; there is no neutral default, callers write a full state
/// (centered sticks are 128, the released D-pad is <see cref="DualSenseDpadDirection.Released"/>
/// and an at-rest controller reports the trigger stop location nine).
/// </summary>
public struct DualSenseInputState
{
    /// <summary>Left stick X, 0 (left) to 255 (right), neutral 128.</summary>
    public byte LeftStickX { get; set; }

    /// <summary>Left stick Y, 0 (up) to 255 (down), neutral 128.</summary>
    public byte LeftStickY { get; set; }

    /// <summary>Right stick X, 0 (left) to 255 (right), neutral 128.</summary>
    public byte RightStickX { get; set; }

    /// <summary>Right stick Y, 0 (up) to 255 (down), neutral 128.</summary>
    public byte RightStickY { get; set; }

    /// <summary>Held D-pad direction; <see cref="DualSenseDpadDirection.Released"/> while none is held.</summary>
    public DualSenseDpadDirection Dpad { get; set; }

    /// <summary>Whether the Square button is held.</summary>
    public bool Square { get; set; }

    /// <summary>Whether the Cross button is held.</summary>
    public bool Cross { get; set; }

    /// <summary>Whether the Circle button is held.</summary>
    public bool Circle { get; set; }

    /// <summary>Whether the Triangle button is held.</summary>
    public bool Triangle { get; set; }

    /// <summary>Whether the L1 shoulder button is held.</summary>
    public bool L1 { get; set; }

    /// <summary>Whether the R1 shoulder button is held.</summary>
    public bool R1 { get; set; }

    /// <summary>Whether the L2 trigger is fully pressed (the L2 button bit, distinct from the analog value).</summary>
    public bool L2 { get; set; }

    /// <summary>Whether the R2 trigger is fully pressed (the R2 button bit, distinct from the analog value).</summary>
    public bool R2 { get; set; }

    /// <summary>Whether the Create button is held (the reference's ButtonCreate).</summary>
    public bool Create { get; set; }

    /// <summary>Whether the Options button is held.</summary>
    public bool Options { get; set; }

    /// <summary>Whether the left stick is clicked (L3).</summary>
    public bool L3 { get; set; }

    /// <summary>Whether the right stick is clicked (R3).</summary>
    public bool R3 { get; set; }

    /// <summary>Whether the PS button is held (the reference's ButtonHome).</summary>
    public bool Ps { get; set; }

    /// <summary>Whether the touchpad is clicked.</summary>
    public bool TouchpadPressed { get; set; }

    /// <summary>Whether the microphone mute button is held (the reference's ButtonMute, distinct from the light state).</summary>
    public bool Mute { get; set; }

    /// <summary>Whether the Edge left Fn button is held (reference ButtonLeftFunction; zero on the base DualSense).</summary>
    public bool FnLeft { get; set; }

    /// <summary>Whether the Edge right Fn button is held (reference ButtonRightFunction; zero on the base DualSense).</summary>
    public bool FnRight { get; set; }

    /// <summary>Whether the Edge left back paddle is held (reference ButtonLeftPaddle; zero on the base DualSense).</summary>
    public bool PaddleLeft { get; set; }

    /// <summary>Whether the Edge right back paddle is held (reference ButtonRightPaddle; zero on the base DualSense).</summary>
    public bool PaddleRight { get; set; }

    /// <summary>L2 analog trigger, 0 (released) to 255 (fully pressed).</summary>
    public byte LeftTrigger { get; set; }

    /// <summary>R2 analog trigger, 0 (released) to 255 (fully pressed).</summary>
    public byte RightTrigger { get; set; }

    /// <summary>Gyroscope X axis (the first motion word of the report), raw device units as a little-endian word.</summary>
    public short GyroX { get; set; }

    /// <summary>Gyroscope Z axis (the second motion word of the report; the reference orders the axes X, Z, Y), raw device units.</summary>
    public short GyroZ { get; set; }

    /// <summary>Gyroscope Y axis (the third motion word of the report), raw device units.</summary>
    public short GyroY { get; set; }

    /// <summary>Accelerometer X (the first accelerometer word), raw device units.</summary>
    public short AccelX { get; set; }

    /// <summary>Accelerometer Y (the second accelerometer word), raw device units.</summary>
    public short AccelY { get; set; }

    /// <summary>Accelerometer Z (the third accelerometer word), raw device units.</summary>
    public short AccelZ { get; set; }

    /// <summary>Sensor timestamp of the sample, raw device ticks as a little-endian word (reference SensorTimestamp).</summary>
    public uint SensorTimestamp { get; set; }

    /// <summary>
    /// Temperature byte straight from the hardware (reference "int8_t Temperature", signed raw
    /// units, not degrees); consumers convert with the controller's calibration mapping.
    /// </summary>
    public sbyte Temperature { get; set; }

    /// <summary>Whether the first touch point is active.</summary>
    public bool Touch1Active { get; set; }

    /// <summary>First touch point X, 0 to 1919 (12-bit report field).</summary>
    public ushort Touch1X { get; set; }

    /// <summary>First touch point Y, 0 to 1080 (12-bit report field).</summary>
    public ushort Touch1Y { get; set; }

    /// <summary>First touch point tracking index, 0 to 127 (7-bit report field).</summary>
    public byte Touch1Id { get; set; }

    /// <summary>Whether the second touch point is active.</summary>
    public bool Touch2Active { get; set; }

    /// <summary>Second touch point X, 0 to 1919 (12-bit report field).</summary>
    public ushort Touch2X { get; set; }

    /// <summary>Second touch point Y, 0 to 1080 (12-bit report field).</summary>
    public ushort Touch2Y { get; set; }

    /// <summary>Second touch point tracking index, 0 to 127 (7-bit report field).</summary>
    public byte Touch2Id { get; set; }

    /// <summary>
    /// Right trigger stop location, 0 to 9; the Edge hardware's adjustable stop echoes here and an
    /// at-rest pad reports 9 (reference TriggerRightStopLocation; the reference's neutral payload
    /// carries 0x09 for both arms). The builder masks the low four bits.
    /// </summary>
    public byte TriggerRightStopLocation { get; set; }

    /// <summary>
    /// Right trigger feedback status of the trigger firmware: 0 idle, 1 feedback applied, 2
    /// weapon-fired, 3 vibration (reference TriggerRightStatus; the builder masks the low four bits).
    /// </summary>
    public byte TriggerRightStatus { get; set; }

    /// <summary>Left trigger stop location, 0 to 9 (reference TriggerLeftStopLocation).</summary>
    public byte TriggerLeftStopLocation { get; set; }

    /// <summary>Left trigger feedback status of the trigger firmware (reference TriggerLeftStatus).</summary>
    public byte TriggerLeftStatus { get; set; }

    /// <summary>
    /// Host timestamp echo: the value the controller mirrors from the host's report write
    /// (reference HostTimestamp, "mirrors data from report write"); it belongs together with the
    /// output decode that stores the host's value.
    /// </summary>
    public uint HostTimestamp { get; set; }

    /// <summary>
    /// Active right trigger effect as echoed by the trigger firmware: 0 none, 1 feedback, 2 weapon,
    /// 3 vibration (reference TriggerRightEffect; the builder masks the low four bits).
    /// </summary>
    public byte TriggerRightEffect { get; set; }

    /// <summary>Active left trigger effect (reference TriggerLeftEffect, same encoding).</summary>
    public byte TriggerLeftEffect { get; set; }

    /// <summary>
    /// Device timestamp of the report, raw device ticks as a little-endian word (reference
    /// DeviceTimeStamp, a per-report rolling device clock).
    /// </summary>
    public uint DeviceTimestamp { get; set; }

    /// <summary>
    /// Battery capacity nibble (reference PowerPercent, 0x0-0xA bands); the builder masks the low
    /// four bits. Charging is carried by <see cref="PowerState"/>, the single cable bit of the
    /// DualShock 4 does not exist on the DualSense.
    /// </summary>
    public byte BatteryLevel { get; set; }

    /// <summary>
    /// Power state nibble of the battery byte; the shared <see cref="DualSenseChargingState"/> pins
    /// the encoding and is also the identity's battery default.
    /// </summary>
    public DualSenseChargingState PowerState { get; set; }

    /// <summary>
    /// Host mute-light echo: whether the controller reports the microphone as muted (reference
    /// MicMuted). The reference reads this bit back from every report and re-syncs its mute-light
    /// setting from it, so the persona device mirrors the host's mic-mute light output into this
    /// field.
    /// </summary>
    public bool MicMuted { get; set; }
}