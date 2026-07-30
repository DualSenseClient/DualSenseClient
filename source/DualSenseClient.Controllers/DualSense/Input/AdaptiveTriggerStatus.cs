namespace DualSenseClient.Controllers.DualSense.Input;

/// <summary>
/// Adaptive trigger status and host timestamp echo from the DualSense controller (bytes 41-47).
/// </summary>
public readonly struct AdaptiveTriggerStatus
{
    /// <summary>
    /// Raw payload bytes 41-47 (trigger statuses, host timestamp echo, status2).
    /// </summary>
    private readonly byte[] _raw;

    /// <summary>
    /// Initializes a new adaptive trigger status from bytes 41-47 of the data payload.
    /// </summary>
    public AdaptiveTriggerStatus(byte[] raw, int offset)
    {
        _raw = raw[(offset + 41)..(offset + 48)];
    }

    /// <summary>
    /// Right trigger (R2) adaptive trigger status.
    /// </summary>
    public byte R2Status => _raw[0];

    /// <summary>
    /// Left trigger (L2) adaptive trigger status.
    /// </summary>
    public byte L2Status => _raw[1];

    /// <summary>
    /// Host timestamp echo (uint32 LE).
    /// </summary>
    public uint HostTimestamp => BitConverter.ToUInt32(_raw, 2);

    /// <summary>
    /// Adaptive trigger status 2.
    /// </summary>
    public byte Status2 => _raw[6];
}