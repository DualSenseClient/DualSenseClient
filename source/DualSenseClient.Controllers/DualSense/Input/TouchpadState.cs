namespace DualSenseClient.Controllers.DualSense.Input;

/// <summary>
/// Touchpad state containing two simultaneous touch points (bytes 32-39).
/// </summary>
public readonly struct TouchpadState : IEquatable<TouchpadState>
{
    /// <summary>
    /// Raw payload bytes 32-39 (two touch points, 4 bytes each).
    /// </summary>
    private readonly byte[] _raw;

    /// <summary>
    /// Initializes a new touchpad state from bytes 32-39 of the data payload.
    /// </summary>
    public TouchpadState(byte[] raw, int offset)
    {
        _raw = raw[(offset + 32)..(offset + 40)];
    }

    /// <summary>
    /// First touch point (primary finger) at bytes 0-3.
    /// </summary>
    public TouchPoint Touch1 => new TouchPoint(_raw, 0);

    /// <summary>
    /// Second touch point (secondary finger) at bytes 4-7.
    /// </summary>
    public TouchPoint Touch2 => new TouchPoint(_raw, 4);

    /// <summary>
    /// Returns true if all 8 raw bytes are equal.
    /// </summary>
    public bool Equals(TouchpadState other) => _raw.AsSpan().SequenceEqual(other._raw);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TouchpadState other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_raw);
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
/// A single touch point on the DualSense touchpad (4 bytes).
/// </summary>
public readonly struct TouchPoint : IEquatable<TouchPoint>
{
    /// <summary>
    /// Raw 4 bytes for this touch point (status + packed 12-bit coordinates).
    /// </summary>
    private readonly byte[] _raw;

    /// <summary>
    /// Initializes a new touch point from 4 bytes at the given offset.
    /// </summary>
    public TouchPoint(byte[] raw, int offset)
    {
        _raw = raw[offset..(offset + 4)];
    }

    /// <summary>
    /// Tracking identifier (0-127). Remains constant for the lifetime of a touch.
    /// </summary>
    public byte TrackingId => (byte)(_raw[0] & 0x7F);

    /// <summary>
    /// Whether a finger is currently detected at this point.
    /// </summary>
    public bool IsActive => (_raw[0] & 0x80) == 0;

    /// <summary>
    /// Horizontal position (0-1919).
    /// </summary>
    public ushort X => (ushort)(((_raw[2] & 0x0F) << 8) | _raw[1]);

    /// <summary>
    /// Vertical position (0-1079).
    /// </summary>
    public ushort Y => (ushort)((_raw[3] << 4) | (_raw[2] >> 4));

    /// <summary>
    /// Returns true if all 4 raw bytes are equal.
    /// </summary>
    public bool Equals(TouchPoint other) => _raw.AsSpan().SequenceEqual(other._raw);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TouchPoint other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_raw);
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