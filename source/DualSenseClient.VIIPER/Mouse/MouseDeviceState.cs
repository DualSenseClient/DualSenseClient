using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.Mouse;

/// <summary>
/// Input state of a mouse device.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MouseDeviceState
{
    /// <summary>
    /// <see cref="MouseButtons"/> bitmask.
    /// </summary>
    public byte Buttons;

    /// <summary>
    /// Relative X movement since the last poll.
    /// </summary>
    public short DX;

    /// <summary>
    /// Relative Y movement since the last poll.
    /// </summary>
    public short DY;

    /// <summary>
    /// Relative vertical wheel movement.
    /// </summary>
    public short Wheel;

    /// <summary>
    /// Relative horizontal wheel movement (pan).
    /// </summary>
    public short Pan;
}