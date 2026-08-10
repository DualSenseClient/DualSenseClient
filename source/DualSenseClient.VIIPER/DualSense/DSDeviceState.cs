using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.DualSense;

/// <summary>
/// Input state of a DualSense device.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DSDeviceState
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
    /// <see cref="DualSenseButtons"/> bitmask.
    /// </summary>
    public uint Buttons;

    /// <summary>
    /// <see cref="DualSenseDPad"/> bitmask.
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
    /// Touchpad 1 Y position (0-1080).
    /// </summary>
    public ushort Touch1Y;

    /// <summary>
    /// 1 if touch 1 is active.
    /// </summary>
    public byte Touch1Active;

    /// <summary>
    /// Touch 1 tracking ID.
    /// </summary>
    public byte Touch1Tracking;

    /// <summary>
    /// Touchpad 2 X position (0-1920).
    /// </summary>
    public ushort Touch2X;

    /// <summary>
    /// Touchpad 2 Y position (0-1080).
    /// </summary>
    public ushort Touch2Y;

    /// <summary>
    /// 1 if touch 2 is active.
    /// </summary>
    public byte Touch2Active;

    /// <summary>
    /// Touch 2 tracking ID.
    /// </summary>
    public byte Touch2Tracking;

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