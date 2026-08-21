namespace DualSenseClient.Controllers.DualSense.Input;

/// <summary>
/// Touchpad state containing two simultaneous touch points (bytes 32-39).
/// </summary>
public readonly struct TouchpadState : IEquatable<TouchpadState>
{
    /// <summary>
    /// First touch point (primary finger) at bytes 32-35.
    /// </summary>
    public TouchPoint Touch1 { get; }

    /// <summary>
    /// Second touch point (secondary finger) at bytes 36-39.
    /// </summary>
    public TouchPoint Touch2 { get; }

    /// <summary>
    /// Initializes a new touchpad state from bytes 32-39 of the data payload.
    /// </summary>
    public TouchpadState(byte[] raw, int offset)
    {
        Touch1 = new TouchPoint(raw, offset + 32);
        Touch2 = new TouchPoint(raw, offset + 36);
    }

    /// <summary>
    /// Returns true if both touch points are equal.
    /// </summary>
    public bool Equals(TouchpadState other) => Touch1 == other.Touch1 && Touch2 == other.Touch2;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TouchpadState other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        hash.Add(Touch1);
        hash.Add(Touch2);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Returns true if the two states are equal.
    /// </summary>
    public static bool operator ==(TouchpadState left, TouchpadState right) => left.Equals(right);

    /// <summary>
    /// Returns true if the two states are not equal.
    /// </summary>
    public static bool operator !=(TouchpadState left, TouchpadState right) => !left.Equals(right);
}

/// <summary>
/// A single touch point on the DualSense touchpad (4 bytes). Values are parsed once
/// at construction into inline fields, so constructing a <see cref="TouchPoint"/>
/// never allocates.
/// </summary>
public readonly struct TouchPoint : IEquatable<TouchPoint>
{
    /// <summary>
    /// Tracking identifier (0-127). Remains constant for the lifetime of a touch.
    /// </summary>
    public byte TrackingId { get; }

    /// <summary>
    /// Whether a finger is currently detected at this point.
    /// </summary>
    public bool IsActive { get; }

    /// <summary>
    /// Horizontal position (0-1919).
    /// </summary>
    public ushort X { get; }

    /// <summary>
    /// Vertical position (0-1079).
    /// </summary>
    public ushort Y { get; }

    /// <summary>
    /// Initializes a new touch point from 4 bytes at the given offset
    /// (status byte + packed 12-bit coordinates).
    /// </summary>
    public TouchPoint(byte[] raw, int offset)
    {
        byte status = raw[offset];
        TrackingId = (byte)(status & 0x7F);
        IsActive = (status & 0x80) == 0;
        X = (ushort)(((raw[offset + 2] & 0x0F) << 8) | raw[offset + 1]);
        Y = (ushort)((raw[offset + 3] << 4) | (raw[offset + 2] >> 4));
    }

    /// <summary>
    /// Returns true if all parsed touch-point values are equal.
    /// </summary>
    public bool Equals(TouchPoint other)
        => TrackingId == other.TrackingId && IsActive == other.IsActive && X == other.X && Y == other.Y;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TouchPoint other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        hash.Add(TrackingId);
        hash.Add(IsActive);
        hash.Add(X);
        hash.Add(Y);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Returns true if the two touch points are equal.
    /// </summary>
    public static bool operator ==(TouchPoint left, TouchPoint right) => left.Equals(right);

    /// <summary>
    /// Returns true if the two touch points are not equal.
    /// </summary>
    public static bool operator !=(TouchPoint left, TouchPoint right) => !left.Equals(right);
}