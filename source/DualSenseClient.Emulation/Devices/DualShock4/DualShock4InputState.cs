namespace DualSenseClient.Emulation.Devices.DualShock4;

/// <summary>
/// D-pad hat switch values of the DualShock 4 input report 0x01 (4-bit field, logical range 0-7)
/// plus the null value 8 the controller sends while no direction is held. Encoding per the v2
/// reference: the neutral report initializes "hat released (0x08)" and the wake path documents the
/// "D-pad direction (0x08 idle)" low nibble; the numeric order (0 = up, clockwise) is the standard
/// hat switch table of the served HID report descriptor.
/// </summary>
public enum DualShock4DpadDirection : byte
{
    /// <summary>Up, hat value 0.</summary>
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
/// Mutable input-state carrier of the DualShock 4 persona: the field set of the 64-byte USB input
/// report 0x01 (sticks, buttons, analog triggers, motion sensors, battery and two touch points).
/// <see cref="DualShock4InputReport.Build"/> reads it every frame; the persona device owns the
/// report counters and passes them in. Values are used as-is and each field is masked to its
/// report width; there is no neutral default, callers write a full state (centered sticks are 128).
/// </summary>
public struct DualShock4InputState
{
    /// <summary>Left stick X, 0 (left) to 255 (right), neutral 128.</summary>
    public byte LeftStickX { get; set; }

    /// <summary>Left stick Y, 0 (up) to 255 (down), neutral 128.</summary>
    public byte LeftStickY { get; set; }

    /// <summary>Right stick X, 0 (left) to 255 (right), neutral 128.</summary>
    public byte RightStickX { get; set; }

    /// <summary>Right stick Y, 0 (up) to 255 (down), neutral 128.</summary>
    public byte RightStickY { get; set; }

    /// <summary>Held D-pad direction; <see cref="DualShock4DpadDirection.Released"/> while none is held.</summary>
    public DualShock4DpadDirection Dpad { get; set; }

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

    /// <summary>Whether the Share button is held.</summary>
    public bool Share { get; set; }

    /// <summary>Whether the Options button is held.</summary>
    public bool Options { get; set; }

    /// <summary>Whether the left stick is clicked (L3).</summary>
    public bool L3 { get; set; }

    /// <summary>Whether the right stick is clicked (R3).</summary>
    public bool R3 { get; set; }

    /// <summary>Whether the PS button is held.</summary>
    public bool Ps { get; set; }

    /// <summary>Whether the touchpad is clicked.</summary>
    public bool TouchpadPressed { get; set; }

    /// <summary>L2 analog trigger, 0 (released) to 255 (fully pressed).</summary>
    public byte LeftTrigger { get; set; }

    /// <summary>R2 analog trigger, 0 (released) to 255 (fully pressed).</summary>
    public byte RightTrigger { get; set; }

    /// <summary>Gyroscope pitch (report axis 0, little-endian word), raw device units.</summary>
    public short GyroPitch { get; set; }

    /// <summary>Gyroscope yaw (report axis 1, little-endian word), raw device units.</summary>
    public short GyroYaw { get; set; }

    /// <summary>Gyroscope roll (report axis 2, little-endian word), raw device units.</summary>
    public short GyroRoll { get; set; }

    /// <summary>Accelerometer X (report axis 0, little-endian word), raw device units.</summary>
    public short AccelX { get; set; }

    /// <summary>Accelerometer Y (report axis 1, little-endian word), raw device units.</summary>
    public short AccelY { get; set; }

    /// <summary>Accelerometer Z (report axis 2, little-endian word), raw device units.</summary>
    public short AccelZ { get; set; }

    /// <summary>
    /// Battery capacity nibble: 0-10 direct percentage bands on battery (10 = 100%), 11 = full while
    /// charging; 14 and 15 are error states of the hardware. The builder masks the low four bits.
    /// </summary>
    public byte BatteryLevel { get; set; }

    /// <summary>Whether the cable is connected (the charging/cable state bit of the battery byte).</summary>
    public bool Charging { get; set; }

    /// <summary>Whether the first touch point is active.</summary>
    public bool Touch1Active { get; set; }

    /// <summary>First touch point X, 0 to 1919 (12-bit report field).</summary>
    public ushort Touch1X { get; set; }

    /// <summary>First touch point Y, 0 to 941 (12-bit report field).</summary>
    public ushort Touch1Y { get; set; }

    /// <summary>First touch point tracking id, 0 to 127 (7-bit report field).</summary>
    public byte Touch1Id { get; set; }

    /// <summary>Whether the second touch point is active.</summary>
    public bool Touch2Active { get; set; }

    /// <summary>Second touch point X, 0 to 1919 (12-bit report field).</summary>
    public ushort Touch2X { get; set; }

    /// <summary>Second touch point Y, 0 to 941 (12-bit report field).</summary>
    public ushort Touch2Y { get; set; }

    /// <summary>Second touch point tracking id, 0 to 127 (7-bit report field).</summary>
    public byte Touch2Id { get; set; }
}