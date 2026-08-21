namespace DualSenseClient.Controllers.DualSense.Input;

/// <summary>
/// Adaptive trigger status and host timestamp echo from the DualSense controller (bytes 41-47).
/// </summary>
public readonly struct AdaptiveTriggerStatus
{
    /// <summary>
    /// Right trigger (R2) adaptive trigger status.
    /// </summary>
    public byte R2Status { get; }

    /// <summary>
    /// Left trigger (L2) adaptive trigger status.
    /// </summary>
    public byte L2Status { get; }

    /// <summary>
    /// Host timestamp echo (uint32 LE).
    /// </summary>
    public uint HostTimestamp { get; }

    /// <summary>
    /// Adaptive trigger status 2.
    /// </summary>
    public byte Status2 { get; }

    /// <summary>
    /// Initializes a new adaptive trigger status from bytes 41-47 of the data payload.
    /// </summary>
    public AdaptiveTriggerStatus(byte[] raw, int offset)
    {
        R2Status = raw[offset + 41];
        L2Status = raw[offset + 42];
        HostTimestamp = BitConverter.ToUInt32(raw, offset + 43);
        Status2 = raw[offset + 47];
    }
}