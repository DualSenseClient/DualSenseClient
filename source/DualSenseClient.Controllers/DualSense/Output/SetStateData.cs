using DualSenseClient.Controllers.DualSense.Triggers;

namespace DualSenseClient.Controllers.DualSense.Output;

/// <summary>
/// 47-byte output report payload sent to the DualSense controller.
/// Fields are gated by the <see cref="ValidFlags"/> bytes: clearing a bit makes the
/// controller ignore the corresponding field and retain its current state.
/// </summary>
/// <remarks>
/// <para>
/// Create a fresh payload with the parameterless constructor and object initializer,
/// then send it through <see cref="OutputReport"/>. The struct copies the raw buffer at
/// construction time, so the source buffer may be reused afterward.
/// </para>
/// <para>
/// The valid flag bytes are <b>not</b> derived automatically — set them explicitly
/// (or use <see cref="OutputReportBuilder"/> which derives them from the fields set).
/// </para>
/// </remarks>
public readonly struct SetStateData
{
    /// <summary>
    /// Size of the SetStateData payload in bytes.
    /// </summary>
    public const int PayloadSize = 47;

    /// <summary>
    /// Raw 47-byte output buffer.
    /// </summary>
    private readonly byte[] _raw;

    /// <summary>
    /// Initializes a new, zeroed output payload.
    /// </summary>
    public SetStateData()
    {
        _raw = new byte[PayloadSize];
    }

    /// <summary>
    /// Initializes a new output payload from 47 bytes starting at the given offset.
    /// </summary>
    /// <param name="raw">Buffer containing the payload.</param>
    /// <param name="offset">Offset of the payload start within <paramref name="raw"/>.</param>
    public SetStateData(byte[] raw, int offset)
    {
        _raw = raw[offset..(offset + PayloadSize)];
    }

    /// <summary>
    /// Valid Flag 0 (byte 0): rumble and trigger FFB enable bits.
    /// </summary>
    public ValidFlags ValidFlag0
    {
        get => (ValidFlags)_raw[0];
        init => _raw[0] = (byte)value;
    }

    /// <summary>
    /// Valid Flag 1 (byte 1): light and audio enable bits.
    /// </summary>
    public ValidFlags ValidFlag1
    {
        get => (ValidFlags)_raw[1];
        init => _raw[1] = (byte)value;
    }

    /// <summary>
    /// Rumble right motor / high-frequency (byte 2, 0-255).
    /// </summary>
    public byte RumbleRight
    {
        get => _raw[2];
        init => _raw[2] = value;
    }

    /// <summary>
    /// Rumble left motor / low-frequency (byte 3, 0-255).
    /// </summary>
    public byte RumbleLeft
    {
        get => _raw[3];
        init => _raw[3] = value;
    }

    /// <summary>
    /// Headphone volume (byte 4, 0-255).
    /// </summary>
    public byte HeadphoneVolume
    {
        get => _raw[4];
        init => _raw[4] = value;
    }

    /// <summary>
    /// Speaker volume (byte 5, 0-255).
    /// </summary>
    public byte SpeakerVolume
    {
        get => _raw[5];
        init => _raw[5] = value;
    }

    /// <summary>
    /// Microphone volume (byte 6, 0-255).
    /// </summary>
    public byte MicVolume
    {
        get => _raw[6];
        init => _raw[6] = value;
    }

    /// <summary>
    /// Audio control (byte 7, see <see cref="AudioControl"/>).
    /// </summary>
    public AudioControl AudioControl
    {
        get => (AudioControl)_raw[7];
        init => _raw[7] = (byte)value;
    }

    /// <summary>
    /// Mute LED mode (byte 8): <c>0x00</c> off, <c>0x01</c> on, <c>0x02</c> pulse.
    /// </summary>
    public byte MuteLedMode
    {
        get => _raw[8];
        init => _raw[8] = value;
    }

    /// <summary>
    /// Power save / mute control (byte 9).
    /// </summary>
    public byte PowerSaveControl
    {
        get => _raw[9];
        init => _raw[9] = value;
    }

    /// <summary>
    /// Right (R2) adaptive trigger effect block (bytes 10-20).
    /// </summary>
    public TriggerEffectBlock R2TriggerEffect
    {
        get => new TriggerEffectBlock(_raw, 10);
        init => value.CopyTo(_raw, 10);
    }

    /// <summary>
    /// Left (L2) adaptive trigger effect block (bytes 21-31).
    /// </summary>
    public TriggerEffectBlock L2TriggerEffect
    {
        get => new TriggerEffectBlock(_raw, 21);
        init => value.CopyTo(_raw, 21);
    }

    /// <summary>
    /// Host timestamp (bytes 32-35, uint32 little-endian).
    /// </summary>
    public uint HostTimestamp
    {
        get => (uint)(_raw[32] | (_raw[33] << 8) | (_raw[34] << 16) | (_raw[35] << 24));
        init
        {
            _raw[32] = (byte)value;
            _raw[33] = (byte)(value >> 8);
            _raw[34] = (byte)(value >> 16);
            _raw[35] = (byte)(value >> 24);
        }
    }

    /// <summary>
    /// Motor power reduction (byte 36): bits 0-3 trigger, bits 4-7 rumble.
    /// </summary>
    public byte MotorPowerReduction
    {
        get => _raw[36];
        init => _raw[36] = value;
    }

    /// <summary>
    /// Audio Control 2 (byte 37).
    /// </summary>
    public byte AudioControl2
    {
        get => _raw[37];
        init => _raw[37] = value;
    }

    /// <summary>
    /// Valid Flag 2 (byte 38): lightbar and rumble-emulation enable bits.
    /// </summary>
    public ValidFlags ValidFlag2
    {
        get => (ValidFlags)_raw[38];
        init => _raw[38] = (byte)value;
    }

    /// <summary>
    /// Haptic low-pass filter (byte 39): bit 0 = enable.
    /// </summary>
    public byte HapticLowPassFilter
    {
        get => _raw[39];
        init => _raw[39] = value;
    }

    /// <summary>
    /// Light fade animation (byte 41): <c>0x01</c> fade-in, <c>0x02</c> fade-out.
    /// </summary>
    public byte LightFadeAnimation
    {
        get => _raw[41];
        init => _raw[41] = value;
    }

    /// <summary>
    /// Lightbar brightness (byte 42): <c>0x00</c> high, <c>0x01</c> medium, <c>0x02</c> low.
    /// </summary>
    public byte LightBrightness
    {
        get => _raw[42];
        init => _raw[42] = value;
    }

    /// <summary>
    /// Player LED mask (byte 43, see <see cref="PlayerLedMask"/>).
    /// </summary>
    public PlayerLedMask PlayerLeds
    {
        get => (PlayerLedMask)_raw[43];
        init => _raw[43] = (byte)value;
    }

    /// <summary>
    /// Lightbar red channel (byte 44, 0-255).
    /// </summary>
    public byte LedRed
    {
        get => _raw[44];
        init => _raw[44] = value;
    }

    /// <summary>
    /// Lightbar green channel (byte 45, 0-255).
    /// </summary>
    public byte LedGreen
    {
        get => _raw[45];
        init => _raw[45] = value;
    }

    /// <summary>
    /// Lightbar blue channel (byte 46, 0-255).
    /// </summary>
    public byte LedBlue
    {
        get => _raw[46];
        init => _raw[46] = value;
    }

    /// <summary>
    /// Copies the 47-byte payload into a target buffer at the given offset.
    /// </summary>
    /// <param name="target">Destination buffer.</param>
    /// <param name="offset">Offset to write the payload at.</param>
    public void CopyTo(byte[] target, int offset) => Buffer.BlockCopy(_raw, 0, target, offset, PayloadSize);

    /// <summary>
    /// Returns the raw 47-byte payload as a read-only span.
    /// </summary>
    public ReadOnlySpan<byte> AsSpan() => _raw;
}