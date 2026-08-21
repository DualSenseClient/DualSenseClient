namespace DualSenseClient.Controllers.DualSense.Input;

/// <summary>
/// Motion sensor state from the DualSense IMU (gyroscope + accelerometer, bytes 15-31).
/// </summary>
public readonly struct MotionState : IEquatable<MotionState>
{
    /// <summary>
    /// Gyroscope X-axis / pitch (angular velocity, 16.384 LSB/dps).
    /// </summary>
    public short GyroX { get; }

    /// <summary>
    /// Gyroscope Y-axis / yaw (angular velocity, 16.384 LSB/dps).
    /// </summary>
    public short GyroY { get; }

    /// <summary>
    /// Gyroscope Z-axis / roll (angular velocity, 16.384 LSB/dps).
    /// </summary>
    public short GyroZ { get; }

    /// <summary>
    /// Accelerometer X-axis (linear acceleration, 8192 LSB/g).
    /// </summary>
    public short AccelX { get; }

    /// <summary>
    /// Accelerometer Y-axis (linear acceleration, 8192 LSB/g).
    /// </summary>
    public short AccelY { get; }

    /// <summary>
    /// Accelerometer Z-axis (linear acceleration, 8192 LSB/g).
    /// </summary>
    public short AccelZ { get; }

    // Bytes 12-15: Motion timestamp (uint32 LE)
    /// <summary>
    /// Motion timestamp.
    /// </summary>
    public uint Timestamp { get; }

    // Byte 16: IMU temperature (int8)
    /// <summary>
    /// IMU temperature in degrees Celsius.
    /// </summary>
    public sbyte Temperature { get; }

    /// <summary>
    /// Initializes a new motion state from bytes 15-31 of the data payload.
    /// </summary>
    public MotionState(byte[] buffer, int offset)
    {
        GyroX = BitConverter.ToInt16(buffer, offset + 15);
        GyroY = BitConverter.ToInt16(buffer, offset + 17);
        GyroZ = BitConverter.ToInt16(buffer, offset + 19);
        AccelX = BitConverter.ToInt16(buffer, offset + 21);
        AccelY = BitConverter.ToInt16(buffer, offset + 23);
        AccelZ = BitConverter.ToInt16(buffer, offset + 25);
        Timestamp = BitConverter.ToUInt32(buffer, offset + 27);
        Temperature = (sbyte)buffer[offset + 31];
    }

    /// <summary>
    /// Returns true if all parsed motion values are equal.
    /// </summary>
    public bool Equals(MotionState other)
        => GyroX == other.GyroX
           && GyroY == other.GyroY
           && GyroZ == other.GyroZ
           && AccelX == other.AccelX
           && AccelY == other.AccelY
           && AccelZ == other.AccelZ
           && Timestamp == other.Timestamp
           && Temperature == other.Temperature;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MotionState other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        hash.Add(GyroX);
        hash.Add(GyroY);
        hash.Add(GyroZ);
        hash.Add(AccelX);
        hash.Add(AccelY);
        hash.Add(AccelZ);
        hash.Add(Timestamp);
        hash.Add(Temperature);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Returns true if the two states are equal.
    /// </summary>
    public static bool operator ==(MotionState left, MotionState right) => left.Equals(right);

    /// <summary>
    /// Returns true if the two states are not equal.
    /// </summary>
    public static bool operator !=(MotionState left, MotionState right) => !left.Equals(right);
}