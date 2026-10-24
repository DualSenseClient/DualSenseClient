namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// DualSense D-pad direction bit flags used by the virtual-controller mapping pipeline.
/// </summary>
[Flags]
public enum DualSenseDPad : byte
{
    Up = 0x01,
    Down = 0x02,
    Left = 0x04,
    Right = 0x08
}