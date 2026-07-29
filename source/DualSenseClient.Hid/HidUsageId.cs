namespace DualSenseClient.Hid;

/// <summary>
/// Identifies a HID usage within a usage page, as defined by the USB-IF HID Usage Tables.
/// Only usages relevant to the application are enumerated; unrecognized values are reported
/// as <see cref="Unknown"/>.
/// </summary>
public enum HidUsageId : ushort
{
    /// <summary>
    /// The usage ID is not recognized or is zero (reserved).
    /// </summary>
    Unknown = 0x00,

    /// <summary>
    /// Joystick (<c>0x04</c>).
    /// </summary>
    Joystick = 0x04,

    /// <summary>
    /// Game Pad (<c>0x05</c>).
    /// </summary>
    GamePad = 0x05
}