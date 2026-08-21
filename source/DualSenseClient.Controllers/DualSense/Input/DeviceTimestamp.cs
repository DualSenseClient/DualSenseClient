namespace DualSenseClient.Controllers.DualSense.Input;

/// <summary>
/// Device timestamp from the DualSense controller (bytes 48-51, uint32 LE).
/// </summary>
public readonly struct DeviceTimestamp
{
    /// <summary>
    /// Device timestamp value.
    /// </summary>
    public uint Value { get; }

    /// <summary>
    /// Initializes a new device timestamp from bytes 48-51 of the data payload.
    /// </summary>
    public DeviceTimestamp(byte[] raw, int offset)
    {
        Value = BitConverter.ToUInt32(raw, offset + 48);
    }
}