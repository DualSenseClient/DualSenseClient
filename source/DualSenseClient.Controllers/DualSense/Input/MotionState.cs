namespace DualSenseClient.Controllers.DualSense.Input;

/// <summary>
/// Motion sensor state from the DualSense IMU (gyroscope + accelerometer, bytes 15-31).
/// </summary>
public readonly struct MotionState
{
    /// <summary>
    /// Raw payload bytes 15-31 (gyro, accel, timestamp, temperature).
    /// </summary>
    private readonly byte[] _raw;

    /// <summary>
    /// Initializes a new motion state from bytes 15-31 of the data payload.
    /// </summary>
    public MotionState(byte[] raw, int offset)
    {
        _raw = raw[(offset + 15)..(offset + 32)];
    }

    // Bytes 0-5: Gyroscope (3 × int16 LE)
    /// <summary>
    /// Gyroscope X-axis / pitch (angular velocity, 16.384 LSB/dps).
    /// </summary>
    public short GyroX => BitConverter.ToInt16(_raw, 0);

    /// <summary>
    /// Gyroscope Y-axis / yaw (angular velocity, 16.384 LSB/dps).
    /// </summary>
    public short GyroY => BitConverter.ToInt16(_raw, 2);

    /// <summary>
    /// Gyroscope Z-axis / roll (angular velocity, 16.384 LSB/dps).
    /// </summary>
    public short GyroZ => BitConverter.ToInt16(_raw, 4);

    // Bytes 6-11: Accelerometer (3 × int16 LE)
    /// <summary>
    /// Accelerometer X-axis (linear acceleration, 8192 LSB/g).
    /// </summary>
    public short AccelX => BitConverter.ToInt16(_raw, 6);

    /// <summary>
    /// Accelerometer Y-axis (linear acceleration, 8192 LSB/g).
    /// </summary>
    public short AccelY => BitConverter.ToInt16(_raw, 8);

    /// <summary>
    /// Accelerometer Z-axis (linear acceleration, 8192 LSB/g).
    /// </summary>
    public short AccelZ => BitConverter.ToInt16(_raw, 10);

    // Bytes 12-15: Motion timestamp (uint32 LE)
    /// <summary>
    /// Motion timestamp.
    /// </summary>
    public uint Timestamp => BitConverter.ToUInt32(_raw, 12);

    // Byte 16: IMU temperature (int8)
    /// <summary>
    /// IMU temperature in degrees Celsius.
    /// </summary>
    public sbyte Temperature => (sbyte)_raw[16];
}