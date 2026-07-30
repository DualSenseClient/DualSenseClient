namespace DualSenseClient.Controllers.DualSense.Input;

/// <summary>
/// Device timestamp from the DualSense controller (bytes 48-51, uint32 LE).
/// </summary>
public readonly struct DeviceTimestamp
{
    /// <summary>
    /// Raw payload bytes 48-51 (device timestamp).
    /// </summary>
    private readonly byte[] _raw;

    /// <summary>
    /// Initializes a new device timestamp from bytes 48-51 of the data payload.
    /// </summary>
    public DeviceTimestamp(byte[] raw, int offset)
    {
        _raw = raw[(offset + 48)..(offset + 52)];
    }

    /// <summary>
    /// Device timestamp value.
    /// </summary>
    public uint Value => BitConverter.ToUInt32(_raw, 0);
}