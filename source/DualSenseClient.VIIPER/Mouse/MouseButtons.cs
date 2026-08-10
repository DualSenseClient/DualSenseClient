namespace DualSenseClient.VIIPER.Mouse;

/// <summary>
/// Mouse button bit flags.
/// </summary>
[Flags]
public enum MouseButtons : byte
{
    Left = 0x01,
    Right = 0x02,
    Middle = 0x04,
    Back = 0x08,
    Forward = 0x10
}