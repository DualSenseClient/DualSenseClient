namespace DualSenseClient.Controllers.DualSense.Input;

/// <summary>
/// Complete snapshot of all input state from a DualSense controller report (bytes 0-9).
/// </summary>
public readonly struct InputState : IEquatable<InputState>
{
    /// <summary>
    /// Payload bytes 0-7 (sticks, triggers, sequence number), little-endian.
    /// </summary>
    private readonly ulong _head;

    /// <summary>
    /// Payload bytes 8-9 (shoulder/system buttons), little-endian.
    /// </summary>
    private readonly ushort _tail;

    /// <summary>
    /// Initializes a new input state from bytes 0-9 of the data payload.
    /// </summary>
    public InputState(byte[] buffer, int offset)
    {
        _head = buffer[offset]
                | (ulong)buffer[offset + 1] << 8
                | (ulong)buffer[offset + 2] << 16
                | (ulong)buffer[offset + 3] << 24
                | (ulong)buffer[offset + 4] << 32
                | (ulong)buffer[offset + 5] << 40
                | (ulong)buffer[offset + 6] << 48
                | (ulong)buffer[offset + 7] << 56;
        _tail = (ushort)(buffer[offset + 8] | buffer[offset + 9] << 8);
    }

    // Bytes 0-3: Analog sticks
    /// <summary>
    /// Left stick horizontal position (0-255, center is 128).
    /// </summary>
    public byte LeftStickX => (byte)_head;

    /// <summary>
    /// Left stick vertical position (0-255, center is 128, 0 is up).
    /// </summary>
    public byte LeftStickY => (byte)(_head >> 8);

    /// <summary>
    /// Right stick horizontal position (0-255, center is 128).
    /// </summary>
    public byte RightStickX => (byte)(_head >> 16);

    /// <summary>
    /// Right stick vertical position (0-255, center is 128, 0 is up).
    /// </summary>
    public byte RightStickY => (byte)(_head >> 24);

    // Bytes 4-5: Analog triggers
    /// <summary>
    /// Left analog trigger value (0-255, released to fully pressed).
    /// </summary>
    public byte L2 => (byte)(_head >> 32);

    /// <summary>
    /// Right analog trigger value (0-255, released to fully pressed).
    /// </summary>
    public byte R2 => (byte)(_head >> 40);

    // Byte 6: Sequence number
    /// <summary>
    /// Incrementing report sequence counter. Use to detect dropped reports.
    /// </summary>
    public byte SequenceNumber => (byte)(_head >> 48);

    // Byte 7: D-Pad (bits 0-3) + Face buttons (bits 4-7)
    /// <summary>
    /// Byte 7 of the payload (D-Pad + face buttons).
    /// </summary>
    private byte Byte7 => (byte)(_head >> 56);

    /// <summary>
    /// Lower nibble of byte 7: D-Pad hat switch value (0x0-0x8).
    /// </summary>
    private byte DPadValue => (byte)(Byte7 & 0x0F);

    /// <summary>
    /// D-Pad is neutral (released).
    /// </summary>
    public bool DPadNeutral => DPadValue == 0x8;

    /// <summary>
    /// D-Pad up direction.
    /// </summary>
    public bool DPadUp => DPadValue is 0x0 or 0x1 or 0x7;

    /// <summary>
    /// D-Pad down direction.
    /// </summary>
    public bool DPadDown => DPadValue is 0x4 or 0x3 or 0x5;

    /// <summary>
    /// D-Pad left direction.
    /// </summary>
    public bool DPadLeft => DPadValue is 0x6 or 0x5 or 0x7;

    /// <summary>
    /// D-Pad right direction.
    /// </summary>
    public bool DPadRight => DPadValue is 0x2 or 0x1 or 0x3;

    /// <summary>
    /// Square face button (left).
    /// </summary>
    public bool Square => (Byte7 & 0x10) != 0;

    /// <summary>
    /// Cross face button (bottom).
    /// </summary>
    public bool Cross => (Byte7 & 0x20) != 0;

    /// <summary>
    /// Circle face button (right).
    /// </summary>
    public bool Circle => (Byte7 & 0x40) != 0;

    /// <summary>
    /// Triangle face button (top).
    /// </summary>
    public bool Triangle => (Byte7 & 0x80) != 0;

    // Byte 8: Shoulder + system buttons
    /// <summary>
    /// Byte 8 of the payload (shoulder + system buttons).
    /// </summary>
    private byte Byte8 => (byte)_tail;

    /// <summary>
    /// Left shoulder bumper (L1).
    /// </summary>
    public bool L1 => (Byte8 & 0x01) != 0;

    /// <summary>
    /// Right shoulder bumper (R1).
    /// </summary>
    public bool R1 => (Byte8 & 0x02) != 0;

    /// <summary>
    /// Left trigger digital click (L2 fully pressed).
    /// </summary>
    public bool L2Click => (Byte8 & 0x04) != 0;

    /// <summary>
    /// Right trigger digital click (R2 fully pressed).
    /// </summary>
    public bool R2Click => (Byte8 & 0x08) != 0;

    /// <summary>
    /// Create button (left of touchpad).
    /// </summary>
    public bool Create => (Byte8 & 0x10) != 0;

    /// <summary>
    /// Options button (right of touchpad).
    /// </summary>
    public bool Options => (Byte8 & 0x20) != 0;

    /// <summary>
    /// Left stick click (L3).
    /// </summary>
    public bool L3 => (Byte8 & 0x40) != 0;

    /// <summary>
    /// Right stick click (R3).
    /// </summary>
    public bool R3 => (Byte8 & 0x80) != 0;

    // Byte 9: System + Edge buttons
    /// <summary>
    /// Byte 9 of the payload (system + Edge buttons).
    /// </summary>
    private byte Byte9 => (byte)(_tail >> 8);

    /// <summary>
    /// PlayStation button (center).
    /// </summary>
    public bool PS => (Byte9 & 0x01) != 0;

    /// <summary>
    /// Touchpad surface press.
    /// </summary>
    public bool TouchPad => (Byte9 & 0x02) != 0;

    /// <summary>
    /// Mute button.
    /// </summary>
    public bool Mute => (Byte9 & 0x04) != 0;

    /// <summary>
    /// DualSense Edge left function button (FnL).
    /// </summary>
    public bool EdgeFunctionLeft => (Byte9 & 0x10) != 0;

    /// <summary>
    /// DualSense Edge right function button (FnR).
    /// </summary>
    public bool EdgeFunctionRight => (Byte9 & 0x20) != 0;

    /// <summary>
    /// DualSense Edge left paddle (L4).
    /// </summary>
    public bool EdgePaddleLeft => (Byte9 & 0x40) != 0;

    /// <summary>
    /// DualSense Edge right paddle (R4).
    /// </summary>
    public bool EdgePaddleRight => (Byte9 & 0x80) != 0;

    /// <summary>
    /// Returns true if all 10 raw bytes are equal.
    /// </summary>
    public bool Equals(InputState other) => _head == other._head && _tail == other._tail;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is InputState other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        hash.Add(_head);
        hash.Add(_tail);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Returns true if the two states are equal.
    /// </summary>
    public static bool operator ==(InputState left, InputState right) => left.Equals(right);

    /// <summary>
    /// Returns true if the two states are not equal.
    /// </summary>
    public static bool operator !=(InputState left, InputState right) => !left.Equals(right);
}