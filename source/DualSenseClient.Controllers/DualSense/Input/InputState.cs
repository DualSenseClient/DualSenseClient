namespace DualSenseClient.Controllers.DualSense.Input;

/// <summary>
/// Complete snapshot of all input state from a DualSense controller report (bytes 0-9).
/// </summary>
public readonly struct InputState
{
    /// <summary>
    /// Raw payload bytes 0-9 (sticks, triggers, sequence number, buttons).
    /// </summary>
    private readonly byte[] _raw;

    /// <summary>
    /// Initializes a new input state from bytes 0-9 of the data payload.
    /// </summary>
    public InputState(byte[] raw, int offset)
    {
        _raw = raw[offset..(offset + 10)];
    }

    // Bytes 0-3: Analog sticks
    /// <summary>
    /// Left stick horizontal position (0-255, center is 128).
    /// </summary>
    public byte LeftStickX => _raw[0];

    /// <summary>
    /// Left stick vertical position (0-255, center is 128, 0 is up).
    /// </summary>
    public byte LeftStickY => _raw[1];

    /// <summary>
    /// Right stick horizontal position (0-255, center is 128).
    /// </summary>
    public byte RightStickX => _raw[2];

    /// <summary>
    /// Right stick vertical position (0-255, center is 128, 0 is up).
    /// </summary>
    public byte RightStickY => _raw[3];

    // Bytes 4-5: Analog triggers
    /// <summary>
    /// Left analog trigger value (0-255, released to fully pressed).
    /// </summary>
    public byte L2 => _raw[4];

    /// <summary>
    /// Right analog trigger value (0-255, released to fully pressed).
    /// </summary>
    public byte R2 => _raw[5];

    // Byte 6: Sequence number
    /// <summary>
    /// Incrementing report sequence counter. Use to detect dropped reports.
    /// </summary>
    public byte SequenceNumber => _raw[6];

    // Byte 7: D-Pad (bits 0-3) + Face buttons (bits 4-7)
    /// <summary>
    /// Byte 7 of the payload (D-Pad + face buttons).
    /// </summary>
    private byte Byte7 => _raw[7];

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
    private byte Byte8 => _raw[8];

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
    private byte Byte9 => _raw[9];

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
}