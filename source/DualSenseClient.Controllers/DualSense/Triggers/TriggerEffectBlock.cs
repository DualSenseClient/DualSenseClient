namespace DualSenseClient.Controllers.DualSense.Triggers;

/// <summary>
/// 11-byte adaptive trigger effect block. The first byte is the effect mode and
/// the remaining ten bytes are mode-dependent parameters. Only the parameters a mode
/// consumes are meaningful; the rest stay zero.
/// </summary>
public readonly struct TriggerEffectBlock
{
    /// <summary>
    /// Raw 11-byte effect block.
    /// </summary>
    private readonly byte[] _raw;

    /// <summary>
    /// Initializes a new effect block from 11 bytes starting at the given offset.
    /// </summary>
    /// <param name="raw">Buffer containing the effect block.</param>
    /// <param name="offset">Offset of the block start within <paramref name="raw"/>.</param>
    public TriggerEffectBlock(byte[] raw, int offset)
    {
        _raw = raw[offset..(offset + 11)];
    }

    /// <summary>
    /// Effect mode byte.
    /// </summary>
    public TriggerEffectType Mode
    {
        get
        {
            return (TriggerEffectType)_raw[0];
        }
        init
        {
            _raw[0] = (byte)value;
        }
    }

    /// <summary>
    /// Ten mode-dependent parameter bytes. The controller treats the block as an opaque
    /// array, so which parameters a mode reads varies by mode.
    /// </summary>
    public Span<byte> Parameters
    {
        get
        {
            return _raw.AsSpan(1);
        }
    }

    /// <summary>
    /// Copies this effect block into a target buffer at the given offset.
    /// </summary>
    /// <param name="target">Destination buffer.</param>
    /// <param name="offset">Offset to write the 11 bytes at.</param>
    public void CopyTo(byte[] target, int offset) => Buffer.BlockCopy(_raw, 0, target, offset, 11);
}