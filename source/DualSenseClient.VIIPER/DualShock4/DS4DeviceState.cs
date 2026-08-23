using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.DualShock4;

/// <summary>
/// Input state of a DualShock 4 device.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DS4DeviceState
{
    /// <summary>
    /// Left stick X axis.
    /// </summary>
    public sbyte LX;

    /// <summary>
    /// Left stick Y axis.
    /// </summary>
    public sbyte LY;

    /// <summary>
    /// Right stick X axis.
    /// </summary>
    public sbyte RX;

    /// <summary>
    /// Right stick Y axis.
    /// </summary>
    public sbyte RY;

    /// <summary>
    /// <see cref="DualShock4Buttons"/> bitmask.
    /// </summary>
    public ushort Buttons;

    /// <summary>
    /// <see cref="DualShock4DPad"/> hat switch value.
    /// </summary>
    public byte DPad;

    /// <summary>
    /// Left trigger analog value (0-255).
    /// </summary>
    public byte L2;

    /// <summary>
    /// Right trigger analog value (0-255).
    /// </summary>
    public byte R2;

    /// <summary>
    /// Touchpad 1 X position (0-1920).
    /// </summary>
    public ushort Touch1X;

    /// <summary>
    /// Touchpad 1 Y position (0-942).
    /// </summary>
    public ushort Touch1Y;

    /// <summary>
    /// 1 if touch 1 is active.
    /// </summary>
    public byte Touch1Active;

    /// <summary>
    /// Touchpad 2 X position (0-1920).
    /// </summary>
    public ushort Touch2X;

    /// <summary>
    /// Touchpad 2 Y position (0-942).
    /// </summary>
    public ushort Touch2Y;

    /// <summary>
    /// 1 if touch 2 is active.
    /// </summary>
    public byte Touch2Active;

    /// <summary>
    /// Gyroscope X angular velocity.
    /// </summary>
    public short GyroX;

    /// <summary>
    /// Gyroscope Y angular velocity.
    /// </summary>
    public short GyroY;

    /// <summary>
    /// Gyroscope Z angular velocity.
    /// </summary>
    public short GyroZ;

    /// <summary>
    /// Accelerometer X axis.
    /// </summary>
    public short AccelX;

    /// <summary>
    /// Accelerometer Y axis.
    /// </summary>
    public short AccelY;

    /// <summary>
    /// Accelerometer Z axis.
    /// </summary>
    public short AccelZ;
}