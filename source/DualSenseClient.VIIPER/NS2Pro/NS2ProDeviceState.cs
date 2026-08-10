using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.NS2Pro;

/// <summary>
/// Input state of a Nintendo Switch 2 Pro Controller.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NS2ProDeviceState
{
    /// <summary>
    /// <see cref="NS2ProButtons"/> bitmask.
    /// </summary>
    public uint Buttons;

    /// <summary>
    /// Left stick X axis (0-65535).
    /// </summary>
    public ushort LX;

    /// <summary>
    /// Left stick Y axis (0-65535).
    /// </summary>
    public ushort LY;

    /// <summary>
    /// Right stick X axis (0-65535).
    /// </summary>
    public ushort RX;

    /// <summary>
    /// Right stick Y axis (0-65535).
    /// </summary>
    public ushort RY;

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
}